using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using SmartLost.AuthService.Application.Contracts;
using SmartLost.AuthService.Application.Interfaces;
using SmartLost.AuthService.Domain.Entities;
using SmartLost.AuthService.Infrastructure.Persistence;
using SmartLost.AuthService.IntegrationTests.Fixtures;
using SmartLost.AuthService.IntegrationTests.Helpers;
using SmartLost.BuildingBlocks.Core.Exceptions;
using Xunit;

namespace SmartLost.AuthService.IntegrationTests;

public sealed class AuthServiceIntegrationTests : IClassFixture<AuthServiceFactory>, IAsyncLifetime
{
    private readonly AuthServiceFactory _factory;
    private readonly HttpClient _client;
    private readonly TestDataSeeder _seeder;

    public AuthServiceIntegrationTests(AuthServiceFactory factory)
    {
        _factory = factory;
        _seeder = new TestDataSeeder(factory);
        _client = factory.CreateClient(new WebApplicationFactoryClientOptions { BaseAddress = new Uri("https://localhost") });
    }

    public Task InitializeAsync()
    {
        return _factory.ResetAsync();
    }

    public Task DisposeAsync()
    {
        _client.Dispose();
        return Task.CompletedTask;
    }

    [Fact]
    public async Task TestingEnvironmentUsesPostgreSqlAndAppliedMigrations()
    {
        await using AsyncServiceScope scope = _factory.Services.CreateAsyncScope();
        AuthDbContext context = scope.ServiceProvider.GetRequiredService<AuthDbContext>();
        Assert.Equal("Testing", _factory.Services.GetRequiredService<IWebHostEnvironment>().EnvironmentName);
        Assert.Equal("Npgsql.EntityFrameworkCore.PostgreSQL", context.Database.ProviderName);
        Assert.Equal("authservice_tests", context.Database.GetDbConnection().Database);
        Assert.Equal(2, (await context.Database.GetAppliedMigrationsAsync()).Count());
        Assert.Empty(await context.Database.GetPendingMigrationsAsync());
        Assert.Empty(await context.Users.ToListAsync());
    }

    [Fact]
    public async Task RegisterThenLoginPersistsNormalizedIdentityAndTokenSessions()
    {
        AuthResponse registered = await RegisterAsync();
        using HttpResponseMessage login = await _client.PostAsJsonAsync("/api/auth/login",
            TestRequestFactory.ValidLoginRequest("  MARIA@EXAMPLE.COM  "));
        Assert.Equal(HttpStatusCode.OK, login.StatusCode);
        AuthResponse authenticated = (await login.Content.ReadFromJsonAsync<AuthResponse>())!;
        Assert.Equal(registered.UserId, authenticated.UserId);
        Assert.Equal("Maria.Ionescu", authenticated.UserName);
        Assert.Equal("Maria@Example.com", authenticated.Email);
        Assert.NotEmpty(authenticated.AccessToken);
        Assert.NotEqual(registered.RefreshToken, authenticated.RefreshToken);
        await using AsyncServiceScope scope = _factory.Services.CreateAsyncScope();
        AuthDbContext context = scope.ServiceProvider.GetRequiredService<AuthDbContext>();
        UserAccount user = await context.Users.SingleAsync();
        Assert.Equal("MARIA.IONESCU", user.NormalizedUserName);
        Assert.Equal("MARIA@EXAMPLE.COM", user.NormalizedEmail);
        Assert.Equal(2, await context.RefreshSessions.CountAsync());
        Assert.Equal(2, await context.RefreshTokens.CountAsync());
    }

    [Theory]
    [InlineData("maria.ionescu", "different@example.com")]
    [InlineData("different-user", "  MARIA@EXAMPLE.COM  ")]
    public async Task DuplicateRegistrationReturnsConflict(string userName, string email)
    {
        await RegisterAsync();
        using HttpResponseMessage duplicate = await _client.PostAsJsonAsync("/api/auth/register",
            TestRequestFactory.ValidRegisterRequest(email, userName));
        Assert.Equal(HttpStatusCode.Conflict, duplicate.StatusCode);
        await using AsyncServiceScope scope = _factory.Services.CreateAsyncScope();
        AuthDbContext context = scope.ServiceProvider.GetRequiredService<AuthDbContext>();
        Assert.Equal(1, await context.Users.CountAsync());
        Assert.Equal(1, await context.RefreshSessions.CountAsync());
    }

    [Theory]
    [InlineData("  MARIA.IONESCU  ", "different@example.com", "UserName")]
    [InlineData("different-user", "  MARIA@EXAMPLE.COM  ", "Email")]
    public async Task PostgreSqlUniqueConstraintsMapToAccountConflicts(string userName, string email, string property)
    {
        await using AsyncServiceScope first = _factory.Services.CreateAsyncScope();
        await using AsyncServiceScope second = _factory.Services.CreateAsyncScope();
        IUserAccountRepository firstRepository = first.ServiceProvider.GetRequiredService<IUserAccountRepository>();
        IUserAccountRepository secondRepository = second.ServiceProvider.GetRequiredService<IUserAccountRepository>();
        var existing = UserAccount.Create("Maria.Ionescu", "Maria@Example.com", DateTime.UtcNow);
        var duplicate = UserAccount.Create(userName, email, DateTime.UtcNow);
        // Both preflight checks pass before the first transaction commits.
        Assert.False(await firstRepository.ExistsAsync(existing.NormalizedUserName, existing.NormalizedEmail, CancellationToken.None));
        Assert.False(await secondRepository.ExistsAsync(duplicate.NormalizedUserName, duplicate.NormalizedEmail, CancellationToken.None));
        existing.SetPasswordHash("test-only-hash");
        duplicate.SetPasswordHash("test-only-hash");
        firstRepository.Add(existing);
        secondRepository.Add(duplicate);
        await firstRepository.SaveChangesAsync(CancellationToken.None);
        DomainException exception = await Assert.ThrowsAsync<DomainException>(() => secondRepository.SaveChangesAsync(CancellationToken.None));
        Assert.Equal("auth.account_exists", exception.Error.Code);
        Assert.Equal(property, exception.Error.PropertyName);
        await using AsyncServiceScope verification = _factory.Services.CreateAsyncScope();
        Assert.Equal(1, await verification.ServiceProvider.GetRequiredService<AuthDbContext>().Users.CountAsync());
    }

    [Fact]
    public async Task RefreshRotatesHashesAndReplayRevokesTheSession()
    {
        AuthResponse registered = await RegisterAsync();
        Assert.Equal(0L, registered.RefreshTokenExpiresAtUtc.Ticks % TimeSpan.TicksPerMicrosecond);
        await using (AsyncServiceScope scope = _factory.Services.CreateAsyncScope())
        {
            AuthDbContext context = scope.ServiceProvider.GetRequiredService<AuthDbContext>();
            Assert.Equal(registered.RefreshTokenExpiresAtUtc, (await context.RefreshSessions.SingleAsync()).ExpiresAtUtc);
        }

        using HttpResponseMessage refresh = await RefreshAsync(registered.RefreshToken);
        Assert.Equal(HttpStatusCode.OK, refresh.StatusCode);
        AuthResponse rotated = (await refresh.Content.ReadFromJsonAsync<AuthResponse>())!;
        Assert.NotEqual(registered.RefreshToken, rotated.RefreshToken);
        Assert.NotEqual(registered.AccessToken, rotated.AccessToken);
        Assert.Equal(registered.RefreshTokenExpiresAtUtc, rotated.RefreshTokenExpiresAtUtc);
        await using (AsyncServiceScope scope = _factory.Services.CreateAsyncScope())
        {
            AuthDbContext context = scope.ServiceProvider.GetRequiredService<AuthDbContext>();
            Assert.Equal(HashToken(rotated.RefreshToken), (await context.RefreshSessions.SingleAsync()).CurrentTokenHash);
            Assert.Equal(2, await context.RefreshTokens.CountAsync());
        }

        using HttpResponseMessage replay = await RefreshAsync(registered.RefreshToken);
        Assert.Equal(HttpStatusCode.Unauthorized, replay.StatusCode);
        using HttpResponseMessage revoked = await RefreshAsync(rotated.RefreshToken);
        Assert.Equal(HttpStatusCode.Unauthorized, revoked.StatusCode);
        await using AsyncServiceScope verification = _factory.Services.CreateAsyncScope();
        Assert.NotNull((await verification.ServiceProvider.GetRequiredService<AuthDbContext>().RefreshSessions.SingleAsync()).RevokedAtUtc);
    }

    [Theory]
    [InlineData(TestRequestFactory.DefaultPassword, HttpStatusCode.OK)]
    [InlineData("WrongPassword123!", HttpStatusCode.Unauthorized)]
    public async Task LoginValidatesThePasswordOfAnEfSeededUser(string password, HttpStatusCode expected)
    {
        UserAccount user = await _seeder.SeedUserAsync();
        using HttpResponseMessage response = await _client.PostAsJsonAsync("/api/auth/login",
            TestRequestFactory.ValidLoginRequest(user.Email, password));
        Assert.Equal(expected, response.StatusCode);
        await using AsyncServiceScope scope = _factory.Services.CreateAsyncScope();
        AuthDbContext context = scope.ServiceProvider.GetRequiredService<AuthDbContext>();
        Assert.Equal(expected == HttpStatusCode.OK ? 1 : 0, await context.RefreshSessions.CountAsync());
    }

    [Fact]
    public async Task ExpiredSessionCannotRefresh()
    {
        AuthResponse registered = await RegisterAsync();
        await using (AsyncServiceScope scope = _factory.Services.CreateAsyncScope())
        {
            AuthDbContext context = scope.ServiceProvider.GetRequiredService<AuthDbContext>();
            await context.RefreshSessions.ExecuteUpdateAsync(setters => setters
                .SetProperty(session => session.ExpiresAtUtc, DateTime.UtcNow.AddMinutes(-1)));
        }

        using HttpResponseMessage response = await RefreshAsync(registered.RefreshToken);
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task ConcurrentRefreshCommitsOneRotationAndRevokesTheSession()
    {
        AuthResponse registered = await RegisterAsync();
        _factory.RefreshInterceptor.Arm();
        HttpResponseMessage[] responses = await Task.WhenAll(RefreshAsync(registered.RefreshToken), RefreshAsync(registered.RefreshToken));
        try
        {
            Assert.Single(responses, response => response.StatusCode == HttpStatusCode.OK);
            Assert.Single(responses, response => response.StatusCode == HttpStatusCode.Unauthorized);
            AuthResponse winner = (await responses.Single(response => response.StatusCode == HttpStatusCode.OK)
                .Content.ReadFromJsonAsync<AuthResponse>())!;
            using HttpResponseMessage revoked = await RefreshAsync(winner.RefreshToken);
            Assert.Equal(HttpStatusCode.Unauthorized, revoked.StatusCode);
            await using AsyncServiceScope scope = _factory.Services.CreateAsyncScope();
            AuthDbContext context = scope.ServiceProvider.GetRequiredService<AuthDbContext>();
            Assert.Equal(2, await context.RefreshTokens.CountAsync());
            Assert.NotNull((await context.RefreshSessions.SingleAsync()).RevokedAtUtc);
        }
        finally
        {
            foreach (HttpResponseMessage response in responses)
            {
                response.Dispose();
            }

            _factory.RefreshInterceptor.Disarm();
        }
    }

    private async Task<AuthResponse> RegisterAsync()
    {
        using HttpResponseMessage response = await _client.PostAsJsonAsync("/api/auth/register",
            TestRequestFactory.ValidRegisterRequest("  Maria@Example.com  ", "  Maria.Ionescu  "));
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<AuthResponse>())!;
    }

    private Task<HttpResponseMessage> RefreshAsync(string refreshToken)
    {
        return _client.PostAsJsonAsync("/api/auth/refresh", TestRequestFactory.ValidRefreshRequest(refreshToken));
    }

    private static string HashToken(string token)
    {
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(token)));
    }
}
