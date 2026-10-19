namespace SmartLost.BuildingBlocks.Application.Transactions;

public interface IUnitOfWork
{
    Task<ITransaction> BeginTransactionAsync(CancellationToken cancellationToken);

    Task SaveChangesAsync(CancellationToken cancellationToken);
}
