using MediatR;
using SmartLost.BuildingBlocks.Application.Authorization;
using SmartLost.BuildingBlocks.Application.Transactions;
using SmartLost.BuildingBlocks.Core.Results;

namespace SmartLost.ListingsService.Application.Commands.WithdrawListing;

[Authorize]
public sealed record WithdrawListingCommand(Guid ListingId, int ExpectedRevision) : IRequest<Result>, ITransactionalCommand;
