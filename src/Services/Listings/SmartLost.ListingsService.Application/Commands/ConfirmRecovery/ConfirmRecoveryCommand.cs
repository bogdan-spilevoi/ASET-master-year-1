using MediatR;
using SmartLost.BuildingBlocks.Application.Authorization;
using SmartLost.BuildingBlocks.Application.Transactions;
using SmartLost.BuildingBlocks.Core.Results;
using SmartLost.ListingsService.Application.Dtos;

namespace SmartLost.ListingsService.Application.Commands.ConfirmRecovery;

[Authorize]
public sealed record ConfirmRecoveryCommand(Guid DecisionId) : IRequest<Result<RecoveryDto>>, ITransactionalCommand;
