using System.Security.Cryptography;
using System.Text.Json;
using GoymenERP.BuildingBlocks.Ai;

namespace GoymenERP.Api.Ai.Knowledge;

/// <summary>
/// WP-A4 third knowledge source (DALGA_PLANI_v2 A.4 (3)): usage notes of the software as published
/// (<c>contracts/ai/release-knowledge-v1.json</c>, produced by <c>tools/generate_ai_release_knowledge.py</c> from
/// <c>docs/release-knowledge</c> and packaged into the API output). Only notes that are released AND whose evidence exists in
/// the same tree carry text; an unimplemented feature appears only as an excluded identity, never as text. Names are masked at
/// generation (plan D2); patterns are masked once more here.
/// </summary>
public sealed class AiReleaseKnowledge
{
    public const string Format = "goymen.ai.release-knowledge.v1";

    private readonly Dictionary<string, AiReleaseNote> byId;

    public AiReleaseKnowledge(string sourceSetSha256, string packageSha256, int excludedCount, IReadOnlyList<AiReleaseNote> notes)
    {
        SourceSetSha256 = sourceSetSha256;
        PackageSha256 = packageSha256;
        ExcludedCount = excludedCount;
        Notes = [.. notes.Select(note =>
        {
            var title = AiDecisionIndex.MaskText(note.Title);
            var text = AiDecisionIndex.MaskText(note.Text);
            return title == note.Title && text == note.Text ? note : note with { Title = title, Text = text, Masked = true };
        })];
        byId = new Dictionary<string, AiReleaseNote>(StringComparer.Ordinal);
        foreach (var note in Notes)
        {
            if (!byId.TryAdd(note.Id, note))
            {
                throw new InvalidDataException("Duplicate release note identity.");
            }
        }
    }

    public string SourceSetSha256 { get; }

    /// <summary>The SHA-256 of the packaged bytes: the version an approval binds to.</summary>
    public string PackageSha256 { get; }

    public int ExcludedCount { get; }

    public IReadOnlyList<AiReleaseNote> Notes { get; }

    public AiReleaseNote? Find(string id) => byId.GetValueOrDefault(id);

    public IReadOnlyList<AiReleaseNote> Search(string keyword)
    {
        var needle = AiDecisionIndex.Fold(keyword.Trim());
        return [.. Notes.Where(note =>
            AiDecisionIndex.Fold(note.Id).Contains(needle, StringComparison.Ordinal)
            || AiDecisionIndex.Fold(note.Title).Contains(needle, StringComparison.Ordinal)
            || AiDecisionIndex.Fold(note.Text).Contains(needle, StringComparison.Ordinal))];
    }

    public static AiReleaseKnowledge Parse(byte[] bytes)
    {
        ArgumentNullException.ThrowIfNull(bytes);
        using var document = JsonDocument.Parse(bytes);
        var root = document.RootElement;
        if (root.GetProperty("format").GetString() != Format
            || root.GetProperty("mask_policy").GetString() != AiDecisionIndex.MaskPolicy)
        {
            throw new InvalidDataException("Unknown release knowledge format or mask policy.");
        }

        var notes = root.GetProperty("notes").EnumerateArray().Select(ReadNote).ToArray();
        return new AiReleaseKnowledge(Required(root, "source_set_sha256"), Convert.ToHexStringLower(SHA256.HashData(bytes)),
            root.GetProperty("excluded").GetArrayLength(), notes);
    }

    public static AiReleaseKnowledge? TryLoad(string? path)
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

    private static AiReleaseNote ReadNote(JsonElement element)
    {
        var source = element.GetProperty("source");
        var cloudDataClass = Required(element, "cloud_data_class") switch
        {
            "internal" => AiDataClassV1.Internal,
            "confidential" => AiDataClassV1.Confidential,
            _ => throw new InvalidDataException("Unknown cloud data class."),
        };
        return new AiReleaseNote(
            Required(element, "id"),
            Required(element, "title"),
            Required(element, "text"),
            [.. element.GetProperty("required_permissions").EnumerateArray().Select(value =>
                value.GetString() is { Length: > 0 } name ? name : throw new InvalidDataException("Empty permission."))],
            Required(source, "path"),
            source.GetProperty("line").GetInt32(),
            element.GetProperty("masked").GetBoolean(),
            Required(element, "cloud_eligibility"),
            cloudDataClass);
    }

    private static string Required(JsonElement element, string name) =>
        element.GetProperty(name).GetString() is { Length: > 0 } value ? value : throw new InvalidDataException($"Missing {name}.");
}

public sealed record AiReleaseNote(
    string Id,
    string Title,
    string Text,
    IReadOnlyList<string> RequiredPermissions,
    string SourcePath,
    int SourceLine,
    bool Masked,
    string CloudEligibility,
    AiDataClassV1 CloudDataClass);
