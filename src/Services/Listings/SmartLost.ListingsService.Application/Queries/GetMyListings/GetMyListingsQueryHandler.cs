using MediatR;
using SmartLost.BuildingBlocks.Application.Authentication;
using SmartLost.BuildingBlocks.Core.Pagination;
using SmartLost.BuildingBlocks.Core.Results;
using SmartLost.ListingsService.Application.Dtos;
using SmartLost.ListingsService.Application.Interfaces;

namespace SmartLost.ListingsService.Application.Queries.GetMyListings;

public sealed class GetMyListingsQueryHandler(IListingsReadStore readStore, ICurrentUser user)
    : IRequestHandler<GetMyListingsQuery, Result<PagedResult<OwnedListingDto>>>
{
    public async Task<Result<PagedResult<OwnedListingDto>>> Handle(GetMyListingsQuery request, CancellationToken cancellationToken)
    {
        PageSlice<OwnedListingDto> page = await readStore.GetOwnedListingsAsync(user.GetRequiredUserId(), request, cancellationToken);
        return Result<PagedResult<OwnedListingDto>>.Success(new PagedResult<OwnedListingDto>(page, request.PageIndex, request.PageSize));
    }
}
