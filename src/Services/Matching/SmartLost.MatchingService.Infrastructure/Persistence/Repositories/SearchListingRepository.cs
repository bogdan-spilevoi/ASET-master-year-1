using Microsoft.EntityFrameworkCore;
using SmartLost.MatchingService.Application.Interfaces;
using SmartLost.MatchingService.Domain.Entities;

namespace SmartLost.MatchingService.Infrastructure.Persistence.Repositories;

public sealed class SearchListingRepository(MatchingDbContext context) : ISearchListingRepository
{
    public Task<SearchListing?> GetAsync(Guid listingId, CancellationToken cancellationToken)
    {
        return context.SearchListings.SingleOrDefaultAsync(listing => listing.Id == listingId, cancellationToken);
    }

    public async Task<IReadOnlyList<SearchListing>> GetCandidatePoolAsync(SearchListing source, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(source);
        // Coarse database filter; CandidatePolicy reapplies category and date rules.
        return await context.SearchListings.AsNoTracking()
            .Where(listing => listing.Eligible && listing.Type != source.Type && listing.Id != source.Id &&
                listing.AuthorId != source.AuthorId)
            .ToListAsync(cancellationToken);
    }
}
