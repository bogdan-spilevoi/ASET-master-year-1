using Microsoft.EntityFrameworkCore.Storage;
using SmartLost.BuildingBlocks.Application.Transactions;

namespace SmartLost.AuthService.Infrastructure.Persistence;

internal sealed class AuthTransaction(AuthDbContext context, IDbContextTransaction transaction) : ITransaction
{
    private bool _completed;

    public async Task CommitAsync(CancellationToken cancellationToken)
    {
        await transaction.CommitAsync(cancellationToken);
        context.CompletePipelineTransaction(committed: true);
        _completed = true;
    }

    public async Task RollbackAsync(CancellationToken cancellationToken)
    {
        try
        {
            await transaction.RollbackAsync(cancellationToken);
        }
        finally
        {
            context.CompletePipelineTransaction(committed: false);
            _completed = true;
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
                context.CompletePipelineTransaction(committed: false);
            }
        }
    }
}
