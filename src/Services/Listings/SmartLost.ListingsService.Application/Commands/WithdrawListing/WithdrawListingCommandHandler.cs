using MediatR;
using SmartLost.BuildingBlocks.Application.Authentication;
using SmartLost.BuildingBlocks.Core.Results;
using SmartLost.ListingsService.Application.Interfaces;
using SmartLost.ListingsService.Domain.Entities;

namespace SmartLost.ListingsService.Application.Commands.WithdrawListing;

public sealed class WithdrawListingCommandHandler(IListingLifecycleStore store, ICurrentUser user)
    : IRequestHandler<WithdrawListingCommand, Result>
{
    public async Task<Result> Handle(WithdrawListingCommand request, CancellationToken cancellationToken)
    {
        Listing listing = await store.GetOpenAsync(request.ListingId, cancellationToken);
        listing.Withdraw(user.GetRequiredUserId(), request.ExpectedRevision);
        foreach (MatchDecision decision in await store.GetUnconfirmedAsync(listing.Id, cancellationToken))
        {
            decision.Invalidate();
        }
        return Result.Success();
    }
}
