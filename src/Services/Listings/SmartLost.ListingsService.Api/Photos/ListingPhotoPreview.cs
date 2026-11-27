using SkiaSharp;

namespace SmartLost.ListingsService.Api.Photos;

internal static class ListingPhotoPreview
{
    private const int MaximumEdge = 480;

    public static byte[] Create(byte[] bytes)
    {
        using var image = SKImage.FromEncodedData(bytes);
        double scale = Math.Min(1d, (double)MaximumEdge / Math.Max(image.Width, image.Height));
        int width = Math.Max(1, (int)Math.Round(image.Width * scale));
        int height = Math.Max(1, (int)Math.Round(image.Height * scale));
        using var thumbnail = new SKBitmap(width, height);
        using var canvas = new SKCanvas(thumbnail);
        canvas.Clear(SKColors.White);
        canvas.DrawImage(image, SKRect.Create(width, height), new SKSamplingOptions(SKFilterMode.Linear));
        using SKData encoded = thumbnail.Encode(SKEncodedImageFormat.Jpeg, 80);
        return encoded.ToArray();
    }
}
