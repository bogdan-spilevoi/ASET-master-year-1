using System.Net;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using SmartLost.Contracts.Listings;
using SmartLost.MatchingService.Application.Contracts;
using SmartLost.MatchingService.Application.Exceptions;
using SmartLost.MatchingService.Application.Interfaces;
using SmartLost.MatchingService.Application.Services;
using SmartLost.MatchingService.Domain.Entities;
using SmartLost.MatchingService.Domain.ValueObjects;
using SmartLost.MatchingService.Infrastructure.Ai;
using SmartLost.MatchingService.Infrastructure.Persistence;

namespace SmartLost.MatchingService.Infrastructure.Analysis;

public sealed class DraftAnalysisProcessor(MatchingDbContext db, ListingsPhotoClient photos, IPhotoAnalyzer analyzer,
    IImageEmbeddingGenerator generator, IImageEmbeddingRepository embeddings, MatchingOptions options,
    IConfiguration configuration, TimeProvider clock)
{
    public async Task<bool> ProcessNextAsync(CancellationToken token)
    {
        DateTime now = clock.GetUtcNow().UtcDateTime;
        PhotoAnalysisWork? row = await db.Set<PhotoAnalysisWork>().Where(item =>
                (item.State == "Pending" || item.State == "Processing") && item.AvailableAt <= now)
            .OrderBy(item => item.AvailableAt).ThenBy(item => item.Id).FirstOrDefaultAsync(token);
        if (row is null)
        {
            return false;
        }

        row.Lease = Guid.NewGuid();
        if (row.Attempt >= options.MaximumAttempts)
        {
            row.State = "Failed";
            row.ErrorCode = "analysis.attempts_exhausted";
            await db.SaveChangesAsync(token);
            return true;
        }
        row.Attempt++;
        row.State = "Processing";
        row.AvailableAt = now + options.LeaseDuration;
        await db.SaveChangesAsync(token);

        var reference = new PhotoReference(row.PhotoId, row.PhotoRevision);
        try
        {
            PhotoContent? photo = await photos.ReadAsync(row.ListingId, reference, token);
            if (photo is null)
            {
                row.State = "Failed";
                row.ErrorCode = "photo.unavailable";
            }
            else
            {
                if (row.Kind == DraftAnalysisKind.Traits)
                {
                    PhotoAnalysis analysis = await analyzer.AnalyzeAsync(photo, token);
                    row.SuggestionsJson = JsonSerializer.Serialize(analysis, JsonSerializerOptions.Web);
                    row.ModelVersion = configuration["DraftAnalysis:TraitsModelVersion"] ??
                        (string.IsNullOrWhiteSpace(configuration["AiRuntime:BaseUrl"]) ? "mock-labels-v1" : "qwen3-vl-4b-instruct:image-labels-v1");
                }
                else
                {
                    GeneratedEmbedding result = await generator.GenerateAsync(photo, token);
                    if (result.Version != options.EmbeddingVersion)
                    {
                        throw new AiInferenceException("Embedding model version mismatch.");
                    }
                    var vector = ImageEmbedding.Create(row.ListingId, reference, result.Version, result.Vector);
                    await embeddings.UpsertAsync(vector, token);
                    row.ModelVersion = vector.ModelVersion;
                    row.Dimensions = vector.Dimensions;
                }
                row.State = "Ready";
                row.ErrorCode = null;
            }
        }
        catch (Exception error) when (!token.IsCancellationRequested && error is AiInferenceException or HttpRequestException or IOException or TimeoutException or OperationCanceledException)
        {
            bool busy = error.InnerException is HttpRequestException { StatusCode: HttpStatusCode.TooManyRequests };
            if (busy)
            {
                row.Attempt--;
            }
            row.State = row.Attempt >= options.MaximumAttempts ? "Failed" : "Pending";
            row.ErrorCode = busy ? "ai.busy" : "analysis.unavailable";
            row.AvailableAt = clock.GetUtcNow().UtcDateTime + options.RetryDelay;
        }
        // EF commits the result and optional vector atomically; the lease fences off late writers.
        await db.SaveChangesAsync(token);
        return true;
    }
}
