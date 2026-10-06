using System.Globalization;
using SmartLost.AuthService.Domain.Entities;
using SmartLost.AuthService.Domain.Identity;
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

        Assert.ThrowsAny<ArgumentException>(() => account.ChangeIdentity(userName!, email!));

        Assert.Equal("Bogdan", account.UserName);
        Assert.Equal("BOGDAN", account.NormalizedUserName);
        Assert.Equal("bogdan@example.com", account.Email);
        Assert.Equal("BOGDAN@EXAMPLE.COM", account.NormalizedEmail);
    }
}
