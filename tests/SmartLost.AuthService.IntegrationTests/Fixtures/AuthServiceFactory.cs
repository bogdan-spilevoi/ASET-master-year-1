using System.Net.Http.Headers;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using SmartLost.AuthService.Application.Interfaces;
using SmartLost.AuthService.Domain.Entities;
using SmartLost.AuthService.Infrastructure.Persistence;
using Testcontainers.PostgreSql;
using Xunit;

namespace SmartLost.AuthService.IntegrationTests.Fixtures;

public sealed class AuthServiceFactory : WebApplicationFactory<Program>, IAsyncLifetime
{
    private readonly PostgreSqlContainer _database = new PostgreSqlBuilder("postgres:16-alpine")
        .WithDatabase("authservice_tests")
        .WithUsername("authservice_tests")
        .WithPassword("test-only-database-password")
        .Build();

    public ConcurrentRefreshInterceptor RefreshInterceptor { get; } = new();

    public HttpClient CreateAuthenticatedClient(UserAccount user)
    {
        using IServiceScope scope = Services.CreateScope();
        TokenResult token = scope.ServiceProvider.GetRequiredService<ITokenService>().CreateToken(user);
        HttpClient client = CreateClient(new WebApplicationFactoryClientOptions { BaseAddress = new Uri("https://localhost") });
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token.AccessToken);
        return client;
    }

    public async Task InitializeAsync()
    {
        using CancellationTokenSource timeout = new(TimeSpan.FromMinutes(2));
        try
        {
            await _database.StartAsync(timeout.Token);
            using IServiceScope scope = Services.CreateScope();
            AuthDbContext context = scope.ServiceProvider.GetRequiredService<AuthDbContext>();
            // The connection comes exclusively from this disposable container.
            await context.Database.MigrateAsync(timeout.Token);
        }
        catch
        {
            await DisposeAsync();
            await _database.DisposeAsync();
            throw;
        }
    }

    async Task IAsyncLifetime.DisposeAsync()
    {
        await DisposeAsync();
        await _database.DisposeAsync();
    }

    public async Task ResetAsync()
    {
        RefreshInterceptor.Disarm();
        using IServiceScope scope = Services.CreateScope();
        AuthDbContext context = scope.ServiceProvider.GetRequiredService<AuthDbContext>();
        await context.Database.ExecuteSqlRawAsync("TRUNCATE TABLE \"RefreshTokens\", \"RefreshSessions\", \"Users\";");
    }

    protected override IHost CreateHost(IHostBuilder builder)
    {
        builder.ConfigureHostConfiguration(configuration => configuration.AddInMemoryCollection(TestSettings()));
        return base.CreateHost(builder);
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");
        builder.ConfigureAppConfiguration(configuration => configuration.AddInMemoryCollection(TestSettings()));
        builder.ConfigureServices(services =>
        {
            services.RemoveAll<DbContextOptions<AuthDbContext>>();
            services.RemoveAll<DbContextOptions>();
            services.RemoveAll<IDbContextOptionsConfiguration<AuthDbContext>>();
            services.AddDbContext<AuthDbContext>(options => options
                .UseNpgsql(_database.GetConnectionString())
                .AddInterceptors(RefreshInterceptor));
        });
    }

    private Dictionary<string, string?> TestSettings()
    {
        return new Dictionary<string, string?>
        {
            ["ConnectionStrings:AuthDatabase"] = _database.GetConnectionString(),
            ["Jwt:Issuer"] = "AuthServiceIntegrationTests",
            ["Jwt:Audience"] = "AuthServiceIntegrationTests",
            ["Jwt:SigningKey"] = "test-only-signing-key-at-least-32-characters",
            ["Jwt:ExpiryMinutes"] = "60",
            ["Jwt:RefreshTokenExpiryDays"] = "7"
        };
    }
}
