using MediatR;
using SmartLost.BuildingBlocks.Application.Authentication;
using SmartLost.BuildingBlocks.Core.Results;
using SmartLost.ListingsService.Application.Interfaces;
using SmartLost.ListingsService.Domain.Entities;

namespace SmartLost.ListingsService.Application.Commands.PublishDraft;

public sealed class PublishDraftCommandHandler(IListingLifecycleStore store, ICategoryCatalog catalog, ICurrentUser user, TimeProvider clock)
    : IRequestHandler<PublishDraftCommand, Result>
{
    public async Task<Result> Handle(PublishDraftCommand request, CancellationToken cancellationToken)
    {
        Guid authorId = user.GetRequiredUserId();
        Listing draft = await store.GetOwnedDraftAsync(request.ListingId, authorId, cancellationToken);
        Category? category = draft.DraftCategoryName is null ? null :
            await catalog.ResolveAsync(draft.DraftCategoryName, draft.DraftBroadClass, cancellationToken);
        draft.Publish(authorId, request.ExpectedRevision, clock.GetUtcNow().UtcDateTime, category);
        return Result.Success();
    }
}
