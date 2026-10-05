namespace SmartLost.BuildingBlocks.Application.Transactions;

public interface ITransaction : IAsyncDisposable
{
    Task CommitAsync(CancellationToken cancellationToken);

    Task RollbackAsync(CancellationToken cancellationToken);
}
