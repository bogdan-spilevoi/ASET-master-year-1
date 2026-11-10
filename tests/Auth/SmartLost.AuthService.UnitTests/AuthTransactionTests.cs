using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using SmartLost.AuthService.Application.Commands.Register;
using SmartLost.AuthService.Application.Contracts;
using SmartLost.AuthService.Domain.Entities;
using SmartLost.AuthService.Domain.Errors;
using SmartLost.AuthService.Infrastructure.Persistence;
using SmartLost.BuildingBlocks.Application.Behaviors;
using SmartLost.BuildingBlocks.Application.Transactions;
using SmartLost.BuildingBlocks.Core.Results;
using Xunit;

namespace SmartLost.AuthService.UnitTests;

public sealed class AuthTransactionTests
{
    [Fact]
    public async Task CommitClearsEventsOnlyAfterFinalCommitAndRepeatedSavesDoNotDuplicateOutbox()
    {
        using Store store = new();
        UserAccount user = store.AddUser();
        await using ITransaction transaction = await store.UnitOfWork.BeginTransactionAsync(CancellationToken.None);
        await store.UnitOfWork.SaveChangesAsync(CancellationToken.None);
        Assert.Single(user.DomainEvents);
        store.Context.SaveChanges();
        Assert.Single(user.DomainEvents);
        Assert.Equal(1, await store.Context.OutboxMessages.CountAsync());

        await transaction.CommitAsync(CancellationToken.None);

        Assert.Empty(user.DomainEvents);
        Assert.Equal(1, await store.Context.Users.CountAsync());
        Assert.Equal(1, await store.Context.OutboxMessages.CountAsync());
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task PipelineRollsBackFlushedChangesForFailureResultsAndExceptions(bool throws)
    {
        using Store store = new();
        var user = UserAccount.Create("rollback-user", "rollback@example.com", DateTime.UtcNow);
        var behavior = new TransactionBehavior<RegisterUserCommand, Result<AuthResponse>>(store.UnitOfWork,
            NullLogger<TransactionBehavior<RegisterUserCommand, Result<AuthResponse>>>.Instance);
        async Task<Result<AuthResponse>> Handler(CancellationToken token)
        {
            store.Context.Users.Add(user);
            await store.UnitOfWork.SaveChangesAsync(token);
            Assert.Single(user.DomainEvents);
            if (throws)
            {
                throw new InvalidOperationException("simulated handler failure");
            }

            return Result<AuthResponse>.Failure(AuthErrors.InvalidCredentials);
        }

        var command = new RegisterUserCommand(user.UserName, user.Email, "unused-password");
        if (throws)
        {
            await Assert.ThrowsAsync<InvalidOperationException>(() => behavior.Handle(command, Handler, CancellationToken.None));
        }
        else
        {
            Assert.Equal(AuthErrors.InvalidCredentials,
                Assert.Single((await behavior.Handle(command, Handler, CancellationToken.None)).Errors));
        }

        Assert.Single(user.DomainEvents);
        Assert.Empty(store.Context.ChangeTracker.Entries());
        Assert.Equal(0, await store.Context.Users.CountAsync());
        Assert.Equal(0, await store.Context.OutboxMessages.CountAsync());
        // The same scope can no longer accidentally save work from the failed transaction.
        await store.UnitOfWork.SaveChangesAsync(CancellationToken.None);
        Assert.Equal(0, await store.Context.Users.CountAsync());
    }

    [Fact]
    public async Task DisposalWithoutCommitRollsBackAndAllowsANewTransaction()
    {
        using Store store = new();
        UserAccount first = store.AddUser();
        await using (ITransaction transaction = await store.UnitOfWork.BeginTransactionAsync(CancellationToken.None))
        {
            await store.UnitOfWork.SaveChangesAsync(CancellationToken.None);
        }

        Assert.Single(first.DomainEvents);
        Assert.Empty(store.Context.ChangeTracker.Entries());
        Assert.Equal(0, await store.Context.Users.CountAsync());
        await using ITransaction next = await store.UnitOfWork.BeginTransactionAsync(CancellationToken.None);
        store.AddUser();
        await store.UnitOfWork.SaveChangesAsync(CancellationToken.None);
        await next.CommitAsync(CancellationToken.None);
        Assert.Equal(1, await store.Context.Users.CountAsync());
        Assert.Equal(1, await store.Context.OutboxMessages.CountAsync());
    }

    [Fact]
    public async Task NestedExternalAndAmbientTransactionsAreRejected()
    {
        using Store store = new();
        await using (ITransaction transaction = await store.UnitOfWork.BeginTransactionAsync(CancellationToken.None))
        {
            await Assert.ThrowsAsync<InvalidOperationException>(() => store.UnitOfWork.BeginTransactionAsync(CancellationToken.None));
            await transaction.RollbackAsync(CancellationToken.None);
        }

        await using (Microsoft.EntityFrameworkCore.Storage.IDbContextTransaction external = await store.Context.Database.BeginTransactionAsync())
        {
            await Assert.ThrowsAsync<InvalidOperationException>(() => store.UnitOfWork.BeginTransactionAsync(CancellationToken.None));
        }

        using var ambient = new System.Transactions.TransactionScope(System.Transactions.TransactionScopeAsyncFlowOption.Enabled);
        await Assert.ThrowsAsync<InvalidOperationException>(() => store.UnitOfWork.BeginTransactionAsync(CancellationToken.None));
    }

    private sealed class Store : IDisposable
    {
        private readonly SqliteConnection _connection = new("Data Source=:memory:");
        public AuthDbContext Context
        {
            get;
        }
        public AuthUnitOfWork UnitOfWork
        {
            get;
        }

        public Store()
        {
            _connection.Open();
            Context = new AuthDbContext(new DbContextOptionsBuilder<AuthDbContext>().UseSqlite(_connection).Options);
            Context.Database.EnsureCreated();
            UnitOfWork = new AuthUnitOfWork(Context, new UserAccountRepository(Context));
        }

        public UserAccount AddUser()
        {
            var user = UserAccount.Create("transaction-user", "transaction@example.com", DateTime.UtcNow);
            Context.Users.Add(user);
            return user;
        }

        public void Dispose()
        {
            Context.Dispose();
            _connection.Dispose();
        }
    }
}
