namespace SmartLost.MatchingService.Application.Profiling;

/// <summary>A request that may carry the caller's correlation identifier, for example from Listings.</summary>
public interface ICorrelatedRequest
{
    string? CorrelationId
    {
        get;
    }
}
