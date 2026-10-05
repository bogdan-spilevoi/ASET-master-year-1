using SmartLost.AuthService.Application.Commands.Login;
using SmartLost.AuthService.Application.Commands.Register;
using SmartLost.AuthService.Application.Contracts;
using SmartLost.AuthService.Application.Interfaces;
using SmartLost.AuthService.Domain.Entities;
using SmartLost.BuildingBlocks.Core.Results;
using Xunit;

namespace SmartLost.AuthService.UnitTests;

public sealed class AuthCommandHandlerTests
{
    [Fact]
    public async Task RegisterUserCreatesAccountAndReturnsToken()
    {
        InMemoryUserAccountRepository repository = new();
        RegisterUserCommandHandler handler = new(repository, new TestPasswordService(), new TestTokenService());

        Result<AuthResponse> result = await handler.Handle(new RegisterUserCommand(" alex.popescu ", " alex@example.com ", "P@ssw0rd123!"), CancellationToken.None);

        Assert.Single(repository.Users);
        Assert.True(result.IsSuccess);
        Assert.Equal("token", result.Value.AccessToken);
        Assert.Equal("alex.popescu", result.Value.UserName);
        Assert.Equal("alex@example.com", result.Value.Email);
        Assert.Equal("ALEX.POPESCU", repository.Users[0].NormalizedUserName);
        Assert.Equal("ALEX@EXAMPLE.COM", repository.Users[0].NormalizedEmail);
    }

    [Theory]
    [InlineData("Alex@Example.com")]
    [InlineData("  ALEX@EXAMPLE.COM  ")]
    public async Task LoginUsesTheSameNormalizationAsAccountCreation(string email)
    {
        InMemoryUserAccountRepository repository = new();
        RegisterUserCommandHandler registerHandler = new(repository, new TestPasswordService(), new TestTokenService());
        Result<AuthResponse> registered = await registerHandler.Handle(
            new RegisterUserCommand("Alex.Popescu", "Alex@Example.com", "P@ssw0rd123!"), CancellationToken.None);
        LoginUserCommandHandler loginHandler = new(repository, new TestPasswordService(), new TestTokenService());

        Result<AuthResponse> result = await loginHandler.Handle(new LoginUserCommand(email, "P@ssw0rd123!"), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(registered.Value.UserId, result.Value.UserId);
        Assert.Equal("Alex.Popescu", result.Value.UserName);
        Assert.Equal("Alex@Example.com", result.Value.Email);
    }

    [Theory]
    [InlineData(" ALEX.POPESCU ", "other@example.com")]
    [InlineData("other-user", " ALEX@EXAMPLE.COM ")]
    public async Task RegisterRejectsDuplicatesWithDifferentCasingAndWhitespace(string userName, string email)
    {
        InMemoryUserAccountRepository repository = new();
        RegisterUserCommandHandler handler = new(repository, new TestPasswordService(), new TestTokenService());
        await handler.Handle(new RegisterUserCommand("Alex.Popescu", "Alex@Example.com", "P@ssw0rd123!"), CancellationToken.None);

        Result<AuthResponse> result = await handler.Handle(new RegisterUserCommand(userName, email, "P@ssw0rd123!"), CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("auth.account_exists", Assert.Single(result.Errors).Code);
        Assert.Single(repository.Users);
    }

    [Fact]
    public async Task LoginRejectsUserNameEvenWithCorrectPassword()
    {
        InMemoryUserAccountRepository repository = new();
        RegisterUserCommandHandler registerHandler = new(repository, new TestPasswordService(), new TestTokenService());
        await registerHandler.Handle(new RegisterUserCommand("alex.popescu", "alex@example.com", "P@ssw0rd123!"), CancellationToken.None);
        LoginUserCommandHandler loginHandler = new(repository, new TestPasswordService(), new TestTokenService());

        Result<AuthResponse> result = await loginHandler.Handle(new LoginUserCommand("alex.popescu", "P@ssw0rd123!"), CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("auth.invalid_credentials", Assert.Single(result.Errors).Code);
    }

    [Fact]
    public async Task LoginUserRejectsInvalidPassword()
    {
        InMemoryUserAccountRepository repository = new();
        RegisterUserCommandHandler registerHandler = new(repository, new TestPasswordService(), new TestTokenService());
        await registerHandler.Handle(new RegisterUserCommand("alex.popescu", "alex@example.com", "P@ssw0rd123!"), CancellationToken.None);
        LoginUserCommandHandler loginHandler = new(repository, new TestPasswordService(), new TestTokenService());

        Result<AuthResponse> result = await loginHandler.Handle(new LoginUserCommand("alex@example.com", "incorrect"), CancellationToken.None);
        Assert.True(result.IsFailure);
        Assert.Equal(ErrorKind.Unauthorized, Assert.Single(result.Errors).Kind);
    }

    private sealed class InMemoryUserAccountRepository : IUserAccountRepository
    {
        public List<UserAccount> Users { get; } = [];
        public void Add(UserAccount userAccount)
        {
            Users.Add(userAccount);
        }

        public Task<bool> ExistsAsync(string normalizedUserName, string normalizedEmail, CancellationToken cancellationToken)
        {
            return Task.FromResult(Users.Any(user => user.NormalizedUserName == normalizedUserName || user.NormalizedEmail == normalizedEmail));
        }

        public Task<UserAccount?> FindByEmailAsync(string normalizedEmail, CancellationToken cancellationToken)
        {
            return Task.FromResult(Users.SingleOrDefault(user => user.NormalizedEmail == normalizedEmail));
        }

        public Task SaveChangesAsync(CancellationToken cancellationToken)
        {
            return Task.CompletedTask;
        }
    }

    private sealed class TestPasswordService : IPasswordService
    {
        public string Hash(UserAccount userAccount, string password)
        {
            return password;
        }

        public bool Verify(UserAccount userAccount, string password)
        {
            return userAccount.PasswordHash == password;
        }
    }

    private sealed class TestTokenService : ITokenService
    {
        public TokenResult CreateToken(UserAccount userAccount)
        {
            return new("token", DateTime.UtcNow.AddHours(1));
        }
    }
}
