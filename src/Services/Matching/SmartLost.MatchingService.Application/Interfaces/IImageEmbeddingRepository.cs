using SmartLost.MatchingService.Domain.Entities;
using SmartLost.MatchingService.Domain.ValueObjects;

namespace SmartLost.MatchingService.Application.Interfaces;

public interface IImageEmbeddingRepository
{
    /// <summary>Reads cached embeddings for exact photo revisions and one model/preprocessing version.</summary>
    Task<IReadOnlyList<ImageEmbedding>> GetAsync(
        IReadOnlyCollection<PhotoReference> photos, EmbeddingVersion version, CancellationToken cancellationToken);

    /// <summary>Stages an insert or replacement for the unique listing/photo/revision/version target.</summary>
    Task UpsertAsync(ImageEmbedding embedding, CancellationToken cancellationToken);
}
