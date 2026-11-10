using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using SmartLost.BuildingBlocks.Application.Transactions;

namespace SmartLost.BuildingBlocks.Infrastructure.Transactions;

/// <summary>Local EF transaction boundary, reusable by each service with its own context.</summary>
public class EfUnitOfWork<TContext>(TContext context) : IUnitOfWork where TContext : DbContext
{
    protected TContext Context { get; } = context;

    public async Task<ITransaction> BeginTransactionAsync(CancellationToken cancellationToken)
    {
        if (Context.Database.CurrentTransaction is not null || System.Transactions.Transaction.Current is not null)
        {
            throw new InvalidOperationException("Unit-of-work transactions cannot be nested or join an external transaction.");
        }

        IDbContextTransaction transaction = await Context.Database.BeginTransactionAsync(cancellationToken);
        (Context as IEfTransactionParticipant)?.OnTransactionStarted(transaction);
        return new EfTransaction(Context, transaction);
    }

    public virtual async Task SaveChangesAsync(CancellationToken cancellationToken)
    {
        await Context.SaveChangesAsync(cancellationToken);
    }
}
