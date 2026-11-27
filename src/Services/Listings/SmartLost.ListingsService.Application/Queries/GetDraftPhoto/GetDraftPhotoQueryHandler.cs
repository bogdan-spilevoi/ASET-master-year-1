using MediatR;
using SmartLost.BuildingBlocks.Application.Authentication;
using SmartLost.BuildingBlocks.Core.Results;
using SmartLost.ListingsService.Application.Dtos;
using SmartLost.ListingsService.Application.Interfaces;
using SmartLost.ListingsService.Domain.Errors;

namespace SmartLost.ListingsService.Application.Queries.GetDraftPhoto;

public sealed class GetDraftPhotoQueryHandler(IDraftPhotoStore store, ICurrentUser user)
    : IRequestHandler<GetDraftPhotoQuery, Result<PhotoContentDto>>
{
    public async Task<Result<PhotoContentDto>> Handle(GetDraftPhotoQuery request, CancellationToken cancellationToken)
    {
        PhotoContentDto? photo = await store.ReadOwnedDraftPhotoAsync(request.ListingId, user.GetRequiredUserId(), cancellationToken);
        return photo is null ? Result<PhotoContentDto>.Failure(ListingErrors.NotFound) : Result<PhotoContentDto>.Success(photo);
    }
}
