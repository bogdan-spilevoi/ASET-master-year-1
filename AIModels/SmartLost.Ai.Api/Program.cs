using System.Security.Cryptography;
using System.Text;
using SmartLost.Ai.Api;

var builder = WebApplication.CreateBuilder(args);
if (builder.Configuration["urls"] is null)
{
    builder.WebHost.UseUrls("http://127.0.0.1:5050");
}
builder.WebHost.ConfigureKestrel(options => options.Limits.MaxRequestBodySize = 10 * 1024 * 1024);
string apiKey = builder.Configuration["AiRuntime:ApiKey"] ?? "";
if (string.IsNullOrWhiteSpace(apiKey))
{
    throw new InvalidOperationException("Configure AiRuntime:ApiKey before starting the AI API.");
}
byte[] expectedKey = SHA256.HashData(Encoding.UTF8.GetBytes(apiKey));
builder.Services.AddSingleton<ModelRunner>();
var app = builder.Build();
app.Use(async (context, next) =>
{
    if (!context.Request.Headers.TryGetValue("X-Api-Key", out var keys) || keys.Count != 1 ||
        !CryptographicOperations.FixedTimeEquals(expectedKey, SHA256.HashData(Encoding.UTF8.GetBytes(keys.ToString()))))
    {
        context.Response.StatusCode = StatusCodes.Status401Unauthorized;
        return;
    }
    await next(context);
});

app.MapPost("/embeddings", (HttpContext context, ModelRunner runner) => runner.RunAsync(context, runner.DinoPath));
app.MapPost("/photo-analysis", (HttpContext context, ModelRunner runner) => runner.RunAsync(context, runner.QwenPath));
app.Run();
