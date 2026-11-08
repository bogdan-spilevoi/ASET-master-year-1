using SmartLost.MatchingService.Application.Contracts;
using SmartLost.MatchingService.Domain.ValueObjects;

namespace SmartLost.MatchingService.Application.Interfaces;

/// <summary>Obtains permitted photo bytes through the Listings contract; Matching never reads Listings storage.</summary>
public interface IListingPhotoReader
{
    /// <summary>Returns null when the photo revision is missing or no longer permitted.</summary>
    Task<PhotoContent?> ReadAsync(Guid listingId, PhotoReference photo, CancellationToken cancellationToken);
}
