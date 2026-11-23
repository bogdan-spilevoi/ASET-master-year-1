using SmartLost.Contracts.Listings;
using SmartLost.ListingsService.Domain.Entities;

namespace SmartLost.ListingsService.Application.Dtos;

public sealed record DraftDetailsDto(Guid Id, ListingType Type, int Revision, string Title, string Description,
    string Location, DateOnly? OccurredOn, string? Colour, IReadOnlyList<string> Traits,
    IReadOnlyList<CategoryDto> Categories, PhotoDto? Photo)
{
    public static DraftDetailsDto From(Listing draft)
    {
        return new(draft.Id, draft.Type, draft.Revision, draft.Title, draft.Description, draft.Location,
            draft.OccurredOn == default ? null : draft.OccurredOn, draft.Colour, [.. draft.Traits],
            [.. draft.Categories.OrderBy(c => c.Category.NormalizedName, StringComparer.Ordinal)
                .Select(c => new CategoryDto(c.CategoryId, c.Category.NormalizedName))],
            draft.Photo is { } photo ? new PhotoDto(photo.Id, photo.Format, photo.ByteSize, photo.Revision) : null);
    }
}
