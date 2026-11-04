using SmartLost.BuildingBlocks.Core.Entities;
using SmartLost.BuildingBlocks.Core.Validation;
using SmartLost.ListingsService.Domain.Errors;

namespace SmartLost.ListingsService.Domain.Entities;

public partial class Photo : Entity<Guid>
{
    private Photo()
    {
    }

    public string BlobKey { get; private set; } = string.Empty;

    public string Format { get; private set; } = string.Empty;

    public long ByteSize
    {
        get; private set;
    }

    public int Revision { get; private set; } = 1;

    public Guid ListingId
    {
        get; private set;
    }

    public Listing Listing { get; private set; } = null!;

    public const long MaximumByteSize = 10 * 1024 * 1024;

    /// <summary>Creates metadata after the caller has validated decoding and stored the private bytes.</summary>
    public static Photo Create(string blobKey, string format, long byteSize)
    {
        var photo = new Photo
        {
            Id = Guid.NewGuid(),
            BlobKey = ListingRules.Text(blobKey),
            Format = ListingRules.Text(format).ToLowerInvariant(),
            ByteSize = byteSize,
            Revision = 1
        };
        ValidateMetadata(photo);
        return photo;
    }

    internal void AttachTo(Listing listing)
    {
        Listing = listing;
        ListingId = listing.Id;
    }

    internal void Detach()
    {
        // Keep the persisted FK stable while EF marks the removed dependent as deleted.
        Listing = null!;
    }

    internal static void ValidateMetadata(Photo photo)
    {
        ListingRules.Identity(photo.Id);
        Guard.Against(string.IsNullOrWhiteSpace(photo.BlobKey) ||
            photo.Format is not ("image/jpeg" or "image/png") ||
            photo.ByteSize <= 0 || photo.ByteSize > MaximumByteSize || photo.Revision < 1, ListingErrors.InvalidPhoto);
    }
}
