using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace GoymenERP.BuildingBlocks.Ai;

/// <summary>Pure representation only; neither schema admission nor authorization.</summary>
public static class AiCanonicalJson
{
    private static readonly JsonSerializerOptions Web = new(JsonSerializerDefaults.Web);

    public static string Canonicalize(string json)
    {
        ArgumentNullException.ThrowIfNull(json);
        using var document = JsonDocument.Parse(json, new JsonDocumentOptions
        {
            MaxDepth = 32, AllowTrailingCommas = false, CommentHandling = JsonCommentHandling.Disallow,
        });
        using var buffer = new MemoryStream();
        using (var writer = new Utf8JsonWriter(buffer)) Write(writer, document.RootElement);
        return Encoding.UTF8.GetString(buffer.ToArray());
    }

    /// <summary>The full value snapshot is the approval basis; its digest is only an index/check.</summary>
    public static string PreviewIdentity(AiServerContextV1 context, AiToolDescriptorV1 tool, AiPreviewV1 preview)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(tool);
        ArgumentNullException.ThrowIfNull(preview);
        context.RequireBound();
        // JSON strings frame arbitrary text unambiguously. Never join free fields with delimiters.
        return Canonicalize(JsonSerializer.Serialize(new
        {
            format = "goymen.ai.preview.v1", context, tool, preview,
        }, Web));
    }

    public static string PreviewHash(AiServerContextV1 context, AiToolDescriptorV1 tool, AiPreviewV1 preview) =>
        Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(PreviewIdentity(context, tool, preview))));

    private static void Write(Utf8JsonWriter writer, JsonElement value)
    {
        switch (value.ValueKind)
        {
            case JsonValueKind.Object:
                var members = value.EnumerateObject().ToArray();
                var names = new HashSet<string>(StringComparer.Ordinal);
                foreach (var member in members)
                    if (!names.Add(member.Name)) throw new JsonException("Duplicate JSON property.");
                writer.WriteStartObject();
                foreach (var member in members.OrderBy(member => member.Name, StringComparer.Ordinal))
                {
                    writer.WritePropertyName(member.Name);
                    Write(writer, member.Value);
                }
                writer.WriteEndObject();
                break;
            case JsonValueKind.Array:
                writer.WriteStartArray();
                foreach (var item in value.EnumerateArray()) Write(writer, item);
                writer.WriteEndArray();
                break;
            case JsonValueKind.String: writer.WriteStringValue(value.GetString()); break;
            // Keep the validated numeric token: 1, 1.0 and 1e0 are not repriced or rounded.
            case JsonValueKind.Number: writer.WriteRawValue(value.GetRawText()); break;
            case JsonValueKind.True: writer.WriteBooleanValue(true); break;
            case JsonValueKind.False: writer.WriteBooleanValue(false); break;
            case JsonValueKind.Null: writer.WriteNullValue(); break;
            default: throw new JsonException("Unsupported JSON value.");
        }
    }
}
