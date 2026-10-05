using MediatR;
using SmartLost.AuthService.Application.Contracts;
using SmartLost.BuildingBlocks.Core.Results;

namespace SmartLost.AuthService.Application.Commands.Register;

public sealed record RegisterUserCommand(string UserName, string Email, string Password) : IRequest<Result<AuthResponse>>;
