using MediatR;
using SmartLost.AuthService.Application.Contracts;

namespace SmartLost.AuthService.Application.Commands.Register;

public sealed record RegisterUserCommand(string UserName, string Email, string Password) : IRequest<AuthResponse>;
