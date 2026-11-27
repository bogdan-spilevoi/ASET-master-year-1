using MediatR;
using SmartLost.BuildingBlocks.Application.Authentication;
using SmartLost.BuildingBlocks.Core.Results;
using SmartLost.ListingsService.Application.Dtos;
using SmartLost.ListingsService.Application.Interfaces;
using SmartLost.ListingsService.Domain.Entities;

namespace SmartLost.ListingsService.Application.Commands.PatchDraft;

public sealed class PatchDraftCommandHandler(IListingLifecycleStore store, ICurrentUser user, TimeProvider clock)
    : IRequestHandler<PatchDraftCommand, Result<DraftDetailsDto>>
{
    public async Task<Result<DraftDetailsDto>> Handle(PatchDraftCommand request, CancellationToken cancellationToken)
    {
        Guid authorId = user.GetRequiredUserId();
        Listing draft = await store.GetOwnedDraftAsync(request.ListingId, authorId, cancellationToken);
        DraftPatchDto patch = request.Patch;
        List<Category>? categories = patch.CategoryIds is null ? null : await store.GetCategoriesAsync(patch.CategoryIds, cancellationToken);
        draft.PatchDraft(authorId, patch.ExpectedRevision, clock.GetUtcNow().UtcDateTime, patch.Title, patch.Description,
            patch.Location, patch.OccurredOn, patch.Colour, patch.Traits, categories, patch.CategoryName, patch.BroadClass);
        return Result<DraftDetailsDto>.Success(DraftDetailsDto.From(draft));
    }
}
