using SmartLost.Contracts.Listings;

namespace SmartLost.ListingsService.Application.Interfaces;

public interface IMatchingAnalysisReader
{
    Task<DraftAnalysisDto> ReadAsync(DraftPhotoTarget photo, CancellationToken token);
}
