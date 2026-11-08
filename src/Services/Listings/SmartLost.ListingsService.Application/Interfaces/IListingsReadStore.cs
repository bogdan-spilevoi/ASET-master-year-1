using SmartLost.BuildingBlocks.Core.Pagination;
using SmartLost.Contracts.Listings;
using SmartLost.ListingsService.Application.Dtos;

namespace SmartLost.ListingsService.Application.Interfaces;

public interface IListingsReadStore
{
    Task<IReadOnlyList<CategoryDto>> GetCategoriesAsync(CancellationToken cancellationToken);

    Task<PageSlice<ListingSummaryDto>> GetOpenListingsAsync(ListingType? type, Guid? categoryId,
        int pageIndex, int pageSize, CancellationToken cancellationToken);

    Task<ListingDetailsDto?> GetOpenListingAsync(Guid id, CancellationToken cancellationToken);
}
