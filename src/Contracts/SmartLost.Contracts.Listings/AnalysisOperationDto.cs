namespace SmartLost.Contracts.Listings;

public sealed record AnalysisOperationDto(string State, string? ModelVersion = null, int? Dimensions = null, string? ErrorCode = null);
