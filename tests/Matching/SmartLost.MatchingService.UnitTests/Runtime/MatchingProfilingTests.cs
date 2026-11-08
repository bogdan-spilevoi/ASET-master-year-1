using System.Diagnostics;
using MediatR;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using SmartLost.BuildingBlocks.Application.Behaviors;
using SmartLost.BuildingBlocks.Application.Profiling;
using SmartLost.BuildingBlocks.Application.Transactions;
using SmartLost.BuildingBlocks.Core.Results;
using SmartLost.MatchingService.Application;
using SmartLost.MatchingService.Application.Contracts;
using SmartLost.MatchingService.Application.Exceptions;
using SmartLost.MatchingService.Application.Interfaces;
using SmartLost.MatchingService.Application.Profiling;
using SmartLost.MatchingService.Application.Profiling.Decorators;
using SmartLost.MatchingService.Application.Queries.AnalyzeListingPhoto;
using SmartLost.MatchingService.Application.Queries.GetMatchSuggestions;
using SmartLost.MatchingService.Application.Services;
using SmartLost.MatchingService.Domain.Entities;
using SmartLost.MatchingService.Domain.Enums;
using SmartLost.MatchingService.Domain.Suggestions;
using Xunit;

namespace SmartLost.MatchingService.UnitTests.Runtime;

public sealed class MatchingProfilingTests
{
    private static readonly PhotoContent _photo = new(new byte[] { 1 }, "image/jpeg");

    [Fact]
    public async Task StageDecoratorsRecordDurationsAndCandidateCounts()
    {
        (MatchingProfiler profiler, _) = CreateProfiler(new MatchingProfilingOptions());
        var listings = new FakeListings();
        SearchListing source = MatchingData.Listing(ListingType.Lost);
        listings.Items.AddRange([source, MatchingData.Listing(ListingType.Found), MatchingData.Listing(ListingType.Found)]);
        var selector = new ProfiledCandidateSelector(new CandidateSelector(listings, new MatchingOptions()), profiler);
        var ranker = new ProfiledRelevanceRanker(
            new RelevanceRanker(new FakeEmbeddings(), new MatchingOptions(), RankingPolicy.Baseline), profiler);
        var profile = new OperationProfile("Probe", "corr-1");

        using (profiler.Begin(profile))
        {
            IReadOnlyList<SearchListing> candidates = await selector.SelectAsync(source, CancellationToken.None);
            await ranker.RankAsync(source, candidates, CancellationToken.None);
        }

        Assert.Null(profiler.Current);
        Assert.Collection(profile.Stages,
            stage => Assert.Equal((ProfilingStages.CandidateSelection, StageKind.Stage, ProfilingOutcome.Succeeded, (int?)2),
                (stage.Name, stage.Kind, stage.Outcome, stage.ItemCount)),
            stage => Assert.Equal((ProfilingStages.RelevanceRanking, ProfilingOutcome.Succeeded, (int?)2),
                (stage.Name, stage.Outcome, stage.ItemCount)));
        Assert.All(profile.Stages, stage => Assert.True(stage.Duration >= TimeSpan.Zero));
        Assert.Contains("CandidateSelection=", profile.Describe(), StringComparison.Ordinal);
        Assert.Contains("/items=2", profile.Describe(), StringComparison.Ordinal);
        Assert.Equal("none", new OperationProfile("Empty", "c").Describe());
    }

    [Fact]
    public async Task AiDecoratorsReportTimeoutsCancellationsAndFailures()
    {
        (MatchingProfiler profiler, ListLogger<MatchingProfiler> logger) = CreateProfiler(
            new MatchingProfilingOptions { AiCallTimeout = TimeSpan.FromMilliseconds(50) });
        var hanging = new ProfiledImageEmbeddingGenerator(new FakeGenerator(async token =>
        {
            await Task.Delay(Timeout.InfiniteTimeSpan, token);
            throw new UnreachableException();
        }), profiler);
        var failing = new ProfiledPhotoAnalyzer(new FakeAnalyzer(_ => throw new AiInferenceException("runtime offline")), profiler);
        var adapterTimeout = new ProfiledPhotoAnalyzer(new FakeAnalyzer(_ => throw new TimeoutException()), profiler);
        var profile = new OperationProfile("Probe", "corr-2");

        using (profiler.Begin(profile))
        {
            TimeoutException timeout = await Assert.ThrowsAsync<TimeoutException>(
                () => hanging.GenerateAsync(_photo, CancellationToken.None));
            Assert.IsType<OperationCanceledException>(timeout.InnerException, exactMatch: false);

            using var cancellation = new CancellationTokenSource();
            await cancellation.CancelAsync();
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => hanging.GenerateAsync(_photo, cancellation.Token));

            await Assert.ThrowsAsync<AiInferenceException>(() => failing.AnalyzeAsync(_photo, CancellationToken.None));
            await Assert.ThrowsAsync<TimeoutException>(() => adapterTimeout.AnalyzeAsync(_photo, CancellationToken.None));
        }

        Assert.Equal([ProfilingOutcome.TimedOut, ProfilingOutcome.Cancelled, ProfilingOutcome.Failed, ProfilingOutcome.TimedOut],
            profile.Stages.Select(stage => stage.Outcome));
        Assert.All(profile.Stages, stage => Assert.Equal(StageKind.AiCall, stage.Kind));
        Assert.Equal(nameof(AiInferenceException), profile.Stages[2].ExceptionType);
        Assert.Equal(ProfilingStages.PhotoAnalysis, profile.Stages[2].Name);
        Assert.Equal(4, logger.Entries.Count(entry => entry.EventId.Id == 1303 && entry.Level == LogLevel.Warning));
        Assert.DoesNotContain(logger.Entries, entry => entry.Message.Contains("runtime offline", StringComparison.Ordinal));
    }

    [Fact]
    public async Task SlowAiCallsAreWarnedAndCallsWithoutProfileAreStillMeasured()
    {
        (MatchingProfiler profiler, ListLogger<MatchingProfiler> logger) = CreateProfiler(
            new MatchingProfilingOptions { SlowAiCallThreshold = TimeSpan.Zero });
        var generator = new ProfiledImageEmbeddingGenerator(new FakeGenerator(_ =>
            Task.FromResult(new GeneratedEmbedding(MatchingOptions.Dinov3Version, [1f]))), profiler);
        var analyzer = new ProfiledPhotoAnalyzer(new FakeAnalyzer(_ => Task.FromResult(new PhotoAnalysis([], []))), profiler);

        await generator.GenerateAsync(_photo, CancellationToken.None);
        await analyzer.AnalyzeAsync(_photo, CancellationToken.None);

        Assert.Equal(2, logger.Entries.Count(entry => entry.EventId.Id == 1304));
        Assert.Contains(logger.Entries, entry => entry.Message.Contains("(none) (-)", StringComparison.Ordinal));
    }

    [Fact]
    public async Task BehaviorLogsOutcomeStagesAndSlowOperationWarnings()
    {
        var options = new MatchingProfilingOptions { SlowOperationThreshold = TimeSpan.Zero };
        (MatchingProfiler profiler, _) = CreateProfiler(options);
        var logger = new ListLogger<ProfilingBehavior<GetMatchSuggestionsQuery, Result<MatchSuggestionsResponse>>>();
        var behavior = new ProfilingBehavior<GetMatchSuggestionsQuery, Result<MatchSuggestionsResponse>>(
            profiler, options, TimeProvider.System, logger);
        var query = new GetMatchSuggestionsQuery(Guid.NewGuid(), 1, "listing-req-42");

        Result<MatchSuggestionsResponse> failure = await behavior.Handle(query, async token =>
        {
            Assert.Equal("listing-req-42", profiler.Current!.CorrelationId);
            await profiler.MeasureStageAsync(ProfilingStages.CandidateSelection, _ => Task.FromResult(3), count => count, token);
            return Result<MatchSuggestionsResponse>.Failure(new Error("matching.test", "Test failure.", ErrorKind.Failure));
        }, CancellationToken.None);

        Assert.True(failure.IsFailure);
        Assert.Null(profiler.Current);
        (LogLevel Level, EventId EventId, string Message) completed = Assert.Single(logger.Entries, entry => entry.EventId.Id == 1301);
        Assert.Equal(LogLevel.Information, completed.Level);
        Assert.Contains("GetMatchSuggestionsQuery (listing-req-42) finished as Failed", completed.Message, StringComparison.Ordinal);
        Assert.Contains("CandidateSelection=", completed.Message, StringComparison.Ordinal);
        Assert.Contains("matching.test", completed.Message, StringComparison.Ordinal);
        Assert.Single(logger.Entries, entry => entry.EventId.Id == 1302 && entry.Level == LogLevel.Warning);
    }

    [Fact]
    public async Task BehaviorRecordsExceptionsTimeoutsAndCancellationsAndRethrows()
    {
        var options = new MatchingProfilingOptions();
        (MatchingProfiler profiler, _) = CreateProfiler(options);
        var logger = new ListLogger<ProfilingBehavior<AnalyzeListingPhotoQuery, Result<PhotoAnalysis>>>();
        var behavior = new ProfilingBehavior<AnalyzeListingPhotoQuery, Result<PhotoAnalysis>>(
            profiler, options, TimeProvider.System, logger);
        var query = new AnalyzeListingPhotoQuery(Guid.NewGuid(), Guid.NewGuid(), 1, "bad id with spaces");

        await Assert.ThrowsAsync<InvalidOperationException>(() => behavior.Handle(query, _ => throw new InvalidOperationException("secret"),
            CancellationToken.None));
        await Assert.ThrowsAsync<TimeoutException>(() => behavior.Handle(query, _ => throw new TimeoutException(), CancellationToken.None));
        using var cancellation = new CancellationTokenSource();
        await cancellation.CancelAsync();
        await Assert.ThrowsAsync<OperationCanceledException>(() => behavior.Handle(query,
            token => throw new OperationCanceledException(token), cancellation.Token));
        Result<PhotoAnalysis> success = await behavior.Handle(query,
            _ => Task.FromResult(Result<PhotoAnalysis>.Success(new PhotoAnalysis([], []))), CancellationToken.None);

        Assert.True(success.IsSuccess);
        string[] messages = [.. logger.Entries.Select(entry => entry.Message)];
        Assert.Contains("finished as Failed", messages[0], StringComparison.Ordinal);
        Assert.Contains(nameof(InvalidOperationException), messages[0], StringComparison.Ordinal);
        Assert.DoesNotContain("secret", messages[0], StringComparison.Ordinal);
        Assert.Contains("finished as TimedOut", messages[1], StringComparison.Ordinal);
        Assert.Contains("finished as Cancelled", messages[2], StringComparison.Ordinal);
        Assert.Contains("finished as Succeeded", messages[3], StringComparison.Ordinal);
        Assert.All(messages, message => Assert.DoesNotContain("bad id with spaces", message, StringComparison.Ordinal));
        Assert.DoesNotContain(logger.Entries, entry => entry.EventId.Id == 1302);
    }

    [Fact]
    public void CorrelationIdsAcceptOnlyLogSafeValues()
    {
        Assert.Equal("abc-123_x.y:z", CorrelationIds.Resolve("abc-123_x.y:z"));
        Assert.Equal(32, CorrelationIds.Resolve(null).Length);
        Assert.NotEqual("bad\nvalue", CorrelationIds.Resolve("bad\nvalue"));
        Assert.Equal(32, CorrelationIds.Resolve(new string('a', CorrelationIds.MaximumLength + 1)).Length);
        using var activity = new Activity("probe");
        activity.SetIdFormat(ActivityIdFormat.W3C);
        activity.Start();
        Assert.Equal(activity.TraceId.ToHexString(), CorrelationIds.Resolve(""));
    }

    [Fact]
    public void OptionsRejectInvalidProfilingAndLeaseSettings()
    {
        Assert.Throws<InvalidOperationException>(new MatchingProfilingOptions { AiCallTimeout = TimeSpan.Zero }.Validate);
        Assert.Throws<InvalidOperationException>(new MatchingProfilingOptions { SlowOperationThreshold = TimeSpan.FromSeconds(-1) }.Validate);
        Assert.Throws<InvalidOperationException>(() => new MatchingOptions { LeaseDuration = TimeSpan.FromMinutes(1) }
            .Validate(new MatchingProfilingOptions()));
        Assert.Throws<InvalidOperationException>(() => new MatchingOptions { MaximumAttempts = 0 }.Validate(new MatchingProfilingOptions()));
        Assert.Throws<InvalidOperationException>(() => new ServiceCollection().AddMatchingAiProfiling());
        Assert.Throws<ArgumentNullException>(() => new OperationProfile("p", "c").Add(null!));
    }

    [Fact]
    public async Task MediatorPipelineProfilesValidationFailuresAndDecoratedAiCalls()
    {
        var provider = new SharedLoggerProvider();
        var listings = new FakeListings();
        var services = new ServiceCollection();
        services.AddLogging(builder => builder.AddProvider(provider));
        services.AddApplication();
        services.AddSingleton<ISearchListingRepository>(listings);
        services.AddSingleton<IImageEmbeddingRepository>(new FakeEmbeddings());
        services.AddSingleton<IEmbeddingPreparationTaskRepository>(new FakeTasks());
        services.AddSingleton<IListingPhotoReader>(new FakePhotos());
        services.AddSingleton<IUnitOfWork>(new FakeUnitOfWork());
        services.AddScoped<IPhotoAnalyzer>(_ => new FakeAnalyzer(_ => Task.FromResult(new PhotoAnalysis([], ["blurry"]))));
        services.AddSingleton<IImageEmbeddingGenerator, ThrowingGenerator>();
        services.AddMatchingAiProfiling();
        await using ServiceProvider root = services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true });
        await using AsyncServiceScope scope = root.CreateAsyncScope();
        ISender sender = scope.ServiceProvider.GetRequiredService<ISender>();

        Assert.IsType<ProfiledImageEmbeddingGenerator>(scope.ServiceProvider.GetRequiredService<IImageEmbeddingGenerator>());
        Result<MatchSuggestionsResponse> invalid = await sender.Send(new GetMatchSuggestionsQuery(Guid.Empty, 0, "corr-invalid"));
        Result<PhotoAnalysis> analysis = await sender.Send(new AnalyzeListingPhotoQuery(Guid.NewGuid(), Guid.NewGuid(), 1, "corr-ai"));
        SearchListing source = MatchingData.Listing(ListingType.Lost);
        listings.Items.AddRange([source, MatchingData.Listing(ListingType.Found)]);
        Result<MatchSuggestionsResponse> ready = await sender.Send(new GetMatchSuggestionsQuery(source.Id, 1, "corr-ready"));

        Assert.True(invalid.IsFailure);
        Assert.Equal(["blurry"], analysis.Value.Uncertainties);
        Assert.Equal(SuggestionStatus.Ready, ready.Value.Status);
        string[] summaries = [.. provider.Entries.Where(entry => entry.EventId.Id == 1301).Select(entry => entry.Message)];
        Assert.Contains(summaries, message => message.Contains("(corr-invalid) finished as Failed", StringComparison.Ordinal) &&
            message.Contains("matching.listing_id_required", StringComparison.Ordinal));
        Assert.Contains(summaries, message => message.Contains("(corr-ai) finished as Succeeded", StringComparison.Ordinal) &&
            message.Contains(ProfilingStages.PhotoAnalysis, StringComparison.Ordinal));
        Assert.Contains(summaries, message => message.Contains("(corr-ready)", StringComparison.Ordinal) &&
            message.Contains("CandidateSelection=", StringComparison.Ordinal) &&
            message.Contains("RelevanceRanking=", StringComparison.Ordinal) &&
            message.Contains("/items=1", StringComparison.Ordinal));
    }

    private static (MatchingProfiler Profiler, ListLogger<MatchingProfiler> Logger) CreateProfiler(MatchingProfilingOptions options)
    {
        var logger = new ListLogger<MatchingProfiler>();
        return (new MatchingProfiler(options, TimeProvider.System, logger), logger);
    }

    private sealed class ThrowingGenerator : IImageEmbeddingGenerator
    {
        public Task<GeneratedEmbedding> GenerateAsync(PhotoContent photo, CancellationToken cancellationToken)
        {
            throw new AiInferenceException();
        }
    }
}
