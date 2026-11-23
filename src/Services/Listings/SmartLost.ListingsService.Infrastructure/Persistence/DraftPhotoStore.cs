using Microsoft.EntityFrameworkCore;
using SmartLost.ListingsService.Application.Dtos;
using SmartLost.ListingsService.Application.Interfaces;
using SmartLost.ListingsService.Domain.Entities;

namespace SmartLost.ListingsService.Infrastructure.Persistence;

public sealed class DraftPhotoStore(ListingsDbContext db) : IDraftPhotoStore
{
    public void Add(Listing draft, byte[] bytes)
    {
        db.Listings.Add(draft);
        db.Add(new PhotoPayload(draft.Photo!.Id, bytes));
    }

    public Task<PrivatePhotoDto?> ReadAsync(Guid listingId, Guid photoId, int revision, CancellationToken cancellationToken)
    {
        return (from photo in db.Photos
                join payload in db.Set<PhotoPayload>() on photo.Id equals payload.PhotoId
                where photo.Id == photoId && photo.ListingId == listingId && photo.Revision == revision
                select new PrivatePhotoDto(photo.Format, payload.Bytes)).SingleOrDefaultAsync(cancellationToken);
    }
}
