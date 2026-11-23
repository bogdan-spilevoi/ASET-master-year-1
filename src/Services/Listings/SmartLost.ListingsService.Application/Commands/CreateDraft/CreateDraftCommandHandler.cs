using MediatR;
using SmartLost.BuildingBlocks.Application.Authentication;
using SmartLost.BuildingBlocks.Core.Results;
using SmartLost.ListingsService.Application.Dtos;
using SmartLost.ListingsService.Application.Interfaces;
using SmartLost.ListingsService.Domain.Entities;

namespace SmartLost.ListingsService.Application.Commands.CreateDraft;

public sealed class CreateDraftCommandHandler(ICurrentUser currentUser, IDraftPhotoStore store)
    : IRequestHandler<CreateDraftCommand, Result<DraftCreatedDto>>
{
    public Task<Result<DraftCreatedDto>> Handle(CreateDraftCommand request, CancellationToken cancellationToken)
    {
        Guid authorId = currentUser.GetRequiredUserId();
        var draft = Listing.CreateDraft(authorId, request.Type);
        var photo = Photo.Create($"drafts/{draft.Id:D}/{Guid.NewGuid():N}", request.Photo.MediaType, request.Photo.Bytes.Length);
        draft.ReplacePhoto(authorId, draft.Revision, photo);
        store.Add(draft, request.Photo.Bytes);
        return Task.FromResult(Result<DraftCreatedDto>.Success(new(draft.Id, draft.Revision, photo.Id, photo.Revision)));
    }
}
