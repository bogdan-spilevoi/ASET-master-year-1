using MediatR;
using SmartLost.BuildingBlocks.Application.Validation;
using SmartLost.BuildingBlocks.Core.Pagination;
using SmartLost.BuildingBlocks.Core.Results;
using SmartLost.Contracts.Listings;
using SmartLost.ListingsService.Application.Dtos;

namespace SmartLost.ListingsService.Application.Queries.GetListings;

public sealed record GetListingsQuery(ListingType? Type = null, Guid? CategoryId = null,
    int PageIndex = 0, int PageSize = PagedDataGuard.DefaultPageSize)
    : IRequest<Result<PagedResult<ListingSummaryDto>>>, IPaginatedRequest;
