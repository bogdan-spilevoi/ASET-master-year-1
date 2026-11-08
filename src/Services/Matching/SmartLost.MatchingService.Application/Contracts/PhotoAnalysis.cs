namespace SmartLost.MatchingService.Application.Contracts;

/// <summary>
/// Advisory labels matching the <c>objects</c>/<c>uncertainties</c> structure of
/// AIModels/Qwen3-VL-4B-Instruct-ONNX/Prompts/image-labels.txt. Users confirm final values in Listings.
/// </summary>
public sealed record PhotoAnalysis(IReadOnlyList<DetectedObject> Objects, IReadOnlyList<string> Uncertainties);
