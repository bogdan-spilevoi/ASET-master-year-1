using MediatR;
using SmartLost.BuildingBlocks.Core.Results;
using SmartLost.ListingsService.Application.Dtos;

namespace SmartLost.ListingsService.Application.Queries.GetListingById;

public sealed record GetListingByIdQuery(Guid Id) : IRequest<Result<ListingDetailsDto>>;
