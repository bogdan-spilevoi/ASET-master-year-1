using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using SmartLost.MatchingService.Api.Diagnostics;
using SmartLost.MatchingService.Application.Contracts;
using SmartLost.MatchingService.Domain.Entities;
using SmartLost.MatchingService.Domain.Suggestions;
using SmartLost.MatchingService.Infrastructure.Demo;
using SmartLost.MatchingService.Infrastructure.Persistence;
using Xunit;

namespace SmartLost.MatchingService.UnitTests.Runtime;

public sealed class MatchingApiTests
{
    private const string Source = "10000000-0000-0000-0000-000000000001";
    private static readonly JsonSerializerOptions _json = new(JsonSerializerDefaults.Web) { Converters = { new JsonStringEnumConverter() } };

    [Fact]
    public async Task SuggestionResultsReevaluateCurrentEligibilityWithCachingBehaviorRegistered()
    {
        using MatchingApiFactory factory = new();
        using HttpClient client = factory.CreateClient();
        using HttpResponseMessage seeded = await client.PostAsync("/api/matching/demo/seed", null);
        seeded.EnsureSuccessStatusCode();
        Assert.Equal(SuggestionStatus.Ready, (await SuggestAsync(client, Source, 1)).Status);
        using (IServiceScope scope = factory.Services.CreateScope())
        {
            MatchingDbContext context = scope.ServiceProvider.GetRequiredService<MatchingDbContext>();
            SearchListing source = await context.SearchListings.SingleAsync(listing => listing.Id == Guid.Parse(Source));
            context.Entry(source).Property(listing => listing.Eligible).CurrentValue = false;
            await context.SaveChangesAsync();
        }

        Assert.Equal(SuggestionStatus.Unavailable, (await SuggestAsync(client, Source, 1)).Status);
    }

    [Fact]
    public async Task DemoFlowRanksWithVisualEvidenceAfterEmbeddingsArePrepared()
    {
        using MatchingApiFactory factory = new();
        using HttpClient client = factory.CreateClient();

        DemoScenario scenario = (await (await client.PostAsync("/api/matching/demo/seed", null)).Content
            .ReadFromJsonAsync<DemoScenario>(_json))!;
        Assert.Equal(Guid.Parse(Source), scenario.SourceListingId);
        Assert.Equal(10, scenario.Listings.Count);

        MatchSuggestionsResponse before = await SuggestAsync(client, Source, 1);
        Assert.Equal(SuggestionStatus.Ready, before.Status);
        Assert.Equal(3, before.Suggestions.Count);
        Assert.All(before.Suggestions, suggestion => Assert.Null(suggestion.VisualSimilarity));

        foreach (string listing in new[] { Source, "20000000-0000-0000-0000-000000000001", "20000000-0000-0000-0000-000000000002" })
        {
            HttpResponseMessage processed = await client.PostAsync($"/api/matching/demo/listings/{listing}/embedding/process", null);
            Assert.Equal(HttpStatusCode.OK, processed.StatusCode);
            Assert.Equal("\"Ready\"", await processed.Content.ReadAsStringAsync());
        }

        MatchSuggestionsResponse after = await SuggestAsync(client, Source, 1);
        MatchSuggestion best = after.Suggestions[0];
        Assert.Equal(Guid.Parse("20000000-0000-0000-0000-000000000001"), best.CandidateListingId);
        Assert.Equal(1, best.VisualSimilarity!.Value, 3);
        Assert.Contains(MatchReason.VisualSimilarity, best.Reasons);
        double partial = after.Suggestions.Single(item => item.CandidateListingId == Guid.Parse("20000000-0000-0000-0000-000000000002"))
            .VisualSimilarity!.Value;
        Assert.InRange(partial, 0.2, 0.8);

        Guid sourceTask = scenario.Listings[0].EmbeddingTaskId!.Value;
        HttpResponseMessage byTask = await client.PostAsync($"/api/matching/embedding-tasks/{sourceTask}/process", null);
        Assert.Equal("\"Ready\"", await byTask.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task ProfilingScenariosAndStatusesMapToHttpResponses()
    {
        using MatchingApiFactory factory = new();
        using HttpClient client = factory.CreateClient();
        await client.PostAsync("/api/matching/demo/seed", null);

        Assert.Equal(HttpStatusCode.OK, (await ProcessAsync(client, "30000000-0000-0000-0000-000000000001")).StatusCode);
        HttpResponseMessage timeout = await ProcessAsync(client, "30000000-0000-0000-0000-000000000002");
        Assert.Equal(HttpStatusCode.ServiceUnavailable, timeout.StatusCode);
        Assert.Contains("matching.inference_timed_out", await timeout.Content.ReadAsStringAsync(), StringComparison.Ordinal);
        HttpResponseMessage error = await ProcessAsync(client, "30000000-0000-0000-0000-000000000003");
        Assert.Equal(HttpStatusCode.ServiceUnavailable, error.StatusCode);
        Assert.Contains("matching.inference_unavailable", await error.Content.ReadAsStringAsync(), StringComparison.Ordinal);
        Assert.Equal(HttpStatusCode.Conflict, (await ProcessAsync(client, "30000000-0000-0000-0000-000000000003")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await ProcessAsync(client, "20000000-0000-0000-0000-000000000003")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await client.PostAsync($"/api/matching/embedding-tasks/{Guid.NewGuid()}/process", null)).StatusCode);

        Assert.Equal(SuggestionStatus.Pending, (await SuggestAsync(client, Source, 2)).Status);
        Assert.Equal(SuggestionStatus.Unavailable, (await SuggestAsync(client, "30000000-0000-0000-0000-000000000001", 1)).Status);
        HttpResponseMessage invalid = await client.PostAsJsonAsync("/api/matching/suggestions", new
        {
            listingId = Guid.Empty,
            expectedSourceRevision = 0
        });
        Assert.Equal(HttpStatusCode.BadRequest, invalid.StatusCode);
        Assert.Contains("matching.listing_id_required", await invalid.Content.ReadAsStringAsync(), StringComparison.Ordinal);

        HttpResponseMessage analysis = await client.PostAsJsonAsync("/api/matching/photo-analysis",
            new
            {
                listingId = Source,
                photoId = "10000000-f0f0-0000-0000-000000000001",
                photoRevision = 1
            });
        PhotoAnalysis labels = (await analysis.Content.ReadFromJsonAsync<PhotoAnalysis>(_json))!;
        Assert.Equal("backpack", Assert.Single(labels.Objects).ObjectType);
        HttpResponseMessage missingPhoto = await client.PostAsJsonAsync("/api/matching/photo-analysis",
            new
            {
                listingId = Source,
                photoId = Guid.NewGuid(),
                photoRevision = 1
            });
        Assert.Equal(HttpStatusCode.NotFound, missingPhoto.StatusCode);
        HttpResponseMessage failedAnalysis = await client.PostAsJsonAsync("/api/matching/photo-analysis",
            new
            {
                listingId = "30000000-0000-0000-0000-000000000003",
                photoId = "30000000-f0f0-0000-0000-000000000003",
                photoRevision = 1
            });
        Assert.Equal(HttpStatusCode.ServiceUnavailable, failedAnalysis.StatusCode);
    }

    [Fact]
    public async Task TestEndpointsAreHiddenOutsideDevelopment()
    {
        using MatchingApiFactory factory = new("Production");
        using HttpClient client = factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
        HttpResponseMessage response = await client.PostAsync("/api/matching/demo/seed", null);
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        HttpResponseMessage suggestions = await client.PostAsJsonAsync("/api/matching/suggestions", new
        {
            listingId = Source,
            expectedSourceRevision = 1
        });
        Assert.Contains(suggestions.StatusCode, new[] { HttpStatusCode.NotFound, HttpStatusCode.TemporaryRedirect, HttpStatusCode.PermanentRedirect });
    }

    [Fact]
    public async Task RecentLogsEndpointReturnsNewestProfilingEntries()
    {
        using MatchingApiFactory factory = new();
        using HttpClient client = factory.CreateClient();
        await client.PostAsync("/api/matching/demo/seed", null);
        await SuggestAsync(client, Source, 1);

        RecentLogEntry[] entries = (await client.GetFromJsonAsync<RecentLogEntry[]>("/api/matching/demo/logs", _json))!;
        Assert.InRange(entries.Length, 1, 10);
        RecentLogEntry latest = entries[0];
        Assert.Equal(1301, latest.EventId);
        Assert.Equal("Information", latest.Level);
        Assert.Contains("GetMatchSuggestionsQuery (api-test)", latest.Message, StringComparison.Ordinal);
        Assert.All(entries, entry => Assert.True(
            entry.Category.StartsWith(RecentLogProvider.CategoryPrefix, StringComparison.Ordinal) ||
            entry.Category.StartsWith(RecentLogProvider.SharedProfilingCategoryPrefix, StringComparison.Ordinal)));
        Assert.Contains(entries, entry => entry.Category.StartsWith(RecentLogProvider.SharedProfilingCategoryPrefix, StringComparison.Ordinal));

        Assert.Single((await client.GetFromJsonAsync<RecentLogEntry[]>("/api/matching/demo/logs?count=1", _json))!);
        Assert.Equal(HttpStatusCode.BadRequest, (await client.GetAsync("/api/matching/demo/logs?count=0")).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await client.GetAsync("/api/matching/demo/logs?count=101")).StatusCode);
    }

    [Fact]
    public void LogBufferKeepsNewestEntriesAndProviderFiltersCategoriesAndLevels()
    {
        var buffer = new RecentLogBuffer();
        for (int index = 0; index < RecentLogBuffer.Capacity + 5; index++)
        {
            buffer.Add(new RecentLogEntry(DateTimeOffset.UtcNow, "Information", "c", index, null, $"m{index}", null));
        }

        IReadOnlyList<RecentLogEntry> latest = buffer.GetLatest(RecentLogBuffer.Capacity + 50);
        Assert.Equal(RecentLogBuffer.Capacity, latest.Count);
        Assert.Equal(RecentLogBuffer.Capacity + 4, latest[0].EventId);
        Assert.Throws<ArgumentOutOfRangeException>(() => buffer.GetLatest(0));

        var empty = new RecentLogBuffer();
        using var provider = new RecentLogProvider(empty, TimeProvider.System);
        Assert.Same(Microsoft.Extensions.Logging.Abstractions.NullLogger.Instance, provider.CreateLogger("Microsoft.AspNetCore"));
        Microsoft.Extensions.Logging.ILogger logger = provider.CreateLogger("SmartLost.MatchingService.Test");
        Assert.Null(logger.BeginScope("scope"));
        logger.Log(Microsoft.Extensions.Logging.LogLevel.Debug, new Microsoft.Extensions.Logging.EventId(1), "skip", null, (state, _) => state);
        logger.Log(Microsoft.Extensions.Logging.LogLevel.Warning, new Microsoft.Extensions.Logging.EventId(7, "Probe"), "kept",
            new InvalidOperationException("secret"), (state, exception) => state + exception?.Message);

        RecentLogEntry entry = Assert.Single(empty.GetLatest(10));
        Assert.Equal(("Warning", 7, "Probe", "kept", nameof(InvalidOperationException)),
            (entry.Level, entry.EventId, entry.EventName, entry.Message, entry.ExceptionType));
    }

    [Theory]
    [InlineData("MockAdapters:Enabled", "false", "MockAdapters:Enabled=true")]
    [InlineData("ConnectionStrings:MatchingDatabase", "", "ConnectionStrings:MatchingDatabase")]
    public void StartupRequiresExplicitMockOptInAndConnectionString(string key, string? value, string expected)
    {
        using MatchingApiFactory factory = new(overrides: new Dictionary<string, string?> { [key] = value });
        InvalidOperationException exception = Assert.Throws<InvalidOperationException>(() => factory.CreateClient());
        Assert.Contains(expected, exception.Message, StringComparison.Ordinal);
    }

    private static async Task<MatchSuggestionsResponse> SuggestAsync(HttpClient client, string listingId, int revision)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/matching/suggestions")
        {
            Content = JsonContent.Create(new { listingId, expectedSourceRevision = revision })
        };
        request.Headers.Add("X-Correlation-ID", "api-test");
        HttpResponseMessage response = await client.SendAsync(request);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<MatchSuggestionsResponse>(_json))!;
    }

    private static Task<HttpResponseMessage> ProcessAsync(HttpClient client, string listingId)
    {
        return client.PostAsync($"/api/matching/demo/listings/{listingId}/embedding/process", null);
    }
}

public sealed class MatchingApiFactory(string environment = "Development", IDictionary<string, string?>? overrides = null)
    : WebApplicationFactory<Program>
{
    private readonly string _database = $"MatchingApiTests-{Guid.NewGuid()}";

    protected override IHost CreateHost(IHostBuilder builder)
    {
        Dictionary<string, string?> settings = new()
        {
            ["ConnectionStrings:MatchingDatabase"] = "Host=localhost;Database=test-only",
            ["MockAdapters:Enabled"] = "true",
            ["MockAdapters:SlowDelay"] = "00:00:00.050",
            ["Matching:Profiling:SlowOperationThreshold"] = "00:00:02",
            ["Matching:Profiling:SlowAiCallThreshold"] = "00:00:00.020",
            ["Matching:Profiling:AiCallTimeout"] = "00:00:00.200"
        };
        foreach (KeyValuePair<string, string?> item in overrides ?? new Dictionary<string, string?>())
        {
            settings[item.Key] = item.Value;
        }

        builder.ConfigureHostConfiguration(configuration => configuration.AddInMemoryCollection(settings));
        return base.CreateHost(builder);
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment(environment);
        builder.ConfigureServices(services =>
        {
            services.RemoveAll<DbContextOptions<MatchingDbContext>>();
            services.RemoveAll<DbContextOptions>();
            services.RemoveAll<IDbContextOptionsConfiguration<MatchingDbContext>>();
            services.AddDbContext<MatchingDbContext>(options => options.UseInMemoryDatabase(_database));
        });
    }
}
