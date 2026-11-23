using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using SmartLost.Contracts.Listings;
using SmartLost.MatchingService.Infrastructure.Persistence;

namespace SmartLost.MatchingService.Infrastructure.Analysis;

public sealed class DraftAnalysisResults(MatchingDbContext db)
{
    public async Task<DraftAnalysisDto> ReadAsync(DraftPhotoTarget target, CancellationToken token)
    {
        Guid traitsId = target.RequestId(DraftAnalysisKind.Traits);
        Guid embeddingId = target.RequestId(DraftAnalysisKind.Embedding);
        Dictionary<Guid, PhotoAnalysisWork> rows = await db.Set<PhotoAnalysisWork>().AsNoTracking().Where(row => row.Id == traitsId || row.Id == embeddingId)
            .ToDictionaryAsync(row => row.Id, token);
        rows.TryGetValue(traitsId, out PhotoAnalysisWork? traits);
        rows.TryGetValue(embeddingId, out PhotoAnalysisWork? embedding);
        PhotoAnalysis? labels = traits?.SuggestionsJson is string json ? JsonSerializer.Deserialize<PhotoAnalysis>(json, JsonSerializerOptions.Web) : null;
        return new DraftAnalysisDto(target, Status(traits), Status(embedding), labels);
    }

    private static AnalysisOperationDto Status(PhotoAnalysisWork? row)
    {
        return row is null ? new("NotRequested") : new(row.State, row.ModelVersion, row.Dimensions, row.ErrorCode);
    }
}
