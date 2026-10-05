using MediatR;
using SmartLost.AuthService.Application.Contracts;
using SmartLost.BuildingBlocks.Core.Results;

namespace SmartLost.AuthService.Application.Commands.Login;

public sealed record LoginUserCommand(string UserNameOrEmail, string Password) : IRequest<Result<AuthResponse>>;
