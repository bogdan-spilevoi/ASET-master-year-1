using SmartLost.AuthService.Domain.Errors;
using SmartLost.AuthService.Domain.Events;
using SmartLost.AuthService.Domain.Identity;
using SmartLost.BuildingBlocks.Core.Entities;
using SmartLost.BuildingBlocks.Core.Validation;

namespace SmartLost.AuthService.Domain.Entities;

public sealed class UserAccount : AggregateRoot<Guid>
{
    public string UserName { get; private set; } = string.Empty;

    public string NormalizedUserName { get; private set; } = string.Empty;

    public string Email { get; private set; } = string.Empty;

    public string NormalizedEmail { get; private set; } = string.Empty;

    public string PasswordHash { get; private set; } = string.Empty;

    public long IdentityVersion
    {
        get; private set;
    } = 1;

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
        Guard.Against(createdAtUtc == default || createdAtUtc.Kind != DateTimeKind.Utc, AuthErrors.InvalidUtcTimestamp);
        UserAccount userAccount = new()
        {
            Id = Guid.NewGuid(),
            CreatedAtUtc = createdAtUtc
        };
        userAccount.SetIdentity(userName, email);
        userAccount.AddDomainEvent(new UserAccountCreated(
            userAccount.Id, userAccount.UserName, userAccount.Email, userAccount.IdentityVersion, new DateTimeOffset(createdAtUtc)));
        return userAccount;
    }

    public void ChangeIdentity(string userName, string email)
    {
        Guard.Against(IdentityVersion < 1 || IdentityVersion == long.MaxValue, AuthErrors.InvalidIdentityVersion);
        string previousUserName = UserName;
        string previousEmail = Email;
        SetIdentity(userName, email);
        if (!string.Equals(previousUserName, UserName, StringComparison.Ordinal) ||
            !string.Equals(previousEmail, Email, StringComparison.Ordinal))
        {
            IdentityVersion++;
            AddDomainEvent(new UserIdentityChanged(Id, UserName, Email, IdentityVersion, DateTimeOffset.UtcNow));
        }
    }

    public void SetPasswordHash(string passwordHash)
    {
        Guard.NotNullOrWhiteSpace(passwordHash, AuthErrors.PasswordHashRequired);
        PasswordHash = passwordHash;
    }

    private void SetIdentity(string userName, string email)
    {
        Guard.NotNullOrWhiteSpace(userName, AuthErrors.UserNameRequired);
        Guard.NotNullOrWhiteSpace(email, AuthErrors.EmailRequired);
        string trimmedUserName = userName.Trim();
        string trimmedEmail = email.Trim();
        Guard.InRange(trimmedUserName.Length, 3, 100, AuthErrors.InvalidUserName);
        Guard.NotNullOrWhiteSpace(trimmedEmail, AuthErrors.InvalidEmail, 256);
        // Calculate both keys first so invalid input cannot partially change the identity.
        string normalizedUserName = UserIdentityNormalizer.Normalize(trimmedUserName);
        string normalizedEmail = UserIdentityNormalizer.Normalize(trimmedEmail);
        Guard.InRange(normalizedUserName.Length, 3, 100, AuthErrors.InvalidUserName);
        Guard.NotNullOrWhiteSpace(normalizedEmail, AuthErrors.InvalidEmail, 256);

        UserName = trimmedUserName;
        NormalizedUserName = normalizedUserName;
        Email = trimmedEmail;
        NormalizedEmail = normalizedEmail;
    }
}
