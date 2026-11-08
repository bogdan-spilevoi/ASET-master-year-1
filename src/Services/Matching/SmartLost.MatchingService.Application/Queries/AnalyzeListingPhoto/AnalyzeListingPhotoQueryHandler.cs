using MediatR;
using SmartLost.BuildingBlocks.Core.Results;
using SmartLost.MatchingService.Application.Contracts;
using SmartLost.MatchingService.Application.Interfaces;
using SmartLost.MatchingService.Domain.Errors;
using SmartLost.MatchingService.Domain.ValueObjects;

namespace SmartLost.MatchingService.Application.Queries.AnalyzeListingPhoto;

public sealed class AnalyzeListingPhotoQueryHandler(IListingPhotoReader photos, ISender sender)
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

        return await sender.Send(new AnalyzePhotoContentQuery(request.ListingId, request.PhotoId,
            request.PhotoRevision, photo, request.CorrelationId), cancellationToken);
    }
}
