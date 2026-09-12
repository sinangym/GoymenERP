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
    public override bool CanConvert(Type typeToConvert) =>
        typeToConvert == typeof(Optional<string>) || typeToConvert == typeof(Optional<Guid?>)
        || typeToConvert == typeof(Optional<ProductRoleInputs>)
        || typeToConvert == typeof(Optional<ProductReferenceChanges>)
        || typeToConvert == typeof(Optional<IReadOnlyDictionary<string, JsonElement>>)
        || typeToConvert == typeof(Optional<ProductUnitChanges>);

    public override JsonConverter CreateConverter(Type typeToConvert, JsonSerializerOptions options)
    {
        if (typeToConvert == typeof(Optional<string>)) { return new OptionalJsonConverter<string>(); }
        if (typeToConvert == typeof(Optional<Guid?>)) { return new OptionalJsonConverter<Guid?>(); }
        if (typeToConvert == typeof(Optional<ProductRoleInputs>)) { return new OptionalJsonConverter<ProductRoleInputs>(); }
        if (typeToConvert == typeof(Optional<ProductReferenceChanges>)) { return new OptionalJsonConverter<ProductReferenceChanges>(); }
        if (typeToConvert == typeof(Optional<IReadOnlyDictionary<string, JsonElement>>))
        {
            return new OptionalJsonConverter<IReadOnlyDictionary<string, JsonElement>>();
        }
        if (typeToConvert == typeof(Optional<ProductUnitChanges>)) { return new OptionalJsonConverter<ProductUnitChanges>(); }
        throw new NotSupportedException("Desteklenmeyen ürün değişiklik alanı türü.");
    }

    private sealed class OptionalJsonConverter<T> : JsonConverter<Optional<T>>
    {
        public override Optional<T> Read(ref Utf8JsonReader reader, Type typeToConvert,
            JsonSerializerOptions options) => new(JsonSerializer.Deserialize<T>(ref reader, options)!);

        public override void Write(Utf8JsonWriter writer, Optional<T> value,
            JsonSerializerOptions options) => JsonSerializer.Serialize(writer, value.Value, options);
    }
}
