using Microsoft.EntityFrameworkCore;
using Npgsql;
using SmartLost.BuildingBlocks.Core.Exceptions;
using SmartLost.BuildingBlocks.Infrastructure.Transactions;
using SmartLost.ListingsService.Domain.Errors;

namespace SmartLost.ListingsService.Infrastructure.Persistence;

public sealed class ListingsUnitOfWork(ListingsDbContext context) : EfUnitOfWork<ListingsDbContext>(context)
{
    public override async Task SaveChangesAsync(CancellationToken cancellationToken)
    {
        try
        {
            await base.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            throw new DomainException(ListingErrors.RevisionConflict);
        }
        catch (DbUpdateException e) when (e.InnerException is PostgresException postgres &&
            postgres.SqlState == PostgresErrorCodes.UniqueViolation && postgres.ConstraintName == "PK_ListingCategories")
        {
            throw new DomainException(ListingErrors.RevisionConflict);
        }
        catch (DbUpdateException e) when (e.InnerException is PostgresException postgres &&
            postgres.SqlState == PostgresErrorCodes.UniqueViolation && postgres.ConstraintName == "IX_Recoveries_MatchDecisionId")
        {
            throw new DomainException(ListingErrors.RecoveryExists);
        }
    }
}
