using Microsoft.EntityFrameworkCore;
using SmartLost.AuthService.Application.Interfaces;
using SmartLost.AuthService.Domain.Entities;

namespace SmartLost.AuthService.Infrastructure.Persistence;

public sealed class UserAccountRepository(AuthDbContext dbContext) : IUserAccountRepository
{
    public Task<bool> ExistsAsync(string normalizedUserName, string normalizedEmail, CancellationToken cancellationToken)
    {
        return dbContext.Users.AnyAsync(
            user => user.NormalizedUserName == normalizedUserName || user.NormalizedEmail == normalizedEmail,
            cancellationToken);
    }

    public Task<UserAccount?> FindByUserNameOrEmailAsync(string normalizedValue, CancellationToken cancellationToken)
    {
        return dbContext.Users.SingleOrDefaultAsync(
            user => user.NormalizedUserName == normalizedValue || user.NormalizedEmail == normalizedValue,
            cancellationToken);
    }

    public void Add(UserAccount userAccount)
    {
        dbContext.Users.Add(userAccount);
    }

    public Task SaveChangesAsync(CancellationToken cancellationToken)
    {
        return dbContext.SaveChangesAsync(cancellationToken);
    }
}
