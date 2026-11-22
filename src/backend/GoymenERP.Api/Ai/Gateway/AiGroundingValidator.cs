using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace GoymenERP.Api.Ai.Gateway;

/// <summary>
/// WP-A1 "numbers only from tools": deterministic check of untrusted composed text against the sources it cites. The text
/// must be non-empty, cite at least one existing source, cite each source once, and every number written in it (after the
/// <c>[n]</c> citation markers are removed) must appear verbatim as a number in a cited source — its content, title, version
/// or as-of date. A number the sources do not contain (a total, a difference, a rounded or reformatted value) fails the
/// check and the gateway answers "cannot answer". The model never adds, rounds or converts (data class §5a; calculators
/// stay in the backend).
/// </summary>
public static partial class AiGroundingValidator
{
    public static bool IsSupported(AiComposedTextV1? composed, IReadOnlyList<AiComposeSourceV1> sources)
    {
        ArgumentNullException.ThrowIfNull(sources);
        if (composed?.Text is not { } text || string.IsNullOrWhiteSpace(text) || composed.CitedSources is not { Count: > 0 } cited)
            return false;

        if (cited.Distinct().Count() != cited.Count || cited.Any(number => number < 1 || number > sources.Count))
            return false;

        var allowed = new HashSet<string>(StringComparer.Ordinal);
        foreach (var source in cited.Select(number => sources[number - 1]))
        {
            AddNumbers(allowed, source.ContentJson);
            AddJsonStringNumbers(allowed, source.ContentJson);
            AddNumbers(allowed, source.Title);
            AddNumbers(allowed, source.SourceVersion);
            AddNumbers(allowed, source.AsOfUtc.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture));
        }

        var body = CitationMarker().Replace(text, " ");
        return NumberToken().Matches(body).All(match => allowed.Contains(match.Value));
    }

    /// <summary>
    /// WP-A2 finding: canonical content JSON escapes non-ASCII characters (<c>"§9.1"</c> is written <c>"\u00a79.1"</c>), so a
    /// number next to such a character is not a token of the raw text. The decoded string values (and property names) are
    /// scanned as well; this only admits numbers that are really in the source.
    /// </summary>
    private static void AddJsonStringNumbers(HashSet<string> allowed, string? contentJson)
    {
        if (string.IsNullOrEmpty(contentJson)) return;
        try
        {
            using var document = JsonDocument.Parse(contentJson);
            AddElementNumbers(allowed, document.RootElement);
        }
        catch (JsonException)
        {
            // Not JSON: the raw text scan above is all there is.
        }
    }

    private static void AddElementNumbers(HashSet<string> allowed, JsonElement element)
    {
        switch (element.ValueKind)
        {
            case JsonValueKind.String:
                AddNumbers(allowed, element.GetString());
                break;
            case JsonValueKind.Object:
                foreach (var property in element.EnumerateObject())
                {
                    AddNumbers(allowed, property.Name);
                    AddElementNumbers(allowed, property.Value);
                }

                break;
            case JsonValueKind.Array:
                foreach (var item in element.EnumerateArray()) AddElementNumbers(allowed, item);
                break;
        }
    }

    private static void AddNumbers(HashSet<string> allowed, string? value)
    {
        if (string.IsNullOrEmpty(value)) return;
        foreach (Match match in NumberToken().Matches(value)) allowed.Add(match.Value);
    }

    [GeneratedRegex(@"\d+(?:[.,]\d+)*", RegexOptions.CultureInvariant)]
    private static partial Regex NumberToken();

    [GeneratedRegex(@"\[\d+\]", RegexOptions.CultureInvariant)]
    private static partial Regex CitationMarker();
}
