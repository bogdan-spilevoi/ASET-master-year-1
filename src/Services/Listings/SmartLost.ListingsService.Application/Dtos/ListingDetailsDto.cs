using SmartLost.Contracts.Listings;

namespace SmartLost.ListingsService.Application.Dtos;

public sealed record ListingDetailsDto(Guid Id, ListingType Type, string Title, string Description,
    string Location, DateOnly OccurredOn, string? Colour, IReadOnlyCollection<string> Traits, int Revision,
    DateTime? PublishedAt, IReadOnlyList<CategoryDto> Categories, PhotoDto? Photo);
