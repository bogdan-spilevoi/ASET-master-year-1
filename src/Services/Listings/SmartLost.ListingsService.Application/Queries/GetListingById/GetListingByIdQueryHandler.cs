using MediatR;
using SmartLost.BuildingBlocks.Core.Results;
using SmartLost.ListingsService.Application.Dtos;
using SmartLost.ListingsService.Application.Interfaces;

namespace SmartLost.ListingsService.Application.Queries.GetListingById;

public sealed class GetListingByIdQueryHandler(IListingsReadStore readStore)
    : IRequestHandler<GetListingByIdQuery, Result<ListingDetailsDto>>
{
    private static readonly Error _notFound = new("listings.not_found", "The listing was not found.", ErrorKind.NotFound);

    public async Task<Result<ListingDetailsDto>> Handle(GetListingByIdQuery request, CancellationToken cancellationToken)
    {
        ListingDetailsDto? listing = await readStore.GetOpenListingAsync(request.Id, cancellationToken);
        return listing is null ? Result<ListingDetailsDto>.Failure(_notFound) : Result<ListingDetailsDto>.Success(listing);
    }
}
