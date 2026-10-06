using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using SmartLost.AuthService.Api.Contracts;
using SmartLost.AuthService.Application.Contracts;
using SmartLost.AuthService.Domain.Entities;
using SmartLost.AuthService.Infrastructure.Persistence;
using Xunit;

namespace SmartLost.AuthService.UnitTests;

public sealed class RefreshTokenTests
{
    [Fact]
    public async Task RegisterLoginAndRefreshIssueHashedRotatingTokens()
    {
        using SqliteAuthFixture fixture = new();
        AuthResponse registered = await RegisterAsync(fixture.Client);
        Assert.Equal(86, registered.RefreshToken.Length);
        Assert.True(registered.RefreshTokenExpiresAtUtc > registered.ExpiresAtUtc);

        using HttpResponseMessage login = await fixture.Client.PostAsJsonAsync("/api/auth/login",
            new LoginRequest { Email = registered.Email, Password = "ValidPassword123!" });
        Assert.Equal(HttpStatusCode.OK, login.StatusCode);
        AuthResponse loggedIn = (await login.Content.ReadFromJsonAsync<AuthResponse>())!;
        Assert.NotEqual(registered.RefreshToken, loggedIn.RefreshToken);

        using HttpResponseMessage refresh = await RefreshAsync(fixture.Client, registered.RefreshToken);
        Assert.Equal(HttpStatusCode.OK, refresh.StatusCode);
        Assert.True(refresh.Headers.CacheControl!.NoStore);
        AuthResponse rotated = (await refresh.Content.ReadFromJsonAsync<AuthResponse>())!;
        Assert.Equal(registered.UserId, rotated.UserId);
        Assert.Equal(registered.UserName, rotated.UserName);
        Assert.Equal(registered.Email, rotated.Email);
        Assert.NotEqual(registered.RefreshToken, rotated.RefreshToken);
        Assert.NotEqual(registered.AccessToken, rotated.AccessToken);
        Assert.Equal(registered.RefreshTokenExpiresAtUtc, rotated.RefreshTokenExpiresAtUtc);

        using IServiceScope scope = fixture.Factory.Services.CreateScope();
        AuthDbContext context = scope.ServiceProvider.GetRequiredService<AuthDbContext>();
        string hash = HashToken(rotated.RefreshToken);
        Assert.Equal(2, await context.RefreshSessions.CountAsync());
        Assert.Equal(3, await context.RefreshTokens.CountAsync());
        Assert.True(await context.RefreshTokens.AnyAsync(token => token.TokenHash == hash));
        Assert.All(await context.RefreshTokens.ToListAsync(), token => Assert.Equal(64, token.TokenHash.Length));
        Assert.DoesNotContain(await context.RefreshTokens.Select(token => token.TokenHash).ToListAsync(), value => value == rotated.RefreshToken);
    }

    [Fact]
    public async Task ReplayingAnOldTokenRevokesItsSessionButNotAnotherLogin()
    {
        using SqliteAuthFixture fixture = new();
        AuthResponse registered = await RegisterAsync(fixture.Client);
        using HttpResponseMessage login = await fixture.Client.PostAsJsonAsync("/api/auth/login",
            new LoginRequest { Email = registered.Email, Password = "ValidPassword123!" });
        AuthResponse anotherSession = (await login.Content.ReadFromJsonAsync<AuthResponse>())!;
        using HttpResponseMessage refresh = await RefreshAsync(fixture.Client, registered.RefreshToken);
        AuthResponse rotated = (await refresh.Content.ReadFromJsonAsync<AuthResponse>())!;

        using HttpResponseMessage replay = await RefreshAsync(fixture.Client, registered.RefreshToken);
        Assert.Equal(HttpStatusCode.Unauthorized, replay.StatusCode);
        Assert.Contains("auth.invalid_refresh_token", await replay.Content.ReadAsStringAsync(), StringComparison.Ordinal);
        using HttpResponseMessage revoked = await RefreshAsync(fixture.Client, rotated.RefreshToken);
        Assert.Equal(HttpStatusCode.Unauthorized, revoked.StatusCode);
        using HttpResponseMessage unaffected = await RefreshAsync(fixture.Client, anotherSession.RefreshToken);
        Assert.Equal(HttpStatusCode.OK, unaffected.StatusCode);
    }

    [Fact]
    public async Task ExpiredAndUnknownTokensAreRejected()
    {
        using SqliteAuthFixture fixture = new();
        AuthResponse registered = await RegisterAsync(fixture.Client);
        using (IServiceScope scope = fixture.Factory.Services.CreateScope())
        {
            AuthDbContext context = scope.ServiceProvider.GetRequiredService<AuthDbContext>();
            RefreshSession session = await context.RefreshSessions.SingleAsync();
            context.Entry(session).Property(value => value.ExpiresAtUtc).CurrentValue = DateTime.UtcNow.AddSeconds(-1);
            await context.SaveChangesAsync();
        }

        using HttpResponseMessage expired = await RefreshAsync(fixture.Client, registered.RefreshToken);
        Assert.Equal(HttpStatusCode.Unauthorized, expired.StatusCode);
        using HttpResponseMessage unknown = await RefreshAsync(fixture.Client, new string('A', 86));
        Assert.Equal(HttpStatusCode.Unauthorized, unknown.StatusCode);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("short")]
    [InlineData(" ")]
    public async Task MalformedRefreshRequestsReturnBadRequest(string? token)
    {
        using SqliteAuthFixture fixture = new();
        using HttpResponseMessage response = await fixture.Client.PostAsJsonAsync("/api/auth/refresh", new
        {
            RefreshToken = token
        });
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task ConcurrentRotationFailsAndRevokesTheWinningSession()
    {
        CompetingRotationInterceptor interceptor = new();
        using SqliteAuthFixture fixture = new(interceptor);
        AuthResponse registered = await RegisterAsync(fixture.Client);
        interceptor.BeforeSave = async () =>
        {
            // Commit a competing rotation after the request reads the old hash, before
            // its UPDATE. This deterministically exercises the database concurrency check.
            DbContextOptions<AuthDbContext> options = new DbContextOptionsBuilder<AuthDbContext>().UseSqlite(fixture.Connection).Options;
            await using AuthDbContext competitor = new(options);
            RefreshSession session = await competitor.RefreshSessions.SingleAsync();
            string hash = new('B', 64);
            session.Rotate(hash);
            competitor.RefreshTokens.Add(RefreshToken.Create(session, hash));
            await competitor.SaveChangesAsync();
        };

        using HttpResponseMessage refresh = await RefreshAsync(fixture.Client, registered.RefreshToken);
        Assert.Equal(HttpStatusCode.Unauthorized, refresh.StatusCode);
        using IServiceScope scope = fixture.Factory.Services.CreateScope();
        AuthDbContext context = scope.ServiceProvider.GetRequiredService<AuthDbContext>();
        Assert.NotNull((await context.RefreshSessions.SingleAsync()).RevokedAtUtc);
        // The losing request's replacement token is rolled back with its failed UPDATE.
        Assert.Equal(2, await context.RefreshTokens.CountAsync());
    }

    [Fact]
    public void RefreshEntitiesGuardInvalidConstructionAndRotation()
    {
        var user = UserAccount.Create("user", "user@example.com", DateTime.UtcNow);
        var session = RefreshSession.Create(user, "hash", DateTime.UtcNow.AddDays(7));
        Assert.Throws<ArgumentNullException>(() => RefreshSession.Create(null!, "hash", DateTime.UtcNow));
        Assert.Throws<ArgumentException>(() => RefreshSession.Create(user, " ", DateTime.UtcNow));
        Assert.Throws<ArgumentNullException>(() => RefreshToken.Create(null!, "hash"));
        Assert.Throws<ArgumentException>(() => RefreshToken.Create(session, " "));
        Assert.Throws<ArgumentException>(() => session.Rotate(" "));
    }

    private static async Task<AuthResponse> RegisterAsync(HttpClient client)
    {
        using HttpResponseMessage response = await client.PostAsJsonAsync("/api/auth/register",
            new RegisterRequest { UserName = "refresh-user", Email = "refresh@example.com", Password = "ValidPassword123!" });
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<AuthResponse>())!;
    }

    private static Task<HttpResponseMessage> RefreshAsync(HttpClient client, string token)
    {
        return client.PostAsJsonAsync("/api/auth/refresh", new RefreshRequest { RefreshToken = token });
    }

    private static string HashToken(string token)
    {
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(token)));
    }

    private sealed class CompetingRotationInterceptor : SaveChangesInterceptor
    {
        public Func<Task>? BeforeSave
        {
            get; set;
        }

        public override async ValueTask<InterceptionResult<int>> SavingChangesAsync(
            DbContextEventData eventData, InterceptionResult<int> result, CancellationToken cancellationToken = default)
        {
            if (BeforeSave is { } action)
            {
                BeforeSave = null;
                await action();
            }

            return result;
        }
    }

    private sealed class SqliteAuthFixture : IDisposable
    {
        private readonly AuthApiFactory _parent = new();

        public SqliteConnection Connection { get; } = new("Data Source=:memory:");

        public WebApplicationFactory<Program> Factory
        {
            get;
        }

        public HttpClient Client
        {
            get;
        }

        public SqliteAuthFixture(IInterceptor? interceptor = null)
        {
            Connection.Open();
            Factory = _parent.WithWebHostBuilder(builder => builder.ConfigureServices(services =>
            {
                services.RemoveAll<DbContextOptions<AuthDbContext>>();
                services.RemoveAll<DbContextOptions>();
                services.RemoveAll<IDbContextOptionsConfiguration<AuthDbContext>>();
                services.AddDbContext<AuthDbContext>(options =>
                {
                    options.UseSqlite(Connection);
                    if (interceptor is not null)
                    {
                        options.AddInterceptors(interceptor);
                    }
                });
            }));
            Client = Factory.CreateClient();
            using IServiceScope scope = Factory.Services.CreateScope();
            // Isolated in-memory schema only: never runs migrations against PostgreSQL.
            scope.ServiceProvider.GetRequiredService<AuthDbContext>().Database.EnsureCreated();
        }

        public void Dispose()
        {
            Client.Dispose();
            Factory.Dispose();
            _parent.Dispose();
            Connection.Dispose();
        }
    }
}
