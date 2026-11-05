namespace SmartLost.MatchingService.Api.Contracts;

public sealed record MatchSuggestionsRequest(Guid ListingId, int ExpectedSourceRevision);
