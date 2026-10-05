using SmartLost.AuthService.Application.Commands.Login;
using SmartLost.AuthService.Application.Commands.Register;
using SmartLost.AuthService.Application.Interfaces;
using SmartLost.AuthService.Domain.Entities;
using Xunit;

namespace SmartLost.AuthService.UnitTests;

public sealed class AuthCommandHandlerTests
{
    [Fact]
    public async Task RegisterUserCreatesAccountAndReturnsToken()
    {
        InMemoryUserAccountRepository repository = new();
        RegisterUserCommandHandler handler = new(repository, new TestPasswordService(), new TestTokenService());

        await handler.Handle(new RegisterUserCommand("alex.popescu", "alex@example.com", "P@ssw0rd123!"), CancellationToken.None);

        Assert.Single(repository.Users);
    }

    [Fact]
    public async Task LoginUserRejectsInvalidPassword()
    {
        InMemoryUserAccountRepository repository = new();
        RegisterUserCommandHandler registerHandler = new(repository, new TestPasswordService(), new TestTokenService());
        await registerHandler.Handle(new RegisterUserCommand("alex.popescu", "alex@example.com", "P@ssw0rd123!"), CancellationToken.None);
        LoginUserCommandHandler loginHandler = new(repository, new TestPasswordService(), new TestTokenService());

        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => loginHandler.Handle(new LoginUserCommand("alex@example.com", "incorrect"), CancellationToken.None));
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

        public Task<UserAccount?> FindByUserNameOrEmailAsync(string normalizedValue, CancellationToken cancellationToken)
        {
            return Task.FromResult(Users.SingleOrDefault(user => user.NormalizedUserName == normalizedValue || user.NormalizedEmail == normalizedValue));
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
