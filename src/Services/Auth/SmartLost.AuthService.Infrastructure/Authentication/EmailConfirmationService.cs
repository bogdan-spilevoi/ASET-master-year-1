using System.Net;
using System.Security.Cryptography;
using System.Text;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using SmartLost.AuthService.Application.Contracts;
using SmartLost.AuthService.Application.Interfaces;
using SmartLost.AuthService.Domain.Entities;
using SmartLost.AuthService.Domain.Errors;
using SmartLost.AuthService.Infrastructure.Persistence;
using SmartLost.BuildingBlocks.Application.Emails;
using SmartLost.BuildingBlocks.Core.Results;

namespace SmartLost.AuthService.Infrastructure.Authentication;

public sealed class EmailConfirmationService(
    AuthDbContext dbContext,
    IUserAccountRepository userAccountRepository,
    IEmailSender emailSender,
    IOptions<EmailConfirmationOptions> options) : IEmailConfirmationService
{
    public async Task SendConfirmationAsync(UserAccount user, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(user);
        EmailConfirmationOptions settings = options.Value;
        string rawToken = Base64UrlEncoder.Encode(RandomNumberGenerator.GetBytes(32));
        string tokenHash = HashToken(rawToken);
        DateTime nowUtc = TruncateToMicroseconds(DateTime.UtcNow);
        DateTime expiresAtUtc = nowUtc.AddHours(settings.TokenExpiryHours);

        var confirmationToken = EmailConfirmationToken.Create(user, tokenHash, nowUtc, expiresAtUtc);
        dbContext.EmailConfirmationTokens.Add(confirmationToken);
        // Flush pending user, session, token and outbox changes before dispatching the email.
        await userAccountRepository.SaveChangesAsync(cancellationToken);

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

        await emailSender.SendAsync(
            new EmailMessage(user.Email, "Confirm your SmartLost email address", textBody, htmlBody),
            cancellationToken);
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
