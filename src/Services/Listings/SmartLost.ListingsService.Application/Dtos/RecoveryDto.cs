namespace SmartLost.ListingsService.Application.Dtos;

public sealed record RecoveryDto(Guid RecoveryId, Guid DecisionId, Guid LostListingId, Guid FoundListingId);
