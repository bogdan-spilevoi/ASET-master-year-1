using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using SmartLost.Contracts.Listings;
using SmartLost.ListingsService.Application.Interfaces;
using SmartLost.ListingsService.Domain.Enums;

namespace SmartLost.ListingsService.Infrastructure.Persistence;

public sealed class DraftAnalysisStore(ListingsDbContext db, TimeProvider clock) : IDraftAnalysisStore
{
    public Task<DraftPhotoTarget?> FindOwnedPhotoAsync(Guid authorId, Guid listingId, CancellationToken token)
    {
        return db.Photos.Where(p => p.ListingId == listingId && p.Listing.AuthorId == authorId && p.Listing.State == ListingState.Draft)
            .Join(db.Set<PhotoPayload>(), p => p.Id, payload => payload.PhotoId, (p, _) => new DraftPhotoTarget(p.ListingId, p.Id, p.Revision))
            .SingleOrDefaultAsync(token);
    }

    public async Task EnqueueAsync(DraftAnalysisRequest request, CancellationToken token)
    {
        Guid id = request.Photo.RequestId(request.Kind);
        string json = JsonSerializer.Serialize(request, JsonSerializerOptions.Web);
        await db.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO "OutboxMessages" ("Id", "Type", "Version", "Payload", "OccurredOn", "AttemptCount")
            VALUES ({id}, {DraftAnalysisRequest.RoutingKey}, 1, {json}, {clock.GetUtcNow()}, 0)
            ON CONFLICT ("Id") DO NOTHING
            """, token);
    }

    public async Task<AnalysisOperationDto> DeliveryAsync(Guid requestId, CancellationToken token)
    {
        var row = await db.OutboxMessages.Where(m => m.Id == requestId).Select(m => new { m.DeadLetteredAt }).SingleOrDefaultAsync(token);
        return row is null ? new("NotRequested") : row.DeadLetteredAt.HasValue ? new("Failed", ErrorCode: "delivery.failed") : new("Pending");
    }
}
