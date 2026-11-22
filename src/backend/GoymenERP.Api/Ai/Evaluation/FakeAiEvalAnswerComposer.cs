using System.Globalization;
using System.Text;
using System.Text.Json;
using GoymenERP.Api.Ai.Gateway;
using GoymenERP.BuildingBlocks.Ai;

namespace GoymenERP.Api.Ai.Evaluation;

/// <summary>
/// WP-A2 deterministic in-process composer for the evaluation harness (Fake provider kind; never reaches the network,
/// never admitted in Production by the runtime). Unlike <see cref="FakeAiAnswerComposer"/> it restates the shapes of the
/// evaluated tools, so the harness can be checked end to end through the gateway: a decision lookup result is restated
/// record by record (identity, title, status, text, updating decisions), a pending-shipment result by its counts (or as
/// incomplete when the counts are withheld). It copies values only — it never adds, rounds or invents a number — and cites
/// every source. One usage unit per started 4 characters of output.
/// </summary>
public sealed class FakeAiEvalAnswerComposer(AiModelPlacementV1 placement) : IAiAnswerComposerV1
{
    private const int CharactersPerUsageUnit = 4;

    public string ProviderKind => AiProviderKindsV1.Fake;

    public AiModelPlacementV1 Placement { get; } = placement;

    public Task<AiComposedTextV1> ComposeAsync(AiComposeRequestV1 request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        cancellationToken.ThrowIfCancellationRequested();
        var text = new StringBuilder();
        foreach (var source in request.Sources)
        {
            if (text.Length > 0) text.Append(' ');
            text.Append(CultureInfo.InvariantCulture, $"[{source.Number}] {source.Title}:");
            using var document = JsonDocument.Parse(source.ContentJson);
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object) continue;
            if (root.TryGetProperty("records", out var records) && records.ValueKind == JsonValueKind.Array)
                AppendDecisions(text, records);
            else if (root.TryGetProperty("pendingCount", out var pending))
                AppendShipments(text, root, pending);
            else
                AppendScalars(text, root);
        }

        var composed = text.ToString();
        var units = Math.Max(1, (composed.Length + CharactersPerUsageUnit - 1) / CharactersPerUsageUnit);
        return Task.FromResult(new AiComposedTextV1(composed, request.Sources.Select(source => source.Number).ToArray(), units));
    }

    private static void AppendDecisions(StringBuilder text, JsonElement records)
    {
        foreach (var record in records.EnumerateArray())
        {
            text.Append(CultureInfo.InvariantCulture,
                $" {Read(record, "id")} — {Read(record, "title")}. Durum: {StatusPhrase(Read(record, "status"))}. {Read(record, "text")}");
            if (record.TryGetProperty("supersededBy", out var links) && links.ValueKind == JsonValueKind.Array
                && links.GetArrayLength() > 0)
            {
                var by = links.EnumerateArray().Select(link => Read(link, "by")).Where(value => value.Length > 0);
                text.Append(CultureInfo.InvariantCulture, $" Güncelleyen karar: {string.Join(", ", by)}.");
            }
        }
    }

    private static void AppendShipments(StringBuilder text, JsonElement root, JsonElement pending)
    {
        if (pending.ValueKind != JsonValueKind.Number)
        {
            text.Append(" Sonuç eksik; kesin sayı verilmez.");
            return;
        }

        text.Append(CultureInfo.InvariantCulture, $" Bekleyen: {pending.GetRawText()}");
        foreach (var (name, label) in new[] { ("draftCount", "taslak"), ("issuedCount", "yayımlanmış"), ("listedCount", "listelenen") })
        {
            if (root.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.Number)
                text.Append(CultureInfo.InvariantCulture, $"; {label}: {value.GetRawText()}");
        }

        text.Append('.');
    }

    private static void AppendScalars(StringBuilder text, JsonElement root)
    {
        foreach (var property in root.EnumerateObject())
        {
            if (property.Value.ValueKind is JsonValueKind.String or JsonValueKind.Number or JsonValueKind.True or JsonValueKind.False)
                text.Append(CultureInfo.InvariantCulture,
                    $" {property.Name}: {(property.Value.ValueKind == JsonValueKind.String ? property.Value.GetString() : property.Value.GetRawText())};");
        }
    }

    private static string StatusPhrase(string status) => status switch
    {
        "acik" => "açık karar, karara bağlanmamış; kesin değer yok",
        "belirsiz" => "belirsiz",
        "gecici" => "geçici",
        "gecersiz" => "geçersiz",
        "kilitli" => "kilitli",
        _ => status,
    };

    private static string Read(JsonElement element, string name) =>
        element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString()! : string.Empty;
}
