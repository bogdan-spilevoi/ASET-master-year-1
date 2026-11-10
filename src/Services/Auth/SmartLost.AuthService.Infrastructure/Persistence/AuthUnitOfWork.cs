using SmartLost.AuthService.Application.Interfaces;
using SmartLost.BuildingBlocks.Infrastructure.Transactions;

namespace SmartLost.AuthService.Infrastructure.Persistence;

public sealed class AuthUnitOfWork(AuthDbContext context, IUserAccountRepository repository) : EfUnitOfWork<AuthDbContext>(context)
{
    public override Task SaveChangesAsync(CancellationToken cancellationToken)
    {
        // Preserve the repository's unique-constraint/concurrency error mapping.
        return repository.SaveChangesAsync(cancellationToken);
    }
}
