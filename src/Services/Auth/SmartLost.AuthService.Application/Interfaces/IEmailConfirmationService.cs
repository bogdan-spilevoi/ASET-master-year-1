using SmartLost.AuthService.Application.Contracts;
using SmartLost.AuthService.Domain.Entities;
using SmartLost.BuildingBlocks.Core.Results;

namespace SmartLost.AuthService.Application.Interfaces;

public interface IEmailConfirmationService
{
    Task SendConfirmationAsync(UserAccount user, CancellationToken cancellationToken);

    Task<Result<EmailConfirmationResponse>> ConfirmAsync(string token, CancellationToken cancellationToken);
}
