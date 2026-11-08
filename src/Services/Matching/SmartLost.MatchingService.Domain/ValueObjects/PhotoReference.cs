using SmartLost.BuildingBlocks.Core.Validation;
using SmartLost.MatchingService.Domain.Errors;

namespace SmartLost.MatchingService.Domain.ValueObjects;

/// <summary>Logical identity of one Listings photo revision; not a navigation into the Listings database.</summary>
public sealed record PhotoReference
{
    public PhotoReference(Guid photoId, int revision)
    {
        Guard.Against(photoId == Guid.Empty, MatchingErrors.PhotoIdRequired);
        Guard.Against(revision < 1, MatchingErrors.InvalidPhotoRevision);
        PhotoId = photoId;
        Revision = revision;
    }

    public Guid PhotoId
    {
        get;
    }

    public int Revision
    {
        get;
    }
}
