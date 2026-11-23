using MediatR;
using SmartLost.BuildingBlocks.Application.Authentication;
using SmartLost.BuildingBlocks.Core.Results;
using SmartLost.ListingsService.Application.Interfaces;
using SmartLost.ListingsService.Domain.Entities;

namespace SmartLost.ListingsService.Application.Commands.CancelAcceptance;

public sealed class CancelAcceptanceCommandHandler(IListingLifecycleStore store, ICurrentUser user)
    : IRequestHandler<CancelAcceptanceCommand, Result>
{
    public async Task<Result> Handle(CancelAcceptanceCommand request, CancellationToken cancellationToken)
    {
        Guid authorId = user.GetRequiredUserId();
        MatchDecision decision = await store.GetOwnedDecisionAsync(request.DecisionId, authorId, cancellationToken);
        decision.CancelAcceptance(authorId);
        return Result.Success();
    }
}
