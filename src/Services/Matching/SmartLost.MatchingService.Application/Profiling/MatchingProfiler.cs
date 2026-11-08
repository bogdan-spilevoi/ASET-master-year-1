using Microsoft.Extensions.Logging;
using SmartLost.BuildingBlocks.Application.Profiling;

namespace SmartLost.MatchingService.Application.Profiling;

/// <summary>Measures stages and AI calls of the current Matching operation and enforces the AI call timeout.</summary>
public sealed class MatchingProfiler(
    MatchingProfilingOptions options, TimeProvider timeProvider, ILogger<MatchingProfiler> logger) : IProfilingContext
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

    private readonly ProfilingContext _context = new();

    public OperationProfile? Current => _context.Current;

    /// <summary>Starts a profile that flows to awaited calls; disposing the scope restores the previous profile.</summary>
    public IDisposable Begin(OperationProfile profile)
    {
        return _context.Begin(profile);
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
            linked.Token.ThrowIfCancellationRequested();
            Task<T> operation = action(linked.Token);
            // AI adapters have a response deadline even when cancellation is ignored.
            // Stages may use a scoped DbContext, so await those to completion.
            T result = timeout is not null ? await operation.WaitAsync(linked.Token) : await operation;
            linked.Token.ThrowIfCancellationRequested();
            if (timeout is TimeSpan deadline && timeProvider.GetElapsedTime(started) >= deadline)
            {
                throw new TimeoutException($"{name} exceeded the configured {deadline} timeout.");
            }

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
        OperationProfile? profile = Current;
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

}
