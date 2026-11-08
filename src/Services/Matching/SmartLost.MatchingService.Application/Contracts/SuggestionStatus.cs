namespace SmartLost.MatchingService.Application.Contracts;

public enum SuggestionStatus
{
    /// <summary>The ranked pool is current; it may be empty.</summary>
    Ready = 0,

    /// <summary>The projection is missing or older than the expected source revision.</summary>
    Pending = 1,

    /// <summary>The current source is not eligible for matching.</summary>
    Unavailable = 2
}
