using Microsoft.EntityFrameworkCore.Storage;
using SmartLost.BuildingBlocks.Application.Transactions;

namespace SmartLost.MatchingService.Infrastructure.Persistence;

public sealed class MatchingUnitOfWork(MatchingDbContext context) : IUnitOfWork
{
    public async Task<ITransaction> BeginTransactionAsync(CancellationToken cancellationToken)
    {
        return new Transaction(await context.Database.BeginTransactionAsync(cancellationToken));
    }

    public Task SaveChangesAsync(CancellationToken cancellationToken)
    {
        return context.SaveChangesAsync(cancellationToken);
    }

    private sealed class Transaction(IDbContextTransaction transaction) : ITransaction
    {
        public Task CommitAsync(CancellationToken cancellationToken)
        {
            return transaction.CommitAsync(cancellationToken);
        }

        public Task RollbackAsync(CancellationToken cancellationToken)
        {
            return transaction.RollbackAsync(cancellationToken);
        }

        public ValueTask DisposeAsync()
        {
            return transaction.DisposeAsync();
        }
    }
}
