using Microsoft.AspNetCore.Mvc;
using SmartLost.BuildingBlocks.AspNetCore.Authentication;
using SmartLost.ListingsService.Application.Dtos;
using SmartLost.ListingsService.Application.Interfaces;

namespace SmartLost.ListingsService.Api.Controllers;

[ApiController]
[ApiExplorerSettings(IgnoreApi = true)]
[Route("internal/listings/{listingId:guid}/photos/{photoId:guid}/revisions/{revision:int:min(1)}")]
public sealed class InternalPhotosController(IDraftPhotoStore store, IConfiguration configuration) : ControllerBase
{
    [HttpGet]
    [ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
    public async Task<IActionResult> Get(Guid listingId, Guid photoId, int revision, CancellationToken cancellationToken)
    {
        if (HttpContext.Connection.LocalPort != 8082)
        {
            return NotFound();
        }
        string? key = configuration["ListingsPhotos:ApiKey"];
        if (!ServiceAccessKey.Matches(Request, "X-Listings-Photo-Key", key))
        {
            return Unauthorized();
        }
        PrivatePhotoDto? photo = await store.ReadAsync(listingId, photoId, revision, cancellationToken);
        return photo is null ? NotFound() : File(photo.Bytes, photo.MediaType);
    }
}
