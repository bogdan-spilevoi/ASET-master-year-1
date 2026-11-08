using MediatR;
using SmartLost.BuildingBlocks.Application.Authentication;
using SmartLost.BuildingBlocks.Application.Authorization;
using SmartLost.BuildingBlocks.Application.Behaviors;
using SmartLost.BuildingBlocks.Core.Exceptions;
using SmartLost.BuildingBlocks.Core.Results;
using Xunit;

namespace SmartLost.BuildingBlocks.UnitTests;

public sealed class AuthorizationBehaviorTests
{
    [Fact]
    public async Task AnonymousCallerIsRejectedBeforeTheHandlerRuns()
    {
        var behavior = new AuthorizationBehavior<ProtectedCommand, Result<string>>(new TestCurrentUser(false));
        bool handlerCalled = false;
        Result<string> result = await behavior.Handle(new ProtectedCommand(), _ =>
        {
            handlerCalled = true;
            return Task.FromResult(Result<string>.Success("unexpected"));
        }, CancellationToken.None);

        Assert.False(handlerCalled);
        Error error = Assert.Single(result.Errors);
        Assert.Equal("authorization.unauthenticated", error.Code);
        Assert.Equal(ErrorKind.Unauthorized, error.Kind);
    }

    [Fact]
    public async Task AuthenticatedCallerWithoutRequiredClaimIsForbidden()
    {
        var behavior = new AuthorizationBehavior<ProtectedQuery, Result<string>>(new TestCurrentUser(true));
        Result<string> result = await behavior.Handle(new ProtectedQuery(),
            _ => Task.FromResult(Result<string>.Success("unexpected")), CancellationToken.None);

        Error error = Assert.Single(result.Errors);
        Assert.Equal("authorization.forbidden", error.Code);
        Assert.Equal(ErrorKind.Forbidden, error.Kind);
    }

    [Fact]
    public async Task MatchingClaimAllowsCommandsAndPublicRequestsBypassTheBehavior()
    {
        var behavior = new AuthorizationBehavior<ClaimProtectedCommand, Result<string>>(new TestCurrentUser(true, ["audit.read"]));
        Result<string> authorized = await behavior.Handle(new ClaimProtectedCommand(),
            _ => Task.FromResult(Result<string>.Success("allowed")), CancellationToken.None);
        Assert.Equal("allowed", authorized.Value);

        var publicBehavior = new AuthorizationBehavior<PublicCommand, Result<string>>(new TestCurrentUser(false));
        Result<string> publicResult = await publicBehavior.Handle(new PublicCommand(),
            _ => Task.FromResult(Result<string>.Success("public")), CancellationToken.None);
        Assert.Equal("public", publicResult.Value);
    }

    [Theory]
    [InlineData(null, "val")]
    [InlineData("", "val")]
    [InlineData("   ", "val")]
    [InlineData("type", null)]
    [InlineData("type", "")]
    [InlineData("type", "   ")]
    public void AuthorizeAttributeWithInvalidArgumentsThrowsArgumentException(string? claimType, string? claimValue)
    {
        Assert.Throws<ArgumentException>(() => new AuthorizeAttribute(claimType!, claimValue!));
    }

    [Fact]
    public void AuthorizeAttributeDefaultConstructorInitializesNullClaims()
    {
        var attribute = new AuthorizeAttribute();
        Assert.Null(attribute.ClaimType);
        Assert.Null(attribute.ClaimValue);
    }

    [Authorize]
    private sealed record ProtectedCommand : IRequest<Result<string>>;

    [Authorize("permission", "audit.read")]
    private sealed record ProtectedQuery : IRequest<Result<string>>;

    [Authorize("permission", "audit.read")]
    private sealed record ClaimProtectedCommand : IRequest<Result<string>>;
    private sealed record PublicCommand : IRequest<Result<string>>;

    private sealed class TestCurrentUser(bool isAuthenticated, IReadOnlyList<string>? permissions = null) : ICurrentUser
    {
        public bool IsAuthenticated => isAuthenticated;
        public Guid? UserId => null;
        public string? UserName => null;
        public string? Email => null;
        public string? TokenId => null;
        public string? Issuer => null;
        public IReadOnlyList<string> Audiences => [];
        public DateTimeOffset? NotBeforeUtc => null;
        public DateTimeOffset? ExpiresAtUtc => null;
        public Guid GetRequiredUserId()
        {
            throw new DomainException(new Error("authorization.unauthenticated", "An authenticated user is required.", ErrorKind.Unauthorized));
        }

        public string? GetClaim(string claimType)
        {
            return GetClaims(claimType).SingleOrDefault();
        }

        public IReadOnlyList<string> GetClaims(string claimType)
        {
            return claimType == "permission" ? permissions ?? [] : [];
        }
    }
}
