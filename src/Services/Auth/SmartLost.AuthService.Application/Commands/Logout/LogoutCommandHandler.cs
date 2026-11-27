using MediatR;
using SmartLost.AuthService.Application.Interfaces;
using SmartLost.BuildingBlocks.Core.Results;

namespace SmartLost.AuthService.Application.Commands.Logout;

public sealed class LogoutCommandHandler(IAuthenticationSessionService sessions) : IRequestHandler<LogoutCommand, Result>
{
    public async Task<Result> Handle(LogoutCommand request, CancellationToken cancellationToken)
    {
        await sessions.RevokeAsync(request.RefreshToken, cancellationToken);
        return Result.Success();
    }
}
