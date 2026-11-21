using System.Text.Json;
using System.Text.Json.Serialization;
using SmartLost.BuildingBlocks.Core.Results;

namespace SmartLost.BuildingBlocks.Infrastructure.Caching;

/// <summary>Serializes <see cref="Result"/> and <see cref="Result{T}"/> through their public factories.</summary>
public sealed class ResultJsonConverterFactory : JsonConverterFactory
{
    public override bool CanConvert(Type typeToConvert)
    {
        ArgumentNullException.ThrowIfNull(typeToConvert);
        return typeToConvert == typeof(Result)
            || (typeToConvert.IsGenericType && typeToConvert.GetGenericTypeDefinition() == typeof(Result<>));
    }

    public override JsonConverter CreateConverter(Type typeToConvert, JsonSerializerOptions options)
    {
        ArgumentNullException.ThrowIfNull(typeToConvert);
        return typeToConvert == typeof(Result)
            ? new ResultConverter()
            : (JsonConverter)Activator.CreateInstance(typeof(ResultConverter<>).MakeGenericType(typeToConvert.GetGenericArguments()[0]))!;
    }

    private static (bool? IsSuccess, IReadOnlyList<Error>? Errors) ReadCommon<T>(ref Utf8JsonReader reader,
        JsonSerializerOptions options, out T? value, bool hasValue)
    {
        if (reader.TokenType != JsonTokenType.StartObject)
        {
            throw new JsonException("A Result must be a JSON object.");
        }

        bool? isSuccess = null;
        IReadOnlyList<Error>? errors = null;
        value = default;
        while (reader.Read() && reader.TokenType != JsonTokenType.EndObject)
        {
            string? name = reader.TokenType == JsonTokenType.PropertyName ? reader.GetString() : null;
            reader.Read();
            switch (name)
            {
                case "isSuccess":
                    isSuccess = reader.GetBoolean();
                    break;
                case "errors":
                    errors = JsonSerializer.Deserialize<Error[]>(ref reader, options);
                    break;
                case "value" when hasValue:
                    value = JsonSerializer.Deserialize<T>(ref reader, options);
                    break;
                default:
                    throw new JsonException($"Unexpected Result property '{name}'.");
            }
        }

        return (isSuccess, errors);
    }

    private static void WriteCommon(Utf8JsonWriter writer, IResult result, JsonSerializerOptions options)
    {
        writer.WriteBoolean("isSuccess", result.IsSuccess);
        if (result.IsFailure)
        {
            writer.WritePropertyName("errors");
            JsonSerializer.Serialize(writer, result.Errors, options);
        }
    }

    private sealed class ResultConverter : JsonConverter<Result>
    {
        public override Result Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        {
            (bool? isSuccess, IReadOnlyList<Error>? errors) = ReadCommon<object>(ref reader, options, out _, hasValue: false);
            return isSuccess switch
            {
                true => Result.Success(),
                false => Result.Failure(errors ?? throw new JsonException("A failed Result requires errors.")),
                null => throw new JsonException("A Result requires isSuccess.")
            };
        }

        public override void Write(Utf8JsonWriter writer, Result value, JsonSerializerOptions options)
        {
            writer.WriteStartObject();
            WriteCommon(writer, value, options);
            writer.WriteEndObject();
        }
    }

    private sealed class ResultConverter<T> : JsonConverter<Result<T>>
    {
        public override Result<T> Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        {
            (bool? isSuccess, IReadOnlyList<Error>? errors) = ReadCommon(ref reader, options, out T? value, hasValue: true);
            return isSuccess switch
            {
                true => Result<T>.Success(value ?? throw new JsonException("A successful Result requires a value.")),
                false => Result<T>.Failure(errors ?? throw new JsonException("A failed Result requires errors.")),
                null => throw new JsonException("A Result requires isSuccess.")
            };
        }

        public override void Write(Utf8JsonWriter writer, Result<T> value, JsonSerializerOptions options)
        {
            writer.WriteStartObject();
            WriteCommon(writer, value, options);
            if (value.IsSuccess)
            {
                writer.WritePropertyName("value");
                JsonSerializer.Serialize(writer, value.Value, options);
            }

            writer.WriteEndObject();
        }
    }
}
