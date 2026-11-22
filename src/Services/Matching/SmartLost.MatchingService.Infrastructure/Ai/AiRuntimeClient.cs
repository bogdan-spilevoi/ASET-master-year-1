using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using SmartLost.MatchingService.Application.Contracts;
using SmartLost.MatchingService.Application.Exceptions;
using SmartLost.MatchingService.Application.Interfaces;
using SmartLost.MatchingService.Application.Services;
using SmartLost.MatchingService.Domain.ValueObjects;

namespace SmartLost.MatchingService.Infrastructure.Ai;

public sealed class AiRuntimeClient(HttpClient http) : IImageEmbeddingGenerator, IPhotoAnalyzer
{
    private static readonly JsonSerializerOptions _embeddingJson = new(JsonSerializerDefaults.Web)
    {
        RespectRequiredConstructorParameters = true,
        RespectNullableAnnotations = true
    };
    private static readonly JsonSerializerOptions _labelJson = new(_embeddingJson)
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower
    };

    public async Task<GeneratedEmbedding> GenerateAsync(PhotoContent photo, CancellationToken cancellationToken)
    {
        EmbeddingResponse result = await PostAsync<EmbeddingResponse>("embeddings", photo, _embeddingJson, cancellationToken);
        if (result.Dimensions != 1024 || result.Embedding.Length != result.Dimensions ||
            string.IsNullOrWhiteSpace(result.Model) || result.Model.Length + "onnx:".Length > EmbeddingVersion.MaximumLength ||
            string.IsNullOrWhiteSpace(result.PreprocessingVersion) || result.PreprocessingVersion.Length > EmbeddingVersion.MaximumLength)
        {
            throw new AiInferenceException("Invalid embedding received from the AI runtime.");
        }
        return new GeneratedEmbedding(MatchingOptions.FromRuntime(result.Model, result.PreprocessingVersion), result.Embedding);
    }

    public async Task<PhotoAnalysis> AnalyzeAsync(PhotoContent photo, CancellationToken cancellationToken)
    {
        PhotoAnalysis labels = await PostAsync<PhotoAnalysis>("photo-analysis", photo, _labelJson, cancellationToken);
        if (labels.Objects.Any(item => item is null))
        {
            throw new AiInferenceException("Invalid object received from the AI runtime.");
        }
        return labels;
    }

    private async Task<T> PostAsync<T>(string route, PhotoContent photo, JsonSerializerOptions json, CancellationToken token)
    {
        ArgumentNullException.ThrowIfNull(photo);
        using var body = new ReadOnlyMemoryContent(photo.Content);
        body.Headers.ContentType = new MediaTypeHeaderValue(photo.MediaType);
        try
        {
            using HttpResponseMessage response = await http.PostAsync(route, body, token);
            response.EnsureSuccessStatusCode();
            return await response.Content.ReadFromJsonAsync<T>(json, token)
                ?? throw new AiInferenceException("Empty response from the AI runtime.");
        }
        catch (Exception exception) when (exception is HttpRequestException or JsonException or IOException)
        {
            throw new AiInferenceException("AI runtime request failed.", exception);
        }
    }

    private sealed record EmbeddingResponse(string Model, string PreprocessingVersion, int Dimensions, float[] Embedding);
}
