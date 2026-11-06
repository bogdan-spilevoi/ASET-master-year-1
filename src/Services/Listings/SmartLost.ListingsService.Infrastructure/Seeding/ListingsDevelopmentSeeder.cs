using Microsoft.EntityFrameworkCore;
using SmartLost.ListingsService.Domain.Entities;
using SmartLost.ListingsService.Domain.Enums;
using SmartLost.ListingsService.Infrastructure.Persistence;

namespace SmartLost.ListingsService.Infrastructure.Seeding;

/// <summary>Explicit development fixtures; existing records are preserved on sequential reruns.</summary>
public sealed class ListingsDevelopmentSeeder(ListingsDbContext context)
{
    private static readonly Guid _lostAuthor = Guid.Parse("11111111-1111-4111-8111-111111111111");
    private static readonly Guid _foundAuthor = Guid.Parse("22222222-2222-4222-8222-222222222222");

    public async Task SeedAsync(CancellationToken cancellationToken = default)
    {
        await using Microsoft.EntityFrameworkCore.Storage.IDbContextTransaction transaction = await context.Database.BeginTransactionAsync(cancellationToken);
        Dictionary<string, Category> categories = new(StringComparer.Ordinal);
        foreach (string name in new[] { "backpack", "wallet", "keys" })
        {
            Category? category = await context.Categories.OrderBy(value => value.Id)
                .FirstOrDefaultAsync(value => value.NormalizedName == name, cancellationToken);
            category ??= Category.Create(name);
            if (context.Entry(category).State == EntityState.Detached)
            {
                context.Categories.Add(category);
            }

            categories.Add(name, category);
        }

        DateTime now = DateTime.UtcNow;
        await AddAsync("Demo lost blue backpack", ListingType.Lost, categories["backpack"], ListingState.Open, true, now, cancellationToken);
        await AddAsync("Demo found blue backpack", ListingType.Found, categories["backpack"], ListingState.Open, false, now, cancellationToken);
        await AddAsync("Demo lost brown wallet", ListingType.Lost, categories["wallet"], ListingState.Open, false, now, cancellationToken);
        await AddAsync("Demo found brown wallet", ListingType.Found, categories["wallet"], ListingState.Open, false, now, cancellationToken);
        await AddAsync("Demo draft keys", ListingType.Lost, categories["keys"], ListingState.Draft, false, now, cancellationToken);
        await AddAsync("Demo withdrawn keys", ListingType.Found, categories["keys"], ListingState.Closed, false, now, cancellationToken);
        await context.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
    }

    private async Task AddAsync(string title, ListingType type, Category category, ListingState state,
        bool withPhoto, DateTime now, CancellationToken cancellationToken)
    {
        Guid author = type == ListingType.Lost ? _lostAuthor : _foundAuthor;
        if (await context.Listings.AnyAsync(listing => listing.AuthorId == author && listing.Title == title, cancellationToken))
        {
            return;
        }

        var listing = Listing.CreateDraft(author, type);
        listing.UpdateDetails(author, listing.Revision, title, "Fictional campus demonstration listing.", "Campus library",
            DateOnly.FromDateTime(now.AddDays(-1)), now, traits: ["demo item"]);
        listing.AddCategory(author, listing.Revision, category);
        if (withPhoto)
        {
            listing.ReplacePhoto(author, listing.Revision, Photo.Create("demo/metadata-only/backpack.jpg", "image/jpeg", 1024));
        }

        if (state != ListingState.Draft)
        {
            listing.Publish(author, listing.Revision, now);
            if (state == ListingState.Closed)
            {
                listing.Withdraw(author, listing.Revision);
            }
        }

        context.Listings.Add(listing);
    }
}
