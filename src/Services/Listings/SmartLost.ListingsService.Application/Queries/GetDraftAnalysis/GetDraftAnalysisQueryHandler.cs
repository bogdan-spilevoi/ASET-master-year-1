using MediatR;
using SmartLost.BuildingBlocks.Application.Authentication;
using SmartLost.BuildingBlocks.Core.Results;
using SmartLost.Contracts.Listings;
using SmartLost.ListingsService.Application.Interfaces;

namespace SmartLost.ListingsService.Application.Queries.GetDraftAnalysis;

public sealed class GetDraftAnalysisQueryHandler(ICurrentUser user, IDraftAnalysisStore store, IMatchingAnalysisReader matching)
    : IRequestHandler<GetDraftAnalysisQuery, Result<DraftAnalysisDto>>
{
    public async Task<Result<DraftAnalysisDto>> Handle(GetDraftAnalysisQuery request, CancellationToken cancellationToken)
    {
        DraftPhotoTarget? photo = await store.FindOwnedPhotoAsync(user.GetRequiredUserId(), request.ListingId, cancellationToken);
        if (photo is null)
        {
            return Result<DraftAnalysisDto>.Failure(new Error("draft.not_found", "Draft photo was not found.", ErrorKind.NotFound));
        }
        DraftAnalysisDto result;
        try
        {
            result = await matching.ReadAsync(photo, cancellationToken);
        }
        catch (Exception error) when (!cancellationToken.IsCancellationRequested && error is HttpRequestException or OperationCanceledException)
        {
            return Result<DraftAnalysisDto>.Failure(new Error("analysis.status_unavailable", "Matching is temporarily unavailable.", ErrorKind.Unavailable));
        }
        if (result.Traits.State == "NotRequested")
        {
            result = result with
            {
                Traits = await store.DeliveryAsync(photo.RequestId(DraftAnalysisKind.Traits), cancellationToken)
            };
        }
        if (result.Embedding.State == "NotRequested")
        {
            result = result with
            {
                Embedding = await store.DeliveryAsync(photo.RequestId(DraftAnalysisKind.Embedding), cancellationToken)
            };
        }
        return Result<DraftAnalysisDto>.Success(result);
    }
}
