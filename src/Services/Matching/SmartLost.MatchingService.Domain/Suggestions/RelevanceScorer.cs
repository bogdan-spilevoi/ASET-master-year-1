using SmartLost.MatchingService.Domain.Entities;

namespace SmartLost.MatchingService.Domain.Suggestions;

/// <summary>
/// Combines the available category, colour, location, date and optional visual evidence.
/// Missing evidence is excluded from the weighted average instead of being scored as zero.
/// </summary>
public static class RelevanceScorer
{
    public static MatchSuggestion Score(SearchListing source, SearchListing candidate, double? visualSimilarity, RankingPolicy policy)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(candidate);
        ArgumentNullException.ThrowIfNull(policy);

        List<(MatchReason Reason, double Score, double Weight)> components =
        [
            (MatchReason.SameCategory, Jaccard(source.CategoryKeys, candidate.CategoryKeys), policy.CategoryWeight),
            (MatchReason.SameArea, string.Equals(source.Location, candidate.Location, StringComparison.OrdinalIgnoreCase) ? 1 : 0,
                policy.LocationWeight),
            (MatchReason.CloseDates, DateProximity(source.OccurredOn, candidate.OccurredOn, policy.DateWindowDays), policy.DateWeight)
        ];
        if (source.Colour is not null && candidate.Colour is not null)
        {
            components.Add((MatchReason.SimilarColour, string.Equals(source.Colour, candidate.Colour, StringComparison.Ordinal) ? 1 : 0,
                policy.ColourWeight));
        }

        if (visualSimilarity is double similarity)
        {
            components.Add((MatchReason.VisualSimilarity, Math.Max(0, similarity), policy.VisualWeight));
        }

        double totalWeight = components.Sum(component => component.Weight);
        double relevance = totalWeight <= 0 ? 0 : components.Sum(component => component.Score * component.Weight) / totalWeight;
        MatchReason[] reasons = [.. components
            .Where(component => component.Weight > 0 && component.Score >= policy.ReasonThreshold)
            .Select(component => component.Reason)
            .Order()];
        return new MatchSuggestion(candidate.Id, candidate.SourceRevision, relevance, visualSimilarity, reasons, policy.Version);
    }

    public static IReadOnlyList<MatchSuggestion> Rank(SearchListing source,
        IEnumerable<(SearchListing Candidate, double? VisualSimilarity)> candidates, RankingPolicy policy)
    {
        ArgumentNullException.ThrowIfNull(candidates);
        ArgumentNullException.ThrowIfNull(policy);
        return [.. candidates
            .Select(pair => Score(source, pair.Candidate, pair.VisualSimilarity, policy))
            .Where(suggestion => suggestion.Relevance >= policy.MinimumRelevance)
            .OrderByDescending(suggestion => suggestion.Relevance)
            .ThenBy(suggestion => suggestion.CandidateListingId)];
    }

    private static double Jaccard(IReadOnlySet<string> left, IReadOnlySet<string> right)
    {
        int union = left.Union(right).Count();
        return union == 0 ? 0 : (double)left.Intersect(right).Count() / union;
    }

    private static double DateProximity(DateOnly left, DateOnly right, int windowDays)
    {
        int days = Math.Abs(left.DayNumber - right.DayNumber);
        return Math.Max(0, 1 - ((double)days / windowDays));
    }
}
