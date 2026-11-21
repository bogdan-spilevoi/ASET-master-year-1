using SmartLost.AuthService.Application.Commands.ConfirmEmail;
using SmartLost.AuthService.Application.Commands.Login;
using SmartLost.AuthService.Application.Commands.Register;
using SmartLost.AuthService.Application.Contracts;
using SmartLost.AuthService.Application.Interfaces;
using SmartLost.AuthService.Domain.Entities;
using SmartLost.AuthService.Domain.Errors;
using SmartLost.BuildingBlocks.Core.Results;
using Xunit;

namespace SmartLost.AuthService.UnitTests;

public sealed class AuthCommandHandlerTests
{
    [Fact]
    public async Task RegisterUserCreatesAccountAndReturnsToken()
    {
        InMemoryUserAccountRepository repository = new();
        TestEmailConfirmationService confirmationService = new();
        RegisterUserCommandHandler handler = new(repository, new TestPasswordService(), new TestTokenService(), confirmationService);

        Result<AuthResponse> result = await handler.Handle(new RegisterUserCommand(" alex.popescu ", " alex@example.com ", "P@ssw0rd123!"), CancellationToken.None);

        Assert.Single(repository.Users);
        Assert.Single(confirmationService.SentUsers);
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
        RegisterUserCommandHandler registerHandler = new(repository, new TestPasswordService(), new TestTokenService(), new TestEmailConfirmationService());
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
        RegisterUserCommandHandler handler = new(repository, new TestPasswordService(), new TestTokenService(), new TestEmailConfirmationService());
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
        RegisterUserCommandHandler registerHandler = new(repository, new TestPasswordService(), new TestTokenService(), new TestEmailConfirmationService());
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
        RegisterUserCommandHandler registerHandler = new(repository, new TestPasswordService(), new TestTokenService(), new TestEmailConfirmationService());
        await registerHandler.Handle(new RegisterUserCommand("alex.popescu", "alex@example.com", "P@ssw0rd123!"), CancellationToken.None);
        LoginUserCommandHandler loginHandler = new(repository, new TestPasswordService(), new TestTokenService());

        Result<AuthResponse> result = await loginHandler.Handle(new LoginUserCommand("alex@example.com", "incorrect"), CancellationToken.None);
        Assert.True(result.IsFailure);
        Assert.Equal(ErrorKind.Unauthorized, Assert.Single(result.Errors).Kind);
    }

    [Fact]
    public async Task ConfirmEmailCommandHandlerDelegatesToConfirmationService()
    {
        TestEmailConfirmationService confirmationService = new();
        ConfirmEmailCommandHandler handler = new(confirmationService);
        string token = new('a', 43);

        Result<EmailConfirmationResponse> success = await handler.Handle(new ConfirmEmailCommand(token), CancellationToken.None);
        Assert.True(success.IsSuccess);
        Assert.True(success.Value.EmailConfirmed);

        Result<EmailConfirmationResponse> invalid = await handler.Handle(new ConfirmEmailCommand("wrong-token"), CancellationToken.None);
        Assert.True(invalid.IsFailure);
        Assert.Equal("auth.invalid_confirmation_token", Assert.Single(invalid.Errors).Code);
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

    private sealed class TestTokenService : IAuthenticationSessionService
    {
        public Task<AuthResponse> CreateAsync(UserAccount user, CancellationToken cancellationToken)
        {
            return Task.FromResult(new AuthResponse(user.Id, user.UserName, user.Email, "token",
                DateTime.UtcNow.AddHours(1), "test-refresh-token", DateTime.UtcNow.AddDays(7)));
        }

        public Task<Result<AuthResponse>> RefreshAsync(string refreshToken, CancellationToken cancellationToken)
        {
            throw new NotSupportedException();
        }
    }

    private sealed class TestEmailConfirmationService : IEmailConfirmationService
    {
        public List<UserAccount> SentUsers { get; } = [];

        public Task EnqueueConfirmationAsync(UserAccount user, CancellationToken cancellationToken)
        {
            SentUsers.Add(user);
            return Task.CompletedTask;
        }

        public Task<Result<EmailConfirmationResponse>> ConfirmAsync(string token, CancellationToken cancellationToken)
        {
            if (token.Length == 43)
            {
                return Task.FromResult(Result<EmailConfirmationResponse>.Success(
                    new EmailConfirmationResponse(Guid.NewGuid(), "alex@example.com", true, DateTime.UtcNow)));
            }

            return Task.FromResult(Result<EmailConfirmationResponse>.Failure(AuthErrors.InvalidEmailConfirmationToken));
        }
    }
}
