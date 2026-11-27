using MediatR;
using SmartLost.BuildingBlocks.Core.Results;

namespace SmartLost.AuthService.Application.Commands.Logout;

public sealed record LogoutCommand(string RefreshToken) : IRequest<Result>;
