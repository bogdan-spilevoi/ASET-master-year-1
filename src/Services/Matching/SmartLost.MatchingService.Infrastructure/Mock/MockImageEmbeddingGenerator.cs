using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Options;
using SmartLost.MatchingService.Application.Contracts;
using SmartLost.MatchingService.Application.Interfaces;
using SmartLost.MatchingService.Application.Services;

namespace SmartLost.MatchingService.Infrastructure.Mock;

/// <summary>
/// Deterministic DINOv3 stand-in. Each appearance token maps to a fixed pseudo-random direction, so photos
/// that share tokens (black-backpack, blue-backpack) get partially similar normalized vectors.
/// </summary>
public sealed class MockImageEmbeddingGenerator(IOptions<MockAdapterOptions> options, TimeProvider timeProvider)
    : IImageEmbeddingGenerator
{
    public const int Dimensions = 1024;

    public async Task<GeneratedEmbedding> GenerateAsync(PhotoContent photo, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(photo);
        var appearance = MockAppearance.Parse(photo.Content);
        await appearance.SimulateAsync(options.Value.SlowDelay, timeProvider, cancellationToken);
        double[] sum = new double[Dimensions];
        foreach (string token in appearance.Tokens.DefaultIfEmpty("unknown"))
        {
            AddTokenDirection(sum, token);
        }

        double norm = Math.Sqrt(sum.Sum(value => value * value));
        return new GeneratedEmbedding(MatchingOptions.Dinov3Version, [.. sum.Select(value => (float)(value / norm))]);
    }

    private static void AddTokenDirection(double[] sum, string token)
    {
        double[] direction = new double[Dimensions];
        for (int block = 0; (block * 32) < Dimensions; block++)
        {
            byte[] hash = SHA256.HashData(Encoding.UTF8.GetBytes($"{token}:{block}"));
            for (int index = 0; index < hash.Length && ((block * 32) + index) < Dimensions; index++)
            {
                direction[(block * 32) + index] = (hash[index] - 127.5) / 127.5;
            }
        }

        double norm = Math.Sqrt(direction.Sum(value => value * value));
        for (int index = 0; index < Dimensions; index++)
        {
            sum[index] += direction[index] / norm;
        }
    }
}
