using MediatR;
using SmartLost.BuildingBlocks.Application.Authorization;
using SmartLost.BuildingBlocks.Core.Results;
using SmartLost.ListingsService.Application.Dtos;

namespace SmartLost.ListingsService.Application.Queries.GetDraft;

[Authorize]
public sealed record GetDraftQuery(Guid ListingId) : IRequest<Result<DraftDetailsDto>>;
