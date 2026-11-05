namespace SmartLost.MatchingService.Api.Diagnostics;

/// <summary>One captured log entry. Only the exception type name is kept, never its message.</summary>
public sealed record RecentLogEntry(
    DateTimeOffset TimestampUtc,
    string Level,
    string Category,
    int EventId,
    string? EventName,
    string Message,
    string? ExceptionType);
