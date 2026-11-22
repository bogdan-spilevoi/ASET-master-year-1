using System.Collections.Concurrent;
using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using SmartLost.AuthService.Api.Contracts;
using SmartLost.AuthService.Application.Contracts;
using SmartLost.AuthService.Application.IntegrationEvents;
using SmartLost.AuthService.Application.Interfaces;
using SmartLost.AuthService.Domain.Entities;
using SmartLost.AuthService.Infrastructure.Authentication;
using SmartLost.AuthService.Infrastructure.Outbox;
using SmartLost.AuthService.Infrastructure.Persistence;
using SmartLost.AuthService.Worker;
using SmartLost.BuildingBlocks.Application.Emails;
using SmartLost.BuildingBlocks.Application.Outbox;
using SmartLost.BuildingBlocks.Infrastructure.Outbox;
using Xunit;

namespace SmartLost.AuthService.UnitTests;

public sealed class EmailConfirmationTests
{
    [Fact]
    public async Task RegisterStagesOutboxEmailAndGetPageDoesNotConsumeBeforePostConfirmation()
    {
        using SqliteConfirmFixture fixture = new();

        using HttpResponseMessage registerResponse = await fixture.Client.PostAsJsonAsync("/api/auth/register", new RegisterRequest
        {
            UserName = "confirm.user",
            Email = "confirm.user@example.com",
            Password = "ValidPassword123!"
        });
        Assert.Equal(HttpStatusCode.Created, registerResponse.StatusCode);
        AuthResponse registered = (await registerResponse.Content.ReadFromJsonAsync<AuthResponse>())!;

        RecordingEmailSender emailSender = new();
        using (IServiceScope scope = fixture.Factory.Services.CreateScope())
        {
            AuthDbContext context = scope.ServiceProvider.GetRequiredService<AuthDbContext>();
            UserAccount persistedUser = await context.Users.SingleAsync();
            EmailConfirmationToken persistedToken = await context.EmailConfirmationTokens.SingleAsync();
            OutboxMessage[] messages = await context.OutboxMessages.OrderBy(item => item.Type).ToArrayAsync();

            Assert.False(persistedUser.EmailConfirmed);
            Assert.Null(persistedToken.UsedAtUtc);
            Assert.Equal(2, messages.Length);
            Assert.Contains(messages, message => message.Type == AuthOutboxMessageTypes.ConfirmationEmailRequested && message.ProcessedOn is null);
            Assert.Contains(messages, message => message.Type == AuthIntegrationEventTypes.UserCreated && message.ProcessedOn is null);

            OutboxProcessor<AuthDbContext> processor = CreateEmailProcessor(scope.ServiceProvider, emailSender, fixture.Clock);
            int delivered = await processor.ProcessBatchAsync(CancellationToken.None);
            Assert.Equal(1, delivered);

            OutboxMessage emailOutbox = await context.OutboxMessages.AsNoTracking()
                .SingleAsync(message => message.Type == AuthOutboxMessageTypes.ConfirmationEmailRequested);
            OutboxMessage profileOutbox = await context.OutboxMessages.AsNoTracking()
                .SingleAsync(message => message.Type == AuthIntegrationEventTypes.UserCreated);
            Assert.NotNull(emailOutbox.ProcessedOn);
            Assert.Null(profileOutbox.ProcessedOn);
            Assert.Equal(0, profileOutbox.AttemptCount);
        }

        EmailMessage sentEmail = Assert.Single(emailSender.Messages);
        Assert.Equal("confirm.user@example.com", sentEmail.ToAddress);
        Assert.Contains("http://localhost:8080/api/auth/confirm-email?token=", sentEmail.TextBody, StringComparison.Ordinal);
        Assert.NotNull(sentEmail.HtmlBody);

        string token = ExtractToken(sentEmail.TextBody);
        for (int attempt = 0; attempt < 2; attempt++)
        {
            using HttpResponseMessage pageResponse = await fixture.Client.GetAsync(
                $"/api/auth/confirm-email?token={Uri.EscapeDataString(token)}");
            Assert.Equal(HttpStatusCode.OK, pageResponse.StatusCode);
            Assert.Equal("text/html", pageResponse.Content.Headers.ContentType!.MediaType);
            AssertSecurityHeaders(pageResponse);
            string html = await pageResponse.Content.ReadAsStringAsync();
            Assert.Contains("id=\"confirm-button\"", html, StringComparison.Ordinal);
            Assert.Contains($"value=\"{token}\"", html, StringComparison.Ordinal);
        }

        using (IServiceScope scope = fixture.Factory.Services.CreateScope())
        {
            AuthDbContext context = scope.ServiceProvider.GetRequiredService<AuthDbContext>();
            UserAccount stillUnconfirmed = await context.Users.SingleAsync();
            EmailConfirmationToken stillUnused = await context.EmailConfirmationTokens.SingleAsync();
            Assert.False(stillUnconfirmed.EmailConfirmed);
            Assert.Null(stillUnused.UsedAtUtc);
        }

        using HttpResponseMessage confirmResponse = await fixture.Client.PostAsJsonAsync(
            "/api/auth/confirm-email",
            new ConfirmEmailRequest { Token = token });
        Assert.Equal(HttpStatusCode.OK, confirmResponse.StatusCode);
        EmailConfirmationResponse confirmed = (await confirmResponse.Content.ReadFromJsonAsync<EmailConfirmationResponse>())!;
        Assert.Equal(registered.UserId, confirmed.UserId);
        Assert.Equal("confirm.user@example.com", confirmed.Email);
        Assert.True(confirmed.EmailConfirmed);

        using (IServiceScope scope = fixture.Factory.Services.CreateScope())
        {
            AuthDbContext context = scope.ServiceProvider.GetRequiredService<AuthDbContext>();
            UserAccount persistedUser = await context.Users.SingleAsync();
            EmailConfirmationToken persistedToken = await context.EmailConfirmationTokens.SingleAsync();
            Assert.True(persistedUser.EmailConfirmed);
            Assert.NotNull(persistedUser.EmailConfirmedAtUtc);
            Assert.NotNull(persistedToken.UsedAtUtc);
        }

        using HttpResponseMessage replayResponse = await fixture.Client.PostAsJsonAsync(
            "/api/auth/confirm-email",
            new ConfirmEmailRequest { Token = token });
        Assert.Equal(HttpStatusCode.Conflict, replayResponse.StatusCode);
        ProblemDetails? replayProblem = await replayResponse.Content.ReadFromJsonAsync<ProblemDetails>();
        Assert.Equal("auth.confirmation_token_already_used", replayProblem!.Extensions["code"]!.ToString());
    }

    [Fact]
    public async Task OutboxEmailDeliveryRetriesAfterSmtpFailureAndDeliversLater()
    {
        using SqliteConfirmFixture fixture = new();

        using HttpResponseMessage registerResponse = await fixture.Client.PostAsJsonAsync("/api/auth/register", new RegisterRequest
        {
            UserName = "retry.user",
            Email = "retry.user@example.com",
            Password = "ValidPassword123!"
        });
        Assert.Equal(HttpStatusCode.Created, registerResponse.StatusCode);

        using (IServiceScope firstScope = fixture.Factory.Services.CreateScope())
        {
            AuthDbContext context = firstScope.ServiceProvider.GetRequiredService<AuthDbContext>();
            OutboxProcessor<AuthDbContext> failingProcessor = CreateEmailProcessor(firstScope.ServiceProvider, new FailingEmailSender(), fixture.Clock);
            Assert.Equal(0, await failingProcessor.ProcessBatchAsync(CancellationToken.None));

            OutboxMessage failed = await context.OutboxMessages.AsNoTracking()
                .SingleAsync(message => message.Type == AuthOutboxMessageTypes.ConfirmationEmailRequested);
            Assert.Equal(1, failed.AttemptCount);
            Assert.Equal("outbox.delivery_failed", failed.LastError);
            Assert.Equal(fixture.Clock.GetUtcNow().AddSeconds(2), failed.NextAttemptAt);
            Assert.Null(failed.ProcessedOn);
        }

        RecordingEmailSender recoverySender = new();
        using (IServiceScope retryScope = fixture.Factory.Services.CreateScope())
        {
            AuthDbContext context = retryScope.ServiceProvider.GetRequiredService<AuthDbContext>();
            OutboxProcessor<AuthDbContext> retryProcessor = CreateEmailProcessor(retryScope.ServiceProvider, recoverySender, fixture.Clock);
            Assert.Equal(0, await retryProcessor.ProcessBatchAsync(CancellationToken.None));

            fixture.Clock.Advance(TimeSpan.FromSeconds(2));
            Assert.Equal(1, await retryProcessor.ProcessBatchAsync(CancellationToken.None));

            OutboxMessage delivered = await context.OutboxMessages.AsNoTracking()
                .SingleAsync(message => message.Type == AuthOutboxMessageTypes.ConfirmationEmailRequested);
            Assert.Equal(2, delivered.AttemptCount);
            Assert.Null(delivered.LastError);
            Assert.NotNull(delivered.ProcessedOn);
        }

        EmailMessage sent = Assert.Single(recoverySender.Messages);
        string token = ExtractToken(sent.TextBody);
        using HttpResponseMessage confirm = await fixture.Client.PostAsJsonAsync(
            "/api/auth/confirm-email",
            new ConfirmEmailRequest { Token = token });
        Assert.Equal(HttpStatusCode.OK, confirm.StatusCode);
    }

    [Fact]
    public async Task GetConfirmationPageEscapesTokensAndRejectsMissingTokensWithoutConfirmButton()
    {
        using AuthApiFactory factory = new();
        using HttpClient client = factory.CreateClient();

        foreach (string path in new[] { "/api/auth/confirm-email", "/api/auth/confirm-email?token=%20%20" })
        {
            using HttpResponseMessage missingResponse = await client.GetAsync(path);
            Assert.Equal(HttpStatusCode.BadRequest, missingResponse.StatusCode);
            Assert.Equal("text/html", missingResponse.Content.Headers.ContentType!.MediaType);
            AssertSecurityHeaders(missingResponse);
            string missingHtml = await missingResponse.Content.ReadAsStringAsync();
            Assert.DoesNotContain("confirm-button", missingHtml, StringComparison.Ordinal);
        }

        string xssPayload = "\"><script>alert(1)</script>&";
        using HttpResponseMessage escapedResponse = await client.GetAsync(
            $"/api/auth/confirm-email?token={Uri.EscapeDataString(xssPayload)}");
        Assert.Equal(HttpStatusCode.OK, escapedResponse.StatusCode);
        AssertSecurityHeaders(escapedResponse);
        string escapedHtml = await escapedResponse.Content.ReadAsStringAsync();
        Assert.Contains("value=\"&quot;&gt;&lt;script&gt;alert(1)&lt;/script&gt;&amp;\"", escapedHtml, StringComparison.Ordinal);
        Assert.DoesNotContain(xssPayload, escapedHtml, StringComparison.Ordinal);
    }

    [Fact]
    public async Task PostEndpointSupportsBaseUrlWithQueryString()
    {
        using SqliteConfirmFixture customFixture = new(configureBuilder: builder =>
            builder.ConfigureAppConfiguration((_, config) => config.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["EmailConfirmation:ConfirmationBaseUrl"] = "https://auth.smartlost.local/api/auth/confirm-email?source=mailpit"
            })));

        using HttpResponseMessage registerResponse = await customFixture.Client.PostAsJsonAsync("/api/auth/register", new RegisterRequest
        {
            UserName = "post.confirm",
            Email = "post.confirm@example.com",
            Password = "ValidPassword123!"
        });
        Assert.Equal(HttpStatusCode.Created, registerResponse.StatusCode);

        RecordingEmailSender sender = new();
        using (IServiceScope scope = customFixture.Factory.Services.CreateScope())
        {
            Assert.Equal(1, await CreateEmailProcessor(scope.ServiceProvider, sender, customFixture.Clock).ProcessBatchAsync(CancellationToken.None));
        }

        EmailMessage sentEmail = Assert.Single(sender.Messages);
        Assert.Contains("https://auth.smartlost.local/api/auth/confirm-email?source=mailpit&token=", sentEmail.TextBody, StringComparison.Ordinal);

        string token = ExtractToken(sentEmail.TextBody);
        using HttpResponseMessage confirmResponse = await customFixture.Client.PostAsJsonAsync(
            "/api/auth/confirm-email",
            new ConfirmEmailRequest { Token = token });
        Assert.Equal(HttpStatusCode.OK, confirmResponse.StatusCode);
        EmailConfirmationResponse confirmed = (await confirmResponse.Content.ReadFromJsonAsync<EmailConfirmationResponse>())!;
        Assert.True(confirmed.EmailConfirmed);
    }

    [Fact]
    public async Task ExpiredUnknownAndMalformedTokensReturnExpectedErrors()
    {
        using SqliteConfirmFixture fixture = new();

        using HttpResponseMessage emptyPostResponse = await fixture.Client.PostAsJsonAsync(
            "/api/auth/confirm-email",
            new ConfirmEmailRequest { Token = " " });
        Assert.Equal(HttpStatusCode.BadRequest, emptyPostResponse.StatusCode);
        ProblemDetails emptyProblem = (await emptyPostResponse.Content.ReadFromJsonAsync<ProblemDetails>())!;
        Assert.Equal("auth.confirmation_token_required", emptyProblem.Extensions["code"]!.ToString());

        using HttpResponseMessage malformedResponse = await fixture.Client.PostAsJsonAsync(
            "/api/auth/confirm-email",
            new ConfirmEmailRequest { Token = "short-token" });
        Assert.Equal(HttpStatusCode.BadRequest, malformedResponse.StatusCode);
        ProblemDetails malformedProblem = (await malformedResponse.Content.ReadFromJsonAsync<ProblemDetails>())!;
        Assert.Equal("auth.invalid_confirmation_token", malformedProblem.Extensions["code"]!.ToString());

        using HttpResponseMessage unknownResponse = await fixture.Client.PostAsJsonAsync(
            "/api/auth/confirm-email",
            new ConfirmEmailRequest { Token = new string('Z', 43) });
        Assert.Equal(HttpStatusCode.BadRequest, unknownResponse.StatusCode);
        ProblemDetails unknownProblem = (await unknownResponse.Content.ReadFromJsonAsync<ProblemDetails>())!;
        Assert.Equal("auth.invalid_confirmation_token", unknownProblem.Extensions["code"]!.ToString());

        await fixture.Client.PostAsJsonAsync("/api/auth/register", new RegisterRequest
        {
            UserName = "expired.user",
            Email = "expired.user@example.com",
            Password = "ValidPassword123!"
        });
        RecordingEmailSender sender = new();
        using (IServiceScope scope = fixture.Factory.Services.CreateScope())
        {
            Assert.Equal(1, await CreateEmailProcessor(scope.ServiceProvider, sender, fixture.Clock).ProcessBatchAsync(CancellationToken.None));
            AuthDbContext context = scope.ServiceProvider.GetRequiredService<AuthDbContext>();
            EmailConfirmationToken storedToken = await context.EmailConfirmationTokens.SingleAsync();
            context.Entry(storedToken).Property(value => value.ExpiresAtUtc).CurrentValue = DateTime.UtcNow.AddMinutes(-1);
            await context.SaveChangesAsync();
        }

        string token = ExtractToken(Assert.Single(sender.Messages).TextBody);
        using HttpResponseMessage expiredResponse = await fixture.Client.PostAsJsonAsync(
            "/api/auth/confirm-email",
            new ConfirmEmailRequest { Token = token });
        Assert.Equal(HttpStatusCode.BadRequest, expiredResponse.StatusCode);
        ProblemDetails expiredProblem = (await expiredResponse.Content.ReadFromJsonAsync<ProblemDetails>())!;
        Assert.Equal("auth.confirmation_token_expired", expiredProblem.Extensions["code"]!.ToString());
    }

    [Fact]
    public async Task FailureAfterStagingEmailOutboxRollsBackUserSessionTokenAndOutbox()
    {
        using SqliteConfirmFixture fixture = new(configureBuilder: builder =>
            builder.ConfigureServices(services => services.DecorateConfirmationWithFailure()));

        using HttpResponseMessage response = await fixture.Client.PostAsJsonAsync("/api/auth/register", new RegisterRequest
        {
            UserName = "rollback.email",
            Email = "rollback.email@example.com",
            Password = "ValidPassword123!"
        });
        Assert.Equal(HttpStatusCode.InternalServerError, response.StatusCode);

        using IServiceScope scope = fixture.Factory.Services.CreateScope();
        AuthDbContext context = scope.ServiceProvider.GetRequiredService<AuthDbContext>();
        Assert.Equal(0, await context.Users.CountAsync());
        Assert.Equal(0, await context.RefreshSessions.CountAsync());
        Assert.Equal(0, await context.RefreshTokens.CountAsync());
        Assert.Equal(0, await context.EmailConfirmationTokens.CountAsync());
        Assert.Equal(0, await context.OutboxMessages.CountAsync());
    }

    [Fact]
    public async Task DirectEnqueueWithoutManagedTransactionPersistsTokenAndOutboxAtomically()
    {
        using SqliteConfirmFixture fixture = new();
        using IServiceScope scope = fixture.Factory.Services.CreateScope();
        AuthDbContext context = scope.ServiceProvider.GetRequiredService<AuthDbContext>();
        IEmailConfirmationService service = scope.ServiceProvider.GetRequiredService<IEmailConfirmationService>();

        var user = UserAccount.Create("direct.user", "direct.user@example.com", DateTime.UtcNow);
        user.SetPasswordHash("test-hash");
        context.Users.Add(user);

        await service.EnqueueConfirmationAsync(user, CancellationToken.None);

        Assert.Equal(1, await context.Users.CountAsync());
        Assert.Equal(1, await context.EmailConfirmationTokens.CountAsync());
        OutboxMessage emailOutbox = await context.OutboxMessages
            .SingleAsync(message => message.Type == AuthOutboxMessageTypes.ConfirmationEmailRequested);
        Assert.Equal(2, emailOutbox.Version);
        using var json = JsonDocument.Parse(emailOutbox.Payload);
        Assert.Equal(["encryptedToken", "expiresAtUtc", "recipient"],
            json.RootElement.EnumerateObject().Select(property => property.Name).Order(StringComparer.Ordinal));
        Assert.Equal("direct.user@example.com", json.RootElement.GetProperty("recipient").GetString());
        Assert.Equal((await context.EmailConfirmationTokens.SingleAsync()).ExpiresAtUtc,
            json.RootElement.GetProperty("expiresAtUtc").GetDateTimeOffset().UtcDateTime);
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

        RecordingEmailSender sender = new();
        using (IServiceScope scope = fixture.Factory.Services.CreateScope())
        {
            Assert.Equal(1, await CreateEmailProcessor(scope.ServiceProvider, sender, fixture.Clock).ProcessBatchAsync(CancellationToken.None));
        }

        string token = ExtractToken(Assert.Single(sender.Messages).TextBody);

        interceptor.BeforeSave = async () =>
        {
            DbContextOptions<AuthDbContext> options = new DbContextOptionsBuilder<AuthDbContext>()
                .UseSqlite(fixture.Connection)
                .ReplaceService<IModelCustomizer, SqliteDateTimeOffsetModelCustomizer>()
                .Options;
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

    [Fact]
    public async Task EmailPublisherValidatesEnvelopeContractAndPayload()
    {
        RecordingEmailSender sender = new();
        using SqliteConfirmFixture fixture = new();
        AuthOutboxEmailPublisher publisher = CreatePublisher(fixture.Factory.Services, sender, fixture.Clock);

        await Assert.ThrowsAsync<ArgumentNullException>(() => publisher.PublishAsync(null!, CancellationToken.None));
        await Assert.ThrowsAsync<InvalidOperationException>(() => publisher.PublishAsync(
            new OutboxEnvelope(Guid.NewGuid(), AuthIntegrationEventTypes.UserCreated, 1, "{}", DateTimeOffset.UtcNow),
            CancellationToken.None));
        await Assert.ThrowsAsync<InvalidOperationException>(() => publisher.PublishAsync(
            new OutboxEnvelope(Guid.NewGuid(), AuthOutboxMessageTypes.ConfirmationEmailRequested, 3, "{}", DateTimeOffset.UtcNow),
            CancellationToken.None));
        await Assert.ThrowsAsync<InvalidOperationException>(() => publisher.PublishAsync(
            new OutboxEnvelope(Guid.NewGuid(), AuthOutboxMessageTypes.ConfirmationEmailRequested, 1, "null", DateTimeOffset.UtcNow),
            CancellationToken.None));
        Assert.Empty(sender.Messages);

        var legacy = new EmailMessage("old@example.com", "Legacy confirmation", "Already queued", null);
        var previousEnvelope = new OutboxEnvelope(Guid.NewGuid(), AuthOutboxMessageTypes.ConfirmationEmailRequested,
            1, JsonSerializer.Serialize(legacy, JsonSerializerOptions.Web), DateTimeOffset.UtcNow);
        await publisher.PublishAsync(previousEnvelope, CancellationToken.None);
        Assert.Equal(legacy.TextBody, Assert.Single(sender.Messages).TextBody);
    }

    [Fact]
    public async Task WorkerRendersOnlyUnexpiredRequestsAndReusesTheSameToken()
    {
        using SqliteConfirmFixture fixture = new();
        using IServiceScope scope = fixture.Factory.Services.CreateScope();
        var user = UserAccount.Create("template.user", "template@example.com", DateTime.UtcNow);
        user.SetPasswordHash("test-hash");
        AuthDbContext context = scope.ServiceProvider.GetRequiredService<AuthDbContext>();
        context.Users.Add(user);
        await scope.ServiceProvider.GetRequiredService<IEmailConfirmationService>()
            .EnqueueConfirmationAsync(user, CancellationToken.None);
        OutboxMessage row = await context.OutboxMessages.SingleAsync(m => m.Type == AuthOutboxMessageTypes.ConfirmationEmailRequested);
        OutboxEnvelope envelope = row.ToEnvelope();

        // A separate worker host receives the same secret, as it would in another pod.
        string sharedKey = scope.ServiceProvider.GetRequiredService<IOptions<EmailConfirmationOptions>>().Value.EncryptionKey;
        using IHost worker = WorkerProgram.BuildHost(
        [
            "--ConnectionStrings:AuthDatabase=Host=localhost;Database=unused",
            "--Worker:Mode=Email",
            "--EmailConfirmation:EncryptionKey=" + sharedKey,
            "--EmailConfirmation:ConfirmationBaseUrl=https://worker.example.com/api/auth/confirm-email"
        ]);
        RecordingEmailSender sender = new();
        AuthOutboxEmailPublisher publisher = new(sender, worker.Services.GetRequiredService<AuthEmailComposer>(), fixture.Clock);
        await publisher.PublishAsync(envelope, CancellationToken.None);
        await publisher.PublishAsync(envelope, CancellationToken.None);
        EmailMessage[] sent = [.. sender.Messages];
        Assert.Equal(2, sent.Length);
        Assert.Contains("https://worker.example.com/api/auth/confirm-email?", sent[0].TextBody, StringComparison.Ordinal);
        string token = ExtractToken(sent[0].TextBody);
        Assert.Equal(token, ExtractToken(sent[1].TextBody));
        Assert.DoesNotContain(token, row.Payload, StringComparison.Ordinal);

        fixture.Clock.Advance(TimeSpan.FromDays(2));
        await publisher.PublishAsync(envelope, CancellationToken.None);
        Assert.Equal(2, sender.Messages.Count);
    }

    [Fact]
    public void ProtectedTokenIsBoundToItsKeyAndRequest()
    {
        IOptions<EmailConfirmationOptions> options = Options.Create(new EmailConfirmationOptions
        {
            EncryptionKey = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32))
        });
        ConfirmationTokenProtector protector = new(options);
        var id = Guid.NewGuid();
        DateTimeOffset expiry = DateTimeOffset.UtcNow.AddHours(1);
        const string Recipient = "intended@example.com";
        string token = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32));
        string ciphertext = protector.Protect(token, id, Recipient, expiry);
        Assert.NotEqual(ciphertext, protector.Protect(token, id, Recipient, expiry));
        Assert.Equal(token, new ConfirmationTokenProtector(options).Unprotect(ciphertext, id, Recipient, expiry));
        Assert.Throws<AuthenticationTagMismatchException>(() => protector.Unprotect(ciphertext, id, "other@example.com", expiry));
        Assert.Throws<AuthenticationTagMismatchException>(() => protector.Unprotect(ciphertext, Guid.NewGuid(), Recipient, expiry));
        Assert.Throws<AuthenticationTagMismatchException>(() => protector.Unprotect(ciphertext, id, Recipient, expiry.AddMinutes(1)));
        byte[] tampered = Convert.FromBase64String(ciphertext);
        tampered[^1] ^= 1;
        Assert.Throws<AuthenticationTagMismatchException>(() => protector.Unprotect(Convert.ToBase64String(tampered), id, Recipient, expiry));
        Assert.Throws<CryptographicException>(() => protector.Unprotect("AA==", id, Recipient, expiry));
        ConfirmationTokenProtector otherKey = new(Options.Create(new EmailConfirmationOptions
        {
            EncryptionKey = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32))
        }));
        Assert.Throws<AuthenticationTagMismatchException>(() => otherKey.Unprotect(ciphertext, id, Recipient, expiry));
    }

    private static OutboxProcessor<AuthDbContext> CreateEmailProcessor(
        IServiceProvider services, IEmailSender emailSender, TimeProvider timeProvider)
    {
        return new OutboxProcessor<AuthDbContext>(
            services.GetRequiredService<AuthDbContext>(),
            CreatePublisher(services, emailSender, timeProvider),
            Options.Create(new OutboxOptions
            {
                MessageTypes = [AuthOutboxMessageTypes.ConfirmationEmailRequested]
            }),
            timeProvider,
            NullLogger<OutboxProcessor<AuthDbContext>>.Instance);
    }

    private static AuthOutboxEmailPublisher CreatePublisher(IServiceProvider services, IEmailSender sender, TimeProvider clock)
    {
        IOptions<EmailConfirmationOptions> options = services.GetRequiredService<IOptions<EmailConfirmationOptions>>();
        return new AuthOutboxEmailPublisher(sender, new AuthEmailComposer(new ConfirmationTokenProtector(options), options), clock);
    }

    private static void AssertSecurityHeaders(HttpResponseMessage response)
    {
        Assert.Equal("no-referrer", Assert.Single(response.Headers.GetValues("Referrer-Policy")));
        Assert.Equal("nosniff", Assert.Single(response.Headers.GetValues("X-Content-Type-Options")));
        Assert.Equal("DENY", Assert.Single(response.Headers.GetValues("X-Frame-Options")));
        string csp = Assert.Single(response.Headers.GetValues("Content-Security-Policy"));
        Assert.Contains("default-src 'none'", csp, StringComparison.Ordinal);
        Assert.Contains("frame-ancestors 'none'", csp, StringComparison.Ordinal);
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

    private sealed class RecordingEmailSender : IEmailSender
    {
        public ConcurrentQueue<EmailMessage> Messages { get; } = new();

        public Task SendAsync(EmailMessage message, CancellationToken cancellationToken)
        {
            Messages.Enqueue(message);
            return Task.CompletedTask;
        }
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

    private sealed class TestClock : TimeProvider
    {
        private DateTimeOffset _now = DateTimeOffset.UtcNow;

        public override DateTimeOffset GetUtcNow()
        {
            return _now;
        }

        public void Advance(TimeSpan duration)
        {
            _now = _now.Add(duration);
        }
    }

    private sealed class SqliteDateTimeOffsetModelCustomizer(ModelCustomizerDependencies dependencies)
        : RelationalModelCustomizer(dependencies)
    {
        public override void Customize(ModelBuilder modelBuilder, DbContext context)
        {
            base.Customize(modelBuilder, context);
            ValueConverter<DateTimeOffset, long> converter = new(
                value => value.UtcTicks,
                value => new DateTimeOffset(value, TimeSpan.Zero));
            foreach (IMutableProperty property in modelBuilder.Model.GetEntityTypes()
                .SelectMany(entity => entity.GetProperties())
                .Where(property => property.ClrType == typeof(DateTimeOffset) || property.ClrType == typeof(DateTimeOffset?)))
            {
                property.SetValueConverter(converter);
            }
        }
    }

    private sealed class SqliteConfirmFixture : IDisposable
    {
        private readonly AuthApiFactory _parent = new();

        public SqliteConnection Connection { get; } = new("Data Source=:memory:");

        public TestClock Clock { get; } = new();

        public WebApplicationFactory<Program> Factory
        {
            get;
        }

        public HttpClient Client
        {
            get;
        }

        public SqliteConfirmFixture(
            IInterceptor? interceptor = null,
            Action<Microsoft.AspNetCore.Hosting.IWebHostBuilder>? configureBuilder = null)
        {
            Connection.Open();
            Factory = _parent.WithWebHostBuilder(builder =>
            {
                builder.ConfigureServices(services =>
                {
                    services.RemoveAll<DbContextOptions<AuthDbContext>>();
                    services.RemoveAll<DbContextOptions>();
                    services.RemoveAll<IDbContextOptionsConfiguration<AuthDbContext>>();
                    services.AddDbContext<AuthDbContext>(options =>
                    {
                        options.UseSqlite(Connection);
                        options.ReplaceService<IModelCustomizer, SqliteDateTimeOffsetModelCustomizer>();
                        if (interceptor is not null)
                        {
                            options.AddInterceptors(interceptor);
                        }
                    });
                });
                configureBuilder?.Invoke(builder);
            });
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

internal static class EmailConfirmationServiceTestExtensions
{
    public static void DecorateConfirmationWithFailure(this IServiceCollection services)
    {
        services.AddScoped<SmartLost.AuthService.Infrastructure.Authentication.EmailConfirmationService>();
        services.RemoveAll<IEmailConfirmationService>();
        services.AddScoped<IEmailConfirmationService, FlushThenFailConfirmationService>();
    }

    private sealed class FlushThenFailConfirmationService(
        SmartLost.AuthService.Infrastructure.Authentication.EmailConfirmationService inner,
        AuthDbContext context) : IEmailConfirmationService
    {
        public async Task EnqueueConfirmationAsync(UserAccount user, CancellationToken cancellationToken)
        {
            await inner.EnqueueConfirmationAsync(user, cancellationToken);
            await context.SaveChangesAsync(cancellationToken);
            throw new InvalidOperationException("Simulated failure before commit.");
        }

        public Task<SmartLost.BuildingBlocks.Core.Results.Result<EmailConfirmationResponse>> ConfirmAsync(
            string token, CancellationToken cancellationToken)
        {
            return inner.ConfirmAsync(token, cancellationToken);
        }
    }
}
