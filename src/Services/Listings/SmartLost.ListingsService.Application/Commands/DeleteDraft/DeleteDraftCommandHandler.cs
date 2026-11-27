using MediatR;
using SmartLost.BuildingBlocks.Application.Authentication;
using SmartLost.BuildingBlocks.Core.Results;
using SmartLost.ListingsService.Application.Interfaces;
using SmartLost.ListingsService.Domain.Entities;

namespace SmartLost.ListingsService.Application.Commands.DeleteDraft;

public sealed class DeleteDraftCommandHandler(IListingLifecycleStore store, ICurrentUser user)
    : IRequestHandler<DeleteDraftCommand, Result>
{
    public async Task<Result> Handle(DeleteDraftCommand request, CancellationToken cancellationToken)
    {
        Guid authorId = user.GetRequiredUserId();
        Listing draft = await store.GetOwnedDraftAsync(request.ListingId, authorId, cancellationToken);
        store.Remove(draft);
        return Result.Success();
    }
}
