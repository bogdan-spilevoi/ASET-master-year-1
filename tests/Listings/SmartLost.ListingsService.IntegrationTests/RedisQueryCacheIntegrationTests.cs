using System.Data.Common;
using System.Diagnostics;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Caching.StackExchangeRedis;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using SmartLost.BuildingBlocks.Infrastructure.Caching;
using SmartLost.ListingsService.Api;
using SmartLost.ListingsService.Domain.Entities;
using SmartLost.ListingsService.Infrastructure.Persistence;
using StackExchange.Redis;
using Testcontainers.PostgreSql;
using Testcontainers.Redis;
using Xunit;

namespace SmartLost.ListingsService.IntegrationTests;

public sealed class RedisQueryCacheIntegrationTests
{
    private const string RedisImage = "redis:8.8.4-alpine";
    private const string CategoryKeyPrefix = "smartlost:listings:development:listings:categories:v1:";

    [Fact]
    public async Task SecondInstanceReadsEmptyAndPopulatedCategoriesFromRedisWithoutQueryingTheDatabase()
    {
        using CancellationTokenSource timeout = new(TimeSpan.FromMinutes(3));
        await using PostgreSqlContainer database = await StartDatabaseAsync(timeout.Token);
        await using RedisContainer redis = new RedisBuilder(RedisImage).Build();
        await redis.StartAsync(timeout.Token);
        CategoryReads firstReads = new();
        CategoryReads secondReads = new();
        await using ApiFactory first = new(database.GetConnectionString(), redis.GetConnectionString(), firstReads);
        await using ApiFactory second = new(database.GetConnectionString(), redis.GetConnectionString(), secondReads);
        using HttpClient firstClient = first.CreateClient();
        using HttpClient secondClient = second.CreateClient();

        Assert.Equal("[]", await GetCategoriesAsync(firstClient));
        Assert.Equal(1, firstReads.Count);
        Assert.Equal("[]", await GetCategoriesAsync(secondClient));
        Assert.Equal(0, secondReads.Count);

        await using ConnectionMultiplexer connection = await ConnectionMultiplexer.ConnectAsync(redis.GetConnectionString());
        IDatabase store = connection.GetDatabase();
        RedisKey key = Assert.Single(connection.GetServer(connection.GetEndPoints()[0]).Keys(pattern: CategoryKeyPrefix + "*"));
        TimeSpan? ttl = await store.KeyTimeToLiveAsync(key);
        Assert.InRange(ttl!.Value, TimeSpan.FromSeconds(290), TimeSpan.FromSeconds(300));

        await using (ListingsDbContext context = CreateContext(database.GetConnectionString()))
        {
            context.Categories.AddRange(Category.Create("charger"), Category.Create("backpack"));
            await context.SaveChangesAsync(timeout.Token);
        }

        // The cached empty list stays authoritative until the absolute TTL; the new rows are not read.
        Assert.Equal("[]", await GetCategoriesAsync(secondClient));
        Assert.Equal(0, secondReads.Count);

        // An unreadable entry runs the handler exactly once and is replaced by a readable one.
        using (RedisCache raw = RedisQueryCacheStore.Create(redis.GetConnectionString()))
        {
            await raw.SetAsync(key.ToString(), Encoding.UTF8.GetBytes("{not a cache entry"),
                new DistributedCacheEntryOptions { AbsoluteExpirationRelativeToNow = TimeSpan.FromMinutes(5) }, timeout.Token);
        }

        string populated = await GetCategoriesAsync(firstClient);
        Assert.Equal(2, firstReads.Count);
        using (var categories = JsonDocument.Parse(populated))
        {
            Assert.Equal(["backpack", "charger"], categories.RootElement.EnumerateArray()
                .Select(category => category.GetProperty("name").GetString()));
        }

        Assert.Equal(populated, await GetCategoriesAsync(secondClient));
        Assert.Equal(0, secondReads.Count);
        Assert.Equal(2, firstReads.Count);
    }

    [Fact]
    public async Task RedisExpiresEntriesAtTheAbsoluteLifetime()
    {
        using CancellationTokenSource timeout = new(TimeSpan.FromMinutes(2));
        await using RedisContainer redis = new RedisBuilder(RedisImage).Build();
        await redis.StartAsync(timeout.Token);
        // A frozen clock never expires the envelope, so a second population proves Redis removed the key.
        using DistributedQueryCache cache = new(RedisQueryCacheStore.Create(redis.GetConnectionString()),
            QueryCacheScope.For("listings", "Testing"), new FrozenClock(), NullLogger<DistributedQueryCache>.Instance, "Redis", ownsStore: true);
        await using ConnectionMultiplexer connection = await ConnectionMultiplexer.ConnectAsync(redis.GetConnectionString());
        int calls = 0;
        Task<string> Factory(CancellationToken _)
        {
            return Task.FromResult($"value-{++calls}");
        }

        Assert.Equal("value-1", await cache.GetOrCreateAsync("expiry", TimeSpan.FromSeconds(1), Factory, _ => true, timeout.Token));
        Assert.Equal("value-1", await cache.GetOrCreateAsync("expiry", TimeSpan.FromSeconds(1), Factory, _ => true, timeout.Token));
        Assert.True(await connection.GetDatabase().KeyExistsAsync("smartlost:listings:testing:expiry"));
        var elapsed = Stopwatch.StartNew();
        while (await connection.GetDatabase().KeyExistsAsync("smartlost:listings:testing:expiry"))
        {
            Assert.True(elapsed.Elapsed < TimeSpan.FromSeconds(10), "Redis did not expire the entry.");
            await Task.Delay(100, timeout.Token);
        }

        Assert.Equal("value-2", await cache.GetOrCreateAsync("expiry", TimeSpan.FromSeconds(1), Factory, _ => true, timeout.Token));
        Assert.Equal(2, calls);
    }

    [Fact]
    public async Task UnavailableRedisRunsTheHandlerOncePerRequestAndStillSucceeds()
    {
        using CancellationTokenSource timeout = new(TimeSpan.FromMinutes(3));
        await using PostgreSqlContainer database = await StartDatabaseAsync(timeout.Token);
        CategoryReads neverReads = new();
        // Nothing listens on port 1: Redis is unreachable from the first request.
        await using (ApiFactory unreachable = new(database.GetConnectionString(), "127.0.0.1:1,connectTimeout=1000", neverReads))
        {
            using HttpClient client = unreachable.CreateClient();
            var elapsed = Stopwatch.StartNew();
            for (int request = 1; request <= 3; request++)
            {
                Assert.Equal("[]", await GetCategoriesAsync(client));
                Assert.Equal(request, neverReads.Count);
            }

            Assert.True(elapsed.Elapsed < TimeSpan.FromSeconds(15), $"Degraded requests took {elapsed.Elapsed}.");
        }

        // Redis stops after an entry was populated: later requests read PostgreSQL once each.
        await using RedisContainer redis = new RedisBuilder(RedisImage).Build();
        await redis.StartAsync(timeout.Token);
        CategoryReads reads = new();
        await using ApiFactory factory = new(database.GetConnectionString(), redis.GetConnectionString(), reads);
        using HttpClient cached = factory.CreateClient();
        await GetCategoriesAsync(cached);
        await GetCategoriesAsync(cached);
        Assert.Equal(1, reads.Count);
        await redis.StopAsync(timeout.Token);
        Assert.Equal("[]", await GetCategoriesAsync(cached));
        Assert.Equal("[]", await GetCategoriesAsync(cached));
        Assert.Equal(3, reads.Count);
    }

    private static async Task<PostgreSqlContainer> StartDatabaseAsync(CancellationToken cancellationToken)
    {
        PostgreSqlContainer database = new PostgreSqlBuilder("postgres:16-alpine")
            .WithDatabase("listings_cache_tests").WithUsername("listings_tests")
            .WithPassword("test-only-database-password").Build();
        await database.StartAsync(cancellationToken);
        await using ListingsDbContext migration = CreateContext(database.GetConnectionString());
        await migration.Database.MigrateAsync(cancellationToken);
        return database;
    }

    private static ListingsDbContext CreateContext(string connectionString)
    {
        return new ListingsDbContext(new DbContextOptionsBuilder<ListingsDbContext>().UseNpgsql(connectionString).Options);
    }

    private static async Task<string> GetCategoriesAsync(HttpClient client)
    {
        using HttpResponseMessage response = await client.GetAsync("/api/categories");
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadAsStringAsync();
    }

    private sealed class ApiFactory(string databaseConnection, string redisConnection, CategoryReads reads)
        : WebApplicationFactory<Program>
    {
        protected override IHost CreateHost(IHostBuilder builder)
        {
            builder.ConfigureHostConfiguration(configuration => configuration.AddInMemoryCollection(Settings()));
            return base.CreateHost(builder);
        }

        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseEnvironment("Development");
            builder.ConfigureAppConfiguration(configuration => configuration.AddInMemoryCollection(Settings()));
            builder.ConfigureTestServices(services =>
            {
                services.RemoveAll<DbContextOptions<ListingsDbContext>>();
                services.RemoveAll<IDbContextOptionsConfiguration<ListingsDbContext>>();
                services.AddDbContext<ListingsDbContext>(options => options.UseNpgsql(databaseConnection).AddInterceptors(reads));
            });
        }

        private Dictionary<string, string?> Settings()
        {
            return new Dictionary<string, string?>
            {
                ["ConnectionStrings:ListingsDatabase"] = databaseConnection,
                ["ConnectionStrings:Redis"] = redisConnection,
                ["SeedData:Enabled"] = "false"
            };
        }
    }

    private sealed class CategoryReads : DbCommandInterceptor
    {
        private int _count;

        internal int Count => Volatile.Read(ref _count);

        public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(DbCommand command,
            CommandEventData eventData, InterceptionResult<DbDataReader> result, CancellationToken cancellationToken = default)
        {
            if (command.CommandText.Contains("FROM \"Categories\"", StringComparison.Ordinal))
            {
                Interlocked.Increment(ref _count);
            }

            return ValueTask.FromResult(result);
        }
    }

    private sealed class FrozenClock : TimeProvider
    {
        private readonly DateTimeOffset _now = DateTimeOffset.UtcNow;

        public override DateTimeOffset GetUtcNow()
        {
            return _now;
        }
    }
}
