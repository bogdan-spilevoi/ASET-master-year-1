using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using SmartLost.AuthService.Api.Contracts;
using SmartLost.AuthService.Application.Contracts;
using SmartLost.AuthService.Domain.Entities;
using SmartLost.AuthService.Infrastructure.Persistence;
using SmartLost.BuildingBlocks.Application.Emails;
using Xunit;

namespace SmartLost.AuthService.UnitTests;

public sealed class EmailConfirmationTests
{
    [Fact]
    public async Task RegisterSendsConfirmationLinkAndGetEndpointConfirmsAddressOnce()
    {
        using AuthApiFactory factory = new();
        using HttpClient client = factory.CreateClient();

        using HttpResponseMessage registerResponse = await client.PostAsJsonAsync("/api/auth/register", new RegisterRequest
        {
            UserName = "confirm.user",
            Email = "confirm.user@example.com",
            Password = "ValidPassword123!"
        });
        Assert.Equal(HttpStatusCode.Created, registerResponse.StatusCode);
        AuthResponse registered = (await registerResponse.Content.ReadFromJsonAsync<AuthResponse>())!;

        EmailMessage sentEmail = Assert.Single(factory.EmailSender.Messages);
        Assert.Equal("confirm.user@example.com", sentEmail.ToAddress);
        Assert.Contains("http://localhost:8080/api/auth/confirm-email?token=", sentEmail.TextBody, StringComparison.Ordinal);
        Assert.NotNull(sentEmail.HtmlBody);

        string token = ExtractToken(sentEmail.TextBody);
        using HttpResponseMessage confirmResponse = await client.GetAsync($"/api/auth/confirm-email?token={Uri.EscapeDataString(token)}");
        Assert.Equal(HttpStatusCode.OK, confirmResponse.StatusCode);
        EmailConfirmationResponse confirmed = (await confirmResponse.Content.ReadFromJsonAsync<EmailConfirmationResponse>())!;
        Assert.Equal(registered.UserId, confirmed.UserId);
        Assert.Equal("confirm.user@example.com", confirmed.Email);
        Assert.True(confirmed.EmailConfirmed);

        using (IServiceScope scope = factory.Services.CreateScope())
        {
            AuthDbContext context = scope.ServiceProvider.GetRequiredService<AuthDbContext>();
            UserAccount persistedUser = await context.Users.SingleAsync();
            EmailConfirmationToken persistedToken = await context.EmailConfirmationTokens.SingleAsync();
            Assert.True(persistedUser.EmailConfirmed);
            Assert.NotNull(persistedUser.EmailConfirmedAtUtc);
            Assert.NotNull(persistedToken.UsedAtUtc);
        }

        using HttpResponseMessage replayResponse = await client.PostAsJsonAsync(
            "/api/auth/confirm-email",
            new ConfirmEmailRequest { Token = token });
        Assert.Equal(HttpStatusCode.Conflict, replayResponse.StatusCode);
        ProblemDetails? replayProblem = await replayResponse.Content.ReadFromJsonAsync<ProblemDetails>();
        Assert.Equal("auth.confirmation_token_already_used", replayProblem!.Extensions["code"]!.ToString());
    }

    [Fact]
    public async Task PostEndpointConfirmsTokenAndSupportsBaseUrlWithQueryString()
    {
        using AuthApiFactory parent = new();
        using WebApplicationFactory<Program> customFactory = parent.WithWebHostBuilder(builder =>
            builder.ConfigureAppConfiguration((_, config) => config.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["EmailConfirmation:ConfirmationBaseUrl"] = "https://auth.smartlost.local/api/auth/confirm-email?source=mailpit"
            })));
        using HttpClient client = customFactory.CreateClient();

        using HttpResponseMessage registerResponse = await client.PostAsJsonAsync("/api/auth/register", new RegisterRequest
        {
            UserName = "post.confirm",
            Email = "post.confirm@example.com",
            Password = "ValidPassword123!"
        });
        Assert.Equal(HttpStatusCode.Created, registerResponse.StatusCode);

        EmailMessage sentEmail = Assert.Single(parent.EmailSender.Messages);
        Assert.Contains("https://auth.smartlost.local/api/auth/confirm-email?source=mailpit&token=", sentEmail.TextBody, StringComparison.Ordinal);

        string token = ExtractToken(sentEmail.TextBody);
        using HttpResponseMessage confirmResponse = await client.PostAsJsonAsync(
            "/api/auth/confirm-email",
            new ConfirmEmailRequest { Token = token });
        Assert.Equal(HttpStatusCode.OK, confirmResponse.StatusCode);
        EmailConfirmationResponse confirmed = (await confirmResponse.Content.ReadFromJsonAsync<EmailConfirmationResponse>())!;
        Assert.True(confirmed.EmailConfirmed);
    }

    [Fact]
    public async Task ExpiredUnknownAndMalformedTokensReturnExpectedErrors()
    {
        using AuthApiFactory factory = new();
        using HttpClient client = factory.CreateClient();

        using HttpResponseMessage missingResponse = await client.GetAsync("/api/auth/confirm-email");
        Assert.Equal(HttpStatusCode.BadRequest, missingResponse.StatusCode);
        ProblemDetails missingProblem = (await missingResponse.Content.ReadFromJsonAsync<ProblemDetails>())!;
        Assert.Equal("auth.confirmation_token_required", missingProblem.Extensions["code"]!.ToString());

        using HttpResponseMessage malformedResponse = await client.PostAsJsonAsync(
            "/api/auth/confirm-email",
            new ConfirmEmailRequest { Token = "short-token" });
        Assert.Equal(HttpStatusCode.BadRequest, malformedResponse.StatusCode);
        ProblemDetails malformedProblem = (await malformedResponse.Content.ReadFromJsonAsync<ProblemDetails>())!;
        Assert.Equal("auth.invalid_confirmation_token", malformedProblem.Extensions["code"]!.ToString());

        using HttpResponseMessage unknownResponse = await client.GetAsync($"/api/auth/confirm-email?token={new string('Z', 43)}");
        Assert.Equal(HttpStatusCode.BadRequest, unknownResponse.StatusCode);
        ProblemDetails unknownProblem = (await unknownResponse.Content.ReadFromJsonAsync<ProblemDetails>())!;
        Assert.Equal("auth.invalid_confirmation_token", unknownProblem.Extensions["code"]!.ToString());

        await client.PostAsJsonAsync("/api/auth/register", new RegisterRequest
        {
            UserName = "expired.user",
            Email = "expired.user@example.com",
            Password = "ValidPassword123!"
        });
        string token = ExtractToken(Assert.Single(factory.EmailSender.Messages).TextBody);

        using (IServiceScope scope = factory.Services.CreateScope())
        {
            AuthDbContext context = scope.ServiceProvider.GetRequiredService<AuthDbContext>();
            EmailConfirmationToken storedToken = await context.EmailConfirmationTokens.SingleAsync();
            context.Entry(storedToken).Property(value => value.ExpiresAtUtc).CurrentValue = DateTime.UtcNow.AddMinutes(-1);
            await context.SaveChangesAsync();
        }

        using HttpResponseMessage expiredResponse = await client.GetAsync($"/api/auth/confirm-email?token={Uri.EscapeDataString(token)}");
        Assert.Equal(HttpStatusCode.BadRequest, expiredResponse.StatusCode);
        ProblemDetails expiredProblem = (await expiredResponse.Content.ReadFromJsonAsync<ProblemDetails>())!;
        Assert.Equal("auth.confirmation_token_expired", expiredProblem.Extensions["code"]!.ToString());
    }

    [Fact]
    public async Task SmtpFailureDuringRegistrationRollsBackUserSessionTokenAndOutbox()
    {
        using AuthApiFactory parent = new();
        using WebApplicationFactory<Program> failingFactory = parent.WithWebHostBuilder(builder =>
            builder.ConfigureServices(services =>
            {
                services.RemoveAll<IEmailSender>();
                services.AddSingleton<IEmailSender, FailingEmailSender>();
            }));
        using HttpClient client = failingFactory.CreateClient();

        using HttpResponseMessage response = await client.PostAsJsonAsync("/api/auth/register", new RegisterRequest
        {
            UserName = "smtp.fail",
            Email = "smtp.fail@example.com",
            Password = "ValidPassword123!"
        });
        Assert.Equal(HttpStatusCode.InternalServerError, response.StatusCode);

        using IServiceScope scope = failingFactory.Services.CreateScope();
        AuthDbContext context = scope.ServiceProvider.GetRequiredService<AuthDbContext>();
        Assert.Equal(0, await context.Users.CountAsync());
        Assert.Equal(0, await context.RefreshSessions.CountAsync());
        Assert.Equal(0, await context.RefreshTokens.CountAsync());
        Assert.Equal(0, await context.EmailConfirmationTokens.CountAsync());
        Assert.Equal(0, await context.OutboxMessages.CountAsync());
    }

    [Fact]
    public async Task ConcurrentConfirmationMapsConcurrencyConflictToAlreadyUsed()
    {
        CompetingConfirmInterceptor interceptor = new();
        using SqliteConfirmFixture fixture = new(interceptor);

        await fixture.Client.PostAsJsonAsync("/api/auth/register", new RegisterRequest
        {
            UserName = "race.confirm",
            Email = "race.confirm@example.com",
            Password = "ValidPassword123!"
        });
        string token = ExtractToken(Assert.Single(fixture.EmailSender.Messages).TextBody);

        interceptor.BeforeSave = async () =>
        {
            DbContextOptions<AuthDbContext> options = new DbContextOptionsBuilder<AuthDbContext>().UseSqlite(fixture.Connection).Options;
            await using AuthDbContext competitor = new(options);
            EmailConfirmationToken row = await competitor.EmailConfirmationTokens.Include(entry => entry.User).SingleAsync();
            row.Consume(DateTime.UtcNow);
            await competitor.SaveChangesAsync();
        };

        using HttpResponseMessage response = await fixture.Client.PostAsJsonAsync(
            "/api/auth/confirm-email",
            new ConfirmEmailRequest { Token = token });
        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        ProblemDetails problem = (await response.Content.ReadFromJsonAsync<ProblemDetails>())!;
        Assert.Equal("auth.confirmation_token_already_used", problem.Extensions["code"]!.ToString());
    }

    private static string ExtractToken(string emailBody)
    {
        const string Marker = "token=";
        int start = emailBody.IndexOf(Marker, StringComparison.Ordinal);
        Assert.True(start >= 0, "Confirmation token was not found in email body.");
        start += Marker.Length;
        int end = start;
        while (end < emailBody.Length && !char.IsWhiteSpace(emailBody[end]))
        {
            end++;
        }

        return Uri.UnescapeDataString(emailBody[start..end]);
    }

    private sealed class FailingEmailSender : IEmailSender
    {
        public Task SendAsync(EmailMessage message, CancellationToken cancellationToken)
        {
            throw new InvalidOperationException("SMTP delivery failed.");
        }
    }

    private sealed class CompetingConfirmInterceptor : SaveChangesInterceptor
    {
        public Func<Task>? BeforeSave
        {
            get; set;
        }

        public override async ValueTask<InterceptionResult<int>> SavingChangesAsync(
            DbContextEventData eventData, InterceptionResult<int> result, CancellationToken cancellationToken = default)
        {
            if (BeforeSave is { } action &&
                eventData.Context?.ChangeTracker.Entries<EmailConfirmationToken>().Any(entry => entry.State == EntityState.Modified) == true)
            {
                BeforeSave = null;
                await action();
            }

            return result;
        }
    }

    private sealed class SqliteConfirmFixture : IDisposable
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

        public AuthApiFactory.RecordingEmailSender EmailSender => _parent.EmailSender;

        public SqliteConfirmFixture(IInterceptor interceptor)
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
                    options.AddInterceptors(interceptor);
                });
            }));
            Client = Factory.CreateClient();
            using IServiceScope scope = Factory.Services.CreateScope();
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
