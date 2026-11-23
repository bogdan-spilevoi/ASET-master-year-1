namespace SmartLost.Contracts.Listings;

public sealed record DraftAnalysisDto(DraftPhotoTarget Photo, AnalysisOperationDto Traits, AnalysisOperationDto Embedding,
    PhotoAnalysis? Suggestions);
