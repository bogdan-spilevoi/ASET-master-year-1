using Microsoft.Extensions.Logging;

namespace SmartLost.MatchingService.Application.Profiling;

/// <summary>Measures stages and AI calls of the current Matching operation and enforces the AI call timeout.</summary>
public sealed class MatchingProfiler(
    MatchingProfilingOptions options, TimeProvider timeProvider, ILogger<MatchingProfiler> logger)
{
    private const string NoOperation = "(none)";
    private const string NoCorrelation = "-";

    private static readonly Action<ILogger, string, string, string, ProfilingOutcome, double, string, Exception?> _logAiCallProblem =
        LoggerMessage.Define<string, string, string, ProfilingOutcome, double, string>(
            LogLevel.Warning, new EventId(1303, "MatchingAiCallProblem"),
            "AI call {Call} in {Operation} ({CorrelationId}) finished as {Outcome} after {ElapsedMs:F1} ms. Exception: {ExceptionType}.");

    private static readonly Action<ILogger, string, string, string, double, double, Exception?> _logSlowAiCall =
        LoggerMessage.Define<string, string, string, double, double>(
            LogLevel.Warning, new EventId(1304, "MatchingSlowAiCall"),
            "AI call {Call} in {Operation} ({CorrelationId}) took {ElapsedMs:F1} ms, above the {ThresholdMs} ms threshold.");

    private readonly AsyncLocal<OperationProfile?> _current = new();

    public OperationProfile? Current => _current.Value;

    /// <summary>Starts a profile that flows to awaited calls; disposing the scope restores the previous profile.</summary>
    public IDisposable Begin(OperationProfile profile)
    {
        ArgumentNullException.ThrowIfNull(profile);
        OperationProfile? previous = _current.Value;
        _current.Value = profile;
        return new Scope(this, previous);
    }

    public Task<T> MeasureStageAsync<T>(
        string stage, Func<CancellationToken, Task<T>> action, Func<T, int> countItems, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(countItems);
        return MeasureAsync(stage, StageKind.Stage, action, countItems, timeout: null, cancellationToken);
    }

    public Task<T> MeasureAiCallAsync<T>(string call, Func<CancellationToken, Task<T>> action, CancellationToken cancellationToken)
    {
        return MeasureAsync(call, StageKind.AiCall, action, countItems: null, options.AiCallTimeout, cancellationToken);
    }

    private async Task<T> MeasureAsync<T>(string name, StageKind kind, Func<CancellationToken, Task<T>> action,
        Func<T, int>? countItems, TimeSpan? timeout, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentNullException.ThrowIfNull(action);
        using CancellationTokenSource timeoutSource = timeout is TimeSpan limit
            ? new CancellationTokenSource(limit, timeProvider)
            : new CancellationTokenSource();
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeoutSource.Token);
        long started = timeProvider.GetTimestamp();
        try
        {
            T result = await action(linked.Token);
            Record(name, kind, started, ProfilingOutcome.Succeeded, countItems?.Invoke(result), exception: null);
            return result;
        }
        catch (OperationCanceledException exception) when (timeoutSource.IsCancellationRequested && !cancellationToken.IsCancellationRequested)
        {
            var timeoutException = new TimeoutException($"{name} exceeded the configured {timeout} timeout.", exception);
            Record(name, kind, started, ProfilingOutcome.TimedOut, itemCount: null, timeoutException);
            throw timeoutException;
        }
        catch (OperationCanceledException exception) when (cancellationToken.IsCancellationRequested)
        {
            Record(name, kind, started, ProfilingOutcome.Cancelled, itemCount: null, exception);
            throw;
        }
        catch (TimeoutException exception)
        {
            Record(name, kind, started, ProfilingOutcome.TimedOut, itemCount: null, exception);
            throw;
        }
        catch (Exception exception)
        {
            Record(name, kind, started, ProfilingOutcome.Failed, itemCount: null, exception);
            throw;
        }
    }

    private void Record(string name, StageKind kind, long started, ProfilingOutcome outcome, int? itemCount, Exception? exception)
    {
        TimeSpan elapsed = timeProvider.GetElapsedTime(started);
        string? exceptionType = exception?.GetType().Name;
        OperationProfile? profile = _current.Value;
        profile?.Add(new StageMeasurement(name, kind, elapsed, outcome, itemCount, exceptionType));
        if (kind != StageKind.AiCall)
        {
            return;
        }

        string operation = profile?.OperationName ?? NoOperation;
        string correlationId = profile?.CorrelationId ?? NoCorrelation;
        if (outcome != ProfilingOutcome.Succeeded)
        {
            _logAiCallProblem(logger, name, operation, correlationId, outcome, elapsed.TotalMilliseconds, exceptionType ?? "none", null);
        }
        else if (elapsed >= options.SlowAiCallThreshold)
        {
            _logSlowAiCall(logger, name, operation, correlationId, elapsed.TotalMilliseconds,
                options.SlowAiCallThreshold.TotalMilliseconds, null);
        }
    }

    private sealed class Scope(MatchingProfiler profiler, OperationProfile? previous) : IDisposable
    {
        public void Dispose()
        {
            profiler._current.Value = previous;
        }
    }
}
