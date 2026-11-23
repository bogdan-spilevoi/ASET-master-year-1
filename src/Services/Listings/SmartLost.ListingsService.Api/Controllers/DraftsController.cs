using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using SkiaSharp;
using SmartLost.BuildingBlocks.AspNetCore.Results;
using SmartLost.ListingsService.Api.Contracts;
using SmartLost.ListingsService.Application.Commands.CreateDraft;
using SmartLost.ListingsService.Application.Dtos;
using SmartLost.ListingsService.Domain.Entities;

namespace SmartLost.ListingsService.Api.Controllers;

[ApiController]
[Route("api/listings/drafts")]
public sealed class DraftsController(ISender sender) : ControllerBase
{
    [Authorize]
    [HttpPost]
    [Consumes("multipart/form-data")]
    [RequestSizeLimit(Photo.MaximumByteSize + 65536)]
    [ProducesResponseType<DraftCreatedDto>(StatusCodes.Status201Created)]
    public async Task<IActionResult> Create([FromForm] CreateDraftForm form, CancellationToken cancellationToken)
    {
        if (form.Photo.Length is 0 or > Photo.MaximumByteSize)
        {
            return BadRequest("Provide a nonempty image of at most 10 MiB.");
        }

        using var buffer = new MemoryStream((int)form.Photo.Length);
        await form.Photo.CopyToAsync(buffer, cancellationToken);
        buffer.Position = 0;
        using var codec = SKCodec.Create(buffer);
        string? mediaType = codec?.EncodedFormat switch
        {
            SKEncodedImageFormat.Jpeg => "image/jpeg",
            SKEncodedImageFormat.Png => "image/png",
            _ => null
        };
        if (codec is null || mediaType is null || (long)codec.Info.Width * codec.Info.Height > 16000000)
        {
            return BadRequest("Provide a JPEG or PNG image of at most 16 megapixels.");
        }

        using var bitmap = new SKBitmap(codec.Info.WithColorType(SKColorType.Bgra8888));
        if (bitmap.GetPixels() == IntPtr.Zero || codec.GetPixels(bitmap.Info, bitmap.GetPixels()) != SKCodecResult.Success)
        {
            return BadRequest("The image could not be decoded.");
        }

        var command = new CreateDraftCommand(form.Type!.Value, new(mediaType, buffer.ToArray()));
        return this.ToActionResult(await sender.Send(command, cancellationToken), StatusCodes.Status201Created);
    }
}
