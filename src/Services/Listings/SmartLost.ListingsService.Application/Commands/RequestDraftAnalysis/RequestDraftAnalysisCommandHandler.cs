using MediatR;
using SmartLost.BuildingBlocks.Application.Authentication;
using SmartLost.BuildingBlocks.Core.Results;
using SmartLost.Contracts.Listings;
using SmartLost.ListingsService.Application.Interfaces;

namespace SmartLost.ListingsService.Application.Commands.RequestDraftAnalysis;

public sealed class RequestDraftAnalysisCommandHandler(ICurrentUser user, IDraftAnalysisStore store)
    : IRequestHandler<RequestDraftAnalysisCommand, Result<DraftAnalysisRequest>>
{
    public async Task<Result<DraftAnalysisRequest>> Handle(RequestDraftAnalysisCommand request, CancellationToken cancellationToken)
    {
        DraftPhotoTarget? photo = await store.FindOwnedPhotoAsync(user.GetRequiredUserId(), request.ListingId, cancellationToken);
        if (photo is null)
        {
            return Result<DraftAnalysisRequest>.Failure(new Error("draft.not_found", "Draft photo was not found.", ErrorKind.NotFound));
        }
        var work = new DraftAnalysisRequest(photo, request.Kind);
        await store.EnqueueAsync(work, cancellationToken);
        return Result<DraftAnalysisRequest>.Success(work);
    }
}
