using MediatR;
using SmartLost.BuildingBlocks.Core.Results;
using SmartLost.MatchingService.Application.Profiling;
using SmartLost.MatchingService.Domain.Enums;

namespace SmartLost.MatchingService.Application.Commands.PrepareImageEmbedding;

/// <summary>Processes one due embedding preparation task; intended for a future finite Matching job.</summary>
public sealed record PrepareImageEmbeddingCommand(Guid TaskId, string? CorrelationId = null)
    : IRequest<Result<EmbeddingPreparationState>>, ICorrelatedRequest;
