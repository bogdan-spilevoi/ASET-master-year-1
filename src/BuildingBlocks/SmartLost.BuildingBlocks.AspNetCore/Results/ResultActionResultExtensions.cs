using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using SmartLost.BuildingBlocks.Core.Results;

namespace SmartLost.BuildingBlocks.AspNetCore.Results;

public static class ResultActionResultExtensions
{
    public static IActionResult ToActionResult<T>(
        this ControllerBase controller, Result<T> result, int successStatusCode = StatusCodes.Status200OK)
    {
        return result.IsFailure ? ToProblem(controller, result.Errors) :
            successStatusCode == StatusCodes.Status204NoContent ? controller.NoContent() :
            controller.StatusCode(successStatusCode, result.Value);
    }

    public static IActionResult ToActionResult(
        this ControllerBase controller, Result result, int successStatusCode = StatusCodes.Status204NoContent)
    {
        return result.IsFailure ? ToProblem(controller, result.Errors) : controller.StatusCode(successStatusCode);
    }

    private static ObjectResult ToProblem(ControllerBase controller, IReadOnlyList<Error> errors)
    {
        Error primary = errors[0];
        int status = primary.Kind switch
        {
            ErrorKind.Validation => StatusCodes.Status400BadRequest,
            ErrorKind.NotFound => StatusCodes.Status404NotFound,
            ErrorKind.Conflict => StatusCodes.Status409Conflict,
            ErrorKind.Unauthorized => StatusCodes.Status401Unauthorized,
            ErrorKind.Forbidden => StatusCodes.Status403Forbidden,
            _ => StatusCodes.Status500InternalServerError
        };
        var problem = new ProblemDetails
        {
            Status = status,
            Title = primary.Kind.ToString(),
            Detail = status == StatusCodes.Status500InternalServerError ? "An unexpected error occurred." : primary.Message,
            Type = $"https://httpstatuses.io/{status}"
        };
        problem.Extensions["code"] = status == StatusCodes.Status500InternalServerError ? "error.unexpected" : primary.Code;
        problem.Extensions["traceId"] = controller.HttpContext.TraceIdentifier;
        if (status != StatusCodes.Status500InternalServerError)
        {
            problem.Extensions["errors"] = errors;
        }

        ObjectResult response = controller.StatusCode(status, problem);
        response.ContentTypes.Add("application/problem+json");
        return response;
    }
}
