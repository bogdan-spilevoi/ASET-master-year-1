using MediatR;
using SmartLost.BuildingBlocks.Application.Authorization;
using SmartLost.BuildingBlocks.Application.Transactions;
using SmartLost.BuildingBlocks.Core.Results;
using SmartLost.Contracts.Listings;
using SmartLost.ListingsService.Application.Dtos;

namespace SmartLost.ListingsService.Application.Commands.CreateDraft;

[Authorize]
public sealed record CreateDraftCommand(ListingType Type, PhotoContentDto Photo)
    : IRequest<Result<DraftCreatedDto>>, ITransactionalCommand;
