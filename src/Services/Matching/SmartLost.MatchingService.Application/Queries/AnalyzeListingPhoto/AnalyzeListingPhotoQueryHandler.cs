using MediatR;
using SmartLost.BuildingBlocks.Core.Results;
using SmartLost.MatchingService.Application.Contracts;
using SmartLost.MatchingService.Application.Exceptions;
using SmartLost.MatchingService.Application.Interfaces;
using SmartLost.MatchingService.Domain.Errors;
using SmartLost.MatchingService.Domain.ValueObjects;

namespace SmartLost.MatchingService.Application.Queries.AnalyzeListingPhoto;

public sealed class AnalyzeListingPhotoQueryHandler(IListingPhotoReader photos, IPhotoAnalyzer analyzer)
    : IRequestHandler<AnalyzeListingPhotoQuery, Result<PhotoAnalysis>>
{
    public async Task<Result<PhotoAnalysis>> Handle(AnalyzeListingPhotoQuery request, CancellationToken cancellationToken)
    {
        PhotoContent? photo = await photos.ReadAsync(
            request.ListingId, new PhotoReference(request.PhotoId, request.PhotoRevision), cancellationToken);
        if (photo is null)
        {
            return Result<PhotoAnalysis>.Failure(MatchingErrors.PhotoUnavailable);
        }

        try
        {
            return Result<PhotoAnalysis>.Success(await analyzer.AnalyzeAsync(photo, cancellationToken));
        }
        catch (TimeoutException)
        {
            return Result<PhotoAnalysis>.Failure(MatchingErrors.InferenceTimedOut);
        }
        catch (AiInferenceException)
        {
            return Result<PhotoAnalysis>.Failure(MatchingErrors.InferenceUnavailable);
        }
    }
}
