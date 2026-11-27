using SmartLost.Contracts.Listings;

namespace SmartLost.ListingsService.Application.Dtos;

public sealed record OwnedListingDto(Guid Id, ListingType Type, string State, string? ClosureReason,
    string Title, string Location, DateOnly? OccurredOn);
