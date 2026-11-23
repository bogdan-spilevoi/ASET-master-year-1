using Microsoft.AspNetCore.Mvc;
using SmartLost.BuildingBlocks.AspNetCore.Authentication;
using SmartLost.Contracts.Listings;
using SmartLost.MatchingService.Infrastructure.Analysis;

namespace SmartLost.MatchingService.Api.Controllers;

[ApiController]
[ApiExplorerSettings(IgnoreApi = true)]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
[Route("internal/analysis/{listingId:guid}/{photoId:guid}/{revision:int:min(1)}")]
public sealed class InternalAnalysisController(DraftAnalysisResults results, IConfiguration config) : ControllerBase
{
    [HttpGet]
    public async Task<IActionResult> Get(Guid listingId, Guid photoId, int revision, CancellationToken token)
    {
        if (!ServiceAccessKey.Matches(Request, "X-Listings-Photo-Key", config["ListingsPhotos:ApiKey"]))
        {
            return Unauthorized();
        }
        DraftAnalysisDto dto = await results.ReadAsync(new(listingId, photoId, revision), token);
        return Ok(dto);
    }
}
