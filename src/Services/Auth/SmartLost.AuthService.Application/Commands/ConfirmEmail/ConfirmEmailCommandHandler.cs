using MediatR;
using SmartLost.AuthService.Application.Contracts;
using SmartLost.AuthService.Application.Interfaces;
using SmartLost.BuildingBlocks.Core.Results;

namespace SmartLost.AuthService.Application.Commands.ConfirmEmail;

public sealed class ConfirmEmailCommandHandler(IEmailConfirmationService emailConfirmationService)
    : IRequestHandler<ConfirmEmailCommand, Result<EmailConfirmationResponse>>
{
    public Task<Result<EmailConfirmationResponse>> Handle(ConfirmEmailCommand request, CancellationToken cancellationToken)
    {
        return emailConfirmationService.ConfirmAsync(request.Token, cancellationToken);
    }
}
