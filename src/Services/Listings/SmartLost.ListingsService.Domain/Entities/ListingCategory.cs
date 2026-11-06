using SmartLost.BuildingBlocks.Core.Validation;
using SmartLost.ListingsService.Domain.Errors;

namespace SmartLost.ListingsService.Domain.Entities;

public partial class ListingCategory
{
    private ListingCategory()
    {
    }

    public Guid ListingId
    {
        get; private set;
    }

    public Listing Listing { get; private set; } = null!;

    public Guid CategoryId
    {
        get; private set;
    }

    public Category Category { get; private set; } = null!;

    internal static ListingCategory Create(Listing listing, Category category)
    {
        Guard.NotNull(category, ListingErrors.InvalidCategory);
        Guard.Against(category.Id == Guid.Empty || string.IsNullOrWhiteSpace(category.NormalizedName), ListingErrors.InvalidCategory);
        return new ListingCategory { Listing = listing, ListingId = listing.Id, Category = category, CategoryId = category.Id };
    }

    internal void Detach()
    {
        // The composite key must stay stable until persistence deletes the removed row.
        Listing = null!;
    }
}
