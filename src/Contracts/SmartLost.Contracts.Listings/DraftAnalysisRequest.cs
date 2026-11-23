namespace SmartLost.Contracts.Listings;

public sealed record DraftAnalysisRequest(DraftPhotoTarget Photo, DraftAnalysisKind Kind)
{
    public const string RoutingKey = "listings.draft.analysis-requested";
    public const string Queue = "smartlost.matching.draft-analysis";
}
