using System.Text.Json.Serialization;
using MediatR;
using SmartLost.BuildingBlocks.Application.Caching;
using SmartLost.BuildingBlocks.Application.Profiling;
using SmartLost.BuildingBlocks.Core.Results;
using SmartLost.MatchingService.Application.Contracts;

namespace SmartLost.MatchingService.Application.Queries.AnalyzeListingPhoto;

/// <summary>Input-only inference, dispatched after checking the requested photo is still permitted.</summary>
[CacheableQuery("matching:photo-analysis:v1", TtlSeconds = 300)]
public sealed record AnalyzePhotoContentQuery(Guid ListingId, Guid PhotoId, int PhotoRevision, PhotoContent Photo,
    [property: JsonIgnore] string? CorrelationId = null) : IRequest<Result<PhotoAnalysis>>, ICorrelatedRequest;
