using System.Security.Cryptography;
using System.Text;

namespace SmartLost.Contracts.Listings;

public sealed record DraftPhotoTarget(Guid ListingId, Guid PhotoId, int PhotoRevision)
{
    public Guid RequestId(DraftAnalysisKind kind)
    {
        string target = FormattableString.Invariant($"draft-analysis/v1/{ListingId:D}/{PhotoId:D}/{PhotoRevision}/{kind}");
        return new Guid(SHA256.HashData(Encoding.UTF8.GetBytes(target)).AsSpan(0, 16));
    }
}
