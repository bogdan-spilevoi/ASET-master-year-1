using MediatR;
using SmartLost.BuildingBlocks.Application.Authorization;
using SmartLost.BuildingBlocks.Core.Results;
using SmartLost.Contracts.Listings;

namespace SmartLost.ListingsService.Application.Queries.GetDraftAnalysis;

[Authorize]
public sealed record GetDraftAnalysisQuery(Guid ListingId) : IRequest<Result<DraftAnalysisDto>>;
