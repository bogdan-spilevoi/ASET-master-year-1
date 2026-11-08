using SmartLost.MatchingService.Domain.Entities;

namespace SmartLost.MatchingService.Application.Interfaces;

public interface IEmbeddingPreparationTaskRepository
{
    Task<EmbeddingPreparationTask?> GetAsync(Guid taskId, CancellationToken cancellationToken);
}
