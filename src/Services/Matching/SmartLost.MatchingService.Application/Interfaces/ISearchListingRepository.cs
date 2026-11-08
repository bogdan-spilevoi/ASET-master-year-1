using SmartLost.MatchingService.Domain.Entities;

namespace SmartLost.MatchingService.Application.Interfaces;

public interface ISearchListingRepository
{
    Task<SearchListing?> GetAsync(Guid listingId, CancellationToken cancellationToken);

    /// <summary>
    /// Returns a coarse candidate pool for the source, such as eligible opposite-type projections.
    /// The domain CandidatePolicy reapplies every eligibility rule to the returned pool.
    /// </summary>
    Task<IReadOnlyList<SearchListing>> GetCandidatePoolAsync(SearchListing source, CancellationToken cancellationToken);
}
