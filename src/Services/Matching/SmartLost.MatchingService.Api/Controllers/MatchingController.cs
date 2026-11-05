using MediatR;
using Microsoft.AspNetCore.Mvc;
using SmartLost.BuildingBlocks.Core.Results;
using SmartLost.MatchingService.Api.Contracts;
using SmartLost.MatchingService.Api.Filters;
using SmartLost.MatchingService.Api.Results;
using SmartLost.MatchingService.Application.Commands.PrepareImageEmbedding;
using SmartLost.MatchingService.Application.Contracts;
using SmartLost.MatchingService.Application.Queries.AnalyzeListingPhoto;
using SmartLost.MatchingService.Application.Queries.GetMatchSuggestions;
using SmartLost.MatchingService.Domain.Enums;

namespace SmartLost.MatchingService.Api.Controllers;

/// <summary>Development test endpoints for the Matching use cases; the internal contract is not final.</summary>
[ApiController]
[DevelopmentOnly]
[Route("api/matching")]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public sealed class MatchingController(ISender sender) : ControllerBase
{
    public const string CorrelationHeader = "X-Correlation-ID";

    [HttpPost("suggestions")]
    [ProducesResponseType<MatchSuggestionsResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> GetSuggestions([FromBody] MatchSuggestionsRequest request,
        [FromHeader(Name = CorrelationHeader)] string? correlationId, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        Result<MatchSuggestionsResponse> result = await sender.Send(
            new GetMatchSuggestionsQuery(request.ListingId, request.ExpectedSourceRevision, correlationId), cancellationToken);
        return this.ToMatchingActionResult(result);
    }

    [HttpPost("embedding-tasks/{taskId:guid}/process")]
    [ProducesResponseType<EmbeddingPreparationState>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status503ServiceUnavailable)]
    public async Task<IActionResult> ProcessEmbeddingTask(Guid taskId,
        [FromHeader(Name = CorrelationHeader)] string? correlationId, CancellationToken cancellationToken)
    {
        Result<EmbeddingPreparationState> result = await sender.Send(new PrepareImageEmbeddingCommand(taskId, correlationId),
            cancellationToken);
        return this.ToMatchingActionResult(result);
    }

    [HttpPost("photo-analysis")]
    [ProducesResponseType<PhotoAnalysis>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status503ServiceUnavailable)]
    public async Task<IActionResult> AnalyzePhoto([FromBody] PhotoAnalysisRequest request,
        [FromHeader(Name = CorrelationHeader)] string? correlationId, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        Result<PhotoAnalysis> result = await sender.Send(
            new AnalyzeListingPhotoQuery(request.ListingId, request.PhotoId, request.PhotoRevision, correlationId), cancellationToken);
        return this.ToMatchingActionResult(result);
    }
}
