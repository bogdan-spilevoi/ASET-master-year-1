using System.Globalization;
using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using SmartLost.BuildingBlocks.Application.Authentication;
using SmartLost.BuildingBlocks.AspNetCore;
using SmartLost.BuildingBlocks.AspNetCore.Authentication;
using SmartLost.BuildingBlocks.Core.Exceptions;
using SmartLost.BuildingBlocks.Core.Results;
using Xunit;

namespace SmartLost.BuildingBlocks.UnitTests;

public sealed class CurrentUserTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ReadsRawAndMappedClaimsFromTheAuthenticatedIdentity(bool mapped)
    {
        var id = Guid.NewGuid();
        ClaimsIdentity identity = new([
            new Claim(mapped ? ClaimTypes.NameIdentifier : "sub", id.ToString()),
            new Claim(mapped ? ClaimTypes.Name : "unique_name", "student"),
            new Claim(mapped ? ClaimTypes.Email : "email", "student@example.com"),
            new Claim("jti", "token-id"), new Claim("iss", "AuthService"),
            new Claim("aud", "Listings"), new Claim("aud", "Matching"), new Claim("aud", "Listings"),
            new Claim("nbf", "1000"), new Claim("exp", "2000")
        ], "Bearer");
        HttpCurrentUser user = Create(identity);
        Assert.True(user.IsAuthenticated);
        Assert.Equal(id, user.UserId);
        Assert.Equal(id, user.GetRequiredUserId());
        Assert.Equal("student", user.UserName);
        Assert.Equal("student@example.com", user.Email);
        Assert.Equal("token-id", user.TokenId);
        Assert.Equal("AuthService", user.Issuer);
        Assert.Equal(["Listings", "Matching"], user.Audiences);
        Assert.Equal(DateTimeOffset.FromUnixTimeSeconds(1000), user.NotBeforeUtc);
        Assert.Equal(DateTimeOffset.FromUnixTimeSeconds(2000), user.ExpiresAtUtc);
        Assert.Throws<NotSupportedException>(() => ((IList<string>)user.Audiences).Add("Other"));
    }

    [Fact]
    public void AnonymousAndMissingContextsCannotSupplyAnIdentity()
    {
        HttpCurrentUser[] users = [new(new HttpContextAccessor()), Create(new ClaimsIdentity([new Claim("sub", Guid.NewGuid().ToString())]))];
        foreach (HttpCurrentUser user in users)
        {
            Assert.False(user.IsAuthenticated);
            Assert.Null(user.UserId);
            Assert.Null(user.UserName);
            Assert.Null(user.Email);
            Assert.Null(user.TokenId);
            Assert.Null(user.Issuer);
            Assert.Null(user.NotBeforeUtc);
            Assert.Null(user.ExpiresAtUtc);
            Assert.Empty(user.Audiences);
            Assert.Equal(ErrorKind.Unauthorized, Assert.Throws<DomainException>(() => user.GetRequiredUserId()).Error.Kind);
        }
    }

    [Fact]
    public void RejectsAmbiguousSubjectsAndInvalidTimestampsWithoutCombiningIdentities()
    {
        ClaimsIdentity first = new([
            new Claim("sub", Guid.NewGuid().ToString()), new Claim(ClaimTypes.NameIdentifier, Guid.NewGuid().ToString()),
            new Claim("exp", long.MaxValue.ToString(CultureInfo.InvariantCulture)), new Claim("nbf", "invalid")
        ], "Bearer");
        HttpCurrentUser user = Create(first, new ClaimsIdentity([new Claim("email", "other@example.com")], "Other"));
        Assert.Null(user.UserId);
        Assert.Null(user.Email);
        Assert.Null(user.ExpiresAtUtc);
        Assert.Null(user.NotBeforeUtc);
        Assert.Throws<ArgumentException>(() => user.GetClaims(" "));
        Assert.Throws<DomainException>(() => user.GetRequiredUserId());
        Assert.Null(Create(new ClaimsIdentity([new Claim("sub", Guid.Empty.ToString())], "Bearer")).UserId);
        Assert.Null(Create(new ClaimsIdentity([new Claim("sub", "invalid")], "Bearer")).UserId);
    }

    [Fact]
    public void ApiRegistrationSuppliesAScopedCurrentUser()
    {
        ServiceCollection services = new();
        services.AddLogging();
        services.AddBuildingBlocksApi();
        using ServiceProvider provider = services.BuildServiceProvider();
        using IServiceScope scope = provider.CreateScope();
        Assert.IsType<HttpCurrentUser>(scope.ServiceProvider.GetRequiredService<ICurrentUser>());
    }

    private static HttpCurrentUser Create(params ClaimsIdentity[] identities)
    {
        return new HttpCurrentUser(new HttpContextAccessor
        {
            HttpContext = new DefaultHttpContext { User = new ClaimsPrincipal(identities) }
        });
    }
}
