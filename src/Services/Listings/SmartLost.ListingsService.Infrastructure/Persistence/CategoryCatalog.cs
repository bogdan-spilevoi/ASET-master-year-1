using Microsoft.EntityFrameworkCore;
using SmartLost.ListingsService.Application.Interfaces;
using SmartLost.ListingsService.Domain.Entities;

namespace SmartLost.ListingsService.Infrastructure.Persistence;

public sealed class CategoryCatalog(ListingsDbContext db) : ICategoryCatalog
{
    public Task<long> GetVersionAsync(CancellationToken token)
    {
        return db.Set<CategoryCatalogState>().Select(state => state.Version).SingleAsync(token);
    }

    public async Task<Category> ResolveAsync(string normalizedName, string? broadClass, CancellationToken token)
    {
        var id = Guid.NewGuid();
        int inserted = await db.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO "Categories" ("Id", "NormalizedName", "BroadClass")
            VALUES ({id}, {normalizedName}, {broadClass})
            ON CONFLICT ("NormalizedName") DO NOTHING
            """, token);
        Guid target = await db.Categories.Where(c => c.NormalizedName == normalizedName)
            .Select(c => c.MergedIntoId ?? c.Id).SingleAsync(token);
        int enriched = 0;
        if (inserted == 0 && broadClass is not null)
        {
            enriched = await db.Categories.Where(c => c.Id == target && c.BroadClass == null)
                .ExecuteUpdateAsync(update => update.SetProperty(c => c.BroadClass, broadClass), token);
        }
        if (inserted + enriched > 0)
        {
            await db.Set<CategoryCatalogState>().ExecuteUpdateAsync(update => update.SetProperty(s => s.Version, s => s.Version + 1), token);
        }
        return await db.Categories.SingleAsync(c => c.Id == target, token);
    }
}
