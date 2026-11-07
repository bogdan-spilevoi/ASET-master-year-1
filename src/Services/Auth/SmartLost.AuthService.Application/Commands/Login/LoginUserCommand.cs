using MediatR;
using SmartLost.AuthService.Application.Contracts;
using SmartLost.BuildingBlocks.Application.Security;
using SmartLost.BuildingBlocks.Core.Results;

namespace SmartLost.AuthService.Application.Commands.Login;

public sealed record LoginUserCommand(string Email, string Password)
    : IRequest<Result<AuthResponse>>, ISecurityAuditedRequest
{
    public string SecurityEventType => "UserLogin";

    public string? TargetSubject => Email;
}
