using MediatR;
using SmartLost.AuthService.Application.Contracts;
using SmartLost.BuildingBlocks.Application.Security;
using SmartLost.BuildingBlocks.Core.Results;

namespace SmartLost.AuthService.Application.Commands.Register;

public sealed record RegisterUserCommand(string UserName, string Email, string Password)
    : IRequest<Result<AuthResponse>>, ISecurityAuditedRequest
{
    public string SecurityEventType => "UserRegistration";

    public string? TargetSubject => UserName;
}
