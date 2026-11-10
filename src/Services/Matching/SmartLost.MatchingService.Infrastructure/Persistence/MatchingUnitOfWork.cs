using Microsoft.EntityFrameworkCore;
using SmartLost.BuildingBlocks.Core.Exceptions;
using SmartLost.BuildingBlocks.Infrastructure.Transactions;
using SmartLost.MatchingService.Domain.Errors;

namespace SmartLost.MatchingService.Infrastructure.Persistence;

public sealed class MatchingUnitOfWork(MatchingDbContext context) : EfUnitOfWork<MatchingDbContext>(context)
{
    public override async Task SaveChangesAsync(CancellationToken cancellationToken)
    {
        try
        {
            await Context.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            // Translate the provider exception at the persistence boundary; MediatR maps this to HTTP 409.
            throw new DomainException(MatchingErrors.ConcurrentUpdate);
        }
    }

}
