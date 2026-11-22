using System.Globalization;
using SmartLost.MatchingService.Application.Contracts;
using SmartLost.MatchingService.Application.Interfaces;
using SmartLost.MatchingService.Domain.ValueObjects;

namespace SmartLost.MatchingService.Infrastructure.Ai;

/// <summary>Reads operator-provided test images from a read-only directory until Listings serves photo content.</summary>
public sealed class DirectoryPhotoReader(string directory) : IListingPhotoReader
{
    public async Task<PhotoContent?> ReadAsync(Guid listingId, PhotoReference photo, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(photo);
        string stem = string.Create(CultureInfo.InvariantCulture, $"{listingId:D}_{photo.PhotoId:D}_{photo.Revision}");
        foreach (string extension in new[] { ".jpg", ".png" })
        {
            string path = Path.Combine(directory, stem + extension);
            try
            {
                using FileStream stream = File.OpenRead(path);
                if (stream.Length is 0 or > 10 * 1024 * 1024)
                {
                    return null;
                }
                byte[] bytes = new byte[stream.Length];
                await stream.ReadExactlyAsync(bytes, cancellationToken);
                return new PhotoContent(bytes, extension == ".jpg" ? "image/jpeg" : "image/png");
            }
            catch (FileNotFoundException) { }
            catch (DirectoryNotFoundException) { return null; }
        }
        return null;
    }
}
