using SmartLost.AuthService.Application.Interfaces;
using SmartLost.BuildingBlocks.Application.Transactions;

namespace SmartLost.AuthService.Infrastructure.Persistence;

public sealed class AuthUnitOfWork(AuthDbContext context, IUserAccountRepository repository) : IUnitOfWork
{
    public async Task<ITransaction> BeginTransactionAsync(CancellationToken cancellationToken)
    {
        return new AuthTransaction(context, await context.BeginPipelineTransactionAsync(cancellationToken));
    }

    public Task SaveChangesAsync(CancellationToken cancellationToken)
    {
        // Preserve the repository's unique-constraint/concurrency error mapping.
        return repository.SaveChangesAsync(cancellationToken);
    }
}
