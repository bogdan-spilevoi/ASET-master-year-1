using SmartLost.MatchingService.Domain.Entities;
using SmartLost.MatchingService.Domain.Enums;

namespace SmartLost.MatchingService.Domain.Suggestions;

/// <summary>Eligibility rules for a source/candidate pair, applied to the Matching projection.</summary>
public static class CandidatePolicy
{
    /// <summary>The baseline permits a Found date at most this many days before the Lost date.</summary>
    public const int DefaultFoundBeforeLostToleranceDays = 1;

    public static bool IsCandidate(SearchListing source, SearchListing candidate,
        int foundBeforeLostToleranceDays = DefaultFoundBeforeLostToleranceDays)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(candidate);
        ArgumentOutOfRangeException.ThrowIfNegative(foundBeforeLostToleranceDays);

        if (candidate.Id == source.Id || !candidate.Eligible || candidate.Type == source.Type ||
            candidate.AuthorId == source.AuthorId || !candidate.CategoryKeys.Overlaps(source.CategoryKeys))
        {
            return false;
        }

        (SearchListing lost, SearchListing found) = source.Type == ListingType.Lost ? (source, candidate) : (candidate, source);
        return found.OccurredOn >= lost.OccurredOn.AddDays(-foundBeforeLostToleranceDays);
    }

    public static IReadOnlyList<SearchListing> Select(SearchListing source, IEnumerable<SearchListing> pool,
        int foundBeforeLostToleranceDays = DefaultFoundBeforeLostToleranceDays)
    {
        ArgumentNullException.ThrowIfNull(pool);
        return [.. pool.Where(candidate => IsCandidate(source, candidate, foundBeforeLostToleranceDays))];
    }
}
