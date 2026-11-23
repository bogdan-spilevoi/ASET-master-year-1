using SmartLost.ListingsService.Application.Dtos;
using SmartLost.ListingsService.Domain.Entities;

namespace SmartLost.ListingsService.Application.Interfaces;

public interface IDraftPhotoStore
{
    void Add(Listing draft, byte[] bytes);
    Task<PrivatePhotoDto?> ReadAsync(Guid listingId, Guid photoId, int revision, CancellationToken cancellationToken);
}
