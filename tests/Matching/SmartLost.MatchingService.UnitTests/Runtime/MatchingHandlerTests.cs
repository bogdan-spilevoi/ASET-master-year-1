using SmartLost.BuildingBlocks.Core.Results;
using SmartLost.Contracts.Listings;
using SmartLost.MatchingService.Application.Commands.PrepareImageEmbedding;
using SmartLost.MatchingService.Application.Contracts;
using SmartLost.MatchingService.Application.Exceptions;
using SmartLost.MatchingService.Application.Interfaces;
using SmartLost.MatchingService.Application.Queries.AnalyzeListingPhoto;
using SmartLost.MatchingService.Application.Queries.GetMatchSuggestions;
using SmartLost.MatchingService.Application.Services;
using SmartLost.MatchingService.Domain.Entities;
using SmartLost.MatchingService.Domain.Enums;
using SmartLost.MatchingService.Domain.Errors;
using SmartLost.MatchingService.Domain.Suggestions;
using SmartLost.MatchingService.Domain.ValueObjects;
using Xunit;

namespace SmartLost.MatchingService.UnitTests.Runtime;

public sealed class MatchingHandlerTests
{
    private static readonly MatchingOptions _options = new() { MaximumAttempts = 2 };

    [Fact]
    public async Task SuggestionsReturnExplicitStatuses()
    {
        var listings = new FakeListings();
        GetMatchSuggestionsQueryHandler handler = SuggestionsHandler(listings);
        SearchListing current = MatchingData.Listing(ListingType.Lost, revision: 2);
        SearchListing closed = MatchingData.Listing(ListingType.Lost, eligible: false);
        listings.Items.AddRange([current, closed]);

        MatchSuggestionsResponse missing = (await handler.Handle(new(Guid.NewGuid(), 1), CancellationToken.None)).Value;
        MatchSuggestionsResponse stale = (await handler.Handle(new(current.Id, 3), CancellationToken.None)).Value;
        Result<MatchSuggestionsResponse> newer = await handler.Handle(new(current.Id, 1), CancellationToken.None);
        MatchSuggestionsResponse unavailable = (await handler.Handle(new(closed.Id, 1), CancellationToken.None)).Value;
        MatchSuggestionsResponse empty = (await handler.Handle(new(current.Id, 2), CancellationToken.None)).Value;

        Assert.Equal((SuggestionStatus.Pending, (int?)null), (missing.Status, missing.SourceRevision));
        Assert.Equal((SuggestionStatus.Pending, (int?)2, (string?)null), (stale.Status, stale.SourceRevision, stale.RankingVersion));
        Assert.Equal(MatchingErrors.SourceRevisionNewer, Assert.Single(newer.Errors));
        Assert.Equal(SuggestionStatus.Unavailable, unavailable.Status);
        Assert.Equal(SuggestionStatus.Ready, empty.Status);
        Assert.Empty(empty.Suggestions);
        Assert.Equal(RankingPolicy.Baseline.Version, empty.RankingVersion);
    }

    [Fact]
    public async Task SuggestionsRankSelectedCandidates()
    {
        var listings = new FakeListings();
        SearchListing source = MatchingData.Listing(ListingType.Found);
        SearchListing candidate = MatchingData.Listing(ListingType.Lost);
        listings.Items.AddRange([source, candidate, MatchingData.Listing(ListingType.Lost, categories: ["keys"])]);

        MatchSuggestionsResponse response = (await SuggestionsHandler(listings)
            .Handle(new(source.Id, 1, "corr"), CancellationToken.None)).Value;

        Assert.Equal(source.Id, response.ListingId);
        Assert.Equal(candidate.Id, Assert.Single(response.Suggestions).CandidateListingId);
    }

    [Fact]
    public async Task PreparationStoresEmbeddingAndCompletesTask()
    {
        Fixture fixture = new();
        EmbeddingPreparationTask task = fixture.AddTask();
        var generator = new FakeGenerator(_ => Task.FromResult(new GeneratedEmbedding(_options.EmbeddingVersion, MatchingData.Unit(3, 1))));

        Result<EmbeddingPreparationState> result = await fixture.Handler(generator).Handle(new(task.Id), CancellationToken.None);

        Assert.Equal(EmbeddingPreparationState.Ready, result.Value);
        ImageEmbedding stored = Assert.Single(fixture.Embeddings.Items);
        Assert.Equal((task.ListingId, task.Photo), (stored.ListingId, stored.Photo));
        Assert.Equal(2, fixture.UnitOfWork.Saves);
        Assert.Equal(EmbeddingPreparationState.Ready,
            (await fixture.Handler(generator).Handle(new(task.Id), CancellationToken.None)).Value);
        Assert.Equal(1, generator.Calls);
    }

    [Fact]
    public async Task PreparationMarksOutdatedTargetsObsoleteWithoutInference()
    {
        Fixture fixture = new();
        EmbeddingPreparationTask task = fixture.AddTask();
        fixture.Listings.Items.Clear();
        fixture.Listings.Items.Add(MatchingData.Listing(ListingType.Lost, id: task.ListingId, photo: new PhotoReference(task.Photo.PhotoId, 2)));
        var generator = new FakeGenerator(_ => throw new InvalidOperationException());

        Result<EmbeddingPreparationState> result = await fixture.Handler(generator).Handle(new(task.Id), CancellationToken.None);

        Assert.Equal(EmbeddingPreparationState.Obsolete, result.Value);
        Assert.Equal(0, generator.Calls);
        Assert.Equal(MatchingErrors.PreparationTaskNotFound,
            Assert.Single((await fixture.Handler(generator).Handle(new(Guid.NewGuid()), CancellationToken.None)).Errors));
    }

    public static TheoryData<string, string> FailureCases => new()
    {
        { "timeout", MatchingErrors.InferenceTimedOut.Code },
        { "offline", MatchingErrors.InferenceUnavailable.Code },
        { "version", MatchingErrors.EmbeddingVersionMismatch.Code },
        { "vector", MatchingErrors.InvalidEmbeddingVector.Code },
        { "photo", MatchingErrors.PhotoUnavailable.Code }
    };

    [Theory]
    [MemberData(nameof(FailureCases))]
    public async Task PreparationSchedulesRetryForInferenceFailures(string failure, string expectedCode)
    {
        Fixture fixture = new();
        EmbeddingPreparationTask task = fixture.AddTask();
        if (failure == "photo")
        {
            fixture.Photos.Content = null;
        }

        var generator = new FakeGenerator(_ => failure switch
        {
            "timeout" => throw new TimeoutException(),
            "offline" => throw new AiInferenceException("offline"),
            "version" => Task.FromResult(new GeneratedEmbedding(new EmbeddingVersion("other", "v9"), MatchingData.Unit(3, 0))),
            _ => Task.FromResult(new GeneratedEmbedding(_options.EmbeddingVersion, [0.2f, 0.2f]))
        });

        Result<EmbeddingPreparationState> result = await fixture.Handler(generator).Handle(new(task.Id), CancellationToken.None);

        Assert.Equal(expectedCode, Assert.Single(result.Errors).Code);
        Assert.Equal(EmbeddingPreparationState.Pending, task.State);
        Assert.Equal(expectedCode, task.LastError);
        Assert.Empty(fixture.Embeddings.Items);
        Assert.Equal(2, fixture.UnitOfWork.Saves);
        Assert.Equal(MatchingErrors.TaskNotDue,
            Assert.Single((await fixture.Handler(generator).Handle(new(task.Id), CancellationToken.None)).Errors));
    }

    [Fact]
    public async Task PhotoContentAnalysisMapsAiFailures()
    {
        var photos = new FakePhotos();
        var labels = new PhotoAnalysis([new DetectedObject("containers", "backpack", ["black"], "fabric", null, "solid", [], ["zipper"],
            ["backpack", "black"])], []);
        var query = new AnalyzePhotoContentQuery(Guid.NewGuid(), Guid.NewGuid(), 1, photos.Content!);

        Assert.Equal("backpack", Assert.Single((await new AnalyzePhotoContentQueryHandler(new FakeAnalyzer(_ => Task.FromResult(labels)))
            .Handle(query, CancellationToken.None)).Value.Objects).ObjectType);
        Assert.Equal(MatchingErrors.InferenceTimedOut, Assert.Single((await new AnalyzePhotoContentQueryHandler(
            new FakeAnalyzer(_ => throw new TimeoutException())).Handle(query, CancellationToken.None)).Errors));
        Assert.Equal(MatchingErrors.InferenceUnavailable, Assert.Single((await new AnalyzePhotoContentQueryHandler(
            new FakeAnalyzer(_ => throw new AiInferenceException())).Handle(query, CancellationToken.None)).Errors));
    }

    [Fact]
    public void ValidatorsRejectMissingIdentifiers()
    {
        Assert.False(new GetMatchSuggestionsQueryValidator().Validate(new GetMatchSuggestionsQuery(Guid.Empty, 0)).IsValid);
        Assert.False(new PrepareImageEmbeddingCommandValidator().Validate(new PrepareImageEmbeddingCommand(Guid.Empty)).IsValid);
        Assert.Equal(3, new AnalyzeListingPhotoQueryValidator().Validate(new AnalyzeListingPhotoQuery(Guid.Empty, Guid.Empty, 0)).Errors.Count);
        Assert.True(new AnalyzeListingPhotoQueryValidator().Validate(new AnalyzeListingPhotoQuery(Guid.NewGuid(), Guid.NewGuid(), 1)).IsValid);
    }

    private static GetMatchSuggestionsQueryHandler SuggestionsHandler(FakeListings listings)
    {
        var options = new MatchingOptions();
        return new GetMatchSuggestionsQueryHandler(listings, new CandidateSelector(listings, options),
            new RelevanceRanker(new FakeEmbeddings(), options, RankingPolicy.Baseline), RankingPolicy.Baseline);
    }

    private sealed class Fixture
    {
        public FakeListings Listings { get; } = new();

        public FakeEmbeddings Embeddings { get; } = new();

        public FakeTasks Tasks { get; } = new();

        public FakePhotos Photos { get; } = new();

        public FakeUnitOfWork UnitOfWork { get; } = new();

        public EmbeddingPreparationTask AddTask()
        {
            var photo = new PhotoReference(Guid.NewGuid(), 1);
            SearchListing listing = MatchingData.Listing(ListingType.Lost, photo: photo);
            var task = EmbeddingPreparationTask.Create(listing.Id, photo, _options.EmbeddingVersion,
                TimeProvider.System.GetUtcNow().UtcDateTime.AddMinutes(-1));
            Listings.Items.Add(listing);
            Tasks.Items.Add(task);
            return task;
        }

        public PrepareImageEmbeddingCommandHandler Handler(IImageEmbeddingGenerator generator)
        {
            return new PrepareImageEmbeddingCommandHandler(Tasks, Listings, Embeddings, Photos, generator, UnitOfWork, _options,
                TimeProvider.System);
        }
    }
}
