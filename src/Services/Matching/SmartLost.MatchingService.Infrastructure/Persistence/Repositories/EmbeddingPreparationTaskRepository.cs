using Microsoft.EntityFrameworkCore;
using SmartLost.MatchingService.Application.Interfaces;
using SmartLost.MatchingService.Domain.Entities;

namespace SmartLost.MatchingService.Infrastructure.Persistence.Repositories;

public sealed class EmbeddingPreparationTaskRepository(MatchingDbContext context) : IEmbeddingPreparationTaskRepository
{
    public Task<EmbeddingPreparationTask?> GetAsync(Guid taskId, CancellationToken cancellationToken)
    {
        return context.EmbeddingPreparationTasks.SingleOrDefaultAsync(task => task.Id == taskId, cancellationToken);
    }
}
