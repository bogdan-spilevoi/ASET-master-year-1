using System.Net;
using System.Text.Json;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using SmartLost.BuildingBlocks.Core.Pagination;
using SmartLost.BuildingBlocks.Core.Results;
using SmartLost.Contracts.Listings;
using SmartLost.ListingsService.Application.Dtos;
using SmartLost.ListingsService.Application.Interfaces;
using SmartLost.ListingsService.Application.Queries.GetListings;
using SmartLost.ListingsService.Domain.Enums;
using SmartLost.ListingsService.Infrastructure.Persistence;
using SmartLost.ListingsService.Infrastructure.Seeding;
using Xunit;

namespace SmartLost.ListingsService.UnitTests;

public sealed class ListingsQueryFlowTests
{
    [Fact]
    public async Task PublicQueriesReturnOrderedCategoriesAndOnlyOpenListingsWithSafePhotoMetadata()
    {
        using ListingsApiFactory factory = new();
        using HttpClient client = factory.CreateClient();
        using JsonDocument categories = await GetJsonAsync(client, "/api/categories");
        Assert.Equal(["backpack", "keys", "wallet"], categories.RootElement.EnumerateArray().Select(value => value.GetProperty("name").GetString()));
        using JsonDocument listings = await GetJsonAsync(client, "/api/listings");
        JsonElement page = listings.RootElement;
        Assert.Equal(4, page.GetProperty("totalCount").GetInt32());
        Assert.Equal(0, page.GetProperty("currentPage").GetInt32());
        Assert.Equal(10, page.GetProperty("pageSize").GetInt32());
        Assert.Equal(4, page.GetProperty("items").GetArrayLength());
        foreach (JsonElement item in page.GetProperty("items").EnumerateArray())
        {
            Assert.DoesNotContain("keys", item.GetProperty("title").GetString()!, StringComparison.Ordinal);
            using JsonDocument details = await GetJsonAsync(client, "/api/listings/" + item.GetProperty("id").GetString());
            Assert.Equal(item.GetProperty("id").GetGuid(), details.RootElement.GetProperty("id").GetGuid());
            Assert.True(details.RootElement.GetProperty("revision").GetInt32() > 0);
            Assert.Single(details.RootElement.GetProperty("traits").EnumerateArray());
            if (item.GetProperty("photo").ValueKind != JsonValueKind.Null)
            {
                Assert.Equal("image/jpeg", item.GetProperty("photo").GetProperty("format").GetString());
                Assert.Equal(1024, item.GetProperty("photo").GetProperty("byteSize").GetInt64());
                Assert.Equal(1, item.GetProperty("photo").GetProperty("revision").GetInt32());
                Assert.NotEqual(Guid.Empty, item.GetProperty("photo").GetProperty("id").GetGuid());
            }

            Assert.DoesNotContain("blobKey", item.GetRawText(), StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("demo/metadata-only", details.RootElement.GetRawText(), StringComparison.Ordinal);
        }

        using IServiceScope scope = factory.Services.CreateScope();
        IListingsReadStore readStore = scope.ServiceProvider.GetRequiredService<IListingsReadStore>();
        await readStore.GetCategoriesAsync(CancellationToken.None);
        await readStore.GetOpenListingsAsync(null, null, 0, 10, CancellationToken.None);
        await readStore.GetOpenListingAsync(page.GetProperty("items")[0].GetProperty("id").GetGuid(), CancellationToken.None);
        Assert.Empty(scope.ServiceProvider.GetRequiredService<ListingsDbContext>().ChangeTracker.Entries());
    }

    [Fact]
    public async Task CombinedFiltersPagingAndUnknownCategoriesBehaveConsistently()
    {
        using ListingsApiFactory factory = new();
        using HttpClient client = factory.CreateClient();
        using JsonDocument categories = await GetJsonAsync(client, "/api/categories");
        Guid backpack = categories.RootElement.EnumerateArray().Single(value => value.GetProperty("name").GetString() == "backpack").GetProperty("id").GetGuid();
        using JsonDocument filtered = await GetJsonAsync(client, $"/api/listings?type=Found&categoryId={backpack}");
        Assert.Equal(1, filtered.RootElement.GetProperty("totalCount").GetInt32());
        Assert.Equal("Found", filtered.RootElement.GetProperty("items")[0].GetProperty("type").GetString());
        HashSet<Guid> ids = [];
        for (int page = 0; page < 2; page++)
        {
            using JsonDocument result = await GetJsonAsync(client, $"/api/listings?pageIndex={page}&pageSize=2");
            Assert.Equal(4, result.RootElement.GetProperty("totalCount").GetInt32());
            Assert.Equal(page, result.RootElement.GetProperty("currentPage").GetInt32());
            foreach (JsonElement item in result.RootElement.GetProperty("items").EnumerateArray())
            {
                Assert.True(ids.Add(item.GetProperty("id").GetGuid()));
            }
        }

        using JsonDocument beyond = await GetJsonAsync(client, "/api/listings?pageIndex=10&pageSize=2");
        Assert.Empty(beyond.RootElement.GetProperty("items").EnumerateArray());
        Assert.Equal(4, beyond.RootElement.GetProperty("totalCount").GetInt32());
        using JsonDocument unknown = await GetJsonAsync(client, $"/api/listings?categoryId={Guid.NewGuid()}");
        Assert.Equal(0, unknown.RootElement.GetProperty("totalCount").GetInt32());
    }

    [Fact]
    public async Task DraftClosedAndMissingDetailsAreIndistinguishableToPublicReaders()
    {
        using ListingsApiFactory factory = new();
        using HttpClient client = factory.CreateClient();
        using IServiceScope scope = factory.Services.CreateScope();
        Guid[] hidden = await scope.ServiceProvider.GetRequiredService<ListingsDbContext>().Listings
            .Where(value => value.State != ListingState.Open).Select(value => value.Id).ToArrayAsync();
        Assert.Equal(2, hidden.Length);
        foreach (Guid id in hidden.Append(Guid.NewGuid()))
        {
            using HttpResponseMessage response = await client.GetAsync($"/api/listings/{id}");
            Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
            using var problem = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
            Assert.Equal("listings.not_found", problem.RootElement.GetProperty("code").GetString());
        }
    }

    [Theory]
    [InlineData("/api/listings?pageIndex=-1")]
    [InlineData("/api/listings?pageSize=0")]
    [InlineData("/api/listings?pageSize=101")]
    [InlineData("/api/listings?pageIndex=2147483647&pageSize=100")]
    [InlineData("/api/listings?categoryId=00000000-0000-0000-0000-000000000000")]
    [InlineData("/api/listings?categoryId=invalid")]
    [InlineData("/api/listings?type=invalid")]
    [InlineData("/api/listings/00000000-0000-0000-0000-000000000000")]
    public async Task InvalidQueriesReturnBadRequest(string url)
    {
        using ListingsApiFactory factory = new(seed: false);
        using HttpClient client = factory.CreateClient();
        using HttpResponseMessage response = await client.GetAsync(url);
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task MediatorRejectsUndefinedEnumBeforeExecutingTheReadStore()
    {
        using ListingsApiFactory factory = new(seed: false);
        using HttpClient client = factory.CreateClient();
        using IServiceScope scope = factory.Services.CreateScope();
        Result<PagedResult<ListingSummaryDto>> result = await scope.ServiceProvider.GetRequiredService<ISender>()
            .Send(new GetListingsQuery((ListingType)99));
        Assert.True(result.IsFailure);
        Assert.Contains(result.Errors, error => error.Code == "listings.invalid_type");
    }

    [Fact]
    public async Task SeedPreservesExistingRowsAndDoesNotDuplicateCategoriesOrListings()
    {
        using ListingsApiFactory factory = new();
        using HttpClient client = factory.CreateClient();
        using IServiceScope scope = factory.Services.CreateScope();
        ListingsDbContext context = scope.ServiceProvider.GetRequiredService<ListingsDbContext>();
        Guid[] before = await context.Listings.OrderBy(value => value.Id).Select(value => value.Id).ToArrayAsync();
        await scope.ServiceProvider.GetRequiredService<ListingsDevelopmentSeeder>().SeedAsync();
        Assert.Equal(before, await context.Listings.OrderBy(value => value.Id).Select(value => value.Id).ToArrayAsync());
        Assert.Equal(6, before.Length);
        Assert.Equal(3, await context.Categories.CountAsync());
        Assert.Single(await context.Photos.ToListAsync());
    }

    [Theory]
    [InlineData("Development", false, true)]
    [InlineData("Production", true, false)]
    public async Task StartupHonorsDevelopmentSeedAndDocumentationBoundaries(string environment, bool seed, bool documentation)
    {
        using ListingsApiFactory factory = new(environment, seed);
        using HttpClient client = factory.CreateClient(new Microsoft.AspNetCore.Mvc.Testing.WebApplicationFactoryClientOptions
        {
            BaseAddress = new Uri("https://localhost")
        });
        using JsonDocument categories = await GetJsonAsync(client, "/api/categories");
        Assert.Empty(categories.RootElement.EnumerateArray());
        using HttpResponseMessage openApi = await client.GetAsync("/openapi/v1.json");
        Assert.Equal(documentation ? HttpStatusCode.OK : HttpStatusCode.NotFound, openApi.StatusCode);
        using HttpResponseMessage swagger = await client.GetAsync("/swagger/index.html");
        Assert.Equal(documentation ? HttpStatusCode.OK : HttpStatusCode.NotFound, swagger.StatusCode);
        if (documentation)
        {
            using var schema = JsonDocument.Parse(await openApi.Content.ReadAsStringAsync());
            Assert.True(schema.RootElement.GetProperty("paths").TryGetProperty("/api/listings/{id}", out _));
            Assert.Equal("SmartLost Listings API", schema.RootElement.GetProperty("info").GetProperty("title").GetString());
            using HttpResponseMessage swaggerConfiguration = await client.GetAsync("/swagger/index.js");
            swaggerConfiguration.EnsureSuccessStatusCode();
            Assert.Contains("../openapi/v1.json", await swaggerConfiguration.Content.ReadAsStringAsync(), StringComparison.Ordinal);
        }
    }

    [Theory]
    [InlineData("Development", 6)]
    [InlineData("Production", 0)]
    public async Task SeedOnlyNeverStartsAnHttpServerAndRejectsProduction(string environment, int expectedListings)
    {
        using ListingsApiFactory factory = new(environment, seed: false, seedOnly: true);
        // A seed-only entry point exits before starting the host that TestServer requires.
        InvalidOperationException error = Assert.ThrowsAny<InvalidOperationException>(() => factory.CreateClient());
        if (environment == "Production")
        {
            Assert.Contains("SeedData:Only is available only in Development.", error.Message, StringComparison.Ordinal);
        }

        await using ListingsDbContext context = factory.CreateDbContext();
        Assert.Equal(expectedListings, await context.Listings.CountAsync());
        Assert.Equal(expectedListings == 0 ? 0 : 3, await context.Categories.CountAsync());
    }

    private static async Task<JsonDocument> GetJsonAsync(HttpClient client, string url)
    {
        using HttpResponseMessage response = await client.GetAsync(url);
        response.EnsureSuccessStatusCode();
        return JsonDocument.Parse(await response.Content.ReadAsStringAsync());
    }
}
