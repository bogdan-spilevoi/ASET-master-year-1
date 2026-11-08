using MediatR;
using Microsoft.Extensions.Logging;
using SmartLost.BuildingBlocks.Application.Profiling;
using SmartLost.BuildingBlocks.Core.Results;

namespace SmartLost.BuildingBlocks.Application.Behaviors;

/// <summary>
/// Opt-in outermost pipeline decorator: starts the operation profile, runs the request and logs
/// the total duration, result and stage breakdown recorded by the profiling decorators.
/// </summary>
public sealed class ProfilingBehavior<TRequest, TResponse>(
    IProfilingContext profiler,
    ProfilingOptions options,
    TimeProvider timeProvider,
    ILogger<ProfilingBehavior<TRequest, TResponse>> logger) : IPipelineBehavior<TRequest, TResponse>
    where TRequest : notnull
    where TResponse : IResult
{
    private static readonly Action<ILogger, string, string, ProfilingOutcome, double, string, string, Exception?> _logCompleted =
        LoggerMessage.Define<string, string, ProfilingOutcome, double, string, string>(
            LogLevel.Information, new EventId(1301, "OperationProfiled"),
            "Operation {Operation} ({CorrelationId}) finished as {Outcome} in {ElapsedMs:F1} ms. Stages: {Stages}. Detail: {Detail}.");

    private static readonly Action<ILogger, string, string, double, double, ProfilingOutcome, string, Exception?> _logSlow =
        LoggerMessage.Define<string, string, double, double, ProfilingOutcome, string>(
            LogLevel.Warning, new EventId(1302, "SlowOperation"),
            "Operation {Operation} ({CorrelationId}) took {ElapsedMs:F1} ms, above the {ThresholdMs} ms threshold, " +
            "and finished as {Outcome}. Stages: {Stages}.");

    public async Task<TResponse> Handle(TRequest request, RequestHandlerDelegate<TResponse> next, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(next);
        var profile = new OperationProfile(typeof(TRequest).Name, CorrelationIds.Resolve((request as ICorrelatedRequest)?.CorrelationId));
        using IDisposable scope = profiler.Begin(profile);
        long started = timeProvider.GetTimestamp();
        ProfilingOutcome outcome = ProfilingOutcome.Failed;
        string detail = "none";
        try
        {
            TResponse response = await next(cancellationToken);
            outcome = response.IsSuccess ? ProfilingOutcome.Succeeded : ProfilingOutcome.Failed;
            if (response.IsFailure)
            {
                detail = string.Join(',', response.Errors.Select(error => error.Code).Distinct());
            }

            return response;
        }
        catch (OperationCanceledException exception) when (cancellationToken.IsCancellationRequested)
        {
            outcome = ProfilingOutcome.Cancelled;
            detail = exception.GetType().Name;
            throw;
        }
        catch (TimeoutException exception)
        {
            outcome = ProfilingOutcome.TimedOut;
            detail = exception.GetType().Name;
            throw;
        }
        catch (Exception exception)
        {
            detail = exception.GetType().Name;
            throw;
        }
        finally
        {
            TimeSpan elapsed = timeProvider.GetElapsedTime(started);
            string stages = profile.Describe();
            _logCompleted(logger, profile.OperationName, profile.CorrelationId, outcome, elapsed.TotalMilliseconds, stages, detail, null);
            if (elapsed >= options.SlowOperationThreshold)
            {
                _logSlow(logger, profile.OperationName, profile.CorrelationId, elapsed.TotalMilliseconds,
                    options.SlowOperationThreshold.TotalMilliseconds, outcome, stages, null);
            }
        }
    }
}
