using SmartLost.BuildingBlocks.Core.Pagination;
using SmartLost.Contracts.Listings;
using SmartLost.ListingsService.Application.Dtos;
using SmartLost.ListingsService.Application.Queries.GetMyListings;

namespace SmartLost.ListingsService.Application.Interfaces;

public interface IListingsReadStore
{
    Task<IReadOnlyList<CategoryDto>> GetCategoriesAsync(CancellationToken cancellationToken);

    Task<PageSlice<ListingSummaryDto>> GetOpenListingsAsync(ListingType? type, Guid? categoryId,
        int pageIndex, int pageSize, CancellationToken cancellationToken);

    Task<PageSlice<OwnedListingDto>> GetOwnedListingsAsync(Guid ownerId, GetMyListingsQuery filter, CancellationToken cancellationToken);

    Task<PhotoContentDto?> GetOpenListingPhotoAsync(Guid listingId, CancellationToken cancellationToken);

    Task<ListingDetailsDto?> GetOpenListingAsync(Guid id, CancellationToken cancellationToken);
}
