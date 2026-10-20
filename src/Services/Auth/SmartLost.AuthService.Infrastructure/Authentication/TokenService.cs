using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using SmartLost.AuthService.Application.Interfaces;
using SmartLost.AuthService.Domain.Entities;

namespace SmartLost.AuthService.Infrastructure.Authentication;

public sealed class TokenService(IOptions<JwtOptions> options) : ITokenService
{
    private readonly JwtOptions _options = options.Value;

    public TokenResult CreateToken(UserAccount userAccount)
    {
        DateTime issuedAt = DateTime.UtcNow;
        DateTime expiresAt = issuedAt.AddMinutes(_options.ExpiryMinutes);
        var signingKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(_options.SigningKey));
        var credentials = new SigningCredentials(signingKey, SecurityAlgorithms.HmacSha256);
        Claim[] claims =
        [
            new Claim(JwtRegisteredClaimNames.Sub, userAccount.Id.ToString()),
            new Claim(JwtRegisteredClaimNames.UniqueName, userAccount.UserName),
            new Claim(JwtRegisteredClaimNames.Email, userAccount.Email),
            new Claim(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString("N"))
        ];
        var token = new JwtSecurityToken(_options.Issuer, _options.Audience, claims, issuedAt, expiresAt, credentials);
        return new TokenResult(new JwtSecurityTokenHandler().WriteToken(token), expiresAt);
    }
}
