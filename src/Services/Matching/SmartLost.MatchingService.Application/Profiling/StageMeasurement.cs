namespace SmartLost.MatchingService.Application.Profiling;

/// <summary>One measured stage; ExceptionType contains only the exception type name, never its message.</summary>
public sealed record StageMeasurement(
    string Name,
    StageKind Kind,
    TimeSpan Duration,
    ProfilingOutcome Outcome,
    int? ItemCount,
    string? ExceptionType);
