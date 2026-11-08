using SmartLost.MatchingService.Application.Contracts;
using SmartLost.MatchingService.Application.Exceptions;

namespace SmartLost.MatchingService.Application.Interfaces;

/// <summary>DINOv3 image-embedding port served by the local AI runtime.</summary>
public interface IImageEmbeddingGenerator
{
    /// <summary>Throws <see cref="AiInferenceException"/> or <see cref="TimeoutException"/> when inference fails.</summary>
    Task<GeneratedEmbedding> GenerateAsync(PhotoContent photo, CancellationToken cancellationToken);
}
