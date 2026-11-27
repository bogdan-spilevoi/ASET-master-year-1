using MediatR;
using SmartLost.BuildingBlocks.Application.Authorization;
using SmartLost.BuildingBlocks.Application.Transactions;
using SmartLost.BuildingBlocks.Core.Results;

namespace SmartLost.ListingsService.Application.Commands.DeleteDraft;

[Authorize]
public sealed record DeleteDraftCommand(Guid ListingId) : IRequest<Result>, ITransactionalCommand;
