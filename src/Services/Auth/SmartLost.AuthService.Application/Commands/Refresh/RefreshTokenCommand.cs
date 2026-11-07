using MediatR;
using SmartLost.AuthService.Application.Contracts;
using SmartLost.BuildingBlocks.Application.Security;
using SmartLost.BuildingBlocks.Core.Results;

namespace SmartLost.AuthService.Application.Commands.Refresh;

public sealed record RefreshTokenCommand(string RefreshToken)
    : IRequest<Result<AuthResponse>>, ISecurityAuditedRequest
{
    public string SecurityEventType => "TokenRefresh";

    public string? TargetSubject => null;
}
