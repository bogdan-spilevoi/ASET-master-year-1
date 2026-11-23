using MediatR;
using SmartLost.BuildingBlocks.Application.Authorization;
using SmartLost.BuildingBlocks.Application.Transactions;
using SmartLost.BuildingBlocks.Core.Results;
using SmartLost.ListingsService.Application.Dtos;

namespace SmartLost.ListingsService.Application.Commands.PatchDraft;

[Authorize]
public sealed record PatchDraftCommand(Guid ListingId, DraftPatchDto Patch) : IRequest<Result<DraftDetailsDto>>, ITransactionalCommand;
