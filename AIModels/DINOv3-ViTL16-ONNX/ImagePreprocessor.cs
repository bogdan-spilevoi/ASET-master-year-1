using System.Text.Json;
using SkiaSharp;

namespace DinoVision;

internal static class ImagePreprocessor
{
    public static (float[] Pixels, int Height, int Width) Process(string imagePath, JsonElement config, string? diagnosticsDirectory = null)
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
        if (diagnosticsDirectory is not null)
        {
            DiagnosticWriter.WriteDecoded(diagnosticsDirectory, source);
        }
        // Hugging Face DINOv3 fast processing: rescale -> antialiased resize -> normalize.
        // Like PyTorch's CPU AA kernel, resize width first and then height using float32.
        (int Index, float Weight)[][] xWeights = BuildWeights(source.Width, width);
        (int Index, float Weight)[][] yWeights = BuildWeights(source.Height, height);
        float[] horizontal = new float[checked(3 * source.Height * width)];
        int horizontalPlane = source.Height * width;
        for (int y = 0; y < source.Height; y++)
        {
            for (int x = 0; x < width; x++)
            {
                float red = 0;
                float green = 0;
                float blue = 0;
                foreach ((int sourceX, float weight) in xWeights[x])
                {
                    SKColor color = source.GetPixel(sourceX, y);
                    red += (color.Red * scale) * weight;
                    green += (color.Green * scale) * weight;
                    blue += (color.Blue * scale) * weight;
                }

                int index = (y * width) + x;
                horizontal[index] = red;
                horizontal[horizontalPlane + index] = green;
                horizontal[(2 * horizontalPlane) + index] = blue;
            }
        }

        float[] pixels = new float[3 * height * width];
        int plane = height * width;
        for (int channel = 0; channel < 3; channel++)
        {
            for (int y = 0; y < height; y++)
            {
                for (int x = 0; x < width; x++)
                {
                    float value = 0;
                    foreach ((int sourceY, float weight) in yWeights[y])
                    {
                        value += horizontal[(channel * horizontalPlane) + (sourceY * width) + x] * weight;
                    }

                    pixels[(channel * plane) + (y * width) + x] = (value - mean[channel]) / std[channel];
                }
            }
        }

        return (pixels, height, width);
    }

    private static bool IsEnabled(JsonElement config, string name)
    {
        return config.TryGetProperty(name, out JsonElement value) && value.ValueKind == JsonValueKind.True;
    }

    private static (int Index, float Weight)[][] BuildWeights(int inputSize, int outputSize)
    {
        var result = new (int Index, float Weight)[outputSize][];
        if (inputSize == outputSize)
        {
            for (int index = 0; index < outputSize; index++)
            {
                result[index] = [(index, 1f)];
            }

            return result;
        }

        float scale = (float)inputSize / outputSize;
        float support = Math.Max(1f, scale);
        float inverseScale = scale >= 1f ? (float)(1.0 / scale) : 1f;
        for (int index = 0; index < outputSize; index++)
        {
            float center = (float)(scale * (index + 0.5));
            int start = Math.Max(0, (int)(center - support + 0.5));
            int end = Math.Min(inputSize, (int)(center + support + 0.5));
            var samples = new (int Index, float Weight)[end - start];
            float total = 0;
            for (int sample = start; sample < end; sample++)
            {
                float distance = (float)((sample - center + 0.5) * inverseScale);
                float weight = Math.Max(0f, 1f - Math.Abs(distance));
                samples[sample - start] = (sample, weight);
                total += weight;
            }

            for (int sample = 0; sample < samples.Length; sample++)
            {
                samples[sample].Weight /= total;
            }

            result[index] = samples;
        }

        return result;
    }
}
