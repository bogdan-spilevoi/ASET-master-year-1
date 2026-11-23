using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using SmartLost.BuildingBlocks.Application.Outbox;
using SmartLost.Contracts.Listings;
using SmartLost.MatchingService.Infrastructure.Persistence;

namespace SmartLost.MatchingService.Infrastructure.Analysis;

public sealed class DraftAnalysisInbox(MatchingDbContext db, TimeProvider clock)
{
    public async Task<bool> AcceptAsync(ReadOnlyMemory<byte> bytes, CancellationToken token)
    {
        OutboxEnvelope? envelope;
        DraftAnalysisRequest? work;
        try
        {
            envelope = JsonSerializer.Deserialize<OutboxEnvelope>(bytes.Span, JsonSerializerOptions.Web);
            if (envelope is not { Type: DraftAnalysisRequest.RoutingKey, Version: 1 })
            {
                return false;
            }
            work = JsonSerializer.Deserialize<DraftAnalysisRequest>(envelope.Payload, JsonSerializerOptions.Web);
        }
        catch (Exception error) when (error is JsonException or ArgumentException)
        {
            return false;
        }
        if (work?.Photo is not { } photo || photo.ListingId == Guid.Empty || photo.PhotoId == Guid.Empty ||
            photo.PhotoRevision < 1 || !Enum.IsDefined(work.Kind) || envelope.Id != photo.RequestId(work.Kind))
        {
            return false;
        }
        await db.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO "PhotoAnalyses" ("Id", "ListingId", "PhotoId", "PhotoRevision", "Kind", "State", "Attempt", "AvailableAt", "Lease")
            VALUES ({envelope.Id}, {photo.ListingId}, {photo.PhotoId}, {photo.PhotoRevision}, {(int)work.Kind}, 'Pending', 0, {clock.GetUtcNow().UtcDateTime}, {Guid.Empty})
            ON CONFLICT ("Id") DO NOTHING
            """, token);
        return true;
    }
}
