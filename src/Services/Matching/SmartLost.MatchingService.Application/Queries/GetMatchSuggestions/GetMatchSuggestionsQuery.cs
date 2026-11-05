using MediatR;
using SmartLost.BuildingBlocks.Core.Results;
using SmartLost.MatchingService.Application.Contracts;
using SmartLost.MatchingService.Application.Profiling;

namespace SmartLost.MatchingService.Application.Queries.GetMatchSuggestions;

public sealed record GetMatchSuggestionsQuery(Guid ListingId, int ExpectedSourceRevision, string? CorrelationId = null)
    : IRequest<Result<MatchSuggestionsResponse>>, ICorrelatedRequest;
