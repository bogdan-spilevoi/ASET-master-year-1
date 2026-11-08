using SmartLost.BuildingBlocks.Core.Pagination;
using SmartLost.Contracts.Listings;

namespace SmartLost.ListingsService.Api.Contracts;

public sealed record GetListingsRequest
{
    public ListingType? Type
    {
        get; init;
    }

    public Guid? CategoryId
    {
        get; init;
    }

    public int PageIndex
    {
        get; init;
    }

    public int PageSize { get; init; } = PagedDataGuard.DefaultPageSize;
}
