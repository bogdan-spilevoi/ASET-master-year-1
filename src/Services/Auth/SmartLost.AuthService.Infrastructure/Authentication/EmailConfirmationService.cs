using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using SmartLost.AuthService.Application.Contracts;
using SmartLost.AuthService.Application.IntegrationEvents;
using SmartLost.AuthService.Application.Interfaces;
using SmartLost.AuthService.Domain.Entities;
using SmartLost.AuthService.Domain.Errors;
using SmartLost.AuthService.Infrastructure.Persistence;
using SmartLost.BuildingBlocks.Application.Emails;
using SmartLost.BuildingBlocks.Application.Outbox;
using SmartLost.BuildingBlocks.Core.Results;

namespace SmartLost.AuthService.Infrastructure.Authentication;

public sealed class EmailConfirmationService(
    AuthDbContext dbContext,
    IUserAccountRepository userAccountRepository,
    IOutboxWriter outboxWriter,
    IOptions<EmailConfirmationOptions> options) : IEmailConfirmationService
{
    public async Task EnqueueConfirmationAsync(UserAccount user, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(user);
        EmailConfirmationOptions settings = options.Value;
        string rawToken = Base64UrlEncoder.Encode(RandomNumberGenerator.GetBytes(32));
        string tokenHash = HashToken(rawToken);
        DateTime nowUtc = TruncateToMicroseconds(DateTime.UtcNow);
        DateTime expiresAtUtc = nowUtc.AddHours(settings.TokenExpiryHours);

        var confirmationToken = EmailConfirmationToken.Create(user, tokenHash, nowUtc, expiresAtUtc);
        dbContext.EmailConfirmationTokens.Add(confirmationToken);

        string baseUrl = settings.ConfirmationBaseUrl.Trim();
        string separator = baseUrl.Contains('?', StringComparison.Ordinal) ? "&" : "?";
        string confirmationUrl = $"{baseUrl}{separator}token={Uri.EscapeDataString(rawToken)}";
        string encodedUrl = WebUtility.HtmlEncode(confirmationUrl);
        string encodedUserName = WebUtility.HtmlEncode(user.UserName);
        string textBody =
            $"Hello {user.UserName},\n\n" +
            $"Confirm your SmartLost email address by opening the link below:\n{confirmationUrl}\n\n" +
            $"This link expires in {settings.TokenExpiryHours} hours.";
        string htmlBody =
            $"<p>Hello {encodedUserName},</p>" +
            $"<p>Confirm your SmartLost email address by clicking <a href=\"{encodedUrl}\">Confirm email</a>.</p>" +
            $"<p>If the link does not open, copy this URL into your browser: {encodedUrl}</p>" +
            $"<p>This link expires in {settings.TokenExpiryHours} hours.</p>";

        var emailMessage = new EmailMessage(user.Email, "Confirm your SmartLost email address", textBody, htmlBody);
        string payload = JsonSerializer.Serialize(emailMessage, JsonSerializerOptions.Web);
        outboxWriter.Enqueue(new OutboxEnvelope(
            confirmationToken.Id,
            AuthOutboxMessageTypes.ConfirmationEmailRequested,
            1,
            payload,
            nowUtc));

        if (!dbContext.HasManagedTransaction)
        {
            await userAccountRepository.SaveChangesAsync(cancellationToken);
        }
    }

    public async Task<Result<EmailConfirmationResponse>> ConfirmAsync(string token, CancellationToken cancellationToken)
    {
        string hash = HashToken(token);
        EmailConfirmationToken? storedToken = await dbContext.EmailConfirmationTokens
            .Include(entry => entry.User)
            .SingleOrDefaultAsync(entry => entry.TokenHash == hash, cancellationToken);
        if (storedToken is null)
        {
            return Result<EmailConfirmationResponse>.Failure(AuthErrors.InvalidEmailConfirmationToken);
        }

        if (storedToken.UsedAtUtc is not null)
        {
            return Result<EmailConfirmationResponse>.Failure(AuthErrors.EmailConfirmationTokenAlreadyUsed);
        }

        DateTime nowUtc = TruncateToMicroseconds(DateTime.UtcNow);
        if (storedToken.ExpiresAtUtc <= nowUtc)
        {
            return Result<EmailConfirmationResponse>.Failure(AuthErrors.EmailConfirmationTokenExpired);
        }

        storedToken.Consume(nowUtc);
        try
        {
            await dbContext.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            return Result<EmailConfirmationResponse>.Failure(AuthErrors.EmailConfirmationTokenAlreadyUsed);
        }

        return Result<EmailConfirmationResponse>.Success(
            new EmailConfirmationResponse(
                storedToken.UserId,
                storedToken.User.Email,
                storedToken.User.EmailConfirmed,
                storedToken.User.EmailConfirmedAtUtc!.Value));
    }

    private static string HashToken(string token)
    {
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(token)));
    }

    private static DateTime TruncateToMicroseconds(DateTime timestampUtc)
    {
        return timestampUtc.AddTicks(-(timestampUtc.Ticks % TimeSpan.TicksPerMicrosecond));
    }
}
