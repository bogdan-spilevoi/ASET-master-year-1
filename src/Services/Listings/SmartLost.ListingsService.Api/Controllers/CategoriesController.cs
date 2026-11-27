using MediatR;
using Microsoft.AspNetCore.Mvc;
using SmartLost.BuildingBlocks.AspNetCore.Results;
using SmartLost.ListingsService.Application.Dtos;
using SmartLost.ListingsService.Application.Interfaces;
using SmartLost.ListingsService.Application.Queries.GetCategories;

namespace SmartLost.ListingsService.Api.Controllers;

[ApiController]
[Route("api/categories")]
public sealed class CategoriesController(ISender sender, ICategoryCatalog catalog) : ControllerBase
{
    [HttpGet]
    [ProducesResponseType<IReadOnlyList<CategoryDto>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> Get(CancellationToken cancellationToken)
    {
        long version = await catalog.GetVersionAsync(cancellationToken);
        return this.ToActionResult(await sender.Send(new GetCategoriesQuery(version), cancellationToken));
    }
}
