using Microsoft.Extensions.Options;
using SmartLost.MatchingService.Application.Contracts;
using SmartLost.MatchingService.Application.Interfaces;

namespace SmartLost.MatchingService.Infrastructure.Mock;

/// <summary>Qwen3-VL stand-in returning labels with the image-labels.txt structure.</summary>
public sealed class MockPhotoAnalyzer(IOptions<MockAdapterOptions> options, TimeProvider timeProvider) : IPhotoAnalyzer
{
    private static readonly Dictionary<string, string> _categories = new(StringComparer.Ordinal)
    {
        ["backpack"] = "containers",
        ["bag"] = "containers",
        ["bottle"] = "containers",
        ["wallet"] = "accessories",
        ["umbrella"] = "accessories",
        ["keys"] = "keys",
        ["phone"] = "electronics",
        ["laptop"] = "electronics",
        ["jacket"] = "clothing"
    };

    private static readonly HashSet<string> _colours = new(StringComparer.Ordinal)
    {
        "black", "white", "blue", "red", "green", "grey", "brown", "yellow"
    };

    public async Task<PhotoAnalysis> AnalyzeAsync(PhotoContent photo, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(photo);
        var appearance = MockAppearance.Parse(photo.Content);
        await appearance.SimulateAsync(options.Value.SlowDelay, timeProvider, cancellationToken);
        string? objectType = appearance.Tokens.FirstOrDefault(_categories.ContainsKey);
        if (objectType is null)
        {
            return new PhotoAnalysis([], ["no known object type in the mock appearance"]);
        }

        string[] colours = [.. appearance.Tokens.Where(_colours.Contains).Take(3)];
        string[] features = [.. appearance.Tokens.Where(token => token != objectType && !_colours.Contains(token)).Take(5)];
        var detected = new DetectedObject(_categories[objectType], objectType, colours, null, null, null, [], features,
            [.. appearance.Tokens.Distinct().Take(12)]);
        return new PhotoAnalysis([detected], []);
    }
}
