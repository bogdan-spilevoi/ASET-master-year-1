using MediatR;
using SmartLost.BuildingBlocks.Application.Authorization;
using SmartLost.BuildingBlocks.Core.Results;
using SmartLost.Contracts.Listings;

namespace SmartLost.ListingsService.Application.Commands.RequestDraftAnalysis;

[Authorize]
public sealed record RequestDraftAnalysisCommand(Guid ListingId, DraftAnalysisKind Kind) : IRequest<Result<DraftAnalysisRequest>>;
