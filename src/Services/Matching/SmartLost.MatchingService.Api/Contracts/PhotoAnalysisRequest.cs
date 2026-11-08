namespace SmartLost.MatchingService.Api.Contracts;

public sealed record PhotoAnalysisRequest(Guid ListingId, Guid PhotoId, int PhotoRevision);
