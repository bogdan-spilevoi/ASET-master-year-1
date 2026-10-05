namespace SmartLost.AuthService.Domain.Entities;

public sealed class UserAccount
{
    public Guid Id
    {
        get; private set;
    }

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
        string normalizedUserName,
        string email,
        string normalizedEmail,
        DateTime createdAtUtc)
    {
        return new UserAccount
        {
            Id = Guid.NewGuid(),
            UserName = userName,
            NormalizedUserName = normalizedUserName,
            Email = email,
            NormalizedEmail = normalizedEmail,
            CreatedAtUtc = createdAtUtc
        };
    }

    public void SetPasswordHash(string passwordHash)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(passwordHash);
        PasswordHash = passwordHash;
    }
}
