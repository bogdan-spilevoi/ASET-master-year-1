using System.Text;
using SmartLost.MatchingService.Application.Exceptions;

namespace SmartLost.MatchingService.Infrastructure.Mock;

/// <summary>
/// Text stand-in for photo bytes, e.g. <c>black-backpack</c>. Prefixes <c>slow:</c>, <c>timeout:</c> and
/// <c>error:</c> make the mock AI adapters exercise the profiling paths.
/// </summary>
public sealed record MockAppearance(MockAiBehavior Behavior, IReadOnlyList<string> Tokens)
{
    public const string MediaType = "application/x-smartlost-mock-photo";

    public static MockAppearance Parse(ReadOnlyMemory<byte> content)
    {
        string text = Encoding.UTF8.GetString(content.Span).Trim().ToLowerInvariant();
        MockAiBehavior behavior = MockAiBehavior.Normal;
        foreach ((string prefix, MockAiBehavior value) in new[]
        {
            ("slow:", MockAiBehavior.Slow), ("timeout:", MockAiBehavior.Timeout), ("error:", MockAiBehavior.Error)
        })
        {
            if (text.StartsWith(prefix, StringComparison.Ordinal))
            {
                behavior = value;
                text = text[prefix.Length..];
                break;
            }
        }

        return new MockAppearance(behavior, [.. text.Split('-', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)]);
    }

    /// <summary>Applies the simulated delay, hang or failure before a mock AI result is produced.</summary>
    public async Task SimulateAsync(TimeSpan slowDelay, TimeProvider timeProvider, CancellationToken cancellationToken)
    {
        switch (Behavior)
        {
            case MockAiBehavior.Slow:
                await Task.Delay(slowDelay, timeProvider, cancellationToken);
                break;
            case MockAiBehavior.Timeout:
                await Task.Delay(Timeout.InfiniteTimeSpan, timeProvider, cancellationToken);
                break;
            case MockAiBehavior.Error:
                throw new AiInferenceException("The mock AI runtime simulated a failure.");
            default:
                break;
        }
    }
}
