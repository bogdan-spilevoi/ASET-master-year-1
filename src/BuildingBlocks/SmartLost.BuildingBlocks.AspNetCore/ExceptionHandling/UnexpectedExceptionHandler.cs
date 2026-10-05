using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;

namespace SmartLost.BuildingBlocks.AspNetCore.ExceptionHandling;

public sealed class UnexpectedExceptionHandler(ILogger<UnexpectedExceptionHandler> logger) : IExceptionHandler
{
    private static readonly Action<ILogger, string, Exception?> _logUnexpectedException = LoggerMessage.Define<string>(
        LogLevel.Error, new EventId(1002, "UnexpectedException"), "Unhandled API exception. Trace identifier: {TraceId}.");

    public async ValueTask<bool> TryHandleAsync(HttpContext httpContext, Exception exception, CancellationToken cancellationToken)
    {
        if (exception is OperationCanceledException && httpContext.RequestAborted.IsCancellationRequested)
        {
            return false;
        }

        _logUnexpectedException(logger, httpContext.TraceIdentifier, exception);
        httpContext.Response.StatusCode = StatusCodes.Status500InternalServerError;
        var problem = new ProblemDetails
        {
            Status = StatusCodes.Status500InternalServerError,
            Title = "An unexpected error occurred."
        };
        problem.Extensions["code"] = "error.unexpected";
        problem.Extensions["traceId"] = httpContext.TraceIdentifier;
        await httpContext.Response.WriteAsJsonAsync(problem, options: null, contentType: "application/problem+json", cancellationToken: cancellationToken);
        return true;
    }
}
