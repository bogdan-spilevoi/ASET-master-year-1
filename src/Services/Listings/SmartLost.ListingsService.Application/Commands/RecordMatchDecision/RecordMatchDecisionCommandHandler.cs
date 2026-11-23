using MediatR;
using SmartLost.BuildingBlocks.Application.Authentication;
using SmartLost.BuildingBlocks.Core.Results;
using SmartLost.ListingsService.Application.Dtos;
using SmartLost.ListingsService.Application.Interfaces;
using SmartLost.ListingsService.Domain.Entities;
using SmartLost.ListingsService.Domain.Enums;
using SmartLost.ListingsService.Domain.Errors;

namespace SmartLost.ListingsService.Application.Commands.RecordMatchDecision;

public sealed class RecordMatchDecisionCommandHandler(IListingLifecycleStore store, ICurrentUser user, TimeProvider clock)
    : IRequestHandler<RecordMatchDecisionCommand, Result<DecisionDto>>
{
    public async Task<Result<DecisionDto>> Handle(RecordMatchDecisionCommand request, CancellationToken cancellationToken)
    {
        Guid authorId = user.GetRequiredUserId();
        Listing lost = await store.GetOpenAsync(request.LostListingId, cancellationToken);
        if (lost.AuthorId != authorId)
        {
            return Result<DecisionDto>.Failure(ListingErrors.NotOwner);
        }
        Listing found = await store.GetOpenAsync(request.FoundListingId, cancellationToken);
        if (lost.Revision != request.LostRevision || found.Revision != request.FoundRevision)
        {
            return Result<DecisionDto>.Failure(ListingErrors.StaleDecision);
        }
        if (!Enum.IsDefined(request.Choice))
        {
            return Result<DecisionDto>.Failure(ListingErrors.InvalidChoice);
        }

        MatchDecision? decision = await store.FindReviewedPairAsync(lost, found, cancellationToken);
        MatchDecisionState target = request.Choice == MatchChoice.Accept ? MatchDecisionState.Accepted : MatchDecisionState.Rejected;
        if (decision?.State == target)
        {
            return Result<DecisionDto>.Success(new(decision.Id, target.ToString()));
        }
        if (await store.IsReservedOrRecoveredAsync(lost.Id, found.Id, cancellationToken))
        {
            return Result<DecisionDto>.Failure(ListingErrors.PairUnavailable);
        }
        if (decision is null)
        {
            decision = MatchDecision.CreateSuggested(lost, found);
            store.Add(decision);
        }
        if (target == MatchDecisionState.Accepted)
        {
            decision.Accept(authorId, clock.GetUtcNow().UtcDateTime);
        }
        else
        {
            decision.Reject(authorId, clock.GetUtcNow().UtcDateTime);
        }
        return Result<DecisionDto>.Success(new(decision.Id, decision.State.ToString()));
    }
}
