using MediatR;
using SmartLost.AuthService.Application.Contracts;
using SmartLost.AuthService.Application.Interfaces;
using SmartLost.BuildingBlocks.Core.Results;

namespace SmartLost.AuthService.Application.Commands.Refresh;

public sealed class RefreshTokenCommandHandler(IAuthenticationSessionService sessionService)
    : IRequestHandler<RefreshTokenCommand, Result<AuthResponse>>
{
    public Task<Result<AuthResponse>> Handle(RefreshTokenCommand request, CancellationToken cancellationToken)
    {
        return sessionService.RefreshAsync(request.RefreshToken, cancellationToken);
    }
}
