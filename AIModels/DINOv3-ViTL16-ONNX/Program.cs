using System.Diagnostics;
using System.Text.Json;
using DinoVision;
using Microsoft.Extensions.Configuration;
using Microsoft.ML.OnnxRuntime;
using SmartLost.AI.Configuration;

const string Usage = "Usage: DinoVision [image-path] [--model model-directory]";
if (args.Length == 1 && args[0] is "--help" or "-h")
{
    Console.WriteLine(Usage);
    Console.WriteLine("Without arguments, enter the image path when prompted. Outputs a normalized image embedding as JSON.");
    return 0;
}

if (args.Length is not (0 or 1 or 3) || (args.Length == 3 && args[1] != "--model"))
{
    Console.Error.WriteLine(Usage);
    return 1;
}

try
{
    var configuration = new ConfigurationBuilder()
        .SetBasePath(AppContext.BaseDirectory)
        .AddJsonFile("appsettings.json", optional: false, reloadOnChange: false)
        .Build();
    string imageArgument;
    if (args.Length == 0)
    {
        Console.Error.Write("Image path: ");
        imageArgument = (Console.ReadLine() ?? string.Empty).Trim().Trim('"', '\'');
    }
    else
    {
        imageArgument = args[0];
    }

    if (string.IsNullOrWhiteSpace(imageArgument))
    {
        throw new ArgumentException("An image path is required.");
    }

    string imagePath = Path.GetFullPath(imageArgument);
    if (!File.Exists(imagePath))
    {
        throw new FileNotFoundException("Image not found.", imagePath);
    }

    string modelDirectory = args.Length == 3
        ? PathResolver.Resolve(args[2], Environment.CurrentDirectory)
        : PathResolver.Resolve(configuration["Paths:ModelDirectory"]);
    string modelPath = PathResolver.Resolve(configuration["Paths:ModelFile"], modelDirectory);
    string preprocessorPath = PathResolver.Resolve(configuration["Paths:PreprocessorFile"], modelDirectory);
    // The community ONNX graph refers to this exact external weights filename.
    string weightsPath = Path.Combine(Path.GetDirectoryName(modelPath)!, "model.onnx_data");
    foreach (string required in new[] { modelPath, weightsPath, preprocessorPath })
    {
        if (!File.Exists(required))
        {
            throw new FileNotFoundException($"Required model file missing: {required}. See the download command in README.md.", required);
        }
    }

    Console.Error.WriteLine($"Loading model on CPU: {modelPath}");
    var timer = Stopwatch.StartNew();
    using var session = new InferenceSession(modelPath);
    if (!session.InputMetadata.ContainsKey("pixel_values") || !session.OutputMetadata.ContainsKey("pooler_output"))
    {
        throw new ArgumentException("Expected the onnx-community DINOv3 export with pixel_values and pooler_output.");
    }

    Console.Error.WriteLine($"Model loaded in {timer.Elapsed.TotalSeconds:F1}s.");
    timer.Restart();
    using var config = JsonDocument.Parse(File.ReadAllText(preprocessorPath));
    (float[] pixels, int height, int width) = ImagePreprocessor.Process(imagePath, config.RootElement);
    using var tensor = OrtValue.CreateTensorValueFromMemory(pixels, [1, 3, height, width]);
    using var options = new RunOptions();
    using IDisposableReadOnlyCollection<OrtValue> outputs = session.Run(options, new Dictionary<string, OrtValue> { ["pixel_values"] = tensor }, outputNames);
    long[] shape = outputs[0].GetTensorTypeAndShape().Shape;
    float[] embedding = outputs[0].GetTensorDataAsSpan<float>().ToArray();
    if (shape.Length != 2 || shape[0] != 1 || shape[1] != 1024 || embedding.Length != 1024
        || embedding.Any(value => !float.IsFinite(value)))
    {
        throw new ArgumentException("Model returned an invalid image embedding.");
    }

    double norm = Math.Sqrt(embedding.Sum(value => (double)value * value));
    if (!double.IsFinite(norm) || norm <= 0)
    {
        throw new ArgumentException("Model returned an invalid embedding norm.");
    }

    for (int index = 0; index < embedding.Length; index++)
    {
        embedding[index] = (float)(embedding[index] / norm);
    }

    Console.WriteLine(JsonSerializer.Serialize(new
    {
        model = "onnx-community/dinov3-vitl16-pretrain-lvd1689m-ONNX",
        embeddingType = "L2-normalized pooler_output (CLS)",
        preprocessingVersion = "rgb-bilinear-antialias-v1",
        dimensions = embedding.Length,
        embedding
    }, new JsonSerializerOptions { WriteIndented = true }));
    Console.Error.WriteLine($"Embedding finished in {timer.Elapsed.TotalSeconds:F1}s.");
    return 0;
}
catch (Exception exception) when (exception is OnnxRuntimeException or JsonException
    or IOException or UnauthorizedAccessException or ArgumentException or InvalidOperationException)
{
    Console.Error.WriteLine($"Image embedding failed: {exception.Message}");
    return 1;
}

partial class Program
{
    private static readonly string[] outputNames = new[] { "pooler_output" };
}
