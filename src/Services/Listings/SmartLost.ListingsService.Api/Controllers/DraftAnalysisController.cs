using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using SmartLost.BuildingBlocks.AspNetCore.Results;
using SmartLost.BuildingBlocks.Core.Results;
using SmartLost.Contracts.Listings;
using SmartLost.ListingsService.Application.Commands.RequestDraftAnalysis;
using SmartLost.ListingsService.Application.Queries.GetDraftAnalysis;

namespace SmartLost.ListingsService.Api.Controllers;

[Authorize]
[ApiController]
[Route("api/listings/drafts/{listingId:guid}")]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public sealed class DraftAnalysisController(ISender sender, IConfiguration config) : ControllerBase
{
    [HttpPost("traits")]
    [ProducesResponseType<DraftAnalysisRequest>(StatusCodes.Status202Accepted)]
    public Task<IActionResult> RequestTraits(Guid listingId, CancellationToken token)
    {
        return RequestAsync(listingId, DraftAnalysisKind.Traits, token);
    }

    [HttpPost("embedding")]
    [ProducesResponseType<DraftAnalysisRequest>(StatusCodes.Status202Accepted)]
    public Task<IActionResult> RequestEmbedding(Guid listingId, CancellationToken token)
    {
        return RequestAsync(listingId, DraftAnalysisKind.Embedding, token);
    }

    [HttpGet("analysis")]
    [ProducesResponseType<DraftAnalysisDto>(StatusCodes.Status200OK)]
    public async Task<IActionResult> Analysis(Guid listingId, CancellationToken token)
    {
        return this.ToActionResult(await sender.Send(new GetDraftAnalysisQuery(listingId), token));
    }

    private async Task<IActionResult> RequestAsync(Guid listingId, DraftAnalysisKind kind, CancellationToken token)
    {
        if (!config.GetValue<bool>("DraftAnalysis:Enabled"))
        {
            return Problem(statusCode: StatusCodes.Status503ServiceUnavailable, detail: "Draft analysis delivery is disabled.");
        }
        Result<DraftAnalysisRequest> result = await sender.Send(new RequestDraftAnalysisCommand(listingId, kind), token);
        return result.IsSuccess ? AcceptedAtAction(nameof(Analysis), new
        {
            listingId
        }, result.Value) : this.ToActionResult(result);
    }
}
