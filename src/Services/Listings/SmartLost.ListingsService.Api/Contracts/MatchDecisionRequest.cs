using SmartLost.ListingsService.Application.Dtos;

namespace SmartLost.ListingsService.Api.Contracts;

public sealed record MatchDecisionRequest(Guid FoundListingId, int LostRevision, int FoundRevision, MatchChoice Choice);
