using SmartLost.ListingsService.Application.Dtos;
using SmartLost.ListingsService.Domain.Entities;

namespace SmartLost.ListingsService.Application.Interfaces;

public interface IDraftPhotoStore
{
    void Add(Listing draft, byte[] bytes);
    Task<PhotoContentDto?> ReadOwnedDraftPhotoAsync(Guid listingId, Guid authorId, CancellationToken cancellationToken);
    Task<PhotoContentDto?> ReadAsync(Guid listingId, Guid photoId, int revision, CancellationToken cancellationToken);
}
