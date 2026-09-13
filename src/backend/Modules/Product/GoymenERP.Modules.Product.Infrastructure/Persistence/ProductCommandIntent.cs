using System.Buffers;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using GoymenERP.Modules.Product.Application;
using GoymenERP.Modules.Product.Application.Contracts;

namespace GoymenERP.Modules.Product.Infrastructure.Persistence;

/// <summary>
/// Frozen, canonical command input for the global replay resolver and atomic writer.
/// Hashing is neither authorization nor reservation. The raw key is never retained.
/// </summary>
public sealed record ProductCommandIntent
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    private ProductCommandIntent(string key, JsonElement request)
    {
        if (string.IsNullOrEmpty(key) || key.Length > 128
            || key.Any(character => character < 33 || character > 126 || character == ','))
        {
            throw new ProductValidationException([new("idempotencyKey", "invalid_metadata")]);
        }

        var buffer = new ArrayBufferWriter<byte>();
        using (var writer = new Utf8JsonWriter(buffer)) { WriteCanonical(writer, request); }
        CanonicalRequestJson = Encoding.UTF8.GetString(buffer.WrittenSpan);
        KeyHash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(key)));
        RequestHash = Convert.ToHexString(SHA256.HashData(buffer.WrittenSpan));
    }

    public string KeyHash { get; }
    public string RequestHash { get; }
    // Contains user input. Keep inside the command boundary; never log the object.
    public string CanonicalRequestJson { get; }

    public static ProductCommandIntent CreateDraft(string key, CreateProductDraftRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        var normalized = request with { Name = NormalizeName(request.Name) };
        return new(key, JsonSerializer.SerializeToElement(new
        {
            operation = "product.draft.create.v1",
            request = normalized,
        }, Json));
    }

    public static ProductCommandIntent EditDraft(string key, Guid productId, EditProductDraftRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (productId == Guid.Empty)
            throw new ProductValidationException([new("productId", "invalid_reference")]);
        if (request.Changes is null)
            throw new ProductValidationException([new("changes", "required")]);

        // Preserve Optional.IsSpecified: omission and explicit null are different
        // write intents. A nullable role/reference clear must not alias a no-op.
        var changes = request.Changes;
        if (changes.Name.IsSpecified)
            changes = changes with { Name = new Optional<string?>(NormalizeName(changes.Name.Value!)) };
        try { _ = ProductVersion.Parse(request.ExpectedVersion); }
        catch (ArgumentException)
        {
            throw new ProductValidationException([new("expectedVersion", "invalid_version")]);
        }
        var normalized = request with
        {
            Changes = changes,
            Reason = ProductDraftValidator.NormalizeReason(request.Reason),
        };
        return new(key, JsonSerializer.SerializeToElement(new
        {
            operation = "product.draft.basic-edit.v1",
            productId,
            request = normalized,
        }, Json));
    }

    private static string NormalizeName(string value)
    {
        try { return Domain.Products.Product.NormalizeName(value); }
        catch (ArgumentException)
        {
            throw new ProductValidationException([new("name", "invalid_name")]);
        }
    }

    private static void WriteCanonical(Utf8JsonWriter writer, JsonElement value)
    {
        switch (value.ValueKind)
        {
            case JsonValueKind.Object:
                writer.WriteStartObject();
                var properties = value.EnumerateObject().OrderBy(property => property.Name, StringComparer.Ordinal).ToArray();
                if (properties.Select(property => property.Name).Distinct(StringComparer.Ordinal).Count() != properties.Length)
                    throw new ProductValidationException([new("references.attributes", "invalid_metadata")]);
                foreach (var property in properties)
                {
                    writer.WritePropertyName(property.Name);
                    WriteCanonical(writer, property.Value);
                }
                writer.WriteEndObject();
                break;
            case JsonValueKind.Array:
                writer.WriteStartArray();
                foreach (var item in value.EnumerateArray()) WriteCanonical(writer, item);
                writer.WriteEndArray();
                break;
            default:
                // Keep number lexemes and array order. Do not round a template
                // attribute through double or invent numeric business equivalence.
                value.WriteTo(writer);
                break;
        }
    }

    public override string ToString() => nameof(ProductCommandIntent);
}
