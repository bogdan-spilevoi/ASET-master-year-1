using System.Net;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using SmartLost.ListingsService.Api;
using SmartLost.ListingsService.Domain.Enums;
using SmartLost.ListingsService.Infrastructure.Persistence;
using Testcontainers.PostgreSql;
using Xunit;

namespace SmartLost.ListingsService.IntegrationTests;

public sealed class ListingsApiIntegrationTests
{
    [Fact]
    public async Task PublicHttpQueriesUseMigratedPostgreSqlAndDevelopmentFixtures()
    {
        await using PostgreSqlContainer database = new PostgreSqlBuilder("postgres:16-alpine")
            .WithDatabase("listings_api_tests").WithUsername("listings_tests")
            .WithPassword("test-only-database-password").Build();
        using CancellationTokenSource timeout = new(TimeSpan.FromMinutes(2));
        await database.StartAsync(timeout.Token);
        await using (ListingsDbContext migration = new(new DbContextOptionsBuilder<ListingsDbContext>()
            .UseNpgsql(database.GetConnectionString()).Options))
        {
            await migration.Database.MigrateAsync(timeout.Token);
        }

        await using ApiFactory factory = new(database.GetConnectionString());
        using HttpClient client = factory.CreateClient();
        using JsonDocument categories = await GetJsonAsync(client, "/api/categories");
        Assert.Equal(3, categories.RootElement.GetArrayLength());
        Guid backpack = categories.RootElement.EnumerateArray().Single(value => value.GetProperty("name").GetString() == "backpack").GetProperty("id").GetGuid();
        using JsonDocument listings = await GetJsonAsync(client, "/api/listings");
        Assert.Equal(4, listings.RootElement.GetProperty("totalCount").GetInt32());
        using JsonDocument filtered = await GetJsonAsync(client, $"/api/listings?type=Lost&categoryId={backpack}&pageSize=1");
        JsonElement listing = filtered.RootElement.GetProperty("items")[0];
        Assert.Equal(1, filtered.RootElement.GetProperty("totalCount").GetInt32());
        Assert.Equal("Lost", listing.GetProperty("type").GetString());
        using JsonDocument details = await GetJsonAsync(client, "/api/listings/" + listing.GetProperty("id").GetString());
        Assert.Equal("image/jpeg", details.RootElement.GetProperty("photo").GetProperty("format").GetString());
        Assert.Equal(DateTimeKind.Utc, details.RootElement.GetProperty("publishedAt").GetDateTime().Kind);
        Assert.DoesNotContain("blobKey", details.RootElement.GetRawText(), StringComparison.OrdinalIgnoreCase);
        using IServiceScope scope = factory.Services.CreateScope();
        ListingsDbContext context = scope.ServiceProvider.GetRequiredService<ListingsDbContext>();
        Assert.Equal("listings_api_tests", context.Database.GetDbConnection().Database);
        Guid[] hidden = await context.Listings.Where(value => value.State != ListingState.Open).Select(value => value.Id).ToArrayAsync(timeout.Token);
        Assert.Equal(2, hidden.Length);
        foreach (Guid id in hidden.Append(Guid.NewGuid()))
        {
            using HttpResponseMessage response = await client.GetAsync($"/api/listings/{id}", timeout.Token);
            Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        }

        using HttpResponseMessage invalid = await client.GetAsync("/api/listings?pageSize=101", timeout.Token);
        Assert.Equal(HttpStatusCode.BadRequest, invalid.StatusCode);
    }

    private static async Task<JsonDocument> GetJsonAsync(HttpClient client, string url)
    {
        using HttpResponseMessage response = await client.GetAsync(url);
        response.EnsureSuccessStatusCode();
        return JsonDocument.Parse(await response.Content.ReadAsStringAsync());
    }

    private sealed class ApiFactory(string connectionString) : WebApplicationFactory<Program>
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
                services.AddDbContext<ListingsDbContext>(options => options.UseNpgsql(connectionString));
            });
        }

        private Dictionary<string, string?> Settings()
        {
            return new Dictionary<string, string?>
            {
                ["ConnectionStrings:ListingsDatabase"] = connectionString,
                ["SeedData:Enabled"] = "true"
            };
        }
    }
}
