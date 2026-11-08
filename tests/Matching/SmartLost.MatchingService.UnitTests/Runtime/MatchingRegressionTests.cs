using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Logging.Abstractions;
using SmartLost.BuildingBlocks.Application.Profiling;
using SmartLost.BuildingBlocks.Core.Exceptions;
using SmartLost.BuildingBlocks.Core.Results;
using SmartLost.MatchingService.Application.Commands.PrepareImageEmbedding;
using SmartLost.MatchingService.Application.Profiling;
using SmartLost.MatchingService.Application.Services;
using SmartLost.MatchingService.Domain.Entities;
using SmartLost.MatchingService.Domain.Enums;
using SmartLost.MatchingService.Domain.Errors;
using SmartLost.MatchingService.Domain.ValueObjects;
using SmartLost.MatchingService.Infrastructure.Persistence;
using Xunit;

namespace SmartLost.MatchingService.UnitTests.Runtime;

public sealed class MatchingRegressionTests
{
    [Fact]
    public async Task AiTimeoutRejectsLateResultsAndReturnsWithoutWaitingForAnUncooperativeAdapter()
    {
        MatchingProfilingOptions options = new()
        {
            AiCallTimeout = TimeSpan.FromMilliseconds(25)
        };
        MatchingProfiler profiler = new(options, TimeProvider.System, NullLogger<MatchingProfiler>.Instance);
        OperationProfile profile = new("Probe", "timeout");
        TaskCompletionSource<int> result = new(TaskCreationOptions.RunContinuationsAsynchronously);
        using (profiler.Begin(profile))
        {
            await Assert.ThrowsAsync<TimeoutException>(() => profiler.MeasureAiCallAsync("probe", _ => result.Task, CancellationToken.None));
            Assert.False(result.Task.IsCompleted);
            result.SetResult(42);
        }

        Assert.Equal(ProfilingOutcome.TimedOut, Assert.Single(profile.Stages).Outcome);
    }

    [Fact]
    public async Task CallerCancellationWinsOverAiDeadlineAndDoesNotInvokeAlreadyCancelledWork()
    {
        MatchingProfiler profiler = new(new MatchingProfilingOptions(), TimeProvider.System, NullLogger<MatchingProfiler>.Instance);
        using CancellationTokenSource cancellation = new();
        await cancellation.CancelAsync();
        OperationProfile profile = new("Probe", "cancelled");
        using (profiler.Begin(profile))
        {
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => profiler.MeasureAiCallAsync<int>("probe",
                _ => throw new InvalidOperationException("Must not start."), cancellation.Token));
        }

        Assert.Equal(ProfilingOutcome.Cancelled, Assert.Single(profile.Stages).Outcome);
    }

    [Fact]
    public async Task SynchronousAiResultAfterDeadlineIsRejectedEvenBeforeTimerDispatch()
    {
        AdvancingClock clock = new();
        MatchingProfiler profiler = new(new MatchingProfilingOptions { AiCallTimeout = TimeSpan.FromSeconds(1) }, clock,
            NullLogger<MatchingProfiler>.Instance);
        OperationProfile profile = new("Probe", "late");
        using (profiler.Begin(profile))
        {
            await Assert.ThrowsAsync<TimeoutException>(() => profiler.MeasureAiCallAsync("probe", _ =>
            {
                clock.Timestamp += 2 * clock.TimestampFrequency;
                return Task.FromResult(42);
            }, CancellationToken.None));
        }

        Assert.Equal(ProfilingOutcome.TimedOut, Assert.Single(profile.Stages).Outcome);
    }

    [Fact]
    public async Task ExpiredFinalLeaseBecomesFailedWithoutStartingAnotherInference()
    {
        DateTime now = DateTime.UtcNow;
        PhotoReference photo = new(Guid.NewGuid(), 1);
        SearchListing listing = MatchingData.Listing(ListingType.Lost, photo: photo);
        var task = EmbeddingPreparationTask.Create(listing.Id, photo, MatchingOptions.Dinov3Version, now);
        task.Claim(Guid.NewGuid(), now, TimeSpan.FromMinutes(5), 1);
        FakeTasks tasks = new();
        tasks.Items.Add(task);
        FakeListings listings = new();
        listings.Items.Add(listing);
        FakeGenerator generator = new(_ => throw new InvalidOperationException("Attempt limit must prevent inference."));
        FakeUnitOfWork unitOfWork = new();
        PrepareImageEmbeddingCommandHandler handler = new(tasks, listings, new FakeEmbeddings(), new FakePhotos(), generator,
            unitOfWork, new MatchingOptions { MaximumAttempts = 1 }, new FixedClock(new DateTimeOffset(now.AddMinutes(6))));

        Result<EmbeddingPreparationState> result = await handler.Handle(new PrepareImageEmbeddingCommand(task.Id), CancellationToken.None);

        Assert.Equal(EmbeddingPreparationState.Failed, result.Value);
        Assert.Equal(1, task.AttemptCount);
        Assert.Equal(MatchingErrors.AttemptsExhausted.Code, task.LastError);
        Assert.Null(task.LeaseId);
        Assert.Null(task.LeaseExpiresAtUtc);
        Assert.Null(task.NextAttemptAtUtc);
        Assert.Equal(0, generator.Calls);
        Assert.Equal(1, unitOfWork.Saves);
        Assert.Equal(EmbeddingPreparationState.Failed, (await handler.Handle(new PrepareImageEmbeddingCommand(task.Id), CancellationToken.None)).Value);
        Assert.Equal(1, unitOfWork.Saves);
    }

    [Fact]
    public void AttemptLimitNeverExpiresAnActiveLeaseAndClaimCannotExceedTheLimit()
    {
        DateTime now = DateTime.UtcNow;
        var task = EmbeddingPreparationTask.Create(Guid.NewGuid(), new PhotoReference(Guid.NewGuid(), 1),
            MatchingOptions.Dinov3Version, now);
        Assert.False(task.FailIfAttemptsExhausted(now, 1));
        Assert.Throws<DomainException>(() => task.Claim(Guid.NewGuid(), now, TimeSpan.FromMinutes(5), 0));
        task.Claim(Guid.NewGuid(), now, TimeSpan.FromMinutes(5), 1);
        Assert.False(task.FailIfAttemptsExhausted(now.AddMinutes(1), 1));
        Assert.Equal(EmbeddingPreparationState.Processing, task.State);
        Assert.Equal(MatchingErrors.AttemptsExhausted,
            Assert.Throws<DomainException>(() => task.Claim(Guid.NewGuid(), now.AddMinutes(6), TimeSpan.FromMinutes(5), 1)).Error);
        Assert.Throws<DomainException>(() => task.FailIfAttemptsExhausted(now, 0));
    }

    [Fact]
    public async Task UnitOfWorkMapsProviderConcurrencyToADomainConflict()
    {
        DbContextOptions<MatchingDbContext> options = new DbContextOptionsBuilder<MatchingDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString()).AddInterceptors(new ConcurrencyFailure()).Options;
        await using MatchingDbContext context = new(options);
        MatchingUnitOfWork unitOfWork = new(context);
        DomainException exception = await Assert.ThrowsAsync<DomainException>(() => unitOfWork.SaveChangesAsync(CancellationToken.None));
        Assert.Equal(MatchingErrors.ConcurrentUpdate, exception.Error);
        Assert.Equal(ErrorKind.Conflict, exception.Error.Kind);
    }

    private sealed class FixedClock(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow()
        {
            return now;
        }
    }

    private sealed class AdvancingClock : TimeProvider
    {
        public long Timestamp
        {
            get; set;
        }
        public override long GetTimestamp()
        {
            return Timestamp;
        }
    }

    private sealed class ConcurrencyFailure : SaveChangesInterceptor
    {
        public override ValueTask<InterceptionResult<int>> SavingChangesAsync(DbContextEventData eventData,
            InterceptionResult<int> result, CancellationToken cancellationToken = default)
        {
            throw new DbUpdateConcurrencyException("Simulated competing update.");
        }
    }
}
