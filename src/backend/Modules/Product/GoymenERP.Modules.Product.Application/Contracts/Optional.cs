using System.Text.Json;
using System.Text.Json.Serialization;

namespace GoymenERP.Modules.Product.Application.Contracts;

[JsonConverter(typeof(OptionalJsonConverterFactory))]
public readonly record struct Optional<T>
{
    public Optional(T value) { Value = value; IsSpecified = true; }
    public T? Value { get; }
    public bool IsSpecified { get; }
}

public sealed class OptionalJsonConverterFactory : JsonConverterFactory
{
    public override bool CanConvert(Type typeToConvert) => typeToConvert.IsGenericType
        && typeToConvert.GetGenericTypeDefinition() == typeof(Optional<>);

    public override JsonConverter CreateConverter(Type typeToConvert, JsonSerializerOptions options) =>
        (JsonConverter)Activator.CreateInstance(typeof(OptionalJsonConverter<>)
            .MakeGenericType(typeToConvert.GetGenericArguments()[0]))!;

    private sealed class OptionalJsonConverter<T> : JsonConverter<Optional<T>>
    {
        public override Optional<T> Read(ref Utf8JsonReader reader, Type typeToConvert,
            JsonSerializerOptions options) => new(JsonSerializer.Deserialize<T>(ref reader, options)!);

        public override void Write(Utf8JsonWriter writer, Optional<T> value,
            JsonSerializerOptions options) => JsonSerializer.Serialize(writer, value.Value, options);
    }
}
