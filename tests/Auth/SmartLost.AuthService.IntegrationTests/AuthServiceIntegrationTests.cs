using System.Collections.Concurrent;
using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using SmartLost.AuthService.Application.Contracts;
using SmartLost.AuthService.Application.Interfaces;
using SmartLost.AuthService.Domain.Entities;
using SmartLost.AuthService.Infrastructure.Persistence;
using SmartLost.AuthService.IntegrationTests.Fixtures;
using SmartLost.AuthService.IntegrationTests.Helpers;
using SmartLost.BuildingBlocks.Application.Outbox;
using SmartLost.BuildingBlocks.Core.Exceptions;
using SmartLost.BuildingBlocks.Infrastructure.Outbox;
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
        string[] migrations = [.. await context.Database.GetAppliedMigrationsAsync()];
        Assert.Equal(3, migrations.Length);
        Assert.Contains(migrations, migration => migration.EndsWith("_AddAuthOutbox", StringComparison.Ordinal));
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
        SmartLost.BuildingBlocks.Infrastructure.Outbox.OutboxMessage message = await context.OutboxMessages.SingleAsync();
        using var payload = JsonDocument.Parse(message.Payload);
        Assert.Equal("auth.user.created", message.Type);
        Assert.Equal(user.Id, payload.RootElement.GetProperty("userId").GetGuid());
        Assert.Equal(1, payload.RootElement.GetProperty("identityVersion").GetInt64());
        Assert.False(payload.RootElement.TryGetProperty("email", out _));
        Assert.False(payload.RootElement.TryGetProperty("passwordHash", out _));
        Assert.Null(message.ProcessedOn);
        Assert.Empty(user.DomainEvents);
    }

    [Fact]
    public async Task FailedIdentityChangeRollsBackItsOutboxMessage()
    {
        UserAccount first = await _seeder.SeedUserAsync("first@example.com", "first-user");
        UserAccount second = await _seeder.SeedUserAsync("second@example.com", "second-user");
        await using (AsyncServiceScope scope = _factory.Services.CreateAsyncScope())
        {
            AuthDbContext context = scope.ServiceProvider.GetRequiredService<AuthDbContext>();
            UserAccount user = await context.Users.SingleAsync(item => item.Id == first.Id);
            user.ChangeIdentity("changed-user", second.Email);
            await Assert.ThrowsAsync<DbUpdateException>(() => context.SaveChangesAsync());
            Assert.Single(user.DomainEvents);
        }

        await using AsyncServiceScope verification = _factory.Services.CreateAsyncScope();
        AuthDbContext persisted = verification.ServiceProvider.GetRequiredService<AuthDbContext>();
        UserAccount original = await persisted.Users.SingleAsync(user => user.Id == first.Id);
        Assert.Equal(first.UserName, original.UserName);
        Assert.Equal(1, original.IdentityVersion);
        Assert.Equal(2, await persisted.OutboxMessages.CountAsync());
        Assert.False(await persisted.OutboxMessages.AnyAsync(message => message.Type == "auth.user.identity_changed"));
    }

    [Fact]
    public async Task ConcurrentIdentityChangesPublishOnlyTheCommittedVersion()
    {
        UserAccount seeded = await _seeder.SeedUserAsync();
        await using AsyncServiceScope firstScope = _factory.Services.CreateAsyncScope();
        await using AsyncServiceScope secondScope = _factory.Services.CreateAsyncScope();
        AuthDbContext first = firstScope.ServiceProvider.GetRequiredService<AuthDbContext>();
        AuthDbContext second = secondScope.ServiceProvider.GetRequiredService<AuthDbContext>();
        UserAccount winner = await first.Users.SingleAsync(user => user.Id == seeded.Id);
        UserAccount loser = await second.Users.SingleAsync(user => user.Id == seeded.Id);
        winner.ChangeIdentity("winning-name", winner.Email);
        loser.ChangeIdentity("losing-name", loser.Email);
        await first.SaveChangesAsync();
        await Assert.ThrowsAsync<DbUpdateConcurrencyException>(() => second.SaveChangesAsync());

        await using AsyncServiceScope verification = _factory.Services.CreateAsyncScope();
        AuthDbContext persisted = verification.ServiceProvider.GetRequiredService<AuthDbContext>();
        Assert.Equal("winning-name", (await persisted.Users.SingleAsync()).UserName);
        Assert.Equal(2, await persisted.OutboxMessages.CountAsync());
        string payload = await persisted.OutboxMessages.Where(message => message.Type == "auth.user.identity_changed")
            .Select(message => message.Payload).SingleAsync();
        using var document = JsonDocument.Parse(payload);
        Assert.Equal(2, document.RootElement.GetProperty("identityVersion").GetInt64());
        Assert.Equal("winning-name", document.RootElement.GetProperty("userName").GetString());
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

    [Fact]
    public async Task ConcurrentOutboxWorkersClaimEachPostgreSqlMessageOnce()
    {
        UserAccount seeded = await _seeder.SeedUserAsync();
        await using (AsyncServiceScope setup = _factory.Services.CreateAsyncScope())
        {
            AuthDbContext context = setup.ServiceProvider.GetRequiredService<AuthDbContext>();
            UserAccount user = await context.Users.SingleAsync(item => item.Id == seeded.Id);
            for (int version = 2; version <= 6; version++)
            {
                user.ChangeIdentity($"profile-version-{version}", user.Email);
            }

            await context.SaveChangesAsync();
        }

        await using AsyncServiceScope first = _factory.Services.CreateAsyncScope();
        await using AsyncServiceScope second = _factory.Services.CreateAsyncScope();
        RecordingPublisher publisher = new();
        OutboxProcessor<AuthDbContext> firstProcessor = new(first.ServiceProvider.GetRequiredService<AuthDbContext>(),
            publisher, Options.Create(new OutboxOptions()), TimeProvider.System, NullLogger<OutboxProcessor<AuthDbContext>>.Instance);
        OutboxProcessor<AuthDbContext> secondProcessor = new(second.ServiceProvider.GetRequiredService<AuthDbContext>(),
            publisher, Options.Create(new OutboxOptions()), TimeProvider.System, NullLogger<OutboxProcessor<AuthDbContext>>.Instance);
        using CancellationTokenSource timeout = new(TimeSpan.FromSeconds(20));
        int[] results = await Task.WhenAll(firstProcessor.ProcessBatchAsync(timeout.Token), secondProcessor.ProcessBatchAsync(timeout.Token));
        Assert.Equal(6, results.Sum());
        Assert.Equal(6, publisher.Deliveries.Count);
        Assert.All(publisher.Deliveries.Values, attempts => Assert.Equal(1, attempts));
        await using AsyncServiceScope verification = _factory.Services.CreateAsyncScope();
        AuthDbContext persisted = verification.ServiceProvider.GetRequiredService<AuthDbContext>();
        OutboxMessage[] messages = await persisted.OutboxMessages.ToArrayAsync();
        Assert.Equal(6, messages.Length);
        Assert.All(messages, message =>
        {
            Assert.NotNull(message.ProcessedOn);
            Assert.Null(message.LeaseId);
            Assert.Equal(1, message.AttemptCount);
        });
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

    private sealed class RecordingPublisher : IOutboxMessagePublisher
    {
        public ConcurrentDictionary<Guid, int> Deliveries { get; } = new();

        public async Task PublishAsync(OutboxEnvelope envelope, CancellationToken cancellationToken)
        {
            Deliveries.AddOrUpdate(envelope.Id, 1, (_, count) => count + 1);
            await Task.Delay(TimeSpan.FromMilliseconds(10), cancellationToken);
        }
    }
}
