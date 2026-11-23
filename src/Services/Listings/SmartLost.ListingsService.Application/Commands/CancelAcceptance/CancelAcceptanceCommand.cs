using MediatR;
using SmartLost.BuildingBlocks.Application.Authorization;
using SmartLost.BuildingBlocks.Application.Transactions;
using SmartLost.BuildingBlocks.Core.Results;

namespace SmartLost.ListingsService.Application.Commands.CancelAcceptance;

[Authorize]
public sealed record CancelAcceptanceCommand(Guid DecisionId) : IRequest<Result>, ITransactionalCommand;
