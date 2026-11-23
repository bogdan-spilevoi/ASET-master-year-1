using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using SmartLost.BuildingBlocks.AspNetCore.Results;
using SmartLost.ListingsService.Api.Contracts;
using SmartLost.ListingsService.Application.Commands.CancelAcceptance;
using SmartLost.ListingsService.Application.Commands.ConfirmRecovery;
using SmartLost.ListingsService.Application.Commands.RecordMatchDecision;
using SmartLost.ListingsService.Application.Dtos;

namespace SmartLost.ListingsService.Api.Controllers;

[Authorize]
[ApiController]
[Route("api/match-decisions")]
public sealed class MatchDecisionsController(ISender sender) : ControllerBase
{
    [HttpPost("~/api/listings/{lostId:guid}/match-decisions")]
    [ProducesResponseType<DecisionDto>(StatusCodes.Status200OK)]
    public async Task<IActionResult> Decide(Guid lostId, MatchDecisionRequest body, CancellationToken token)
    {
        return this.ToActionResult(await sender.Send(new RecordMatchDecisionCommand(lostId, body.FoundListingId,
            body.LostRevision, body.FoundRevision, body.Choice), token));
    }

    [HttpPost("{id:guid}/cancel-acceptance")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> Cancel(Guid id, CancellationToken token)
    {
        return this.ToActionResult(await sender.Send(new CancelAcceptanceCommand(id), token));
    }

    [HttpPost("{id:guid}/confirm-recovery")]
    [ProducesResponseType<RecoveryDto>(StatusCodes.Status200OK)]
    public async Task<IActionResult> Confirm(Guid id, CancellationToken token)
    {
        return this.ToActionResult(await sender.Send(new ConfirmRecoveryCommand(id), token));
    }
}
