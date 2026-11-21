using System.Globalization;
using SmartLost.AuthService.Domain.Entities;
using SmartLost.AuthService.Domain.Identity;
using SmartLost.BuildingBlocks.Core.Exceptions;
using Xunit;

namespace SmartLost.AuthService.UnitTests;

public sealed class UserIdentityTests
{
    [Fact]
    public void NormalizationUsesInvariantCasingRegardlessOfCurrentCulture()
    {
        CultureInfo originalCulture = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("tr-TR");
            Assert.Equal("IONESCU", UserIdentityNormalizer.Normalize("  ionescu  "));
            Assert.Equal("IONESCU@EXAMPLE.COM", UserIdentityNormalizer.Normalize("  ionescu@example.com  "));
        }
        finally
        {
            CultureInfo.CurrentCulture = originalCulture;
        }
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData(" \t\n ")]
    public void NormalizationRejectsMissingIdentity(string? value)
    {
        Assert.ThrowsAny<ArgumentException>(() => UserIdentityNormalizer.Normalize(value!));
    }

    [Fact]
    public void CreationDerivesBothKeysAndPreservesOriginalCasing()
    {
        DateTime createdAtUtc = new(2026, 10, 5, 12, 0, 0, DateTimeKind.Utc);
        var account = UserAccount.Create("  Bogdan  ", "  Bogdan@Example.com  ", createdAtUtc);

        Assert.NotEqual(Guid.Empty, account.Id);
        Assert.Equal("Bogdan", account.UserName);
        Assert.Equal("BOGDAN", account.NormalizedUserName);
        Assert.Equal("Bogdan@Example.com", account.Email);
        Assert.Equal("BOGDAN@EXAMPLE.COM", account.NormalizedEmail);
        Assert.Equal(createdAtUtc, account.CreatedAtUtc);
    }

    [Fact]
    public void IdentityChangesUpdateOriginalsAndKeysTogether()
    {
        DateTime createdAtUtc = DateTime.UtcNow;
        var account = UserAccount.Create("Bogdan", "bogdan@example.com", createdAtUtc);
        Guid id = account.Id;
        account.SetPasswordHash("existing-hash");

        account.ChangeIdentity("  Maria  ", "  Maria@Example.com  ");

        Assert.Equal("Maria", account.UserName);
        Assert.Equal("MARIA", account.NormalizedUserName);
        Assert.Equal("Maria@Example.com", account.Email);
        Assert.Equal("MARIA@EXAMPLE.COM", account.NormalizedEmail);
        Assert.Equal(id, account.Id);
        Assert.Equal(createdAtUtc, account.CreatedAtUtc);
        Assert.Equal("existing-hash", account.PasswordHash);
    }

    [Theory]
    [InlineData(null, "new@example.com")]
    [InlineData("   ", "new@example.com")]
    [InlineData("NewName", null)]
    [InlineData("NewName", "   ")]
    public void InvalidChangesLeaveTheEntireIdentityIntact(string? userName, string? email)
    {
        var account = UserAccount.Create("Bogdan", "bogdan@example.com", DateTime.UtcNow);

        Assert.Throws<DomainException>(() => account.ChangeIdentity(userName!, email!));

        Assert.Equal("Bogdan", account.UserName);
        Assert.Equal("BOGDAN", account.NormalizedUserName);
        Assert.Equal("bogdan@example.com", account.Email);
        Assert.Equal("BOGDAN@EXAMPLE.COM", account.NormalizedEmail);
    }

    [Fact]
    public void EmailConfirmationUpdatesUserStateAndResetsWhenEmailChanges()
    {
        DateTime createdAtUtc = new(2026, 11, 18, 10, 0, 0, DateTimeKind.Utc);
        DateTime confirmedAtUtc = createdAtUtc.AddMinutes(5);
        var account = UserAccount.Create("Bogdan", "bogdan@example.com", createdAtUtc);

        Assert.False(account.EmailConfirmed);
        Assert.Null(account.EmailConfirmedAtUtc);
        Assert.Throws<DomainException>(() => account.ConfirmEmail(default));
        Assert.Throws<DomainException>(() => account.ConfirmEmail(DateTime.SpecifyKind(confirmedAtUtc, DateTimeKind.Local)));

        account.ConfirmEmail(confirmedAtUtc);
        account.ConfirmEmail(confirmedAtUtc.AddMinutes(10));
        Assert.True(account.EmailConfirmed);
        Assert.Equal(confirmedAtUtc, account.EmailConfirmedAtUtc);

        account.ChangeIdentity("Bogdan.Updated", "bogdan@example.com");
        Assert.True(account.EmailConfirmed);
        Assert.Equal(confirmedAtUtc, account.EmailConfirmedAtUtc);

        account.ChangeIdentity("Bogdan.Updated", "new@example.com");
        Assert.False(account.EmailConfirmed);
        Assert.Null(account.EmailConfirmedAtUtc);
    }

    [Fact]
    public void EmailConfirmationTokenGuardsCreationExpiryAndSingleUse()
    {
        DateTime createdAtUtc = new(2026, 11, 18, 10, 0, 0, DateTimeKind.Utc);
        DateTime expiresAtUtc = createdAtUtc.AddHours(24);
        var account = UserAccount.Create("Maria", "maria@example.com", createdAtUtc);

        Assert.Throws<DomainException>(() => EmailConfirmationToken.Create(null!, new string('A', 64), createdAtUtc, expiresAtUtc));
        Assert.Throws<DomainException>(() => EmailConfirmationToken.Create(account, " ", createdAtUtc, expiresAtUtc));
        Assert.Throws<DomainException>(() => EmailConfirmationToken.Create(account, new string('A', 64), default, expiresAtUtc));
        Assert.Throws<DomainException>(() => EmailConfirmationToken.Create(account, new string('A', 64), createdAtUtc, createdAtUtc));

        var validToken = EmailConfirmationToken.Create(account, new string('A', 64), createdAtUtc, expiresAtUtc);
        Assert.Equal(account.Id, validToken.UserId);
        Assert.Equal(createdAtUtc, validToken.CreatedAtUtc);
        Assert.Equal(expiresAtUtc, validToken.ExpiresAtUtc);
        Assert.Null(validToken.UsedAtUtc);

        Assert.Throws<DomainException>(() => validToken.Consume(default));
        Assert.Throws<DomainException>(() => validToken.Consume(expiresAtUtc));

        DateTime usedAtUtc = createdAtUtc.AddHours(1);
        validToken.Consume(usedAtUtc);
        Assert.Equal(usedAtUtc, validToken.UsedAtUtc);
        Assert.True(account.EmailConfirmed);
        Assert.Equal(usedAtUtc, account.EmailConfirmedAtUtc);

        DomainException reused = Assert.Throws<DomainException>(() => validToken.Consume(usedAtUtc.AddMinutes(1)));
        Assert.Equal("auth.confirmation_token_already_used", reused.Error.Code);

        var expiredToken = EmailConfirmationToken.Create(account, new string('B', 64), createdAtUtc, expiresAtUtc);
        DomainException expired = Assert.Throws<DomainException>(() => expiredToken.Consume(expiresAtUtc.AddMinutes(1)));
        Assert.Equal("auth.confirmation_token_expired", expired.Error.Code);
    }
}
