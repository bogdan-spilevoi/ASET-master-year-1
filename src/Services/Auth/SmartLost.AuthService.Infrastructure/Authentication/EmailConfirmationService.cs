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
using SmartLost.BuildingBlocks.Application.Outbox;
using SmartLost.BuildingBlocks.Core.Results;

namespace SmartLost.AuthService.Infrastructure.Authentication;

public sealed class EmailConfirmationService(
    AuthDbContext dbContext,
    IUserAccountRepository userAccountRepository,
    IOutboxWriter outboxWriter,
    ConfirmationTokenProtector protector,
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

        string encryptedToken = protector.Protect(rawToken, confirmationToken.Id, user.Email, expiresAtUtc);
        var request = new ConfirmationEmailRequested
        {
            Recipient = user.Email,
            EncryptedToken = encryptedToken,
            ExpiresAtUtc = expiresAtUtc
        };
        string payload = JsonSerializer.Serialize(request, JsonSerializerOptions.Web);
        outboxWriter.Enqueue(new OutboxEnvelope(
            confirmationToken.Id,
            AuthOutboxMessageTypes.ConfirmationEmailRequested,
            2,
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
