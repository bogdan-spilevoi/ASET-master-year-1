using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using SmartLost.BuildingBlocks.Application.Transactions;
using SmartLost.BuildingBlocks.Core.Exceptions;
using SmartLost.MatchingService.Domain.Errors;

namespace SmartLost.MatchingService.Infrastructure.Persistence;

public sealed class MatchingUnitOfWork(MatchingDbContext context) : IUnitOfWork
{
    public async Task<ITransaction> BeginTransactionAsync(CancellationToken cancellationToken)
    {
        return new Transaction(await context.Database.BeginTransactionAsync(cancellationToken));
    }

    public async Task SaveChangesAsync(CancellationToken cancellationToken)
    {
        try
        {
            await context.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            // Translate the provider exception at the persistence boundary; MediatR maps this to HTTP 409.
            throw new DomainException(MatchingErrors.ConcurrentUpdate);
        }
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
