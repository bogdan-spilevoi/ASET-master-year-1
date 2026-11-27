using MediatR;
using SmartLost.BuildingBlocks.Application.Authorization;
using SmartLost.BuildingBlocks.Application.Validation;
using SmartLost.BuildingBlocks.Core.Pagination;
using SmartLost.BuildingBlocks.Core.Results;
using SmartLost.Contracts.Listings;
using SmartLost.ListingsService.Application.Dtos;
using SmartLost.ListingsService.Domain.Enums;

namespace SmartLost.ListingsService.Application.Queries.GetMyListings;

[Authorize]
public sealed record GetMyListingsQuery(ListingState? State = null, ListingType? Type = null,
    ClosureReason? ClosureReason = null, int PageIndex = 0, int PageSize = PagedDataGuard.DefaultPageSize)
    : IRequest<Result<PagedResult<OwnedListingDto>>>, IPaginatedRequest;
