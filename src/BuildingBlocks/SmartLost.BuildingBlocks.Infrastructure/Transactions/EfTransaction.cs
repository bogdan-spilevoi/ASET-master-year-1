using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using SmartLost.BuildingBlocks.Application.Transactions;

namespace SmartLost.BuildingBlocks.Infrastructure.Transactions;

internal sealed class EfTransaction(DbContext context, IDbContextTransaction transaction) : ITransaction
{
    private bool _completed;

    public async Task CommitAsync(CancellationToken cancellationToken)
    {
        await transaction.CommitAsync(cancellationToken);
        Complete(committed: true);
    }

    public async Task RollbackAsync(CancellationToken cancellationToken)
    {
        try
        {
            await transaction.RollbackAsync(cancellationToken);
        }
        finally
        {
            Complete(committed: false);
        }
    }

    public async ValueTask DisposeAsync()
    {
        try
        {
            await transaction.DisposeAsync();
        }
        finally
        {
            if (!_completed)
            {
                Complete(committed: false);
            }
        }
    }

    private void Complete(bool committed)
    {
        if (!committed)
        {
            // SaveChanges may have accepted entities whose database changes were rolled back.
            context.ChangeTracker.Clear();
        }

        (context as IEfTransactionParticipant)?.OnTransactionCompleted(committed);
        _completed = true;
    }
}
