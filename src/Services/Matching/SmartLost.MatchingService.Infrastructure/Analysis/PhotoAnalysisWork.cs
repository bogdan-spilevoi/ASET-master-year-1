using SmartLost.Contracts.Listings;

namespace SmartLost.MatchingService.Infrastructure.Analysis;

public sealed class PhotoAnalysisWork
{
    public Guid Id
    {
        get; init;
    }
    public Guid ListingId
    {
        get; init;
    }
    public Guid PhotoId
    {
        get; init;
    }
    public int PhotoRevision
    {
        get; init;
    }
    public DraftAnalysisKind Kind
    {
        get; init;
    }
    public string State { get; set; } = "Pending";
    public int Attempt
    {
        get; set;
    }
    public DateTime AvailableAt
    {
        get; set;
    }
    public Guid Lease
    {
        get; set;
    }
    public string? SuggestionsJson
    {
        get; set;
    }
    public string? ModelVersion
    {
        get; set;
    }
    public int? Dimensions
    {
        get; set;
    }
    public string? ErrorCode
    {
        get; set;
    }
}
