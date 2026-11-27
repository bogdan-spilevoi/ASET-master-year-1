using SmartLost.ListingsService.Domain.Entities;

namespace SmartLost.ListingsService.Application.Interfaces;

public interface IListingLifecycleStore
{
    Task<Listing> GetOwnedDraftAsync(Guid id, Guid authorId, CancellationToken token);
    Task<Listing> GetOpenAsync(Guid id, CancellationToken token);
    Task<List<Category>> GetCategoriesAsync(Guid[] ids, CancellationToken token);
    Task<List<MatchDecision>> GetUnconfirmedAsync(Guid listingId, CancellationToken token);
    Task<MatchDecision?> FindReviewedPairAsync(Listing lost, Listing found, CancellationToken token);
    Task<bool> IsReservedOrRecoveredAsync(Guid lostId, Guid foundId, CancellationToken token);
    Task<MatchDecision> GetOwnedDecisionAsync(Guid id, Guid authorId, CancellationToken token);
    void Remove(Listing draft);
    void Add(MatchDecision decision);
}
