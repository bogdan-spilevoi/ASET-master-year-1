using MediatR;
using Microsoft.AspNetCore.Mvc;
using SmartLost.BuildingBlocks.AspNetCore.Results;
using SmartLost.BuildingBlocks.Core.Pagination;
using SmartLost.BuildingBlocks.Core.Results;
using SmartLost.ListingsService.Api.Contracts;
using SmartLost.ListingsService.Api.Photos;
using SmartLost.ListingsService.Application.Dtos;
using SmartLost.ListingsService.Application.Queries.GetListingById;
using SmartLost.ListingsService.Application.Queries.GetListingPhoto;
using SmartLost.ListingsService.Application.Queries.GetListings;

namespace SmartLost.ListingsService.Api.Controllers;

[ApiController]
[Route("api/listings")]
public sealed class ListingsController(ISender sender) : ControllerBase
{
    [HttpGet]
    [ProducesResponseType<PagedResult<ListingSummaryDto>>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> Get([FromQuery] GetListingsRequest request, CancellationToken cancellationToken)
    {
        var query = new GetListingsQuery(request.Type, request.CategoryId, request.PageIndex, request.PageSize);
        return this.ToActionResult(await sender.Send(query, cancellationToken));
    }

    [HttpGet("{id:guid}/photo")]
    [ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
    [ProducesResponseType<FileContentResult>(StatusCodes.Status200OK, "image/jpeg", "image/png")]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetPhoto(Guid id, [FromQuery] bool preview, CancellationToken cancellationToken)
    {
        Result<PhotoContentDto> result = await sender.Send(new GetListingPhotoQuery(id), cancellationToken);
        if (result.IsFailure)
        {
            return this.ToActionResult(result);
        }

        PhotoContentDto photo = result.Value;
        return preview ? File(ListingPhotoPreview.Create(photo.Bytes), "image/jpeg") : File(photo.Bytes, photo.MediaType);
    }

    [HttpGet("{id:guid}")]
    [ProducesResponseType<ListingDetailsDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetById(Guid id, CancellationToken cancellationToken)
    {
        return this.ToActionResult(await sender.Send(new GetListingByIdQuery(id), cancellationToken));
    }
}
