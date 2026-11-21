using System.Text;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Caching.StackExchangeRedis;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using SmartLost.BuildingBlocks.Application.Caching;
using SmartLost.BuildingBlocks.Core.Results;
using SmartLost.BuildingBlocks.Infrastructure;
using SmartLost.BuildingBlocks.Infrastructure.Caching;
using Xunit;

namespace SmartLost.BuildingBlocks.UnitTests.Caching;

public sealed class DistributedQueryCacheTests
{
    private static readonly TimeSpan _lifetime = TimeSpan.FromMinutes(5);

    [Fact]
    public async Task ResultsRoundTripThroughSerializedStoreAsReadOnlySnapshots()
    {
        RecordingStore store = new();
        using DistributedQueryCache cache = QueryCachingTests.CreateCache(TimeProvider.System, store);
        int calls = 0;
        Task<Result<IReadOnlyList<Label>>> Empty(CancellationToken _)
        {
            calls++;
            return Task.FromResult(Result<IReadOnlyList<Label>>.Success(Array.AsReadOnly(Array.Empty<Label>())));
        }

        Task<Result<Analysis>> Nested(CancellationToken _)
        {
            calls++;
            return Task.FromResult(Result<Analysis>.Success(new Analysis(
                [new Label("bag", ["black", "blue"], null)], ["blurry"])));
        }

        Assert.Empty((await cache.GetOrCreateAsync("empty", _lifetime, Empty, result => result.IsSuccess, CancellationToken.None)).Value);
        Result<IReadOnlyList<Label>> emptyHit = await cache.GetOrCreateAsync("empty", _lifetime, Empty, result => result.IsSuccess, CancellationToken.None);
        Assert.True(emptyHit.IsSuccess);
        Assert.Empty(emptyHit.Value);
        Assert.Throws<NotSupportedException>(() => ((IList<Label>)emptyHit.Value).Add(new Label("x", [], null)));

        Result<Analysis> first = await cache.GetOrCreateAsync("nested", _lifetime, Nested, result => result.IsSuccess, CancellationToken.None);
        Result<Analysis> hit = await cache.GetOrCreateAsync("nested", _lifetime, Nested, result => result.IsSuccess, CancellationToken.None);
        Assert.Equal(2, calls);
        Assert.NotSame(first.Value, hit.Value);
        Label label = Assert.Single(hit.Value.Labels);
        Assert.Equal("bag", label.Name);
        Assert.Equal(["black", "blue"], label.Colors);
        Assert.Null(label.Material);
        Assert.Equal(["blurry"], hit.Value.Uncertainties);
        Assert.Throws<NotSupportedException>(() => ((IList<string>)label.Colors).Clear());
        Assert.Throws<NotSupportedException>(() => ((IList<Label>)hit.Value.Labels).Clear());
        Assert.All(store.Keys, key => Assert.StartsWith("smartlost:tests:testing:", key, StringComparison.Ordinal));
    }

    [Fact]
    public void SerializerRoundTripsFailuresAndRejectsDriftedOrForeignPayloads()
    {
        DateTimeOffset expires = DateTimeOffset.UtcNow.AddMinutes(1);
        var error = new Error("probe.failed", "Failed.", ErrorKind.Conflict, "name");
        Assert.True(QueryCacheSerializer.TryDeserialize(QueryCacheSerializer.Serialize(Result<int>.Failure(error), expires),
            out Result<int>? failure, out DateTimeOffset readExpiry));
        Assert.Equal(error, Assert.Single(failure!.Errors));
        Assert.Equal(expires, readExpiry);
        Assert.True(QueryCacheSerializer.TryDeserialize(QueryCacheSerializer.Serialize(Result.Success(), expires), out Result? plain, out _));
        Assert.True(plain!.IsSuccess);
        Assert.True(QueryCacheSerializer.TryDeserialize(QueryCacheSerializer.Serialize(Result.Failure(error), expires), out plain, out _));
        Assert.Equal(error, Assert.Single(plain!.Errors));

        Assert.False(QueryCacheSerializer.TryDeserialize<Result<int>>(Encoding.UTF8.GetBytes("not json"), out _, out _));
        Assert.False(QueryCacheSerializer.TryDeserialize<Result<int>>(Encoding.UTF8.GetBytes("null"), out _, out _));
        Assert.False(QueryCacheSerializer.TryDeserialize<Result<int>>(Encoding.UTF8.GetBytes(
            """{"v":2,"expiresAtUtc":"2026-11-20T00:00:00Z","value":{"isSuccess":true,"value":1}}"""), out _, out _));
        Assert.False(QueryCacheSerializer.TryDeserialize<Result<int>>(Encoding.UTF8.GetBytes(
            """{"v":1,"expiresAtUtc":"2026-11-20T00:00:00Z","value":{"isSuccess":true,"value":1,"extra":true}}"""), out _, out _));
        Assert.False(QueryCacheSerializer.TryDeserialize<Result<int>>(Encoding.UTF8.GetBytes(
            """{"v":1,"expiresAtUtc":"2026-11-20T00:00:00Z","value":{"value":1}}"""), out _, out _));
        Assert.False(QueryCacheSerializer.TryDeserialize<Result<Label>>(Encoding.UTF8.GetBytes(
            """{"v":1,"expiresAtUtc":"2026-11-20T00:00:00Z","value":{"isSuccess":true}}"""), out _, out _));
        Assert.False(QueryCacheSerializer.TryDeserialize<Result<int>>(Encoding.UTF8.GetBytes(
            """{"v":1,"expiresAtUtc":"2026-11-20T00:00:00Z","value":{"isSuccess":false}}"""), out _, out _));
        Assert.False(QueryCacheSerializer.TryDeserialize<Result>(Encoding.UTF8.GetBytes(
            """{"v":1,"expiresAtUtc":"2026-11-20T00:00:00Z","value":{"isSuccess":false}}"""), out _, out _));
        Assert.False(QueryCacheSerializer.TryDeserialize<Result>(Encoding.UTF8.GetBytes(
            """{"v":1,"expiresAtUtc":"2026-11-20T00:00:00Z","value":{}}"""), out _, out _));
        Assert.False(QueryCacheSerializer.TryDeserialize<Result>(Encoding.UTF8.GetBytes(
            """{"v":1,"expiresAtUtc":"2026-11-20T00:00:00Z","value":[]}"""), out _, out _));
        Assert.False(QueryCacheSerializer.TryDeserialize<Result<Label>>(Encoding.UTF8.GetBytes(
            """{"v":1,"expiresAtUtc":"2026-11-20T00:00:00Z","value":{"isSuccess":true,"value":{"name":"bag","material":null}}}"""), out _, out _));
        Assert.False(QueryCacheSerializer.TryDeserialize<Result<Analysis>>(Encoding.UTF8.GetBytes(
            """{"v":1,"expiresAtUtc":"2026-11-20T00:00:00Z","value":{"isSuccess":true,"value":{"labels":null,"uncertainties":[]}}}"""), out _, out _));
    }

    [Fact]
    public async Task UnavailableStoreRunsTheHandlerOncePerRequestAndSkipsTheWrite()
    {
        FailingStore store = new(failReads: true, failWrites: true);
        RecordingLogger logger = new();
        using DistributedQueryCache cache = Create(store, logger);
        int calls = 0;
        for (int request = 0; request < 2; request++)
        {
            Result<string> result = await cache.GetOrCreateAsync("down", _lifetime,
                _ => Task.FromResult(Result<string>.Success($"fresh-{++calls}")), value => value.IsSuccess, CancellationToken.None);
            Assert.Equal($"fresh-{calls}", result.Value);
        }

        Assert.Equal(2, calls);
        Assert.Equal(2, store.Reads);
        Assert.Equal(0, store.Writes);
        Assert.Equal(2, logger.Messages.Count(message => message.Contains("read failed with InvalidOperationException", StringComparison.Ordinal)));
    }

    [Fact]
    public async Task WriteFailuresReturnTheSuccessfulResultAndReadsRecoverLater()
    {
        FailingStore store = new(failReads: false, failWrites: true);
        RecordingLogger logger = new();
        using DistributedQueryCache cache = Create(store, logger);
        int calls = 0;
        Task<Result<string>> Next(CancellationToken _) => Task.FromResult(Result<string>.Success($"value-{++calls}"));
        Assert.Equal("value-1", (await cache.GetOrCreateAsync("key", _lifetime, Next, value => value.IsSuccess, CancellationToken.None)).Value);
        Assert.Contains(logger.Messages, message => message.Contains("write failed with InvalidOperationException", StringComparison.Ordinal));
        store.FailWrites = false;
        Assert.Equal("value-2", (await cache.GetOrCreateAsync("key", _lifetime, Next, value => value.IsSuccess, CancellationToken.None)).Value);
        Assert.Equal("value-2", (await cache.GetOrCreateAsync("key", _lifetime, Next, value => value.IsSuccess, CancellationToken.None)).Value);
        Assert.Equal(2, calls);
    }

    [Fact]
    public async Task UnreadableEntriesRunTheHandlerOnceAndAreReplaced()
    {
        RecordingStore store = new();
        RecordingLogger logger = new();
        using DistributedQueryCache cache = Create(store, logger);
        await store.SetAsync("smartlost:tests:testing:key", Encoding.UTF8.GetBytes("{corrupt"), new DistributedCacheEntryOptions());
        int calls = 0;
        Task<Result<string>> Next(CancellationToken _) => Task.FromResult(Result<string>.Success($"value-{++calls}"));
        Assert.Equal("value-1", (await cache.GetOrCreateAsync("key", _lifetime, Next, value => value.IsSuccess, CancellationToken.None)).Value);
        Assert.Equal("value-1", (await cache.GetOrCreateAsync("key", _lifetime, Next, value => value.IsSuccess, CancellationToken.None)).Value);
        Assert.Equal(1, calls);
        Assert.Contains(logger.Messages, message => message.Contains("could not be read", StringComparison.Ordinal));
        Assert.Equal(_lifetime, store.LastOptions!.AbsoluteExpirationRelativeToNow);
        Assert.Null(store.LastOptions.SlidingExpiration);
    }

    [Fact]
    public async Task CancellationDuringAStoreCallPropagatesWithoutRunningTheHandler()
    {
        BlockingStore store = new();
        using DistributedQueryCache cache = Create(store, new RecordingLogger());
        using CancellationTokenSource cancellation = new();
        int calls = 0;
        Task<Result<string>> pending = cache.GetOrCreateAsync("key", _lifetime,
            _ => Task.FromResult(Result<string>.Success($"{++calls}")), value => value.IsSuccess, cancellation.Token);
        await store.Entered.WaitAsync(TimeSpan.FromSeconds(10));
        await cancellation.CancelAsync();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => pending);
        Assert.Equal(0, calls);

        // A store timeout surfacing as OperationCanceledException without caller cancellation is a store failure.
        FailingStore timeouts = new(failReads: true, failWrites: false)
        {
            Failure = new TaskCanceledException("store timeout")
        };
        using DistributedQueryCache degraded = Create(timeouts, new RecordingLogger());
        Assert.Equal("ok", (await degraded.GetOrCreateAsync("key", _lifetime,
            _ => Task.FromResult(Result<string>.Success("ok")), value => value.IsSuccess, CancellationToken.None)).Value);
    }

    [Fact]
    public void RegistrationScopesKeysAndSelectsRedisOnlyWhenConfigured()
    {
        Assert.Equal("smartlost:listings:development:", QueryCacheScope.For("Listings", "Development").KeyPrefix);
        Assert.Equal("smartlost:listings:development:key", QueryCacheScope.For("listings", "development").Apply("key"));
        Assert.Throws<ArgumentException>(() => QueryCacheScope.For("a:b", "Development"));
        Assert.Throws<ArgumentException>(() => QueryCacheScope.For("listings", " "));
        Assert.Throws<ArgumentException>(() => RedisQueryCacheStore.Create(" "));
        using RedisCache redis = RedisQueryCacheStore.Create("redis:6379,password=test-only-placeholder");
        Assert.NotNull(redis);
        StackExchange.Redis.ConfigurationOptions defaults = RedisQueryCacheStore.CreateOptions("redis:6379");
        Assert.False(defaults.AbortOnConnectFail);
        Assert.Same(StackExchange.Redis.BacklogPolicy.FailFast, defaults.BacklogPolicy);
        Assert.Equal((2000, 1000, 1000), (defaults.ConnectTimeout, defaults.AsyncTimeout, defaults.SyncTimeout));
        StackExchange.Redis.ConfigurationOptions explicitOptions = RedisQueryCacheStore.CreateOptions(
            "redis:6379, connectTimeout=4000,asyncTimeout=3000,SYNCTIMEOUT=2500");
        Assert.Equal((4000, 3000, 2500), (explicitOptions.ConnectTimeout, explicitOptions.AsyncTimeout, explicitOptions.SyncTimeout));

        RecordingLogger local = new();
        using (ServiceProvider provider = Build([], local))
        {
            IQueryCache cache = provider.GetRequiredService<IQueryCache>();
            Assert.Same(cache, provider.GetRequiredService<IQueryCache>());
        }

        Assert.Contains(local.Messages, message => message == "Query cache uses the process-local store.");
        RecordingLogger remote = new();
        using (ServiceProvider provider = Build(new Dictionary<string, string?>
        {
            ["ConnectionStrings:Redis"] = "127.0.0.1:1,connectTimeout=100"
        }, remote, new HostEnvironment()))
        {
            Assert.IsType<DistributedQueryCache>(provider.GetRequiredService<IQueryCache>());
        }

        Assert.Contains(remote.Messages, message => message == "Query cache uses the Redis store.");
        IServiceCollection services = new ServiceCollection();
        IConfiguration empty = new ConfigurationBuilder().Build();
        Assert.Throws<ArgumentNullException>(() => services.AddBuildingBlocksQueryCache(null!, "tests"));
        Assert.Throws<ArgumentException>(() => services.AddBuildingBlocksQueryCache(empty, " "));
        Assert.Throws<ArgumentNullException>(() => ((IServiceCollection)null!).AddBuildingBlocksQueryCache(empty, "tests"));
    }

    [Fact]
    public void ConstructorRejectsMissingDependencies()
    {
        MemoryDistributedCache store = new(Options.Create(new MemoryDistributedCacheOptions()));
        var scope = QueryCacheScope.For("tests", "Testing");
        RecordingLogger logger = new();
        Assert.Throws<ArgumentNullException>(() => new DistributedQueryCache(null!, scope, TimeProvider.System, logger, "x"));
        Assert.Throws<ArgumentNullException>(() => new DistributedQueryCache(store, null!, TimeProvider.System, logger, "x"));
        Assert.Throws<ArgumentNullException>(() => new DistributedQueryCache(store, scope, null!, logger, "x"));
        Assert.Throws<ArgumentNullException>(() => new DistributedQueryCache(store, scope, TimeProvider.System, null!, "x"));
        Assert.Throws<ArgumentException>(() => new DistributedQueryCache(store, scope, TimeProvider.System, logger, " "));
        Assert.Throws<ArgumentNullException>(() => QueryCacheSerializer.Serialize<string>(null!, DateTimeOffset.UtcNow));
        Assert.Throws<ArgumentNullException>(() => QueryCacheSerializer.TryDeserialize<string>(null!, out _, out _));
        Assert.Throws<ArgumentNullException>(() => new ResultJsonConverterFactory().CanConvert(null!));
        Assert.Throws<ArgumentNullException>(() => new ReadOnlyListJsonConverterFactory().CanConvert(null!));
    }

    private static DistributedQueryCache Create(IDistributedCache store, RecordingLogger logger)
    {
        return new DistributedQueryCache(store, QueryCacheScope.For("tests", "Testing"), TimeProvider.System, logger, "test");
    }

    private static ServiceProvider Build(Dictionary<string, string?> settings, RecordingLogger logger, IHostEnvironment? environment = null)
    {
        ServiceCollection services = new();
        services.AddLogging(builder => builder.AddProvider(new RecordingLoggerProvider(logger)));
        if (environment is not null)
        {
            services.AddSingleton(environment);
        }

        services.AddBuildingBlocksQueryCache(new ConfigurationBuilder().AddInMemoryCollection(settings).Build(), "tests");
        return services.BuildServiceProvider();
    }

    public sealed record Label(string Name, IReadOnlyList<string> Colors, string? Material);

    public sealed record Analysis(IReadOnlyList<Label> Labels, IReadOnlyList<string> Uncertainties);

    private class RecordingStore : IDistributedCache
    {
        private readonly MemoryDistributedCache _inner = new(Options.Create(new MemoryDistributedCacheOptions()));

        internal List<string> Keys { get; } = [];
        internal DistributedCacheEntryOptions? LastOptions
        {
            get; private set;
        }

        public virtual byte[]? Get(string key)
        {
            return _inner.Get(key);
        }

        public virtual Task<byte[]?> GetAsync(string key, CancellationToken token = default)
        {
            return _inner.GetAsync(key, token);
        }

        public void Refresh(string key)
        {
            _inner.Refresh(key);
        }

        public Task RefreshAsync(string key, CancellationToken token = default)
        {
            return _inner.RefreshAsync(key, token);
        }

        public void Remove(string key)
        {
            _inner.Remove(key);
        }

        public Task RemoveAsync(string key, CancellationToken token = default)
        {
            return _inner.RemoveAsync(key, token);
        }

        public void Set(string key, byte[] value, DistributedCacheEntryOptions options)
        {
            _inner.Set(key, value, options);
        }

        public virtual Task SetAsync(string key, byte[] value, DistributedCacheEntryOptions options, CancellationToken token = default)
        {
            Keys.Add(key);
            LastOptions = options;
            return _inner.SetAsync(key, value, options, token);
        }
    }

    private sealed class FailingStore(bool failReads, bool failWrites) : RecordingStore
    {
        internal bool FailWrites { get; set; } = failWrites;
        internal Exception Failure { get; init; } = new InvalidOperationException("simulated outage");
        internal int Reads
        {
            get; private set;
        }

        internal int Writes
        {
            get; private set;
        }

        public override Task<byte[]?> GetAsync(string key, CancellationToken token = default)
        {
            Reads++;
            return failReads ? Task.FromException<byte[]?>(Failure) : base.GetAsync(key, token);
        }

        public override Task SetAsync(string key, byte[] value, DistributedCacheEntryOptions options, CancellationToken token = default)
        {
            Writes++;
            return FailWrites ? Task.FromException(new InvalidOperationException("simulated write outage")) : base.SetAsync(key, value, options, token);
        }
    }

    private sealed class BlockingStore : RecordingStore
    {
        private readonly TaskCompletionSource _entered = new(TaskCreationOptions.RunContinuationsAsynchronously);

        internal Task Entered => _entered.Task;

        public override Task<byte[]?> GetAsync(string key, CancellationToken token = default)
        {
            _entered.TrySetResult();
            return new TaskCompletionSource<byte[]?>().Task;
        }
    }

    private sealed class HostEnvironment : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = "Development";
        public string ApplicationName { get; set; } = "tests";
        public string ContentRootPath { get; set; } = AppContext.BaseDirectory;
        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
    }

    private sealed class RecordingLogger : ILogger<DistributedQueryCache>
    {
        private readonly List<string> _messages = [];

        internal IReadOnlyList<string> Messages
        {
            get
            {
                lock (_messages)
                {
                    return [.. _messages];
                }
            }
        }

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull
        {
            return null;
        }

        public bool IsEnabled(LogLevel logLevel)
        {
            return true;
        }

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            lock (_messages)
            {
                _messages.Add(formatter(state, exception));
            }
        }
    }

    private sealed class RecordingLoggerProvider(RecordingLogger logger) : ILoggerProvider
    {
        public ILogger CreateLogger(string categoryName)
        {
            return categoryName.EndsWith(nameof(DistributedQueryCache), StringComparison.Ordinal)
            ? logger : Microsoft.Extensions.Logging.Abstractions.NullLogger.Instance;
        }

        public void Dispose()
        {
        }
    }
}
