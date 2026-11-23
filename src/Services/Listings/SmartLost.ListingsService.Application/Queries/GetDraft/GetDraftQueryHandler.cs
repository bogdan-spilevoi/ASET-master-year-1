using MediatR;
using SmartLost.BuildingBlocks.Application.Authentication;
using SmartLost.BuildingBlocks.Core.Results;
using SmartLost.ListingsService.Application.Dtos;
using SmartLost.ListingsService.Application.Interfaces;
using SmartLost.ListingsService.Domain.Entities;

namespace SmartLost.ListingsService.Application.Queries.GetDraft;

public sealed class GetDraftQueryHandler(IListingLifecycleStore store, ICurrentUser user)
    : IRequestHandler<GetDraftQuery, Result<DraftDetailsDto>>
{
    public async Task<Result<DraftDetailsDto>> Handle(GetDraftQuery request, CancellationToken cancellationToken)
    {
        Listing draft = await store.GetOwnedDraftAsync(request.ListingId, user.GetRequiredUserId(), cancellationToken);
        return Result<DraftDetailsDto>.Success(DraftDetailsDto.From(draft));
    }
}
