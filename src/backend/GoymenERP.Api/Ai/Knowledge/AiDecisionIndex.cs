using System.Text.Json;
using System.Text.RegularExpressions;

namespace GoymenERP.Api.Ai.Knowledge;

/// <summary>
/// The decision index (<c>contracts/ai/decision-index-v1.json</c>, produced by <c>tools/generate_ai_decision_index.py</c>
/// from <c>docs/decisions</c> only). It is governance text, never company records. A missing, unreadable or malformed
/// file yields no index, and the lookup tool then answers unavailable (fail-closed).
/// </summary>
public sealed partial class AiDecisionIndex
{
    public const string Format = "goymen.ai.decision-index.v1";
    public const string PersonalDataCandidate = "kisisel_veri_adayi";
    public const string PersonMask = "[KİŞİ]";
    public const string SecretMask = "[GİZLİ]";

    private readonly Dictionary<string, AiDecisionRecord> byId;

    public AiDecisionIndex(string sourceSetSha256, IReadOnlyList<AiDecisionRecord> records, IReadOnlyList<string> personalNameTerms)
    {
        SourceSetSha256 = sourceSetSha256;
        PersonalNameTerms = [.. personalNameTerms.OrderByDescending(term => term.Length).ThenBy(term => term, StringComparer.Ordinal)];
        Records = [.. records.Select(Mask)];
        byId = new Dictionary<string, AiDecisionRecord>(StringComparer.Ordinal);
        foreach (var record in Records)
        {
            // The generator never emits an identity twice; a repeated identity means a tampered index.
            if (!byId.TryAdd(record.Id, record))
            {
                throw new InvalidDataException("Duplicate decision identity.");
            }
        }
    }

    public string SourceSetSha256 { get; }
    public IReadOnlyList<string> PersonalNameTerms { get; }

    /// <summary>Records in index order, already masked for the personal data candidates (AI-D34 d).</summary>
    public IReadOnlyList<AiDecisionRecord> Records { get; }

    public AiDecisionRecord? Find(string id) => byId.GetValueOrDefault(id);

    /// <summary>Case-insensitive (Turkish folding) substring search over identity, title and masked text, in index order.</summary>
    public IReadOnlyList<AiDecisionRecord> Search(string keyword)
    {
        var needle = Fold(keyword.Trim());
        return [.. Records.Where(record =>
            Fold(record.Id).Contains(needle, StringComparison.Ordinal)
            || Fold(record.Title).Contains(needle, StringComparison.Ordinal)
            || Fold(record.Text).Contains(needle, StringComparison.Ordinal))];
    }

    /// <summary>
    /// Case folding without a culture (the host may run with invariant globalization): the four Turkish i letters fold to
    /// "i", so "AI-D12", "ai-d12" and "İskonto"/"iskonto"/"ıskonto" match alike.
    /// </summary>
    public static string Fold(string value) =>
        value.Replace('İ', 'i').Replace('I', 'i').Replace('ı', 'i').ToLowerInvariant();

    public static AiDecisionIndex Parse(Stream stream)
    {
        using var document = JsonDocument.Parse(stream);
        var root = document.RootElement;
        if (root.GetProperty("format").GetString() != Format)
        {
            throw new InvalidDataException("Unknown decision index format.");
        }

        var terms = root.GetProperty("personal_name_terms").EnumerateArray().Select(term => term.GetString()!).ToArray();
        var records = root.GetProperty("records").EnumerateArray().Select(ReadRecord).ToArray();
        return new AiDecisionIndex(Required(root, "source_set_sha256"), records, terms);
    }

    /// <summary>The index at <paramref name="path"/>, or null when it is absent or not a valid v1 index.</summary>
    public static AiDecisionIndex? TryLoad(string? path)
    {
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
        {
            return null;
        }

        try
        {
            using var stream = File.OpenRead(path);
            return Parse(stream);
        }
        catch (Exception exception) when (exception is JsonException or InvalidDataException or KeyNotFoundException
                                              or InvalidOperationException or IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    private static AiDecisionRecord ReadRecord(JsonElement element)
    {
        var source = element.GetProperty("source");
        var scope = element.GetProperty("scope");
        return new AiDecisionRecord(
            Required(element, "id"),
            Required(element, "kind"),
            Required(element, "title"),
            Required(element, "text"),
            Required(element, "status"),
            element.GetProperty("effective_date").GetString(),
            Required(source, "path"),
            source.GetProperty("line").ValueKind == JsonValueKind.Number ? (int?)source.GetProperty("line").GetInt32() : null,
            scope.GetProperty("area").GetString(),
            scope.GetProperty("affected").GetString(),
            [.. element.GetProperty("superseded_by").EnumerateArray().Select(link => new AiDecisionLink(
                Required(link, "by"), Required(link, "type"), Required(link, "confidence"), Required(link, "extent")))],
            Required(element, "data_class_candidate"),
            Masked: false);
    }

    private static string Required(JsonElement element, string name) =>
        element.GetProperty(name).GetString() is { Length: > 0 } value ? value : throw new InvalidDataException($"Missing {name}.");

    private AiDecisionRecord Mask(AiDecisionRecord record)
    {
        if (!string.Equals(record.DataClassCandidate, PersonalDataCandidate, StringComparison.Ordinal))
        {
            return record;
        }

        return record with { Title = MaskText(record.Title), Text = MaskText(record.Text), Masked = true };
    }

    private string MaskText(string text)
    {
        var masked = text;
        foreach (var term in PersonalNameTerms)
        {
            masked = Regex.Replace(masked,
                $"(?<![\\p{{L}}\\p{{N}}]){Regex.Escape(term)}(?![\\p{{L}}])", PersonMask, RegexOptions.CultureInvariant);
        }

        masked = EmailPattern().Replace(masked, SecretMask);
        masked = IbanPattern().Replace(masked, SecretMask);
        masked = PhonePattern().Replace(masked, SecretMask);
        return NationalIdPattern().Replace(masked, SecretMask);
    }

    [GeneratedRegex(@"[A-Za-z0-9._%+-]+@[A-Za-z0-9.-]+\.[A-Za-z]{2,}", RegexOptions.CultureInvariant)]
    private static partial Regex EmailPattern();

    [GeneratedRegex(@"(?<![0-9])(?:\+90|0)\s?5[0-9]{2}\s?[0-9]{3}\s?[0-9]{2}\s?[0-9]{2}(?![0-9])", RegexOptions.CultureInvariant)]
    private static partial Regex PhonePattern();

    [GeneratedRegex(@"(?<![0-9])[1-9][0-9]{10}(?![0-9])", RegexOptions.CultureInvariant)]
    private static partial Regex NationalIdPattern();

    [GeneratedRegex(@"\bTR[0-9]{2}(?:\s?[0-9A-Z]{4}){5}\s?[0-9A-Z]{2}\b", RegexOptions.CultureInvariant)]
    private static partial Regex IbanPattern();
}

public sealed record AiDecisionLink(string By, string Type, string Confidence, string Extent);

public sealed record AiDecisionRecord(
    string Id,
    string Kind,
    string Title,
    string Text,
    string Status,
    string? EffectiveDate,
    string SourcePath,
    int? SourceLine,
    string? ScopeArea,
    string? ScopeAffected,
    IReadOnlyList<AiDecisionLink> SupersededBy,
    string DataClassCandidate,
    bool Masked);
