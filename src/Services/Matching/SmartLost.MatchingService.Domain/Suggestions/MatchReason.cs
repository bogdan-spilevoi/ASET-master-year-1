namespace SmartLost.MatchingService.Domain.Suggestions;

/// <summary>Reason codes derived from the components that actually contributed to relevance.</summary>
public enum MatchReason
{
    SameCategory = 0,
    SimilarColour = 1,
    SameArea = 2,
    CloseDates = 3,
    VisualSimilarity = 4
}
