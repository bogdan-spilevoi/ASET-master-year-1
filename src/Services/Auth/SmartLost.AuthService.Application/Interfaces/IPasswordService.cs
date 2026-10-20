using SmartLost.AuthService.Domain.Entities;

namespace SmartLost.AuthService.Application.Interfaces;

public interface IPasswordService
{
    string Hash(UserAccount userAccount, string password);

    bool Verify(UserAccount userAccount, string password);
}
