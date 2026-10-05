using System.Net;
using System.Text.Json;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using SmartLost.BuildingBlocks.AspNetCore;
using SmartLost.BuildingBlocks.AspNetCore.ExceptionHandling;
using SmartLost.BuildingBlocks.AspNetCore.Results;
using SmartLost.BuildingBlocks.Core.Results;
using Xunit;

namespace SmartLost.BuildingBlocks.UnitTests;

public sealed class HttpResultTests
{
    [Theory]
    [InlineData(ErrorKind.Validation, 400)]
    [InlineData(ErrorKind.NotFound, 404)]
    [InlineData(ErrorKind.Conflict, 409)]
    [InlineData(ErrorKind.Unauthorized, 401)]
    [InlineData(ErrorKind.Forbidden, 403)]
    [InlineData(ErrorKind.Failure, 500)]
    public void FailureMappingPreservesExpectedErrorsAndHidesInternalDetails(ErrorKind kind, int status)
    {
        ControllerBase controller = CreateController();
        Error error = new("probe.error", "Internal or expected detail.", kind);
        ObjectResult response = Assert.IsType<ObjectResult>(controller.ToActionResult(Result<string>.Failure(error)));
        ProblemDetails problem = Assert.IsType<ProblemDetails>(response.Value);
        Assert.Equal(status, response.StatusCode);
        Assert.Equal(status, problem.Status);
        Assert.Equal("trace-test", problem.Extensions["traceId"]);
        Assert.Contains("application/problem+json", response.ContentTypes);
        Assert.Equal(kind.ToString(), problem.Title);
        Assert.NotNull(problem.Type);
        if (kind == ErrorKind.Failure)
        {
            Assert.Equal("An unexpected error occurred.", problem.Detail);
            Assert.Equal("error.unexpected", problem.Extensions["code"]);
            Assert.False(problem.Extensions.ContainsKey("errors"));
        }
        else
        {
            Assert.Equal(error.Message, problem.Detail);
            Assert.Equal(error.Code, problem.Extensions["code"]);
            Assert.Equal(error, Assert.Single(Assert.IsAssignableFrom<IReadOnlyList<Error>>(problem.Extensions["errors"])));
        }

        Assert.Equal(status, Assert.IsType<ObjectResult>(controller.ToActionResult(Result.Failure(error))).StatusCode);
    }

    [Fact]
    public void EndpointChoosesItsSuccessStatusIndependentlyOfTheApplicationResult()
    {
        ControllerBase controller = CreateController();
        var result = Result<string>.Success("value");
        ObjectResult ok = Assert.IsType<ObjectResult>(controller.ToActionResult(result));
        Assert.Equal(200, ok.StatusCode);
        Assert.Equal("value", ok.Value);
        Assert.Equal(201, Assert.IsType<ObjectResult>(controller.ToActionResult(result, 201)).StatusCode);
        Assert.IsType<NoContentResult>(controller.ToActionResult(result, 204));
        Assert.Equal(204, Assert.IsType<StatusCodeResult>(controller.ToActionResult(Result.Success())).StatusCode);
        Assert.Equal(200, Assert.IsType<StatusCodeResult>(controller.ToActionResult(Result.Success(), 200)).StatusCode);
    }

    [Fact]
    public async Task ExceptionHandlerReturnsSanitizedProblemsAndLeavesAbortedRequestsAlone()
    {
        ServiceCollection services = new();
        services.AddLogging();
        services.AddBuildingBlocksApi();
        using ServiceProvider provider = services.BuildServiceProvider();
        Assert.NotNull(provider.GetRequiredService<Microsoft.AspNetCore.Http.IProblemDetailsService>());

        UnexpectedExceptionHandler handler = new(NullLogger<UnexpectedExceptionHandler>.Instance);
        DefaultHttpContext context = new()
        {
            TraceIdentifier = "trace-test"
        };
        using MemoryStream body = new();
        context.Response.Body = body;
        Assert.True(await handler.TryHandleAsync(context, new InvalidOperationException("secret database detail"), CancellationToken.None));
        Assert.Equal((int)HttpStatusCode.InternalServerError, context.Response.StatusCode);
        Assert.StartsWith("application/problem+json", context.Response.ContentType);
        body.Position = 0;
        using JsonDocument document = await JsonDocument.ParseAsync(body);
        Assert.Equal("error.unexpected", document.RootElement.GetProperty("code").GetString());
        Assert.Equal("trace-test", document.RootElement.GetProperty("traceId").GetString());
        Assert.DoesNotContain("secret", document.RootElement.GetRawText(), StringComparison.Ordinal);

        using CancellationTokenSource cancellation = new();
        await cancellation.CancelAsync();
        context.RequestAborted = cancellation.Token;
        Assert.False(await handler.TryHandleAsync(context, new OperationCanceledException(cancellation.Token), CancellationToken.None));
    }

    private static TestController CreateController()
    {
        return new TestController
        {
            ControllerContext = new ControllerContext
            {
                HttpContext = new DefaultHttpContext { TraceIdentifier = "trace-test" }
            }
        };
    }

    private sealed class TestController : ControllerBase;
}
