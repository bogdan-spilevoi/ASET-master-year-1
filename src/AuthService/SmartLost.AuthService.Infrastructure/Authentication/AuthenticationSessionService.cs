using System.Security.Cryptography;
using System.Text;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using SmartLost.AuthService.Application.Contracts;
using SmartLost.AuthService.Application.Interfaces;
using SmartLost.AuthService.Domain.Entities;
using SmartLost.AuthService.Infrastructure.Persistence;
using SmartLost.BuildingBlocks.Core.Results;

namespace SmartLost.AuthService.Infrastructure.Authentication;

public sealed class AuthenticationSessionService(
    AuthDbContext dbContext,
    IUserAccountRepository userAccountRepository,
    ITokenService tokenService,
    IOptions<JwtOptions> options) : IAuthenticationSessionService
{
    public async Task<AuthResponse> CreateAsync(UserAccount user, CancellationToken cancellationToken)
    {
        string refreshToken = CreateRefreshToken();
        string hash = HashToken(refreshToken);
        DateTime expiresAtUtc = DateTime.UtcNow.AddDays(options.Value.RefreshTokenExpiryDays);
        // PostgreSQL stores microseconds; return exactly the precision that will round-trip.
        expiresAtUtc = expiresAtUtc.AddTicks(-(expiresAtUtc.Ticks % TimeSpan.TicksPerMicrosecond));
        var session = RefreshSession.Create(user, hash, expiresAtUtc);
        AuthResponse response = CreateResponse(user, refreshToken, session.ExpiresAtUtc);
        dbContext.RefreshSessions.Add(session);
        dbContext.RefreshTokens.Add(RefreshToken.Create(session, hash));
        // Registration's tracked user and its first session are committed together.
        await userAccountRepository.SaveChangesAsync(cancellationToken);
        return response;
    }

    public async Task<Result<AuthResponse>> RefreshAsync(string refreshToken, CancellationToken cancellationToken)
    {
        string hash = HashToken(refreshToken);
        RefreshToken? storedToken = await dbContext.RefreshTokens
            .Include(token => token.Session).ThenInclude(session => session.User)
            .SingleOrDefaultAsync(token => token.TokenHash == hash, cancellationToken);
        DateTime nowUtc = DateTime.UtcNow;
        if (storedToken is null || !storedToken.Session.IsActive(nowUtc))
        {
            return InvalidToken();
        }

        RefreshSession session = storedToken.Session;
        if (!string.Equals(session.CurrentTokenHash, hash, StringComparison.Ordinal))
        {
            await RevokeSessionAsync(session.Id, nowUtc, cancellationToken);
            return InvalidToken();
        }

        string replacement = CreateRefreshToken();
        string replacementHash = HashToken(replacement);
        AuthResponse response = CreateResponse(session.User, replacement, session.ExpiresAtUtc);
        session.Rotate(replacementHash);
        dbContext.RefreshTokens.Add(RefreshToken.Create(session, replacementHash));
        try
        {
            // EF commits the session update and token history insert in one transaction.
            // Hash + revocation concurrency checks allow only one consumer of a token.
            await dbContext.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            // A concurrent refresh or replay invalidates the whole session.
            await RevokeSessionAsync(session.Id, nowUtc, cancellationToken);
            return InvalidToken();
        }

        return Result<AuthResponse>.Success(response);
    }

    private Task<int> RevokeSessionAsync(Guid sessionId, DateTime nowUtc, CancellationToken cancellationToken)
    {
        // Update the shared session row, so concurrent rotations also observe revocation.
        return dbContext.RefreshSessions.Where(session => session.Id == sessionId && session.RevokedAtUtc == null)
            .ExecuteUpdateAsync(setters => setters.SetProperty(session => session.RevokedAtUtc, nowUtc), cancellationToken);
    }

    private AuthResponse CreateResponse(UserAccount user, string refreshToken, DateTime refreshTokenExpiresAtUtc)
    {
        TokenResult token = tokenService.CreateToken(user);
        return new AuthResponse(user.Id, user.UserName, user.Email, token.AccessToken, token.ExpiresAtUtc,
            refreshToken, refreshTokenExpiresAtUtc);
    }

    private static string CreateRefreshToken()
    {
        return Base64UrlEncoder.Encode(RandomNumberGenerator.GetBytes(64));
    }

    private static string HashToken(string refreshToken)
    {
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(refreshToken)));
    }

    private static Result<AuthResponse> InvalidToken()
    {
        return Result<AuthResponse>.Failure(new Error("auth.invalid_refresh_token", "Invalid or expired refresh token.", ErrorKind.Unauthorized));
    }
}
