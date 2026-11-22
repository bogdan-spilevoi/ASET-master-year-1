using System.Text.Json;
using System.Text.Json.Serialization;

namespace SmartLost.BuildingBlocks.Infrastructure.Caching;

/// <summary>Restores <see cref="IReadOnlyList{T}"/> members as read-only snapshots instead of mutable lists.</summary>
public sealed class ReadOnlyListJsonConverterFactory : JsonConverterFactory
{
    public override bool CanConvert(Type typeToConvert)
    {
        ArgumentNullException.ThrowIfNull(typeToConvert);
        return typeToConvert.IsGenericType && typeToConvert.GetGenericTypeDefinition() == typeof(IReadOnlyList<>);
    }

    public override JsonConverter CreateConverter(Type typeToConvert, JsonSerializerOptions options)
    {
        ArgumentNullException.ThrowIfNull(typeToConvert);
        Type item = typeToConvert.GetGenericArguments()[0];
        return (JsonConverter)Activator.CreateInstance(typeof(ReadOnlyListConverter<>).MakeGenericType(item))!;
    }

    private sealed class ReadOnlyListConverter<T> : JsonConverter<IReadOnlyList<T>>
    {
        public override IReadOnlyList<T>? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        {
            T[]? items = JsonSerializer.Deserialize<T[]>(ref reader, options);
            return items is null ? null : Array.AsReadOnly(items);
        }

        public override void Write(Utf8JsonWriter writer, IReadOnlyList<T> value, JsonSerializerOptions options)
        {
            writer.WriteStartArray();
            foreach (T item in value)
            {
                JsonSerializer.Serialize(writer, item, options);
            }

            writer.WriteEndArray();
        }
    }
}
