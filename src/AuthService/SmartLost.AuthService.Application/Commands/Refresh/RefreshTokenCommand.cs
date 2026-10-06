using MediatR;
using SmartLost.AuthService.Application.Contracts;
using SmartLost.BuildingBlocks.Core.Results;

namespace SmartLost.AuthService.Application.Commands.Refresh;

public sealed record RefreshTokenCommand(string RefreshToken) : IRequest<Result<AuthResponse>>;
