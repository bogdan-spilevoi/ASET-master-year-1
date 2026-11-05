using SmartLost.MatchingService.Domain.Suggestions;

namespace SmartLost.MatchingService.Application.Contracts;

/// <summary>Internal ranked pool; Listings rechecks eligibility and displays at most three results.</summary>
public sealed record MatchSuggestionsResponse(
    SuggestionStatus Status,
    Guid ListingId,
    int? SourceRevision,
    string? RankingVersion,
    IReadOnlyList<MatchSuggestion> Suggestions);
