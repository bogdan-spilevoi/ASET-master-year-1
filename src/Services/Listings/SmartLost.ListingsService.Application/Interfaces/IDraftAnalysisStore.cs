using SmartLost.Contracts.Listings;

namespace SmartLost.ListingsService.Application.Interfaces;

public interface IDraftAnalysisStore
{
    Task<DraftPhotoTarget?> FindOwnedPhotoAsync(Guid authorId, Guid listingId, CancellationToken token);
    Task EnqueueAsync(DraftAnalysisRequest request, CancellationToken token);
    Task<AnalysisOperationDto> DeliveryAsync(Guid requestId, CancellationToken token);
}
