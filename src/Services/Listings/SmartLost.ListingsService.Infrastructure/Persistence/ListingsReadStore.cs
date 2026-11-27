using Microsoft.EntityFrameworkCore;
using SmartLost.BuildingBlocks.Core.Pagination;
using SmartLost.Contracts.Listings;
using SmartLost.ListingsService.Application.Dtos;
using SmartLost.ListingsService.Application.Queries.GetMyListings;
using SmartLost.ListingsService.Application.Interfaces;
using SmartLost.ListingsService.Domain.Entities;
using SmartLost.ListingsService.Domain.Enums;

namespace SmartLost.ListingsService.Infrastructure.Persistence;

public sealed class ListingsReadStore(ListingsDbContext context) : IListingsReadStore
{
    public async Task<IReadOnlyList<CategoryDto>> GetCategoriesAsync(CancellationToken cancellationToken)
    {
        return await context.Categories.AsNoTracking().Where(category => category.MergedIntoId == null)
            .OrderBy(category => category.NormalizedName).ThenBy(category => category.Id)
            .Select(category => new CategoryDto(category.Id, category.NormalizedName, category.BroadClass)).ToListAsync(cancellationToken);
    }

    public async Task<PageSlice<ListingSummaryDto>> GetOpenListingsAsync(ListingType? type, Guid? categoryId,
        int pageIndex, int pageSize, CancellationToken cancellationToken)
    {
        IQueryable<Listing> query = context.Listings.AsNoTracking().Where(listing => listing.State == ListingState.Open);
        if (type.HasValue)
        {
            query = query.Where(listing => listing.Type == type.Value);
        }

        if (categoryId.HasValue)
        {
            IQueryable<Guid> canonical = context.Categories.Where(c => c.Id == categoryId.Value).Select(c => c.MergedIntoId ?? c.Id);
            query = query.Where(listing => listing.Categories.Any(assignment => canonical.Contains(assignment.CategoryId)));
        }

        int count = await query.CountAsync(cancellationToken);
        List<ListingSummaryDto> items = await query.OrderByDescending(listing => listing.PublishedAt).ThenBy(listing => listing.Id)
            .Skip(checked(pageIndex * pageSize)).Take(pageSize)
            .Select(listing => new ListingSummaryDto(listing.Id, listing.Type, listing.Title, listing.Location,
                listing.OccurredOn, listing.PublishedAt,
                listing.Categories.OrderBy(assignment => assignment.Category.NormalizedName).ThenBy(assignment => assignment.CategoryId)
                    .Select(assignment => new CategoryDto(assignment.CategoryId, assignment.Category.NormalizedName, assignment.Category.BroadClass)).ToList(),
                listing.Photo == null ? null : new PhotoDto(listing.Photo.Id, listing.Photo.Format, listing.Photo.ByteSize, listing.Photo.Revision)))
            .ToListAsync(cancellationToken);
        return new PageSlice<ListingSummaryDto>(items, count);
    }

    public async Task<PageSlice<OwnedListingDto>> GetOwnedListingsAsync(Guid ownerId, GetMyListingsQuery filter,
        CancellationToken cancellationToken)
    {
        IQueryable<Listing> query = context.Listings.AsNoTracking().Where(listing => listing.AuthorId == ownerId);
        if (filter.State.HasValue)
        {
            query = query.Where(listing => listing.State == filter.State);
        }

        if (filter.Type.HasValue)
        {
            query = query.Where(listing => listing.Type == filter.Type);
        }

        if (filter.ClosureReason.HasValue)
        {
            query = query.Where(listing => listing.ClosureReason == filter.ClosureReason);
        }

        int count = await query.CountAsync(cancellationToken);
        List<OwnedListingDto> items = await query.OrderByDescending(listing => listing.PublishedAt.HasValue)
            .ThenByDescending(listing => listing.PublishedAt).ThenBy(listing => listing.Id)
            .Skip(checked(filter.PageIndex * filter.PageSize)).Take(filter.PageSize)
            .Select(listing => new OwnedListingDto(listing.Id, listing.Type, listing.State.ToString(),
                listing.ClosureReason.HasValue ? listing.ClosureReason.Value.ToString() : null,
                listing.Title, listing.Location, listing.OccurredOn == default ? null : listing.OccurredOn))
            .ToListAsync(cancellationToken);
        return new PageSlice<OwnedListingDto>(items, count);
    }

    public Task<PhotoContentDto?> GetOpenListingPhotoAsync(Guid listingId, CancellationToken cancellationToken)
    {
        return (from photo in context.Photos
                join payload in context.Set<PhotoPayload>() on photo.Id equals payload.PhotoId
                where photo.ListingId == listingId && photo.Listing.State == ListingState.Open
                select new PhotoContentDto(photo.Format, payload.Bytes)).SingleOrDefaultAsync(cancellationToken);
    }

    public Task<ListingDetailsDto?> GetOpenListingAsync(Guid id, CancellationToken cancellationToken)
    {
        return context.Listings.AsNoTracking().Where(listing => listing.Id == id && listing.State == ListingState.Open)
            .Select(listing => new ListingDetailsDto(listing.Id, listing.Type, listing.Title, listing.Description,
                listing.Location, listing.OccurredOn, listing.Colour, listing.Traits, listing.Revision, listing.PublishedAt,
                listing.Categories.OrderBy(assignment => assignment.Category.NormalizedName).ThenBy(assignment => assignment.CategoryId)
                    .Select(assignment => new CategoryDto(assignment.CategoryId, assignment.Category.NormalizedName, assignment.Category.BroadClass)).ToList(),
                listing.Photo == null ? null : new PhotoDto(listing.Photo.Id, listing.Photo.Format, listing.Photo.ByteSize, listing.Photo.Revision)))
            .SingleOrDefaultAsync(cancellationToken);
    }
}
