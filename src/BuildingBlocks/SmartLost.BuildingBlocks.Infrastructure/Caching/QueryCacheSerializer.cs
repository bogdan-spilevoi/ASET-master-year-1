using System.Text.Json;
using System.Text.Json.Serialization;

namespace SmartLost.BuildingBlocks.Infrastructure.Caching;

/// <summary>
/// Versioned JSON envelope for cached query results. Reading is strict: unknown, missing or null
/// non-nullable members make an entry unreadable, so a changed result shape becomes a miss.
/// </summary>
public static class QueryCacheSerializer
{
    public const int FormatVersion = 1;

    private static readonly JsonSerializerOptions _options = new(JsonSerializerDefaults.Web)
    {
        Converters = { new ResultJsonConverterFactory(), new ReadOnlyListJsonConverterFactory() },
        RespectNullableAnnotations = true,
        RespectRequiredConstructorParameters = true,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow
    };

    public static byte[] Serialize<T>(T value, DateTimeOffset expiresAtUtc) where T : class
    {
        ArgumentNullException.ThrowIfNull(value);
        return JsonSerializer.SerializeToUtf8Bytes(new Envelope<T>(FormatVersion, expiresAtUtc, value), _options);
    }

    /// <summary>Returns false when the bytes are not a readable entry of this format and type.</summary>
    public static bool TryDeserialize<T>(byte[] payload, out T? value, out DateTimeOffset expiresAtUtc) where T : class
    {
        ArgumentNullException.ThrowIfNull(payload);
        value = null;
        expiresAtUtc = default;
        Envelope<T>? envelope;
        try
        {
            envelope = JsonSerializer.Deserialize<Envelope<T>>(payload, _options);
        }
        catch (Exception exception) when (exception is JsonException or NotSupportedException or ArgumentException or InvalidOperationException)
        {
            return false;
        }

        if (envelope is null || envelope.Version != FormatVersion || envelope.Value is null)
        {
            return false;
        }

        value = envelope.Value;
        expiresAtUtc = envelope.ExpiresAtUtc;
        return true;
    }

    private sealed record Envelope<T>(
        [property: JsonPropertyName("v")] int Version,
        [property: JsonPropertyName("expiresAtUtc")] DateTimeOffset ExpiresAtUtc,
        [property: JsonPropertyName("value")] T Value);
}
