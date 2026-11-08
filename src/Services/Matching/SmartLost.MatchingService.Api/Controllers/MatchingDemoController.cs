using System.ComponentModel.DataAnnotations;
using MediatR;
using Microsoft.AspNetCore.Mvc;
using SmartLost.BuildingBlocks.Core.Results;
using SmartLost.MatchingService.Api.Diagnostics;
using SmartLost.MatchingService.Api.Filters;
using SmartLost.MatchingService.Api.Results;
using SmartLost.MatchingService.Application.Commands.PrepareImageEmbedding;
using SmartLost.MatchingService.Domain.Enums;
using SmartLost.MatchingService.Domain.Errors;
using SmartLost.MatchingService.Infrastructure.Demo;

namespace SmartLost.MatchingService.Api.Controllers;

/// <summary>Development-only helpers that seed mock data and drive the flow by fixed listing IDs.</summary>
[ApiController]
[DevelopmentOnly]
[Route("api/matching/demo")]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public sealed class MatchingDemoController(MatchingDemoSeeder seeder, ISender sender, RecentLogBuffer logs) : ControllerBase
{
    /// <summary>Returns the most recent Matching log entries (newest first), e.g. profiling events 1301-1304.</summary>
    [HttpGet("logs")]
    [ProducesResponseType<IReadOnlyList<RecentLogEntry>>(StatusCodes.Status200OK)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    public IActionResult GetRecentLogs([FromQuery, Range(1, RecentLogBuffer.Capacity)] int count = 10)
    {
        return Ok(logs.GetLatest(count));
    }

    /// <summary>Deletes all Matching rows and inserts the fixed demo scenario.</summary>
    [HttpPost("seed")]
    [ProducesResponseType<DemoScenario>(StatusCodes.Status200OK)]
    public async Task<IActionResult> Seed(CancellationToken cancellationToken)
    {
        return Ok(await seeder.SeedAsync(cancellationToken));
    }

    /// <summary>Processes the embedding task for a listing's current photo.</summary>
    [HttpPost("listings/{listingId:guid}/embedding/process")]
    [ProducesResponseType<EmbeddingPreparationState>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status503ServiceUnavailable)]
    public async Task<IActionResult> ProcessListingEmbedding(Guid listingId,
        [FromHeader(Name = MatchingController.CorrelationHeader)] string? correlationId, CancellationToken cancellationToken)
    {
        Guid? taskId = await seeder.FindCurrentTaskIdAsync(listingId, cancellationToken);
        Result<EmbeddingPreparationState> result = taskId is Guid id
            ? await sender.Send(new PrepareImageEmbeddingCommand(id, correlationId), cancellationToken)
            : Result<EmbeddingPreparationState>.Failure(MatchingErrors.PreparationTaskNotFound);
        return this.ToMatchingActionResult(result);
    }
}
