using System.Text;
using SmartLost.MatchingService.Application.Contracts;
using SmartLost.MatchingService.Application.Interfaces;
using SmartLost.MatchingService.Domain.ValueObjects;

namespace SmartLost.MatchingService.Infrastructure.Mock;

public sealed class MockListingPhotoReader(MockPhotoCatalog catalog) : IListingPhotoReader
{
    public Task<PhotoContent?> ReadAsync(Guid listingId, PhotoReference photo, CancellationToken cancellationToken)
    {
        PhotoContent? content = catalog.TryGet(photo, out string appearance)
            ? new PhotoContent(Encoding.UTF8.GetBytes(appearance), MockAppearance.MediaType)
            : null;
        return Task.FromResult(content);
    }
}
