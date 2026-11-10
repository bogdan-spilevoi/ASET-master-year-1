using MediatR;
using SmartLost.AuthService.Application.Contracts;
using SmartLost.BuildingBlocks.Application.Transactions;
using SmartLost.BuildingBlocks.Core.Results;

namespace SmartLost.AuthService.Application.Commands.Login;

public sealed record LoginUserCommand(string Email, string Password) : IRequest<Result<AuthResponse>>, ITransactionalCommand;
