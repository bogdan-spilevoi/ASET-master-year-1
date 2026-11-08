namespace SmartLost.MatchingService.Domain.Suggestions;

/// <summary>
/// Calculated, non-persistent ranking result. Relevance is not an ownership probability;
/// a null visual similarity means no compatible visual evidence, not zero similarity.
/// </summary>
public sealed record MatchSuggestion(
    Guid CandidateListingId,
    int CandidateRevision,
    double Relevance,
    double? VisualSimilarity,
    IReadOnlyList<MatchReason> Reasons,
    string RankingVersion);
