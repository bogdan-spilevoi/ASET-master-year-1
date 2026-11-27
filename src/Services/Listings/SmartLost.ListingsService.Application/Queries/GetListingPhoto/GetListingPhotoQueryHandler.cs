using MediatR;
using SmartLost.BuildingBlocks.Core.Results;
using SmartLost.ListingsService.Application.Dtos;
using SmartLost.ListingsService.Application.Interfaces;
using SmartLost.ListingsService.Domain.Errors;

namespace SmartLost.ListingsService.Application.Queries.GetListingPhoto;

public sealed class GetListingPhotoQueryHandler(IListingsReadStore store)
    : IRequestHandler<GetListingPhotoQuery, Result<PhotoContentDto>>
{
    public async Task<Result<PhotoContentDto>> Handle(GetListingPhotoQuery request, CancellationToken cancellationToken)
    {
        PhotoContentDto? photo = await store.GetOpenListingPhotoAsync(request.ListingId, cancellationToken);
        return photo is null ? Result<PhotoContentDto>.Failure(ListingErrors.NotFound) : Result<PhotoContentDto>.Success(photo);
    }
}
