using Microsoft.EntityFrameworkCore;
using SmartLost.BuildingBlocks.Core.Pagination;
using SmartLost.Contracts.Listings;
using SmartLost.ListingsService.Application.Dtos;
using SmartLost.ListingsService.Application.Interfaces;
using SmartLost.ListingsService.Domain.Entities;
using SmartLost.ListingsService.Domain.Enums;

namespace SmartLost.ListingsService.Infrastructure.Persistence;

public sealed class ListingsReadStore(ListingsDbContext context) : IListingsReadStore
{
    public async Task<IReadOnlyList<CategoryDto>> GetCategoriesAsync(CancellationToken cancellationToken)
    {
        return await context.Categories.AsNoTracking().OrderBy(category => category.NormalizedName).ThenBy(category => category.Id)
            .Select(category => new CategoryDto(category.Id, category.NormalizedName)).ToListAsync(cancellationToken);
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
            query = query.Where(listing => listing.Categories.Any(assignment => assignment.CategoryId == categoryId.Value));
        }

        int count = await query.CountAsync(cancellationToken);
        List<ListingSummaryDto> items = await query.OrderByDescending(listing => listing.PublishedAt).ThenBy(listing => listing.Id)
            .Skip(checked(pageIndex * pageSize)).Take(pageSize)
            .Select(listing => new ListingSummaryDto(listing.Id, listing.Type, listing.Title, listing.Location,
                listing.OccurredOn, listing.PublishedAt,
                listing.Categories.OrderBy(assignment => assignment.Category.NormalizedName).ThenBy(assignment => assignment.CategoryId)
                    .Select(assignment => new CategoryDto(assignment.CategoryId, assignment.Category.NormalizedName)).ToList(),
                listing.Photo == null ? null : new PhotoDto(listing.Photo.Id, listing.Photo.Format, listing.Photo.ByteSize, listing.Photo.Revision)))
            .ToListAsync(cancellationToken);
        return new PageSlice<ListingSummaryDto>(items, count);
    }

    public Task<ListingDetailsDto?> GetOpenListingAsync(Guid id, CancellationToken cancellationToken)
    {
        return context.Listings.AsNoTracking().Where(listing => listing.Id == id && listing.State == ListingState.Open)
            .Select(listing => new ListingDetailsDto(listing.Id, listing.Type, listing.Title, listing.Description,
                listing.Location, listing.OccurredOn, listing.Colour, listing.Traits, listing.Revision, listing.PublishedAt,
                listing.Categories.OrderBy(assignment => assignment.Category.NormalizedName).ThenBy(assignment => assignment.CategoryId)
                    .Select(assignment => new CategoryDto(assignment.CategoryId, assignment.Category.NormalizedName)).ToList(),
                listing.Photo == null ? null : new PhotoDto(listing.Photo.Id, listing.Photo.Format, listing.Photo.ByteSize, listing.Photo.Revision)))
            .SingleOrDefaultAsync(cancellationToken);
    }
}
