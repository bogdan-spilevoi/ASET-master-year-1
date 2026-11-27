using MediatR;
using SmartLost.BuildingBlocks.Core.Results;
using SmartLost.ListingsService.Application.Dtos;

namespace SmartLost.ListingsService.Application.Queries.GetListingPhoto;

public sealed record GetListingPhotoQuery(Guid ListingId) : IRequest<Result<PhotoContentDto>>;
