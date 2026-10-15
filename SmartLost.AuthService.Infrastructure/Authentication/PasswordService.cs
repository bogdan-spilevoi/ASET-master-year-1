using Microsoft.AspNetCore.Identity;
using SmartLost.AuthService.Application.Interfaces;
using SmartLost.AuthService.Domain.Entities;

namespace SmartLost.AuthService.Infrastructure.Authentication;

public sealed class PasswordService(IPasswordHasher<UserAccount> passwordHasher) : IPasswordService
{
    public string Hash(UserAccount userAccount, string password)
    {
        return passwordHasher.HashPassword(userAccount, password);
    }

    public bool Verify(UserAccount userAccount, string password)
    {
        PasswordVerificationResult result = passwordHasher.VerifyHashedPassword(userAccount, userAccount.PasswordHash, password);
        return result != PasswordVerificationResult.Failed;
    }
}
