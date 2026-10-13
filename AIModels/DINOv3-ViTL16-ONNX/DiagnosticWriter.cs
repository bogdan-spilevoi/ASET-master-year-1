using System.Security.Cryptography;
using System.Text.Json;
using SkiaSharp;

namespace DinoVision;

internal static class DiagnosticWriter
{
    public static void WriteDecoded(string directory, SKBitmap bitmap)
    {
        byte[] rgb = new byte[checked(bitmap.Width * bitmap.Height * 3)];
        for (int y = 0; y < bitmap.Height; y++)
        {
            for (int x = 0; x < bitmap.Width; x++)
            {
                SKColor color = bitmap.GetPixel(x, y);
                int index = ((y * bitmap.Width) + x) * 3;
                rgb[index] = color.Red;
                rgb[index + 1] = color.Green;
                rgb[index + 2] = color.Blue;
            }
        }

        File.WriteAllBytes(Path.Combine(directory, "decoded_rgb.bin"), rgb);
        File.WriteAllText(Path.Combine(directory, "decoded_shape.json"),
            JsonSerializer.Serialize(new[] { bitmap.Height, bitmap.Width, 3 }));
    }

    public static void WriteFloat(string directory, string name, float[] values)
    {
        using var stream = File.Create(Path.Combine(directory, name + ".bin"));
        using var writer = new BinaryWriter(stream);
        foreach (float value in values)
        {
            writer.Write(value);
        }
    }

    public static void WriteMetadata(string directory, string imagePath, int height, int width)
    {
        using var image = File.OpenRead(imagePath);
        File.WriteAllText(Path.Combine(directory, "metadata.json"), JsonSerializer.Serialize(new
        {
            imageSha256 = Convert.ToHexStringLower(SHA256.HashData(image)),
            inputShape = new[] { 1, 3, height, width },
            outputShape = new[] { 1, 1024 },
            floatEncoding = "float32 little-endian",
            runtimeVersion = typeof(Microsoft.ML.OnnxRuntime.InferenceSession).Assembly.GetName().Version?.ToString(),
            preprocessingVersion = "rgb-rescale-bilinear-antialias-f32-v2"
        }, new JsonSerializerOptions { WriteIndented = true }));
    }
}
