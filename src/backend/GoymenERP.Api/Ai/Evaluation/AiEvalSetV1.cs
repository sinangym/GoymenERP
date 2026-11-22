using System.Text.Json;

namespace GoymenERP.Api.Ai.Evaluation;

/// <summary>Expected behaviour of an evaluation item (<c>contracts/ai/eval/eval-set-v1.schema.json</c>).</summary>
public enum AiEvalExpectedBehaviorV1
{
    /// <summary><c>kaynakli_cevap</c>: answer from the returned decision and cite it.</summary>
    SourcedAnswer = 1,

    /// <summary><c>guncel_karari_soyle</c>: say which decision updates the asked one.</summary>
    CurrentDecision = 2,

    /// <summary><c>cevap_verme_kaynak_yok</c>: know that there is no settled answer (no source, or an open decision).</summary>
    Abstain = 3,

    /// <summary><c>reddet</c>: refuse (outside authority, invalid request, failed owner read).</summary>
    Refuse = 4,

    /// <summary><c>arac_sonucu</c>: restate the tool result exactly.</summary>
    ToolResult = 5,
}

/// <summary>The tool call an item expects; also the hint an oracle (Fake) planner may receive.</summary>
public sealed record AiEvalExpectedToolCallV1(string ToolId, string Version, string ArgumentsJson);

/// <summary>Recorded owner result of a pending-shipment scenario (verified against the tool in Logistics unit tests).</summary>
public sealed record AiEvalExpectedToolResultV1(
    string Outcome,
    int? PendingCount,
    int? DraftCount,
    int? IssuedCount,
    int? FullyShippedExcludedCount,
    int? ListedCount,
    bool? Complete);

public sealed record AiEvalItemV1(
    string Id,
    string Category,
    string Question,
    string ExpectedAnswerSummary,
    IReadOnlyList<string> ExpectedSourceIds,
    AiEvalExpectedBehaviorV1 ExpectedBehavior,
    AiEvalExpectedToolCallV1? ExpectedToolCall,
    string? FixturePermission,
    AiEvalExpectedToolResultV1? ExpectedToolResult);

/// <summary>
/// WP-A2: strict reader of the SENTETIK evaluation set. It accepts only the v1 format and the SENTETIK marker; anything
/// else is refused (the Python evaluator <c>tools/evaluate_ai_eval_set.py</c> stays the schema gate in CI).
/// </summary>
public sealed record AiEvalSetV1(string Format, IReadOnlyList<AiEvalItemV1> Items)
{
    public const string FormatV1 = "goymen.ai.eval-set.v1";
    public const string RelativePath = "contracts/ai/eval/eval-set-v1.json";
    public const string SyntheticMarker = "SENTETIK";

    public const string CategoryInformation = "bilgi_cikarma";
    public const string CategoryUpdatedDecision = "guncellenen_karar";
    public const string CategoryCannotAnswer = "cevap_verememe";
    public const string CategoryOutOfAuthority = "yetki_disi";
    public const string CategoryOperation = "islem_sorgusu";

    public const string PermissionGranted = "verildi";
    public const string PermissionMissing = "yok";
    public const string PermissionServiceFailure = "servis_hatasi";

    public static AiEvalSetV1 Parse(string json)
    {
        ArgumentNullException.ThrowIfNull(json);
        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;
        if (root.ValueKind != JsonValueKind.Object || Text(root, "format") != FormatV1)
            throw new InvalidDataException("Unknown evaluation set format.");

        var items = new List<AiEvalItemV1>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var element in root.GetProperty("items").EnumerateArray())
        {
            if (Text(element, "data_marker") != SyntheticMarker)
                throw new InvalidDataException("Every evaluation item must be SENTETIK.");
            var item = ReadItem(element);
            if (!seen.Add(item.Id)) throw new InvalidDataException($"Duplicate evaluation item {item.Id}.");
            items.Add(item);
        }

        return new AiEvalSetV1(FormatV1, items);
    }

    public static AiEvalSetV1 Load(string path) => Parse(File.ReadAllText(path));

    private static AiEvalItemV1 ReadItem(JsonElement element)
    {
        AiEvalExpectedToolCallV1? call = null;
        if (element.TryGetProperty("expected_tool_call", out var callElement))
        {
            call = new AiEvalExpectedToolCallV1(Text(callElement, "toolId"), Text(callElement, "version"),
                callElement.GetProperty("arguments").GetRawText());
        }

        string? permission = null;
        if (element.TryGetProperty("fixture", out var fixture)) permission = Text(fixture, "permission");

        AiEvalExpectedToolResultV1? result = null;
        if (element.TryGetProperty("expected_tool_result", out var resultElement))
        {
            result = new AiEvalExpectedToolResultV1(Text(resultElement, "outcome"),
                OptionalInt(resultElement, "pendingCount"), OptionalInt(resultElement, "draftCount"),
                OptionalInt(resultElement, "issuedCount"), OptionalInt(resultElement, "fullyShippedExcludedCount"),
                OptionalInt(resultElement, "listedCount"),
                resultElement.TryGetProperty("complete", out var complete) ? complete.GetBoolean() : null);
        }

        return new AiEvalItemV1(
            Text(element, "id"),
            Text(element, "category"),
            Text(element, "question"),
            Text(element, "expected_answer_summary"),
            [.. element.GetProperty("expected_source_ids").EnumerateArray().Select(id => id.GetString()!)],
            Behavior(Text(element, "expected_behavior")),
            call,
            permission,
            result);
    }

    private static AiEvalExpectedBehaviorV1 Behavior(string value) => value switch
    {
        "kaynakli_cevap" => AiEvalExpectedBehaviorV1.SourcedAnswer,
        "guncel_karari_soyle" => AiEvalExpectedBehaviorV1.CurrentDecision,
        "cevap_verme_kaynak_yok" => AiEvalExpectedBehaviorV1.Abstain,
        "reddet" => AiEvalExpectedBehaviorV1.Refuse,
        "arac_sonucu" => AiEvalExpectedBehaviorV1.ToolResult,
        _ => throw new InvalidDataException($"Unknown expected behaviour {value}."),
    };

    private static string Text(JsonElement element, string name) =>
        element.GetProperty(name).GetString() is { Length: > 0 } value ? value : throw new InvalidDataException($"Missing {name}.");

    private static int? OptionalInt(JsonElement element, string name) =>
        element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.Number ? value.GetInt32() : null;
}
