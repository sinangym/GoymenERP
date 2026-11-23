using System.Security.Cryptography;
using System.Text.Json;
using System.Text.RegularExpressions;
using GoymenERP.BuildingBlocks.Ai;

namespace GoymenERP.Api.Ai.Knowledge;

/// <summary>
/// The decision index (<c>contracts/ai/decision-index-v1.json</c>, produced by <c>tools/generate_ai_decision_index.py</c>
/// from <c>docs/decisions</c> only and packaged into the API output, WP-A4). It is governance text, never company records.
/// Person names are masked AT GENERATION (plan D2, AI-D36): the index carries no name list, and an index without the
/// generation mask policy is refused. E-mail/phone/national id/IBAN patterns are masked once more here (defence in depth).
/// A missing, unreadable or malformed file yields no index, and the lookup tool then answers unavailable (fail-closed).
/// </summary>
public sealed partial class AiDecisionIndex
{
    public const string Format = "goymen.ai.decision-index.v1";
    public const string MaskPolicy = "ai-d36-person-name-mask-at-generation@1";
    public const string PersonalDataCandidate = "kisisel_veri_adayi";
    public const string PersonMask = "[KİŞİ]";
    public const string SecretMask = "[GİZLİ]";

    private readonly Dictionary<string, AiDecisionRecord> byId;

    public AiDecisionIndex(string sourceSetSha256, string packageSha256, int conflictCount, IReadOnlyList<AiDecisionRecord> records)
    {
        SourceSetSha256 = sourceSetSha256;
        PackageSha256 = packageSha256;
        ConflictCount = conflictCount;
        Records = [.. records.Select(MaskPatterns)];
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

    /// <summary>The digest of the decision sources the index was generated from.</summary>
    public string SourceSetSha256 { get; }

    /// <summary>The SHA-256 of the packaged index bytes: the version an approval binds to.</summary>
    public string PackageSha256 { get; }

    public int ConflictCount { get; }

    /// <summary>Records in index order, masked at generation (names) and here (patterns).</summary>
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

    public static AiDecisionIndex Parse(byte[] bytes)
    {
        ArgumentNullException.ThrowIfNull(bytes);
        using var document = JsonDocument.Parse(bytes);
        var root = document.RootElement;
        if (root.GetProperty("format").GetString() != Format)
        {
            throw new InvalidDataException("Unknown decision index format.");
        }

        // Plan D2: an index whose names were not masked at generation is never served, and it never carries names.
        if (root.GetProperty("mask_policy").GetString() != MaskPolicy || root.TryGetProperty("personal_name_terms", out _))
        {
            throw new InvalidDataException("The decision index was not masked at generation.");
        }

        var records = root.GetProperty("records").EnumerateArray().Select(ReadRecord).ToArray();
        var conflicts = root.GetProperty("conflicts").GetArrayLength();
        return new AiDecisionIndex(Required(root, "source_set_sha256"), Convert.ToHexStringLower(SHA256.HashData(bytes)), conflicts,
            records);
    }

    /// <summary>The index at <paramref name="path"/>, or null when it is absent or not a valid v1 index.</summary>
    public static AiDecisionIndex? TryLoad(string? path)
    {
        if (string.IsNullOrWhiteSpace(path) || AiKnowledgePackageFiles.TryRead(path) is not { } file)
        {
            return null;
        }

        try
        {
            return Parse(file.Bytes);
        }
        catch (Exception exception) when (exception is JsonException or InvalidDataException or KeyNotFoundException
                                              or InvalidOperationException or FormatException)
        {
            return null;
        }
    }

    private static AiDecisionRecord ReadRecord(JsonElement element)
    {
        var source = element.GetProperty("source");
        var scope = element.GetProperty("scope");
        var cloudDataClass = Required(element, "cloud_data_class") switch
        {
            "internal" => AiDataClassV1.Internal,
            "confidential" => AiDataClassV1.Confidential,
            _ => throw new InvalidDataException("Unknown cloud data class."),
        };
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
            element.GetProperty("masked").GetBoolean(),
            Required(element, "cloud_eligibility"),
            cloudDataClass);
    }

    private static string Required(JsonElement element, string name) =>
        element.GetProperty(name).GetString() is { Length: > 0 } value ? value : throw new InvalidDataException($"Missing {name}.");

    private static AiDecisionRecord MaskPatterns(AiDecisionRecord record)
    {
        var title = MaskText(record.Title);
        var text = MaskText(record.Text);
        var area = record.ScopeArea is null ? null : MaskText(record.ScopeArea);
        var affected = record.ScopeAffected is null ? null : MaskText(record.ScopeAffected);
        var changed = title != record.Title || text != record.Text || area != record.ScopeArea || affected != record.ScopeAffected;
        return changed
            ? record with { Title = title, Text = text, ScopeArea = area, ScopeAffected = affected, Masked = true }
            : record;
    }

    /// <summary>E-mail, IBAN, phone, national id → [GİZLİ] (same patterns and order as the generator).</summary>
    public static string MaskText(string text)
    {
        var masked = EmailPattern().Replace(text, SecretMask);
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
    bool Masked,
    string CloudEligibility,
    AiDataClassV1 CloudDataClass);
