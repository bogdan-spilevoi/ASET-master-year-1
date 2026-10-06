using System.Text.Json;
using SkiaSharp;

namespace DinoVision;

internal static class ImagePreprocessor
{
    public static (float[] Pixels, int Height, int Width) Process(string imagePath, JsonElement config)
    {
        // Reject different processor configurations rather than silently changing embeddings.
        if (!config.GetProperty("do_resize").GetBoolean()
            || !config.GetProperty("do_rescale").GetBoolean()
            || !config.GetProperty("do_normalize").GetBoolean()
            || config.GetProperty("resample").GetInt32() != 2
            || IsEnabled(config, "do_center_crop") || IsEnabled(config, "do_pad"))
        {
            throw new ArgumentException("Expected DINOv3 RGB bilinear resize, rescale and normalize without crop/pad.");
        }

        int height = config.GetProperty("size").GetProperty("height").GetInt32();
        int width = config.GetProperty("size").GetProperty("width").GetInt32();
        if (height != 224 || width != 224)
        {
            throw new ArgumentException("This preprocessing version requires the selected model's 224x224 processor.");
        }

        float scale = config.GetProperty("rescale_factor").GetSingle();
        float[] mean = config.GetProperty("image_mean").EnumerateArray().Select(value => value.GetSingle()).ToArray();
        float[] std = config.GetProperty("image_std").EnumerateArray().Select(value => value.GetSingle()).ToArray();
        if (mean.Length != 3 || std.Length != 3 || !float.IsFinite(scale)
            || mean.Any(value => !float.IsFinite(value)) || std.Any(value => !float.IsFinite(value) || value <= 0))
        {
            throw new ArgumentException("Invalid RGB normalization settings.");
        }

        using SKBitmap source = SKBitmap.Decode(imagePath)
            ?? throw new ArgumentException("Image could not be decoded.");
        // Full-image resize, as configured by the HF processor; no implicit center crop.
        // Widen the triangular filter when downsampling to provide antialiasing.
        (int Index, double Weight)[][] xWeights = BuildWeights(source.Width, width);
        (int Index, double Weight)[][] yWeights = BuildWeights(source.Height, height);
        float[] pixels = new float[3 * height * width];
        for (int y = 0; y < height; y++)
        {
            for (int x = 0; x < width; x++)
            {
                double red = 0;
                double green = 0;
                double blue = 0;
                foreach ((int sourceY, double yWeight) in yWeights[y])
                {
                    foreach ((int sourceX, double xWeight) in xWeights[x])
                    {
                        SKColor color = source.GetPixel(sourceX, sourceY);
                        double weight = xWeight * yWeight;
                        red += color.Red * weight;
                        green += color.Green * weight;
                        blue += color.Blue * weight;
                    }
                }

                int index = (y * width) + x;
                pixels[index] = ((float)red * scale - mean[0]) / std[0];
                pixels[(height * width) + index] = ((float)green * scale - mean[1]) / std[1];
                pixels[(2 * height * width) + index] = ((float)blue * scale - mean[2]) / std[2];
            }
        }

        return (pixels, height, width);
    }

    private static bool IsEnabled(JsonElement config, string name)
    {
        return config.TryGetProperty(name, out JsonElement value) && value.ValueKind == JsonValueKind.True;
    }

    private static (int Index, double Weight)[][] BuildWeights(int inputSize, int outputSize)
    {
        double scale = (double)inputSize / outputSize;
        double support = Math.Max(1, scale);
        var result = new (int Index, double Weight)[outputSize][];
        for (int index = 0; index < outputSize; index++)
        {
            double center = ((index + 0.5) * scale) - 0.5;
            var samples = new List<(int Index, double Weight)>();
            double total = 0;
            int start = Math.Max(0, (int)Math.Ceiling(center - support));
            int end = Math.Min(inputSize - 1, (int)Math.Floor(center + support));
            for (int sample = start; sample <= end; sample++)
            {
                double weight = Math.Max(0, 1 - (Math.Abs(sample - center) / support));
                if (weight > 0)
                {
                    samples.Add((sample, weight));
                    total += weight;
                }
            }

            result[index] = samples.Select(sample => (sample.Index, sample.Weight / total)).ToArray();
        }

        return result;
    }
}
