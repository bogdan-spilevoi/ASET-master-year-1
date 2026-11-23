using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using SmartLost.BuildingBlocks.AspNetCore.Results;
using SmartLost.ListingsService.Api.Contracts;
using SmartLost.ListingsService.Application.Commands.PatchDraft;
using SmartLost.ListingsService.Application.Commands.PublishDraft;
using SmartLost.ListingsService.Application.Commands.WithdrawListing;
using SmartLost.ListingsService.Application.Dtos;
using SmartLost.ListingsService.Application.Queries.GetDraft;

namespace SmartLost.ListingsService.Api.Controllers;

[Authorize]
[ApiController]
[Route("api/listings")]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public sealed class ListingLifecycleController(ISender sender) : ControllerBase
{
    [HttpGet("drafts/{id:guid}")]
    [ProducesResponseType<DraftDetailsDto>(StatusCodes.Status200OK)]
    public async Task<IActionResult> GetDraft(Guid id, CancellationToken token)
    {
        return this.ToActionResult(await sender.Send(new GetDraftQuery(id), token));
    }

    [HttpPatch("drafts/{id:guid}")]
    [ProducesResponseType<DraftDetailsDto>(StatusCodes.Status200OK)]
    public async Task<IActionResult> PatchDraft(Guid id, DraftPatchDto patch, CancellationToken token)
    {
        return this.ToActionResult(await sender.Send(new PatchDraftCommand(id, patch), token));
    }

    [HttpPost("drafts/{id:guid}/publish")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> Publish(Guid id, ListingRevisionRequest body, CancellationToken token)
    {
        return this.ToActionResult(await sender.Send(new PublishDraftCommand(id, body.ExpectedRevision), token));
    }

    [HttpPost("{id:guid}/withdraw")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> Withdraw(Guid id, ListingRevisionRequest body, CancellationToken token)
    {
        return this.ToActionResult(await sender.Send(new WithdrawListingCommand(id, body.ExpectedRevision), token));
    }

    [HttpGet("{id:guid}/suggestions")]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status501NotImplemented)]
    public IActionResult Suggestions(Guid id)
    {
        return Problem(instance: $"/api/listings/{id:D}/suggestions", statusCode: StatusCodes.Status501NotImplemented,
            detail: "Listing suggestions are not implemented.");
    }
}
