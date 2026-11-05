using System.Diagnostics;

namespace SmartLost.MatchingService.Application.Profiling;

public static class CorrelationIds
{
    public const int MaximumLength = 64;

    /// <summary>
    /// Uses a supplied identifier only when it is short and log-safe; otherwise falls back to the
    /// current W3C trace ID or a new identifier.
    /// </summary>
    public static string Resolve(string? supplied)
    {
        if (supplied is { Length: > 0 and <= MaximumLength } && supplied.All(IsAllowed))
        {
            return supplied;
        }

        return Activity.Current is Activity activity ? activity.TraceId.ToHexString() : Guid.NewGuid().ToString("N");
    }

    private static bool IsAllowed(char character)
    {
        return char.IsAsciiLetterOrDigit(character) || character is '-' or '_' or '.' or ':';
    }
}
