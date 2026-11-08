using Microsoft.AspNetCore.Mvc;
using SmartLost.BuildingBlocks.AspNetCore.Results;
using SmartLost.BuildingBlocks.Core.Results;
using SmartLost.MatchingService.Domain.Errors;

namespace SmartLost.MatchingService.Api.Results;

public static class MatchingResultExtensions
{
    private static readonly HashSet<string> _inferenceCodes = new(StringComparer.Ordinal)
    {
        MatchingErrors.InferenceUnavailable.Code,
        MatchingErrors.InferenceTimedOut.Code,
        MatchingErrors.EmbeddingVersionMismatch.Code
    };

    /// <summary>
    /// Like <see cref="ResultActionResultExtensions.ToActionResult{T}"/>, but reports AI runtime failures as
    /// 503 with their error code instead of a generic 500, so callers can retry later.
    /// </summary>
    public static IActionResult ToMatchingActionResult<T>(this ControllerBase controller, Result<T> result)
    {
        ArgumentNullException.ThrowIfNull(controller);
        ArgumentNullException.ThrowIfNull(result);
        if (result.IsSuccess || !_inferenceCodes.Contains(result.Errors[0].Code))
        {
            return controller.ToActionResult(result);
        }

        Error error = result.Errors[0];
        var problem = new ProblemDetails
        {
            Status = StatusCodes.Status503ServiceUnavailable,
            Title = "AI runtime unavailable",
            Detail = error.Message,
            Type = "https://httpstatuses.io/503"
        };
        problem.Extensions["code"] = error.Code;
        problem.Extensions["traceId"] = controller.HttpContext.TraceIdentifier;
        ObjectResult response = controller.StatusCode(StatusCodes.Status503ServiceUnavailable, problem);
        response.ContentTypes.Add("application/problem+json");
        return response;
    }
}
