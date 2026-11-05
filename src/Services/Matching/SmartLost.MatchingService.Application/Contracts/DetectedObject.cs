namespace SmartLost.MatchingService.Application.Contracts;

/// <summary>One object from the Qwen label prompt; null scalars mean the model could not identify the value.</summary>
public sealed record DetectedObject(
    string Category,
    string ObjectType,
    IReadOnlyList<string> Colors,
    string? Material,
    string? Shape,
    string? Pattern,
    IReadOnlyList<string> VisibleText,
    IReadOnlyList<string> DistinctiveFeatures,
    IReadOnlyList<string> Tags);
