using MediatR;
using SmartLost.AuthService.Application.Contracts;

namespace SmartLost.AuthService.Application.Commands.Login;

public sealed record LoginUserCommand(string UserNameOrEmail, string Password) : IRequest<AuthResponse>;
