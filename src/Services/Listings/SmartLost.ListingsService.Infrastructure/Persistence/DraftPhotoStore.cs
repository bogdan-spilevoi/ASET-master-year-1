using Microsoft.EntityFrameworkCore;
using SmartLost.ListingsService.Application.Dtos;
using SmartLost.ListingsService.Application.Interfaces;
using SmartLost.ListingsService.Domain.Entities;
using SmartLost.ListingsService.Domain.Enums;

namespace SmartLost.ListingsService.Infrastructure.Persistence;

public sealed class DraftPhotoStore(ListingsDbContext db) : IDraftPhotoStore
{
    public void Add(Listing draft, byte[] bytes)
    {
        db.Listings.Add(draft);
        db.Add(new PhotoPayload(draft.Photo!.Id, bytes));
    }

    public Task<PhotoContentDto?> ReadOwnedDraftPhotoAsync(Guid listingId, Guid authorId, CancellationToken cancellationToken)
    {
        return (from photo in db.Photos
                join payload in db.Set<PhotoPayload>() on photo.Id equals payload.PhotoId
                where photo.ListingId == listingId && photo.Listing.AuthorId == authorId && photo.Listing.State == ListingState.Draft
                select new PhotoContentDto(photo.Format, payload.Bytes)).SingleOrDefaultAsync(cancellationToken);
    }

    public Task<PhotoContentDto?> ReadAsync(Guid listingId, Guid photoId, int revision, CancellationToken cancellationToken)
    {
        return (from photo in db.Photos
                join payload in db.Set<PhotoPayload>() on photo.Id equals payload.PhotoId
                where photo.Id == photoId && photo.ListingId == listingId && photo.Revision == revision
                select new PhotoContentDto(photo.Format, payload.Bytes)).SingleOrDefaultAsync(cancellationToken);
    }
}
