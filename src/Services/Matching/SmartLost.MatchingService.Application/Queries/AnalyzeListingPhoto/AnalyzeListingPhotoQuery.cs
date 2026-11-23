using MediatR;
using SmartLost.BuildingBlocks.Application.Profiling;
using SmartLost.BuildingBlocks.Core.Results;
using SmartLost.Contracts.Listings;

namespace SmartLost.MatchingService.Application.Queries.AnalyzeListingPhoto;

/// <summary>Requests advisory Qwen labels for one listing photo revision (UC05).</summary>
public sealed record AnalyzeListingPhotoQuery(Guid ListingId, Guid PhotoId, int PhotoRevision, string? CorrelationId = null)
    : IRequest<Result<PhotoAnalysis>>, ICorrelatedRequest;
