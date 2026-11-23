using MediatR;
using SmartLost.BuildingBlocks.Application.Authorization;
using SmartLost.BuildingBlocks.Application.Transactions;
using SmartLost.BuildingBlocks.Core.Results;
using SmartLost.ListingsService.Application.Dtos;

namespace SmartLost.ListingsService.Application.Commands.RecordMatchDecision;

[Authorize]
public sealed record RecordMatchDecisionCommand(Guid LostListingId, Guid FoundListingId, int LostRevision, int FoundRevision, MatchChoice Choice) : IRequest<Result<DecisionDto>>, ITransactionalCommand;
