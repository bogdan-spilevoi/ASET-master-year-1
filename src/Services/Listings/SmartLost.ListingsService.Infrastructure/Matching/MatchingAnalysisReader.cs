using System.Net.Http.Json;
using SmartLost.Contracts.Listings;
using SmartLost.ListingsService.Application.Interfaces;

namespace SmartLost.ListingsService.Infrastructure.Matching;

public sealed class MatchingAnalysisReader(HttpClient client) : IMatchingAnalysisReader
{
    public async Task<DraftAnalysisDto> ReadAsync(DraftPhotoTarget photo, CancellationToken token)
    {
        string path = FormattableString.Invariant($"internal/analysis/{photo.ListingId:D}/{photo.PhotoId:D}/{photo.PhotoRevision}");
        return await client.GetFromJsonAsync<DraftAnalysisDto>(path, token) ?? throw new InvalidDataException("Missing Matching analysis.");
    }
}
