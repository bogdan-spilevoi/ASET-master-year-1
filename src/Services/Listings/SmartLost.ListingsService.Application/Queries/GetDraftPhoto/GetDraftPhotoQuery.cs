using MediatR;
using SmartLost.BuildingBlocks.Application.Authorization;
using SmartLost.BuildingBlocks.Core.Results;
using SmartLost.ListingsService.Application.Dtos;

namespace SmartLost.ListingsService.Application.Queries.GetDraftPhoto;

[Authorize]
public sealed record GetDraftPhotoQuery(Guid ListingId) : IRequest<Result<PhotoContentDto>>;
