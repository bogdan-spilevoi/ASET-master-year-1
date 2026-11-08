using FluentValidation;
using MediatR;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using SmartLost.BuildingBlocks.Application;
using SmartLost.BuildingBlocks.Application.Behaviors;
using SmartLost.BuildingBlocks.Application.Caching;
using SmartLost.BuildingBlocks.Core.Results;
using SmartLost.BuildingBlocks.Infrastructure;
using SmartLost.BuildingBlocks.Infrastructure.Caching;
using Xunit;

namespace SmartLost.BuildingBlocks.UnitTests.Caching;

public sealed class QueryCachingTests
{
    [Fact]
    public void AttributeAndKeysValidatePolicyAndSeparateParametersRequestAndResponseTypes()
    {
        CacheableQueryAttribute policy = new("probe");
        Assert.Equal("probe", policy.Prefix);
        Assert.Equal(300, policy.TtlSeconds);
        policy.TtlSeconds = 60;
        Assert.Equal(60, policy.TtlSeconds);
        Assert.Throws<ArgumentException>(() => new CacheableQueryAttribute(" "));
        Assert.Throws<ArgumentOutOfRangeException>(() => policy.TtlSeconds = 0);
        Assert.Throws<ArgumentOutOfRangeException>(() => policy.TtlSeconds = -1);
        string first = QueryCacheKey.Create<CachedQuery, Result<string>>(new CachedQuery("private search"), "probe");
        Assert.Equal(first, QueryCacheKey.Create<CachedQuery, Result<string>>(new CachedQuery("private search"), "probe"));
        Assert.NotEqual(first, QueryCacheKey.Create<CachedQuery, Result<string>>(new CachedQuery("another search"), "probe"));
        Assert.NotEqual(first, QueryCacheKey.Create<PlainQuery, Result<string>>(new PlainQuery("private search"), "probe"));
        Assert.NotEqual(first, QueryCacheKey.Create<CachedQuery, Result<int>>(new CachedQuery("private search"), "probe"));
        Assert.DoesNotContain("private search", first, StringComparison.Ordinal);
    }

    [Fact]
    public async Task SuccessfulResultsHitAndExpireAtAbsoluteLifetimeWithoutSliding()
    {
        ManualClock clock = new();
        using MemoryQueryCache cache = new(clock);
        CachingBehavior<CachedQuery, Result<string>> behavior = Create(cache);
        int calls = 0;
        Task<Result<string>> Next(CancellationToken _) => Task.FromResult(Result<string>.Success((++calls).ToString(System.Globalization.CultureInfo.InvariantCulture)));
        var query = new CachedQuery("a");
        Assert.Equal("1", (await behavior.Handle(query, Next, CancellationToken.None)).Value);
        clock.Advance(TimeSpan.FromSeconds(299));
        Assert.Equal("1", (await behavior.Handle(query, Next, CancellationToken.None)).Value);
        clock.Advance(TimeSpan.FromSeconds(1));
        Assert.Equal("2", (await behavior.Handle(query, Next, CancellationToken.None)).Value);
        Assert.Equal(2, calls);
    }

    [Fact]
    public async Task UnattributedRequestsAlwaysReachTheHandler()
    {
        using MemoryQueryCache cache = new(TimeProvider.System);
        var behavior = new CachingBehavior<PlainQuery, Result<string>>(cache, NullLogger<CachingBehavior<PlainQuery, Result<string>>>.Instance);
        int calls = 0;
        Task<Result<string>> Next(CancellationToken _) => Task.FromResult(Result<string>.Success((++calls).ToString(System.Globalization.CultureInfo.InvariantCulture)));
        var query = new PlainQuery("a");
        await behavior.Handle(query, Next, CancellationToken.None);
        await behavior.Handle(query, Next, CancellationToken.None);
        Assert.Equal(2, calls);
    }

    [Fact]
    public async Task FailuresAndThrownExceptionsAreNotCachedAndReleaseTheMissLock()
    {
        using MemoryQueryCache cache = new(TimeProvider.System);
        CachingBehavior<CachedQuery, Result<string>> behavior = Create(cache);
        int calls = 0;
        Task<Result<string>> Failure(CancellationToken _)
        {
            calls++;
            return Task.FromResult(Result<string>.Failure(new Error("probe.failed", "Failed.", ErrorKind.NotFound)));
        }

        var query = new CachedQuery("a");
        Assert.True((await behavior.Handle(query, Failure, CancellationToken.None)).IsFailure);
        Assert.True((await behavior.Handle(query, Failure, CancellationToken.None)).IsFailure);
        Assert.Equal(2, calls);
        await Assert.ThrowsAsync<InvalidOperationException>(() => behavior.Handle(query, _ => throw new InvalidOperationException("failure"), CancellationToken.None));
        Assert.Equal("recovered", (await behavior.Handle(query, _ => Task.FromResult(Result<string>.Success("recovered")), CancellationToken.None)).Value);
    }

    [Fact]
    public async Task ConcurrentRequestsForOneKeyPopulateOnlyOnce()
    {
        using MemoryQueryCache cache = new(TimeProvider.System);
        CachingBehavior<CachedQuery, Result<string>> behavior = Create(cache);
        TaskCompletionSource<Result<string>> complete = new(TaskCreationOptions.RunContinuationsAsynchronously);
        int calls = 0;
        Task<Result<string>> Next(CancellationToken _)
        {
            Interlocked.Increment(ref calls);
            return complete.Task;
        }

        Task<Result<string>>[] requests = [.. Enumerable.Range(0, 20).Select(_ => behavior.Handle(new CachedQuery("same"), Next, CancellationToken.None))];
        Assert.Equal(1, calls);
        complete.SetResult(Result<string>.Success("shared"));
        Result<string>[] results = await Task.WhenAll(requests);
        Assert.All(results, result => Assert.Equal("shared", result.Value));
        Assert.Equal(1, calls);
    }

    [Fact]
    public async Task ReaderHoldingExpiredEntryReusesConcurrentRefreshWithoutRemovingIt()
    {
        ManualClock time = new();
        using PausingClock clock = new(time);
        using MemoryQueryCache cache = new(clock);
        int calls = 0;
        Task<string> Factory(CancellationToken _)
        {
            return Task.FromResult(Interlocked.Increment(ref calls).ToString(System.Globalization.CultureInfo.InvariantCulture));
        }

        Task<string> Read()
        {
            return cache.GetOrCreateAsync("expiry-race", TimeSpan.FromMinutes(5), Factory, _ => true, CancellationToken.None);
        }

        Assert.Equal("1", await Read());
        time.Advance(TimeSpan.FromMinutes(6));
        clock.PauseNextRead();
        Task<string> delayedReader = Task.Run(Read);
        string refreshed;
        try
        {
            await clock.ReadPaused.WaitAsync(TimeSpan.FromSeconds(10));
            refreshed = await Read();
        }
        finally
        {
            clock.Resume();
        }

        Assert.Equal("2", await delayedReader.WaitAsync(TimeSpan.FromSeconds(10)));
        Assert.Equal("2", refreshed);
        Assert.Equal("2", await Read());
        Assert.Equal(2, calls);
    }

    [Fact]
    public async Task CancellingAWaiterDoesNotCancelAnotherRequestsPopulation()
    {
        using MemoryQueryCache cache = new(TimeProvider.System);
        CachingBehavior<CachedQuery, Result<string>> behavior = Create(cache);
        TaskCompletionSource<Result<string>> complete = new(TaskCreationOptions.RunContinuationsAsynchronously);
        var query = new CachedQuery("same");
        Task<Result<string>> first = behavior.Handle(query, _ => complete.Task, CancellationToken.None);
        using CancellationTokenSource cancellation = new();
        Task<Result<string>> waiting = behavior.Handle(query, _ => throw new InvalidOperationException(), cancellation.Token);
        await cancellation.CancelAsync();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => waiting);
        complete.SetResult(Result<string>.Success("ok"));
        Assert.Equal("ok", (await first).Value);
        Assert.Equal("ok", (await behavior.Handle(query, _ => throw new InvalidOperationException(), CancellationToken.None)).Value);
    }

    [Fact]
    public async Task CancellationEvenAfterFactoryCompletionDoesNotStoreTheResult()
    {
        using MemoryQueryCache cache = new(TimeProvider.System);
        CachingBehavior<CachedQuery, Result<string>> behavior = Create(cache);
        using CancellationTokenSource cancellation = new();
        var query = new CachedQuery("a");
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => behavior.Handle(query, _ =>
        {
            cancellation.Cancel();
            return Task.FromResult(Result<string>.Success("cancelled"));
        }, cancellation.Token));
        Assert.Equal("fresh", (await behavior.Handle(query, _ => Task.FromResult(Result<string>.Success("fresh")), CancellationToken.None)).Value);
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => behavior.Handle(query, _ => throw new InvalidOperationException(), cancellation.Token));
    }

    [Fact]
    public async Task RegisteredPipelineRevalidatesBeforeACacheHitAndCacheRegistrationIsSingleton()
    {
        ServiceCollection services = new();
        services.AddLogging();
        services.AddBuildingBlocksApplication(typeof(QueryCachingTests).Assembly, useCaching: true);
        services.AddBuildingBlocksQueryCache();
        services.AddBuildingBlocksQueryCache();
        ProbeState state = new();
        services.AddSingleton(state);
        InlineValidator<PipelineQuery> validator = [];
        validator.RuleFor(query => query.Name).Must(_ => state.Valid).WithErrorCode("probe.invalid");
        services.AddSingleton<IValidator<PipelineQuery>>(validator);
        using ServiceProvider provider = services.BuildServiceProvider();
        using IServiceScope firstScope = provider.CreateScope();
        using IServiceScope secondScope = provider.CreateScope();
        Assert.Same(firstScope.ServiceProvider.GetRequiredService<IQueryCache>(), secondScope.ServiceProvider.GetRequiredService<IQueryCache>());
        ISender first = firstScope.ServiceProvider.GetRequiredService<ISender>();
        var request = new PipelineQuery("a");
        Assert.True((await first.Send(request)).IsSuccess);
        Assert.True((await secondScope.ServiceProvider.GetRequiredService<ISender>().Send(request)).IsSuccess);
        Assert.Equal(1, state.Calls);
        state.Valid = false;
        Result<string> failure = await first.Send(request);
        Assert.True(failure.IsFailure);
        Assert.Equal("probe.invalid", Assert.Single(failure.Errors).Code);
        Assert.Equal(1, state.Calls);
    }

    [Fact]
    public async Task CacheGuardsRejectInvalidArguments()
    {
        using MemoryQueryCache cache = new(TimeProvider.System);
        static Task<string> Factory(CancellationToken _) => Task.FromResult("value");
        await Assert.ThrowsAsync<ArgumentException>(() => cache.GetOrCreateAsync(" ", TimeSpan.FromSeconds(1), Factory, _ => true, CancellationToken.None));
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => cache.GetOrCreateAsync("key", TimeSpan.Zero, Factory, _ => true, CancellationToken.None));
        await Assert.ThrowsAsync<ArgumentNullException>(() => cache.GetOrCreateAsync<string>("key", TimeSpan.FromSeconds(1), null!, _ => true, CancellationToken.None));
        await Assert.ThrowsAsync<ArgumentNullException>(() => cache.GetOrCreateAsync("key", TimeSpan.FromSeconds(1), Factory, null!, CancellationToken.None));
        await Assert.ThrowsAsync<ArgumentNullException>(() => cache.GetOrCreateAsync("key", TimeSpan.FromSeconds(1), _ => Task.FromResult<string>(null!), _ => true, CancellationToken.None));
    }

    private static CachingBehavior<CachedQuery, Result<string>> Create(IQueryCache cache)
    {
        return new CachingBehavior<CachedQuery, Result<string>>(cache, NullLogger<CachingBehavior<CachedQuery, Result<string>>>.Instance);
    }

    [CacheableQuery("probe")]
    private sealed record CachedQuery(string Name);

    private sealed record PlainQuery(string Name);

    [CacheableQuery("pipeline-probe")]
    public sealed record PipelineQuery(string Name) : IRequest<Result<string>>;

    public sealed class ProbeState
    {
        public bool Valid { get; set; } = true;
        public int Calls
        {
            get; set;
        }
    }

    public sealed class PipelineQueryHandler(ProbeState state) : IRequestHandler<PipelineQuery, Result<string>>
    {
        public Task<Result<string>> Handle(PipelineQuery request, CancellationToken cancellationToken)
        {
            state.Calls++;
            return Task.FromResult(Result<string>.Success(request.Name));
        }
    }

    private sealed class PausingClock(TimeProvider inner) : TimeProvider, IDisposable
    {
        private readonly TaskCompletionSource _paused = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly ManualResetEventSlim _resume = new();
        private int _pauseNext;

        internal Task ReadPaused => _paused.Task;

        public override DateTimeOffset GetUtcNow()
        {
            if (Interlocked.Exchange(ref _pauseNext, 0) == 1)
            {
                _paused.TrySetResult();
                if (!_resume.Wait(TimeSpan.FromSeconds(10)))
                {
                    throw new TimeoutException("The paused cache reader was not resumed.");
                }
            }

            return inner.GetUtcNow();
        }

        internal void PauseNextRead()
        {
            Interlocked.Exchange(ref _pauseNext, 1);
        }

        internal void Resume()
        {
            _resume.Set();
        }

        public void Dispose()
        {
            _resume.Dispose();
        }
    }

    private sealed class ManualClock : TimeProvider
    {
        private DateTimeOffset _now = new(2026, 11, 4, 0, 0, 0, TimeSpan.Zero);

        public override DateTimeOffset GetUtcNow()
        {
            return _now;
        }

        internal void Advance(TimeSpan duration)
        {
            _now += duration;
        }
    }
}
