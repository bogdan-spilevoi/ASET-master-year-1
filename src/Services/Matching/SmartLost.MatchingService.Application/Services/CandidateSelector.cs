using SmartLost.MatchingService.Application.Interfaces;
using SmartLost.MatchingService.Domain.Entities;
using SmartLost.MatchingService.Domain.Suggestions;

namespace SmartLost.MatchingService.Application.Services;

public sealed class CandidateSelector(ISearchListingRepository listings, MatchingOptions options) : ICandidateSelector
{
    public async Task<IReadOnlyList<SearchListing>> SelectAsync(SearchListing source, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(source);
        IReadOnlyList<SearchListing> pool = await listings.GetCandidatePoolAsync(source, cancellationToken);
        return CandidatePolicy.Select(source, pool, options.FoundBeforeLostToleranceDays);
    }
}
