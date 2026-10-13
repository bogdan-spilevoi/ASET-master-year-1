using System.Text.Json;
using DinoVision;
using SkiaSharp;
using Xunit;

namespace DinoVision.Tests;

public sealed class ImagePreprocessorTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "dino-preprocessing-" + Guid.NewGuid().ToString("N"));

    public ImagePreprocessorTests()
    {
        Directory.CreateDirectory(_directory);
    }

    [Theory]
    [InlineData(1, 1)]
    [InlineData(224, 224)]
    [InlineData(448, 336)]
    public void SolidImagePreservesRgbNormalizationDuringResize(int width, int height)
    {
        string imagePath = WriteImage(width, height, SKColors.Red);
        using JsonDocument config = CreateConfig();
        (float[] pixels, int actualHeight, int actualWidth) = ImagePreprocessor.Process(imagePath, config.RootElement);
        Assert.Equal(224, actualHeight);
        Assert.Equal(224, actualWidth);
        Assert.Equal(3 * 224 * 224, pixels.Length);
        float[] expected = [(1f - 0.485f) / 0.229f, -0.456f / 0.224f, -0.406f / 0.225f];
        for (int channel = 0; channel < 3; channel++)
        {
            for (int index = 0; index < 224 * 224; index++)
            {
                Assert.InRange(Math.Abs(pixels[(channel * 224 * 224) + index] - expected[channel]), 0, 2e-6f);
            }
        }
    }

    [Fact]
    public void NativeSizeImageKeepsChannelFirstPixelOrder()
    {
        string path = Path.Combine(_directory, "two-colors.png");
        using (var bitmap = new SKBitmap(224, 224))
        {
            bitmap.Erase(SKColors.Black);
            bitmap.SetPixel(0, 0, new SKColor(10, 20, 30));
            bitmap.SetPixel(1, 0, new SKColor(40, 50, 60));
            SavePng(bitmap, path);
        }

        using JsonDocument config = CreateConfig();
        (float[] pixels, _, _) = ImagePreprocessor.Process(path, config.RootElement);
        float scale = (float)(1.0 / 255);
        Assert.Equal(((10 * scale) - 0.485f) / 0.229f, pixels[0]);
        Assert.Equal(((40 * scale) - 0.485f) / 0.229f, pixels[1]);
        Assert.Equal(((20 * scale) - 0.456f) / 0.224f, pixels[224 * 224]);
        Assert.Equal(((30 * scale) - 0.406f) / 0.225f, pixels[2 * 224 * 224]);
    }

    [Fact]
    public void DownsamplingCheckerboardAppliesAntialiasingBeforeNormalization()
    {
        string path = Path.Combine(_directory, "checkerboard.png");
        using (var bitmap = new SKBitmap(448, 448))
        {
            for (int y = 0; y < bitmap.Height; y++)
            {
                for (int x = 0; x < bitmap.Width; x++)
                {
                    bitmap.SetPixel(x, y, (x + y) % 2 == 0 ? SKColors.Black : SKColors.White);
                }
            }

            SavePng(bitmap, path);
        }

        using JsonDocument config = CreateConfig();
        (float[] pixels, _, _) = ImagePreprocessor.Process(path, config.RootElement);
        float redBeforeNormalization = (pixels[(112 * 224) + 112] * 0.229f) + 0.485f;
        Assert.InRange(redBeforeNormalization, 0.499999f, 0.500001f);
    }

    [Theory]
    [InlineData(true, 2, 0.229f)]
    [InlineData(false, 3, 0.229f)]
    [InlineData(false, 2, 0f)]
    public void UnsupportedProcessorSettingsAreRejected(bool crop, int resample, float redStd)
    {
        using JsonDocument config = CreateConfig(crop, resample, redStd);
        Assert.Throws<ArgumentException>(() => ImagePreprocessor.Process("unused.png", config.RootElement));
    }

    [Fact]
    public void InvalidImageHasControlledFailure()
    {
        string path = Path.Combine(_directory, "invalid.png");
        File.WriteAllText(path, "This is not image data.");
        using JsonDocument config = CreateConfig();
        Assert.Throws<ArgumentException>(() => ImagePreprocessor.Process(path, config.RootElement));
    }

    private string WriteImage(int width, int height, SKColor color)
    {
        string path = Path.Combine(_directory, $"solid-{width}-{height}.png");
        using var bitmap = new SKBitmap(width, height);
        bitmap.Erase(color);
        SavePng(bitmap, path);
        return path;
    }

    private static void SavePng(SKBitmap bitmap, string path)
    {
        using SKData encoded = bitmap.Encode(SKEncodedImageFormat.Png, 100);
        using FileStream stream = File.Create(path);
        encoded.SaveTo(stream);
    }

    private static JsonDocument CreateConfig(bool crop = false, int resample = 2, float redStd = 0.229f)
    {
        return JsonDocument.Parse(JsonSerializer.Serialize(new
        {
            do_resize = true,
            do_rescale = true,
            do_normalize = true,
            do_center_crop = crop,
            resample,
            size = new { height = 224, width = 224 },
            rescale_factor = 1.0 / 255,
            image_mean = new[] { 0.485f, 0.456f, 0.406f },
            image_std = new[] { redStd, 0.224f, 0.225f }
        }));
    }

    public void Dispose()
    {
        Directory.Delete(_directory, recursive: true);
    }
}
