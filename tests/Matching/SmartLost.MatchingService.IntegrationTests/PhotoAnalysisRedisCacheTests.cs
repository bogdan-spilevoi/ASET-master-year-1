using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using SmartLost.MatchingService.Application.Contracts;
using SmartLost.MatchingService.Application.Interfaces;
using SmartLost.MatchingService.Domain.ValueObjects;
using StackExchange.Redis;
using Testcontainers.Redis;
using Xunit;

namespace SmartLost.MatchingService.IntegrationTests;

public sealed class PhotoAnalysisRedisCacheTests
{
    [Fact]
    public async Task SecondInstanceReusesNestedLabelsFromRedisButStillChecksPhotoAccess()
    {
        using CancellationTokenSource timeout = new(TimeSpan.FromMinutes(2));
        await using RedisContainer redis = new RedisBuilder("redis:8.8.4-alpine").Build();
        await redis.StartAsync(timeout.Token);
        SharedPhotos photos = new();
        CountingAnalyzer analyzer = new();
        await using ApiFactory first = new(redis.GetConnectionString(), photos, analyzer);
        await using ApiFactory second = new(redis.GetConnectionString(), photos, analyzer);
        using HttpClient firstClient = first.CreateClient();
        using HttpClient secondClient = second.CreateClient();
        var request = new
        {
            listingId = Guid.NewGuid(),
            photoId = Guid.NewGuid(),
            photoRevision = 1
        };

        using HttpResponseMessage populated = await firstClient.PostAsJsonAsync("/api/matching/photo-analysis", request, timeout.Token);
        Assert.Equal(HttpStatusCode.OK, populated.StatusCode);
        string expected = await populated.Content.ReadAsStringAsync(timeout.Token);
        Assert.Contains("\"colors\":[\"black\",\"grey\"]", expected, StringComparison.Ordinal);
        Assert.Equal(1, analyzer.Calls);
        Assert.Equal(1, photos.Reads);

        using HttpResponseMessage hit = await secondClient.PostAsJsonAsync("/api/matching/photo-analysis", request, timeout.Token);
        Assert.Equal(HttpStatusCode.OK, hit.StatusCode);
        Assert.Equal(expected, await hit.Content.ReadAsStringAsync(timeout.Token));
        Assert.Equal(1, analyzer.Calls);
        Assert.Equal(2, photos.Reads);

        // Revoked access still returns 404 although the labels remain cached in Redis.
        photos.Allowed = false;
        using HttpResponseMessage denied = await secondClient.PostAsJsonAsync("/api/matching/photo-analysis", request, timeout.Token);
        Assert.Equal(HttpStatusCode.NotFound, denied.StatusCode);
        Assert.Equal(3, photos.Reads);
        Assert.Equal(1, analyzer.Calls);

        await using ConnectionMultiplexer connection = await ConnectionMultiplexer.ConnectAsync(redis.GetConnectionString());
        RedisKey key = Assert.Single(connection.GetServer(connection.GetEndPoints()[0])
            .Keys(pattern: "smartlost:matching:development:matching:photo-analysis:v1:*"));
        Assert.InRange((await connection.GetDatabase().KeyTimeToLiveAsync(key))!.Value,
            TimeSpan.FromSeconds(290), TimeSpan.FromSeconds(300));
    }

    private sealed class ApiFactory(string redisConnection, SharedPhotos photos, CountingAnalyzer analyzer)
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
                services.RemoveAll<IListingPhotoReader>();
                services.AddSingleton<IListingPhotoReader>(photos);
                services.RemoveAll<IPhotoAnalyzer>();
                services.AddSingleton<IPhotoAnalyzer>(analyzer);
            });
        }

        private Dictionary<string, string?> Settings()
        {
            return new Dictionary<string, string?>
            {
                ["ConnectionStrings:MatchingDatabase"] = "Host=unused;Database=unused",
                ["ConnectionStrings:Redis"] = redisConnection,
                ["MockAdapters:Enabled"] = "true"
            };
        }
    }

    private sealed class SharedPhotos : IListingPhotoReader
    {
        private int _reads;

        public bool Allowed { get; set; } = true;
        public int Reads => Volatile.Read(ref _reads);

        public Task<PhotoContent?> ReadAsync(Guid listingId, PhotoReference photo, CancellationToken cancellationToken)
        {
            Interlocked.Increment(ref _reads);
            return Task.FromResult(Allowed ? new PhotoContent(new byte[] { 1, 2, 3 }, "image/jpeg") : null);
        }
    }

    private sealed class CountingAnalyzer : IPhotoAnalyzer
    {
        private int _calls;

        public int Calls => Volatile.Read(ref _calls);

        public Task<PhotoAnalysis> AnalyzeAsync(PhotoContent photo, CancellationToken cancellationToken)
        {
            Interlocked.Increment(ref _calls);
            return Task.FromResult(new PhotoAnalysis(
                [new DetectedObject("containers", "backpack", ["black", "grey"], "nylon", null, null, ["campus"], ["zipper"], ["bag"])],
                ["strap partly hidden"]));
        }
    }
}
