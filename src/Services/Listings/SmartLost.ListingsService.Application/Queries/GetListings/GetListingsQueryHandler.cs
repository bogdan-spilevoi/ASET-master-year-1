using MediatR;
using SmartLost.BuildingBlocks.Core.Pagination;
using SmartLost.BuildingBlocks.Core.Results;
using SmartLost.ListingsService.Application.Dtos;
using SmartLost.ListingsService.Application.Interfaces;

namespace SmartLost.ListingsService.Application.Queries.GetListings;

public sealed class GetListingsQueryHandler(IListingsReadStore readStore)
    : IRequestHandler<GetListingsQuery, Result<PagedResult<ListingSummaryDto>>>
{
    public async Task<Result<PagedResult<ListingSummaryDto>>> Handle(GetListingsQuery request, CancellationToken cancellationToken)
    {
        PageSlice<ListingSummaryDto> page = await readStore.GetOpenListingsAsync(request.Type, request.CategoryId,
            request.PageIndex, request.PageSize, cancellationToken);
        return Result<PagedResult<ListingSummaryDto>>.Success(new PagedResult<ListingSummaryDto>(page, request.PageIndex, request.PageSize));
    }
}
