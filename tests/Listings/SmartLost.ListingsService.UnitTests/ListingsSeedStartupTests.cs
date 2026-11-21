using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using SmartLost.ListingsService.Api;
using SmartLost.ListingsService.Infrastructure.Persistence;
using Xunit;

namespace SmartLost.ListingsService.UnitTests;

public sealed class ListingsSeedStartupTests
{
    [Theory]
    [InlineData("Development", 6)]
    [InlineData("Production", 0)]
    public async Task SeedOnlyNeverStartsAnHttpServerAndRejectsProduction(string environment, int expectedListings)
    {
        await using SqliteConnection connection = new("Data Source=:memory:");
        await connection.OpenAsync();
        await using WebApplication app = Program.BuildApplication(
            ["--environment", environment, "--applicationName", typeof(Program).Assembly.GetName().Name!,
                "--ConnectionStrings:ListingsDatabase", "Host=unused;Database=unused", "--SeedData:Only", "true"],
            builder =>
            {
                builder.WebHost.UseTestServer();
                builder.Services.RemoveAll<DbContextOptions<ListingsDbContext>>();
                builder.Services.RemoveAll<IDbContextOptionsConfiguration<ListingsDbContext>>();
                builder.Services.AddDbContext<ListingsDbContext>(options => options.UseSqlite(connection));
            });
        bool started = false;
        using CancellationTokenRegistration registration = app.Lifetime.ApplicationStarted.Register(() => started = true);
        await using AsyncServiceScope scope = app.Services.CreateAsyncScope();
        ListingsDbContext context = scope.ServiceProvider.GetRequiredService<ListingsDbContext>();
        await context.Database.EnsureCreatedAsync();
        using CancellationTokenSource timeout = new(TimeSpan.FromSeconds(10));

        // Own the application's lifetime directly: WebApplicationFactory requires a started
        // HTTP host and can race the intentional exit/disposal of a seed-only entry point.
        if (environment == "Production")
        {
            InvalidOperationException error = await Assert.ThrowsAsync<InvalidOperationException>(
                () => Program.RunApplicationAsync(app, timeout.Token));
            Assert.Equal("SeedData:Only is available only in Development.", error.Message);
        }
        else
        {
            await Program.RunApplicationAsync(app, timeout.Token);
        }

        Assert.False(started);
        Assert.Equal(expectedListings, await context.Listings.CountAsync());
        Assert.Equal(expectedListings == 0 ? 0 : 3, await context.Categories.CountAsync());
    }

    [Fact]
    public async Task ProductionEntryPointPreservesTheSeedOnlyRejectionDuringDisposal()
    {
        InvalidOperationException error = await Assert.ThrowsAsync<InvalidOperationException>(() => Program.Main(
            ["--environment", "Production", "--ConnectionStrings:ListingsDatabase", "Host=unused;Database=unused",
                "--SeedData:Only", "true"]));
        Assert.Equal("SeedData:Only is available only in Development.", error.Message);
    }
}
