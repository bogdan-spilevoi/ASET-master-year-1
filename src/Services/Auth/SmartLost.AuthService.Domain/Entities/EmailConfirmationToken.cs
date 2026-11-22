using SmartLost.AuthService.Domain.Errors;
using SmartLost.BuildingBlocks.Core.Entities;
using SmartLost.BuildingBlocks.Core.Validation;

namespace SmartLost.AuthService.Domain.Entities;

public sealed class EmailConfirmationToken : Entity<Guid>
{
    public Guid UserId
    {
        get; private set;
    }

    public UserAccount User { get; private set; } = null!;

    public string TokenHash { get; private set; } = string.Empty;

    public DateTime CreatedAtUtc
    {
        get; private set;
    }

    public DateTime ExpiresAtUtc
    {
        get; private set;
    }

    public DateTime? UsedAtUtc
    {
        get; private set;
    }

    private EmailConfirmationToken()
    {
    }

    public static EmailConfirmationToken Create(
        UserAccount user,
        string tokenHash,
        DateTime createdAtUtc,
        DateTime expiresAtUtc)
    {
        Guard.NotNull(user, AuthErrors.UserRequired);
        Guard.Against(user.Id == Guid.Empty, AuthErrors.UserRequired);
        Guard.NotNullOrWhiteSpace(tokenHash, AuthErrors.InvalidTokenHash, 64);
        Guard.Against(createdAtUtc == default || createdAtUtc.Kind != DateTimeKind.Utc, AuthErrors.InvalidUtcTimestamp);
        Guard.Against(
            expiresAtUtc == default || expiresAtUtc.Kind != DateTimeKind.Utc || expiresAtUtc <= createdAtUtc,
            AuthErrors.InvalidUtcTimestamp);

        return new EmailConfirmationToken
        {
            Id = Guid.NewGuid(),
            UserId = user.Id,
            User = user,
            TokenHash = tokenHash,
            CreatedAtUtc = createdAtUtc,
            ExpiresAtUtc = expiresAtUtc
        };
    }

    public void Consume(DateTime usedAtUtc)
    {
        Guard.Against(usedAtUtc == default || usedAtUtc.Kind != DateTimeKind.Utc, AuthErrors.InvalidUtcTimestamp);
        Guard.Against(UsedAtUtc is not null, AuthErrors.EmailConfirmationTokenAlreadyUsed);
        Guard.Against(ExpiresAtUtc <= usedAtUtc, AuthErrors.EmailConfirmationTokenExpired);
        UsedAtUtc = usedAtUtc;
        User.ConfirmEmail(usedAtUtc);
    }
}
