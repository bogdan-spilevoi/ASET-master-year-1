using SmartLost.MatchingService.Domain.ValueObjects;

namespace SmartLost.MatchingService.Application.Contracts;

/// <summary>Inference output; the domain validates finiteness and normalization before caching it.</summary>
public sealed record GeneratedEmbedding(EmbeddingVersion Version, IReadOnlyList<float> Vector);
