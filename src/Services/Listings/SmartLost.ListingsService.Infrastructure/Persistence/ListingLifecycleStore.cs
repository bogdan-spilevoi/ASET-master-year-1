using Microsoft.EntityFrameworkCore;
using SmartLost.BuildingBlocks.Core.Exceptions;
using SmartLost.ListingsService.Application.Interfaces;
using SmartLost.ListingsService.Domain.Entities;
using SmartLost.ListingsService.Domain.Enums;
using SmartLost.ListingsService.Domain.Errors;

namespace SmartLost.ListingsService.Infrastructure.Persistence;

public sealed class ListingLifecycleStore(ListingsDbContext db) : IListingLifecycleStore
{
    public async Task<Listing> GetOwnedDraftAsync(Guid id, Guid authorId, CancellationToken token)
    {
        return await db.Listings.Include(l => l.Photo).Include(l => l.Categories).ThenInclude(c => c.Category)
            .SingleOrDefaultAsync(l => l.Id == id && l.AuthorId == authorId && l.State == ListingState.Draft, token)
            ?? throw new DomainException(ListingErrors.NotFound);
    }

    public async Task<Listing> GetOpenAsync(Guid id, CancellationToken token)
    {
        return await db.Listings.SingleOrDefaultAsync(l => l.Id == id && l.State == ListingState.Open, token)
            ?? throw new DomainException(ListingErrors.NotFound);
    }

    public async Task<List<Category>> GetCategoriesAsync(Guid[] ids, CancellationToken token)
    {
        // Hold the catalog stable until the ID-based draft assignment commits.
        IQueryable<Category> requested = db.Database.IsNpgsql()
            ? db.Categories.FromSqlInterpolated($"""SELECT * FROM "Categories" WHERE "Id" = ANY ({ids}) FOR SHARE""")
            : db.Categories.Where(c => ids.Contains(c.Id));
        List<Category> categories = await requested.ToListAsync(token);
        if (categories.Count != ids.Length)
        {
            throw new DomainException(ListingErrors.InvalidCategory);
        }
        if (!categories.Any(c => c.MergedIntoId.HasValue))
        {
            return categories;
        }
        Guid[] canonicalIds = [.. categories.Select(c => c.MergedIntoId ?? c.Id).Distinct()];
        return await db.Categories.Where(c => canonicalIds.Contains(c.Id)).ToListAsync(token);
    }

    public Task<List<MatchDecision>> GetUnconfirmedAsync(Guid listingId, CancellationToken token)
    {
        return db.MatchDecisions.Include(d => d.LostListing).Include(d => d.FoundListing)
            .Where(d => (d.LostListingId == listingId || d.FoundListingId == listingId) &&
                d.State != MatchDecisionState.Confirmed && d.State != MatchDecisionState.Invalidated).ToListAsync(token);
    }

    public Task<MatchDecision?> FindReviewedPairAsync(Listing lost, Listing found, CancellationToken token)
    {
        return db.MatchDecisions.SingleOrDefaultAsync(d => d.LostListingId == lost.Id && d.FoundListingId == found.Id &&
            d.LostRevision == lost.Revision && d.FoundRevision == found.Revision && d.State != MatchDecisionState.Invalidated, token);
    }

    public Task<bool> IsReservedOrRecoveredAsync(Guid lostId, Guid foundId, CancellationToken token)
    {
        return db.MatchDecisions.AnyAsync(d => (d.LostListingId == lostId || d.FoundListingId == foundId) &&
            (d.State == MatchDecisionState.Accepted || d.State == MatchDecisionState.Confirmed), token);
    }

    public async Task<MatchDecision> GetOwnedDecisionAsync(Guid id, Guid authorId, CancellationToken token)
    {
        return await db.MatchDecisions.Include(d => d.LostListing).Include(d => d.FoundListing).Include(d => d.Recovery)
            .SingleOrDefaultAsync(d => d.Id == id && d.LostListing.AuthorId == authorId, token)
            ?? throw new DomainException(ListingErrors.DecisionNotFound);
    }

    public void Remove(Listing draft)
    {
        db.Listings.Remove(draft);
    }

    public void Add(MatchDecision decision)
    {
        db.MatchDecisions.Add(decision);
    }
}
