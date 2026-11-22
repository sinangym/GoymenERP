using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace GoymenERP.BuildingBlocks.Ai;

/// <summary>
/// WP-A3a: the strict argument reader the owner READ tools share. The model's arguments are untrusted: the root must be a
/// JSON object, every property must be one the tool names, and every named reader must accept its value. Anything else —
/// an unknown property (a company id included), a wrong type, an out-of-range value, a repeated property — is refused with
/// the field name (or <see cref="InvalidArguments"/>), and the tool releases nothing. A reader never sees a property twice.
/// </summary>
public static class AiReadToolArgumentsV1
{
    public const string InvalidArguments = "arguments";

    public static bool TryRead(string? json, IReadOnlyDictionary<string, Func<JsonElement, bool>> readers, out string invalidField)
    {
        ArgumentNullException.ThrowIfNull(readers);
        invalidField = InvalidArguments;
        JsonDocument document;
        try
        {
            document = JsonDocument.Parse(json ?? string.Empty);
        }
        catch (JsonException)
        {
            return false;
        }

        using (document)
        {
            if (document.RootElement.ValueKind != JsonValueKind.Object)
            {
                return false;
            }

            var seen = new HashSet<string>(StringComparer.Ordinal);
            foreach (var property in document.RootElement.EnumerateObject())
            {
                if (!readers.TryGetValue(property.Name, out var reader))
                {
                    invalidField = InvalidArguments;
                    return false;
                }

                if (!seen.Add(property.Name) || !reader(property.Value))
                {
                    invalidField = property.Name;
                    return false;
                }
            }

            return true;
        }
    }

    /// <summary>An integer within [<paramref name="minimum"/>, <paramref name="maximum"/>].</summary>
    public static bool TryInt(JsonElement value, int minimum, int maximum, out int result)
    {
        result = 0;
        return value.ValueKind == JsonValueKind.Number && value.TryGetInt32(out result) && result >= minimum && result <= maximum;
    }

    /// <summary>One of the exact (ordinal) names of <paramref name="allowed"/>.</summary>
    public static bool TryName(JsonElement value, IReadOnlyCollection<string> allowed, out string? result)
    {
        result = value.ValueKind == JsonValueKind.String ? value.GetString() : null;
        return result is not null && allowed.Contains(result);
    }

    /// <summary>A non-empty GUID in its canonical "D" form (no braces, no other spelling).</summary>
    public static bool TryId(JsonElement value, out Guid result)
    {
        result = Guid.Empty;
        return value.ValueKind == JsonValueKind.String
               && Guid.TryParseExact(value.GetString(), "D", out result)
               && result != Guid.Empty
               && string.Equals(result.ToString("D"), value.GetString(), StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>A trimmed text of <paramref name="minimumLength"/>..<paramref name="maximumLength"/> characters without control characters.</summary>
    public static bool TryText(JsonElement value, int minimumLength, int maximumLength, out string? result)
    {
        result = null;
        if (value.ValueKind != JsonValueKind.String)
        {
            return false;
        }

        var text = value.GetString()!.Trim();
        if (text.Length < minimumLength || text.Length > maximumLength || text.Any(char.IsControl))
        {
            return false;
        }

        result = text;
        return true;
    }

    /// <summary>A stable source version: the SHA-256 of the released rows' identity/version lines, in the given order.</summary>
    public static string SourceVersion(IEnumerable<string> lines)
    {
        ArgumentNullException.ThrowIfNull(lines);
        var text = string.Join('\n', lines);
        return "sha256:" + Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(text)));
    }
}
