using System.Diagnostics;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using Microsoft.ML.OnnxRuntimeGenAI;

if (args.Length == 1 && args[0] is "--help" or "-h")
{
    Console.WriteLine("Usage: QwenVision [image-path] [--model model-directory]");
    Console.WriteLine("Without arguments, enter the image path when prompted.");
    return 0;
}

if (args.Length is not (0 or 1 or 3) || (args.Length == 3 && args[1] != "--model"))
{
    Console.Error.WriteLine("Usage: QwenVision [image-path] [--model model-directory]");
    return 1;
}

try
{
    string promptPath = Path.Combine(AppContext.BaseDirectory, "Prompts", "image-labels.txt");
    string extractionPrompt = File.ReadAllText(promptPath);
    if (string.IsNullOrWhiteSpace(extractionPrompt))
    {
        Console.Error.WriteLine($"Prompt file is empty: {promptPath}");
        return 1;
    }

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
        Console.Error.WriteLine("An image path is required.");
        return 1;
    }

    string imagePath = Path.GetFullPath(imageArgument);
    if (!File.Exists(imagePath))
    {
        Console.Error.WriteLine($"Image not found: {imagePath}");
        return 1;
    }

    string modelDirectory = args.Length == 3
        ? Path.GetFullPath(args[2])
        : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            "Documents", "LocalAI", "QwenVision", "models", "qwen3-vl-4b",
            "onnxruntime", "cpu_and_mobile", "cpu-int4-rtn-block-32");
    if (!File.Exists(Path.Combine(modelDirectory, "genai_config.json")))
    {
        Console.Error.WriteLine($"Model configuration not found in: {modelDirectory}");
        Console.Error.WriteLine("Use --model <model-directory> to specify a different download location.");
        return 1;
    }

    using var runtime = new OgaHandle();
    using var config = new Config(modelDirectory);
    config.ClearProviders();
    config.Overlay("""
        {
          "model": { "context_length": 4096 },
          "search": { "max_length": 4096, "do_sample": false }
        }
        """);

    Console.Error.WriteLine($"Loading model on CPU: {modelDirectory}");
    var timer = Stopwatch.StartNew();
    using var model = new Model(config);
    Console.Error.WriteLine($"Model loaded in {timer.Elapsed.TotalSeconds:F1}s.");

    using var processor = new MultiModalProcessor(model);
    using TokenizerStream stream = processor.CreateStream();
    using var images = Images.Load([imagePath]);

    string prompt = "<|im_start|>system\n" + extractionPrompt
        + "<|im_end|>\n<|im_start|>user\n"
        + "<|vision_start|><|image_pad|><|vision_end|>"
        + "Extract object labels and visible features from this image in English."
        + "<|im_end|>\n<|im_start|>assistant\n";

    using NamedTensors inputs = processor.ProcessImagesAndAudios(prompt, images, null);
    using var parameters = new GeneratorParams(model);
    parameters.SetSearchOption("max_length", 4096);
    parameters.SetSearchOption("do_sample", false);

    using var generator = new Generator(model, parameters);
    generator.SetInputs(inputs);

    timer.Restart();
    var response = new StringBuilder();
    const int MaxOutputTokens = 1024;
    for (int count = 0; count < MaxOutputTokens && !generator.IsDone(); count++)
    {
        generator.GenerateNextToken();
        if (!generator.IsDone())
        {
            response.Append(stream.Decode(generator.GetNextTokens()[0]));
        }
    }

    if (!generator.IsDone())
    {
        Console.Error.WriteLine("Output token limit reached; the response may be incomplete.");
        return 1;
    }

    string rawResponse = response.ToString();
    using var labels = JsonDocument.Parse(rawResponse);
    if (labels.RootElement.ValueKind != JsonValueKind.Object
        || !labels.RootElement.TryGetProperty("objects", out JsonElement objects)
        || objects.ValueKind != JsonValueKind.Array
        || !labels.RootElement.TryGetProperty("uncertainties", out JsonElement uncertainties)
        || uncertainties.ValueKind != JsonValueKind.Array)
    {
        Console.Error.WriteLine("The model response does not contain the expected label structure.");
        Console.Error.WriteLine(rawResponse);
        return 1;
    }

    Console.WriteLine(JsonSerializer.Serialize(labels.RootElement, new JsonSerializerOptions
    {
        WriteIndented = true,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
    }));
    Console.Error.WriteLine($"Generation finished in {timer.Elapsed.TotalSeconds:F1}s.");
    return 0;
}
catch (Exception exception) when (exception is OnnxRuntimeGenAIException or JsonException
    or IOException or UnauthorizedAccessException or ArgumentException)
{
    Console.Error.WriteLine($"Image analysis failed: {exception.Message}");
    return 1;
}
