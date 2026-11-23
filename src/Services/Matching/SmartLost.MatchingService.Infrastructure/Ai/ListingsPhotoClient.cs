using System.Net;
using SmartLost.MatchingService.Application.Contracts;
using SmartLost.MatchingService.Application.Interfaces;
using SmartLost.MatchingService.Domain.ValueObjects;

namespace SmartLost.MatchingService.Infrastructure.Ai;

/// <summary>Private Listings photo source for trusted service workflows.</summary>
public sealed class ListingsPhotoClient(HttpClient client) : IListingPhotoReader
{
    public async Task<PhotoContent?> ReadAsync(Guid listingId, PhotoReference photo, CancellationToken cancellationToken)
    {
        using HttpResponseMessage response = await client.GetAsync(
            FormattableString.Invariant($"internal/listings/{listingId:D}/photos/{photo.PhotoId:D}/revisions/{photo.Revision}"),
            cancellationToken);
        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            return null;
        }
        response.EnsureSuccessStatusCode();
        string? mediaType = response.Content.Headers.ContentType?.MediaType;
        if (mediaType is not ("image/png" or "image/jpeg"))
        {
            throw new InvalidDataException("Invalid Listings photo content type.");
        }
        return new PhotoContent(await response.Content.ReadAsByteArrayAsync(cancellationToken), mediaType);
    }
}
