using System.Diagnostics;
using SkiaSharp;

namespace SmartLost.Ai.Api;

internal sealed class ModelRunner(IConfiguration configuration, IHostEnvironment environment,
    IHostApplicationLifetime lifetime, ILogger<ModelRunner> logger)
{
    private readonly SemaphoreSlim _dinoSlot = new(1, 1);
    private readonly SemaphoreSlim _qwenSlot = new(1, 1);

    public Task<IResult> RunDinoAsync(HttpContext context) =>
        RunAsync(context, Resolve("Dino", "DINOv3-ViTL16-ONNX", "DinoVision"), _dinoSlot);

    public Task<IResult> RunQwenAsync(HttpContext context) =>
        RunAsync(context, Resolve("Qwen", "Qwen3-VL-4B-Instruct-ONNX", "QwenVision"), _qwenSlot);

    private static string DefaultPath(string project, string assembly) =>
        $"../{project}/bin/Release/net10.0/{assembly}.dll";

    private async Task<IResult> RunAsync(HttpContext context, string assembly, SemaphoreSlim slot)
    {
        string? extension = context.Request.ContentType?.ToLowerInvariant() switch
        {
            "image/jpeg" => ".jpg",
            "image/png" => ".png",
            _ => null
        };
        if (extension is null)
        {
            return Results.StatusCode(StatusCodes.Status415UnsupportedMediaType);
        }
        if (!slot.Wait(0, context.RequestAborted))
        {
            context.Response.Headers.RetryAfter = "10";
            return Results.StatusCode(StatusCodes.Status429TooManyRequests);
        }

        string temporary = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid():N}{extension}");
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(context.RequestAborted, lifetime.ApplicationStopping);
        deadline.CancelAfter(TimeSpan.FromMinutes(3));
        try
        {
            await using (var file = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            {
                await context.Request.Body.CopyToAsync(file, deadline.Token);
                if (file.Length == 0)
                {
                    return Results.BadRequest();
                }
            }
            using (var codec = SKCodec.Create(temporary))
            {
                var expected = extension == ".jpg" ? SKEncodedImageFormat.Jpeg : SKEncodedImageFormat.Png;
                if (codec is null || codec.EncodedFormat != expected || (long)codec.Info.Width * codec.Info.Height > 4_000_000)
                {
                    return Results.BadRequest("Supply a JPEG or PNG image of at most 4 megapixels.");
                }
            }
            var start = new ProcessStartInfo("dotnet") { UseShellExecute = false, RedirectStandardOutput = true };
            start.ArgumentList.Add(assembly);
            start.ArgumentList.Add(temporary);
            using var process = Process.Start(start) ?? throw new InvalidOperationException("Cannot start inference.");
            try
            {
                string json = await process.StandardOutput.ReadToEndAsync(deadline.Token);
                await process.WaitForExitAsync(deadline.Token);
                if (process.ExitCode == 0)
                {
                    return Results.Text(json, "application/json");
                }
                logger.LogWarning("{Model} inference exited with code {ExitCode}", Path.GetFileName(assembly), process.ExitCode);
                return Results.Problem(statusCode: StatusCodes.Status502BadGateway);
            }
            finally
            {
                if (!process.HasExited)
                {
                    try { process.Kill(entireProcessTree: true); }
                    catch (InvalidOperationException) when (process.HasExited) { }
                    await process.WaitForExitAsync();
                }
            }
        }
        catch (OperationCanceledException) when (deadline.IsCancellationRequested)
        {
            return Results.StatusCode(StatusCodes.Status504GatewayTimeout);
        }
        finally
        {
            try { File.Delete(temporary); }
            finally { slot.Release(); }
        }
    }

    private string Resolve(string name, string project, string assembly) =>
        Path.GetFullPath(configuration[$"AiRuntime:{name}Assembly"] ?? DefaultPath(project, assembly), environment.ContentRootPath);
}
