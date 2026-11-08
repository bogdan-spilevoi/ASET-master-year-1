using SmartLost.MatchingService.Application.Contracts;
using SmartLost.MatchingService.Application.Exceptions;

namespace SmartLost.MatchingService.Application.Interfaces;

/// <summary>Qwen3-VL label-extraction port served by the local AI runtime.</summary>
public interface IPhotoAnalyzer
{
    /// <summary>Throws <see cref="AiInferenceException"/> or <see cref="TimeoutException"/> when inference fails.</summary>
    Task<PhotoAnalysis> AnalyzeAsync(PhotoContent photo, CancellationToken cancellationToken);
}
