namespace SmartLost.MatchingService.Infrastructure.Demo;

/// <summary>Seeded development data used to exercise the Matching flow through the test endpoints.</summary>
public sealed record DemoScenario(Guid SourceListingId, int SourceRevision, IReadOnlyList<DemoListing> Listings);

public sealed record DemoListing(
    Guid ListingId,
    string Role,
    string Type,
    bool Eligible,
    Guid? PhotoId,
    int? PhotoRevision,
    string? MockAppearance,
    Guid? EmbeddingTaskId);
