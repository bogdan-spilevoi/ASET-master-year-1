using MediatR;
using SmartLost.BuildingBlocks.Application.Authentication;
using SmartLost.BuildingBlocks.Core.Results;
using SmartLost.ListingsService.Application.Dtos;
using SmartLost.ListingsService.Application.Interfaces;
using SmartLost.ListingsService.Domain.Entities;

namespace SmartLost.ListingsService.Application.Commands.ConfirmRecovery;

public sealed class ConfirmRecoveryCommandHandler(IListingLifecycleStore store, ICurrentUser user, TimeProvider clock)
    : IRequestHandler<ConfirmRecoveryCommand, Result<RecoveryDto>>
{
    public async Task<Result<RecoveryDto>> Handle(ConfirmRecoveryCommand request, CancellationToken cancellationToken)
    {
        Guid authorId = user.GetRequiredUserId();
        MatchDecision decision = await store.GetOwnedDecisionAsync(request.DecisionId, authorId, cancellationToken);
        Recovery recovery = decision.Recovery ?? Recovery.Confirm(decision, authorId, clock.GetUtcNow().UtcDateTime);
        return Result<RecoveryDto>.Success(new(recovery.Id, decision.Id, decision.LostListingId, decision.FoundListingId));
    }
}
