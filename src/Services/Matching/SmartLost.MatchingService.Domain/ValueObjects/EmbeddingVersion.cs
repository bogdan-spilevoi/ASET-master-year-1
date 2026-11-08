using SmartLost.BuildingBlocks.Core.Validation;
using SmartLost.MatchingService.Domain.Errors;

namespace SmartLost.MatchingService.Domain.ValueObjects;

/// <summary>Model and preprocessing identity; vectors are comparable only within one version.</summary>
public sealed record EmbeddingVersion
{
    public const int MaximumLength = 200;

    public EmbeddingVersion(string modelVersion, string preprocessingVersion)
    {
        Guard.NotNullOrWhiteSpace(modelVersion, MatchingErrors.ModelVersionRequired, MaximumLength);
        Guard.NotNullOrWhiteSpace(preprocessingVersion, MatchingErrors.PreprocessingVersionRequired, MaximumLength);
        ModelVersion = modelVersion;
        PreprocessingVersion = preprocessingVersion;
    }

    public string ModelVersion
    {
        get;
    }

    public string PreprocessingVersion
    {
        get;
    }
}
