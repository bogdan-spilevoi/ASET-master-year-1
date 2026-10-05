using SmartLost.AuthService.Domain.Entities;

namespace SmartLost.AuthService.Application.Interfaces;

public interface ITokenService
{
    TokenResult CreateToken(UserAccount userAccount);
}

public sealed record TokenResult(string AccessToken, DateTime ExpiresAtUtc);
