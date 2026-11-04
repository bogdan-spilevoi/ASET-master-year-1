using SmartLost.ListingsService.Domain.Enums;

namespace SmartLost.ListingsService.Application.Dtos;

public sealed record ListingSummaryDto(Guid Id, ListingType Type, string Title, string Location,
    DateOnly OccurredOn, DateTime? PublishedAt, IReadOnlyList<CategoryDto> Categories, PhotoDto? Photo);
