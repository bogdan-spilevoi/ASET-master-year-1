using SmartLost.AuthService.Application.Contracts;
using SmartLost.AuthService.Domain.Entities;
using SmartLost.BuildingBlocks.Core.Results;

namespace SmartLost.AuthService.Application.Interfaces;

public interface IAuthenticationSessionService
{
    Task<AuthResponse> CreateAsync(UserAccount user, CancellationToken cancellationToken);

    Task<Result<AuthResponse>> RefreshAsync(string refreshToken, CancellationToken cancellationToken);
}
