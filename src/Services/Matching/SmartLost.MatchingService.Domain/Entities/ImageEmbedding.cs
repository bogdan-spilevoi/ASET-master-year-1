using SmartLost.BuildingBlocks.Core.Entities;
using SmartLost.BuildingBlocks.Core.Validation;
using SmartLost.MatchingService.Domain.Errors;
using SmartLost.MatchingService.Domain.ValueObjects;

namespace SmartLost.MatchingService.Domain.Entities;

/// <summary>Cached, L2-normalized image vector for one photo revision and one model/preprocessing version.</summary>
public sealed class ImageEmbedding : AggregateRoot<Guid>
{
    public const double NormTolerance = 1e-3;

    private float[] _vector = [];

    private ImageEmbedding()
    {
    }

    public Guid ListingId
    {
        get; private set;
    }

    public PhotoReference Photo { get; private set; } = null!;

    public EmbeddingVersion Version { get; private set; } = null!;

    public IReadOnlyList<float> Vector => _vector;

    public int Dimensions => _vector.Length;

    public static ImageEmbedding Create(Guid listingId, PhotoReference photo, EmbeddingVersion version, IReadOnlyList<float> vector)
    {
        Guard.Against(listingId == Guid.Empty, MatchingErrors.ListingIdRequired);
        Guard.NotNull(photo, MatchingErrors.PhotoIdRequired);
        Guard.NotNull(version, MatchingErrors.ModelVersionRequired);
        Guard.NotNull(vector, MatchingErrors.InvalidEmbeddingVector);
        Guard.Against(vector.Count == 0, MatchingErrors.InvalidEmbeddingVector);

        float[] components = [.. vector];
        double squaredNorm = 0;
        foreach (float component in components)
        {
            Guard.Against(!float.IsFinite(component), MatchingErrors.InvalidEmbeddingVector);
            squaredNorm += (double)component * component;
        }

        Guard.Against(Math.Abs(Math.Sqrt(squaredNorm) - 1) > NormTolerance, MatchingErrors.InvalidEmbeddingVector);
        return new ImageEmbedding
        {
            Id = Guid.NewGuid(),
            ListingId = listingId,
            Photo = photo,
            Version = version,
            _vector = components
        };
    }

    public bool IsCompatibleWith(ImageEmbedding other)
    {
        ArgumentNullException.ThrowIfNull(other);
        return Version == other.Version && Dimensions == other.Dimensions;
    }

    /// <summary>Cosine similarity of normalized vectors, or null when the representations are incompatible.</summary>
    public double? CosineSimilarity(ImageEmbedding other)
    {
        if (!IsCompatibleWith(other))
        {
            return null;
        }

        double dot = 0;
        for (int index = 0; index < _vector.Length; index++)
        {
            dot += (double)_vector[index] * other._vector[index];
        }

        return Math.Clamp(dot, -1, 1);
    }
}
