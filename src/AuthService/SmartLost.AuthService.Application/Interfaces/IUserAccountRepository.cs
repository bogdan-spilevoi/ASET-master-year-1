using SmartLost.AuthService.Domain.Entities;

namespace SmartLost.AuthService.Application.Interfaces;

public interface IUserAccountRepository
{
    Task<bool> ExistsAsync(string normalizedUserName, string normalizedEmail, CancellationToken cancellationToken);

    Task<UserAccount?> FindByUserNameOrEmailAsync(string normalizedValue, CancellationToken cancellationToken);

    void Add(UserAccount userAccount);

    Task SaveChangesAsync(CancellationToken cancellationToken);
}
