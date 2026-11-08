using Microsoft.EntityFrameworkCore;
using SmartLost.MatchingService.Application.Interfaces;
using SmartLost.MatchingService.Domain.Entities;
using SmartLost.MatchingService.Domain.ValueObjects;

namespace SmartLost.MatchingService.Infrastructure.Persistence.Repositories;

public sealed class ImageEmbeddingRepository(MatchingDbContext context) : IImageEmbeddingRepository
{
    public async Task<IReadOnlyList<ImageEmbedding>> GetAsync(
        IReadOnlyCollection<PhotoReference> photos, EmbeddingVersion version, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(photos);
        ArgumentNullException.ThrowIfNull(version);
        Guid[] photoIds = [.. photos.Select(photo => photo.PhotoId).Distinct()];
        List<ImageEmbedding> candidates = await context.ImageEmbeddings.AsNoTracking()
            .Where(embedding => photoIds.Contains(embedding.PhotoId) && embedding.ModelVersion == version.ModelVersion &&
                embedding.PreprocessingVersion == version.PreprocessingVersion)
            .ToListAsync(cancellationToken);
        HashSet<PhotoReference> requested = [.. photos];
        return [.. candidates.Where(embedding => requested.Contains(embedding.Photo))];
    }

    public async Task UpsertAsync(ImageEmbedding embedding, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(embedding);
        ImageEmbedding? existing = await context.ImageEmbeddings.SingleOrDefaultAsync(item =>
            item.ListingId == embedding.ListingId && item.PhotoId == embedding.PhotoId && item.PhotoRevision == embedding.PhotoRevision &&
            item.ModelVersion == embedding.ModelVersion && item.PreprocessingVersion == embedding.PreprocessingVersion,
            cancellationToken);
        if (existing is not null)
        {
            context.ImageEmbeddings.Remove(existing);
        }

        await context.ImageEmbeddings.AddAsync(embedding, cancellationToken);
    }
}
