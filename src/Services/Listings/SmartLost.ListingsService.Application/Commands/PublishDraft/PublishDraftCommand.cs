using MediatR;
using SmartLost.BuildingBlocks.Application.Authorization;
using SmartLost.BuildingBlocks.Application.Transactions;
using SmartLost.BuildingBlocks.Core.Results;

namespace SmartLost.ListingsService.Application.Commands.PublishDraft;

[Authorize]
public sealed record PublishDraftCommand(Guid ListingId, int ExpectedRevision) : IRequest<Result>, ITransactionalCommand;
