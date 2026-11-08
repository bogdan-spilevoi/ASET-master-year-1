using Microsoft.EntityFrameworkCore;
using SmartLost.Contracts.Listings;
using SmartLost.MatchingService.Application.Services;
using SmartLost.MatchingService.Domain.Entities;
using SmartLost.MatchingService.Domain.ValueObjects;
using SmartLost.MatchingService.Infrastructure.Persistence;

namespace SmartLost.MatchingService.Infrastructure.Demo;

/// <summary>Replaces all Matching rows with a fixed development scenario. Never use against real data.</summary>
public sealed class MatchingDemoSeeder(MatchingDbContext context, MatchingOptions options, TimeProvider timeProvider)
{
    public static readonly Guid SourceListingId = Guid.Parse("10000000-0000-0000-0000-000000000001");

    private static readonly DateOnly _day = new(2026, 11, 3);
    private static readonly Guid _studentA = Guid.Parse("a0000000-0000-0000-0000-00000000000a");
    private static readonly Guid _studentB = Guid.Parse("b0000000-0000-0000-0000-00000000000b");
    private static readonly Guid _studentC = Guid.Parse("c0000000-0000-0000-0000-00000000000c");

    private static readonly SeedListing[] _listings =
    [
        new("10000000-0000-0000-0000-000000000001", "source: lost black backpack", ListingType.Lost, _studentA,
            "backpack", "black", "Library", 0, true, "black-backpack-zipper"),
        new("20000000-0000-0000-0000-000000000001", "candidate: same backpack, same place and day", ListingType.Found, _studentB,
            "backpack", "black", "Library", 0, true, "black-backpack-zipper"),
        new("20000000-0000-0000-0000-000000000002", "candidate: blue backpack elsewhere", ListingType.Found, _studentC,
            "backpack", "blue", "Canteen", 2, true, "blue-backpack"),
        new("20000000-0000-0000-0000-000000000003", "candidate: no photo, metadata only", ListingType.Found, _studentC,
            "backpack", "black", "Library", 1, true, null),
        new("20000000-0000-0000-0000-000000000004", "excluded: different category", ListingType.Found, _studentB,
            "keys", "grey", "Library", 0, true, "grey-keys"),
        new("20000000-0000-0000-0000-000000000005", "excluded: same author as source", ListingType.Found, _studentA,
            "backpack", "black", "Library", 0, true, null),
        new("20000000-0000-0000-0000-000000000006", "excluded: found three days before loss", ListingType.Found, _studentB,
            "backpack", "black", "Library", -3, true, null),
        new("30000000-0000-0000-0000-000000000001", "profiling: slow AI call", ListingType.Found, _studentB,
            "backpack", "black", "Library", 0, false, "slow:black-backpack"),
        new("30000000-0000-0000-0000-000000000002", "profiling: AI timeout", ListingType.Found, _studentB,
            "backpack", "black", "Library", 0, false, "timeout:black-backpack"),
        new("30000000-0000-0000-0000-000000000003", "profiling: AI failure", ListingType.Found, _studentB,
            "backpack", "black", "Library", 0, false, "error:black-backpack")
    ];

    /// <summary>Mock photo content for the seeded photos; static, so it survives API restarts.</summary>
    public static IReadOnlyDictionary<PhotoReference, string> DemoPhotos
    {
        get;
    } = _listings
        .Where(seed => seed.Appearance is not null)
        .ToDictionary(seed => new PhotoReference(DerivePhotoId(Guid.Parse(seed.Id)), 1), seed => seed.Appearance!);

    public async Task<DemoScenario> SeedAsync(CancellationToken cancellationToken)
    {
        context.EmbeddingPreparationTasks.RemoveRange(await context.EmbeddingPreparationTasks.ToListAsync(cancellationToken));
        context.ImageEmbeddings.RemoveRange(await context.ImageEmbeddings.ToListAsync(cancellationToken));
        context.SearchListings.RemoveRange(await context.SearchListings.ToListAsync(cancellationToken));
        await context.SaveChangesAsync(cancellationToken);

        DateTime nowUtc = timeProvider.GetUtcNow().UtcDateTime;
        List<DemoListing> listings = [];
        foreach (SeedListing seed in _listings)
        {
            var listingId = Guid.Parse(seed.Id);
            PhotoReference? photo = seed.Appearance is null ? null : new PhotoReference(DerivePhotoId(listingId), 1);
            context.SearchListings.Add(SearchListing.Create(listingId, 1, 1, seed.Type, seed.AuthorId, photo, [seed.Category],
                seed.Colour, [], _day.AddDays(seed.DayOffset), seed.Location, seed.Eligible));
            Guid? taskId = null;
            if (photo is not null)
            {
                var task = EmbeddingPreparationTask.Create(listingId, photo, options.EmbeddingVersion, nowUtc);
                context.EmbeddingPreparationTasks.Add(task);
                taskId = task.Id;
            }

            listings.Add(new DemoListing(listingId, seed.Role, seed.Type.ToString(), seed.Eligible, photo?.PhotoId, photo?.Revision,
                seed.Appearance, taskId));
        }

        await context.SaveChangesAsync(cancellationToken);
        return new DemoScenario(SourceListingId, 1, listings);
    }

    /// <summary>Finds the preparation task for a listing's current photo, or null when there is none.</summary>
    public async Task<Guid?> FindCurrentTaskIdAsync(Guid listingId, CancellationToken cancellationToken)
    {
        SearchListing? listing = await context.SearchListings.AsNoTracking()
            .SingleOrDefaultAsync(item => item.Id == listingId, cancellationToken);
        if (listing?.CurrentPhotoId is not Guid photoId)
        {
            return null;
        }

        return await context.EmbeddingPreparationTasks.AsNoTracking()
            .Where(task => task.ListingId == listingId && task.PhotoId == photoId && task.PhotoRevision == listing.CurrentPhotoRevision)
            .Select(task => (Guid?)task.Id)
            .FirstOrDefaultAsync(cancellationToken);
    }

    /// <summary>Readable photo ID: the listing ID with its second group set to f0f0.</summary>
    private static Guid DerivePhotoId(Guid listingId)
    {
        string text = listingId.ToString("D");
        return Guid.Parse($"{text[..9]}f0f0{text[13..]}");
    }

    private sealed record SeedListing(
        string Id,
        string Role,
        ListingType Type,
        Guid AuthorId,
        string Category,
        string Colour,
        string Location,
        int DayOffset,
        bool Eligible,
        string? Appearance);
}
