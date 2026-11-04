using MediatR;
using SmartLost.BuildingBlocks.Core.Results;
using SmartLost.ListingsService.Application.Dtos;
using SmartLost.ListingsService.Application.Interfaces;

namespace SmartLost.ListingsService.Application.Queries.GetCategories;

public sealed class GetCategoriesQueryHandler(IListingsReadStore readStore)
    : IRequestHandler<GetCategoriesQuery, Result<IReadOnlyList<CategoryDto>>>
{
    public async Task<Result<IReadOnlyList<CategoryDto>>> Handle(GetCategoriesQuery request, CancellationToken cancellationToken)
    {
        IReadOnlyList<CategoryDto> categories = await readStore.GetCategoriesAsync(cancellationToken);
        return Result<IReadOnlyList<CategoryDto>>.Success(Array.AsReadOnly(categories.ToArray()));
    }
}
