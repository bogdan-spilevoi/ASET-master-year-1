using System.Diagnostics.CodeAnalysis;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using SmartLost.AuthService.Application.Interfaces;
using SmartLost.AuthService.Domain.Entities;
using SmartLost.BuildingBlocks.Core.Exceptions;
using SmartLost.BuildingBlocks.Core.Results;

namespace SmartLost.AuthService.Infrastructure.Persistence;

public sealed class UserAccountRepository(AuthDbContext dbContext) : IUserAccountRepository
{
    private static readonly Dictionary<string, Error> _uniqueConflicts = new(StringComparer.Ordinal)
    {
        ["IX_Users_NormalizedUserName"] = new(
            "auth.account_exists", "User name already exists.", ErrorKind.Conflict, "UserName"),
        ["IX_Users_NormalizedEmail"] = new(
            "auth.account_exists", "Email already exists.", ErrorKind.Conflict, "Email")
    };

    public Task<bool> ExistsAsync(string normalizedUserName, string normalizedEmail, CancellationToken cancellationToken)
    {
        return dbContext.Users.AnyAsync(
            user => user.NormalizedUserName == normalizedUserName || user.NormalizedEmail == normalizedEmail,
            cancellationToken);
    }

    public Task<UserAccount?> FindByEmailAsync(string normalizedEmail, CancellationToken cancellationToken)
    {
        return dbContext.Users.SingleOrDefaultAsync(
            user => user.NormalizedEmail == normalizedEmail,
            cancellationToken);
    }

    public void Add(UserAccount userAccount)
    {
        dbContext.Users.Add(userAccount);
    }

    public async Task SaveChangesAsync(CancellationToken cancellationToken)
    {
        try
        {
            await dbContext.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException exception) when (TryMapIdentityConflict(exception, out Error? error))
        {
            throw new DomainException(error);
        }
    }

    private static bool TryMapIdentityConflict(DbUpdateException exception, [NotNullWhen(true)] out Error? error)
    {
        if (exception.InnerException is PostgresException
            {
                SqlState: PostgresErrorCodes.UniqueViolation,
                ConstraintName: { } constraintName
            })
        {
            return _uniqueConflicts.TryGetValue(constraintName, out error);
        }

        error = null;
        return false;
    }
}
