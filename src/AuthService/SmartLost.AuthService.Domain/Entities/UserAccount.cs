using SmartLost.AuthService.Domain.Identity;
using SmartLost.BuildingBlocks.Core.Entities;

namespace SmartLost.AuthService.Domain.Entities;

public sealed class UserAccount : Entity<Guid>
{
    public string UserName { get; private set; } = string.Empty;

    public string NormalizedUserName { get; private set; } = string.Empty;

    public string Email { get; private set; } = string.Empty;

    public string NormalizedEmail { get; private set; } = string.Empty;

    public string PasswordHash { get; private set; } = string.Empty;

    public DateTime CreatedAtUtc
    {
        get; private set;
    }

    private UserAccount()
    {
    }

    public static UserAccount Create(
        string userName,
        string email,
        DateTime createdAtUtc)
    {
        UserAccount userAccount = new()
        {
            Id = Guid.NewGuid(),
            CreatedAtUtc = createdAtUtc
        };
        userAccount.ChangeIdentity(userName, email);
        return userAccount;
    }

    public void ChangeIdentity(string userName, string email)
    {
        // Calculate both keys first so invalid input cannot partially change the identity.
        string normalizedUserName = UserIdentityNormalizer.Normalize(userName);
        string normalizedEmail = UserIdentityNormalizer.Normalize(email);

        UserName = userName.Trim();
        NormalizedUserName = normalizedUserName;
        Email = email.Trim();
        NormalizedEmail = normalizedEmail;
    }

    public void SetPasswordHash(string passwordHash)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(passwordHash);
        PasswordHash = passwordHash;
    }
}
