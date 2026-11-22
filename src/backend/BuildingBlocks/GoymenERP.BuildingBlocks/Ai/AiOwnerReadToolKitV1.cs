using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using GoymenERP.BuildingBlocks.Security;

namespace GoymenERP.BuildingBlocks.Ai;

/// <summary>
/// WP-A3b shared pieces for owner READ tools (DALGA_PLANI_v2 A.3): a strict argument reader (closed field list, typed
/// values, nothing coerced), a company-wide authority fence measured for the SERVER-supplied company, an exact decimal
/// text sum and a deterministic source version. They decide no business rule, grant nothing and read no table; each
/// owner tool still reads only through its own module's authorized read service.
/// </summary>
public sealed class AiReadArgumentsV1
{
    public const string WholeArguments = "arguments";
    private readonly Dictionary<string, JsonElement> values;

    private AiReadArgumentsV1(Dictionary<string, JsonElement> values) => this.values = values;

    /// <summary>
    /// The arguments must be one JSON object whose every property is in <paramref name="allowedFields"/>; anything
    /// else (unknown field, array, malformed text) is refused and names the field (or <see cref="WholeArguments"/>).
    /// </summary>
    public static bool TryParse(string? json, IReadOnlyCollection<string> allowedFields, out AiReadArgumentsV1 arguments,
        out string invalidField)
    {
        ArgumentNullException.ThrowIfNull(allowedFields);
        arguments = new AiReadArgumentsV1(new Dictionary<string, JsonElement>(StringComparer.Ordinal));
        invalidField = WholeArguments;
        JsonDocument document;
        try
        {
            document = JsonDocument.Parse(json ?? string.Empty, new JsonDocumentOptions { MaxDepth = 4 });
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

            var read = new Dictionary<string, JsonElement>(StringComparer.Ordinal);
            foreach (var property in document.RootElement.EnumerateObject())
            {
                if (!allowedFields.Contains(property.Name) || read.ContainsKey(property.Name))
                {
                    invalidField = allowedFields.Contains(property.Name) ? property.Name : WholeArguments;
                    return false;
                }

                read[property.Name] = property.Value.Clone();
            }

            arguments = new AiReadArgumentsV1(read);
            return true;
        }
    }

    public bool TryInt(string name, int minimum, int maximum, int fallback, out int value)
    {
        value = fallback;
        if (!values.TryGetValue(name, out var element))
        {
            return true;
        }

        if (element.ValueKind == JsonValueKind.Number && element.TryGetInt32(out var number) && number >= minimum && number <= maximum)
        {
            value = number;
            return true;
        }

        return false;
    }

    public bool TryGuid(string name, out Guid? value)
    {
        value = null;
        if (!values.TryGetValue(name, out var element))
        {
            return true;
        }

        if (element.ValueKind == JsonValueKind.String && Guid.TryParseExact(element.GetString(), "D", out var id) && id != Guid.Empty)
        {
            value = id;
            return true;
        }

        return false;
    }

    public bool TryChoice(string name, IReadOnlyCollection<string> allowed, out string? value)
    {
        value = null;
        if (!values.TryGetValue(name, out var element))
        {
            return true;
        }

        if (element.ValueKind == JsonValueKind.String && element.GetString() is { } text && allowed.Contains(text))
        {
            value = text;
            return true;
        }

        return false;
    }

    public bool TryBool(string name, bool fallback, out bool value)
    {
        value = fallback;
        if (!values.TryGetValue(name, out var element))
        {
            return true;
        }

        if (element.ValueKind is JsonValueKind.True or JsonValueKind.False)
        {
            value = element.GetBoolean();
            return true;
        }

        return false;
    }

    /// <summary>A trimmed, control-free text of bounded length; a padded or blank value is refused, not trimmed.</summary>
    public bool TryText(string name, int minimumLength, int maximumLength, out string? value)
    {
        value = null;
        if (!values.TryGetValue(name, out var element))
        {
            return true;
        }

        if (element.ValueKind == JsonValueKind.String && element.GetString() is { } text
            && text.Length >= minimumLength && text.Length <= maximumLength
            && string.Equals(text, text.Trim(), StringComparison.Ordinal) && !text.Any(char.IsControl))
        {
            value = text;
            return true;
        }

        return false;
    }
}

public static class AiOwnerReadFenceV1
{
    /// <summary>
    /// Measures every permission NOW for the server-supplied company. Only a company-wide grant passes: a grant
    /// limited to a site, location or work center is refused (the owner reads behind these tools are company-wide
    /// lists, so a scoped reader would otherwise see places outside its scope). Null = pass; otherwise the outcome
    /// to return (Denied, or Unavailable when the measurement itself failed).
    /// </summary>
    public static async Task<AiToolOutcomeV1?> RequireCompanyWideAsync(IErpAuthorityRevalidationService authority,
        IEnumerable<string> permissions, Guid companyId, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(authority);
        ArgumentNullException.ThrowIfNull(permissions);
        if (companyId == Guid.Empty)
        {
            return AiToolOutcomeV1.Denied;
        }

        foreach (var permission in permissions)
        {
            PermissionScope? scope;
            try
            {
                scope = await authority.RevalidatePermissionScopeAsync(permission, companyId, cancellationToken);
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                return AiToolOutcomeV1.Unavailable;
            }

            if (scope is not { IsGranted: true, IsCompanyWide: true })
            {
                return AiToolOutcomeV1.Denied;
            }
        }

        return null;
    }
}

public static class AiExactDecimalV1
{
    private const NumberStyles Style = NumberStyles.AllowLeadingSign | NumberStyles.AllowDecimalPoint;

    /// <summary>Owner quantity text (invariant, no exponent, no grouping) to an exact decimal, or false.</summary>
    public static bool TryParse(string? text, out decimal value)
    {
        value = 0m;
        return !string.IsNullOrWhiteSpace(text) && text.Length <= 64
            && decimal.TryParse(text, Style, CultureInfo.InvariantCulture, out value);
    }

    /// <summary>Exact invariant text without trailing zeros ("12.500" → "12.5", "0.000" → "0").</summary>
    public static string Format(decimal value) =>
        value.ToString("0.############################", CultureInfo.InvariantCulture);

    /// <summary>Exact sum of owner texts; false when any text is not an exact decimal or the sum overflows.</summary>
    public static bool TrySum(IEnumerable<string?> texts, out decimal sum)
    {
        ArgumentNullException.ThrowIfNull(texts);
        sum = 0m;
        try
        {
            foreach (var text in texts)
            {
                if (!TryParse(text, out var value))
                {
                    return false;
                }

                sum = checked(sum + value);
            }
        }
        catch (OverflowException)
        {
            return false;
        }

        return true;
    }
}

public static class AiSourceVersionV1
{
    /// <summary>A deterministic version of what was read: sha256 over the given identity/version lines, in order.</summary>
    public static string Of(IEnumerable<string> lines)
    {
        ArgumentNullException.ThrowIfNull(lines);
        var text = string.Join('\n', lines);
        return "sha256:" + Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(text)));
    }
}
