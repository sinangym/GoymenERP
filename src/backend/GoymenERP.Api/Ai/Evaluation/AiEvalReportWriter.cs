using System.Globalization;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace GoymenERP.Api.Ai.Evaluation;

/// <summary>
/// WP-A2 result report: a JSON document (machine-readable, stable enum names) and a Turkish Markdown page (per item
/// passed/failed with reasons, total rates per criterion and per category). No question text, no answer text and no
/// model output is written — only identities, verdicts and stable reason codes.
/// </summary>
public static class AiEvalReportWriter
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        Converters = { new JsonStringEnumConverter() },
    };

    public static string ToJson(AiEvalReportV1 report) => JsonSerializer.Serialize(report, Json);

    public static string ToJson(AiEvalComparisonV1 comparison) => JsonSerializer.Serialize(comparison, Json);

    public static string ToMarkdown(AiEvalReportV1 report)
    {
        ArgumentNullException.ThrowIfNull(report);
        var text = new StringBuilder();
        text.AppendLine(CultureInfo.InvariantCulture, $"# Yapay zekâ değerlendirme raporu — {report.Candidate}");
        text.AppendLine();
        text.AppendLine(CultureInfo.InvariantCulture,
            $"Sözleşme `{report.Contract}`, set `{report.EvalSetFormat}`, aday türü `{report.CandidateKind}`, beklenen araç ipucu: {(report.PlannedWithExpectedHint ? "evet (kâhin Fake; sağlayıcı ölçümü değildir)" : "hayır")}.");
        text.AppendLine();
        AppendTotals(text, report.Totals);
        text.AppendLine();
        text.AppendLine("## Maddeler");
        text.AppendLine();
        text.AppendLine("| Madde | Kategori | Sonuç | Neden | Ağ geçidi | Kaynak | Cevap | Cevap verememe | Red |");
        text.AppendLine("|---|---|---|---|---|---|---|---|---|");
        foreach (var item in report.Items)
        {
            text.AppendLine(CultureInfo.InvariantCulture,
                $"| {item.ItemId} | {item.Category} | {Verdict(item.Verdict)} | {(item.Reasons.Count == 0 ? "—" : string.Join(", ", item.Reasons))} | {item.GatewayStatus?.ToString() ?? "—"} | {Check(item.Checks.SourceAccurate)} | {Check(item.Checks.AnswerCorrect)} | {Check(item.Checks.AbstainedCorrectly)} | {Check(item.Checks.RefusedCorrectly)} |");
        }

        return text.ToString();
    }

    public static string ToMarkdown(AiEvalComparisonV1 comparison)
    {
        ArgumentNullException.ThrowIfNull(comparison);
        var text = new StringBuilder();
        text.AppendLine(CultureInfo.InvariantCulture,
            $"# Sağlayıcı karşılaştırması — {comparison.First.Candidate} / {comparison.Second.Candidate}");
        text.AppendLine();
        text.AppendLine(CultureInfo.InvariantCulture,
            $"Karşılaştırılan madde: {comparison.ComparedItems}; dışarıda kalan (kişisel veri, harici sağlayıcıya gönderilmedi): {comparison.ExcludedItems} (YOL-D21, AI-D44).");
        text.AppendLine();
        text.AppendLine(CultureInfo.InvariantCulture, $"## {comparison.First.Candidate}");
        text.AppendLine();
        AppendTotals(text, comparison.FirstTotals);
        text.AppendLine();
        text.AppendLine(CultureInfo.InvariantCulture, $"## {comparison.Second.Candidate}");
        text.AppendLine();
        AppendTotals(text, comparison.SecondTotals);
        text.AppendLine();
        text.AppendLine("## Farklı sonuçlanan maddeler");
        text.AppendLine();
        if (comparison.Differences.Count == 0)
        {
            text.AppendLine("Yok.");
            return text.ToString();
        }

        text.AppendLine(CultureInfo.InvariantCulture, $"| Madde | Kategori | {comparison.First.Candidate} | {comparison.Second.Candidate} |");
        text.AppendLine("|---|---|---|---|");
        foreach (var difference in comparison.Differences)
        {
            text.AppendLine(CultureInfo.InvariantCulture,
                $"| {difference.ItemId} | {difference.Category} | {Verdict(difference.First)} | {Verdict(difference.Second)} |");
        }

        return text.ToString();
    }

    private static void AppendTotals(StringBuilder text, AiEvalTotalsV1 totals)
    {
        text.AppendLine("| Ölçüt | Geçti / değerlendirilen | Oran |");
        text.AppendLine("|---|---|---|");
        Row(text, "Genel", totals.Overall);
        Row(text, "Kaynak doğruluğu (atıf yalnız dönen kaynağa)", totals.SourceAccuracy);
        Row(text, "Doğru cevap", totals.CorrectAnswer);
        Row(text, "\"Cevap veremem\"i bilme", totals.Abstention);
        Row(text, "Yetki dışı / geçersiz soruda red", totals.Refusal);
        foreach (var (category, rate) in totals.ByCategory) Row(text, $"Kategori `{category}`", rate);
        text.AppendLine();
        text.AppendLine(CultureInfo.InvariantCulture,
            $"Madde: {totals.Items}; gönderilmeyen (kişisel veri): {totals.Withheld}; kullanım birimi: {totals.UsageUnits}.");
    }

    private static void Row(StringBuilder text, string label, AiEvalRateV1 rate) =>
        text.AppendLine(CultureInfo.InvariantCulture,
            $"| {label} | {rate.Passed} / {rate.Evaluated} | {(rate.Rate is { } value ? value.ToString("P1", CultureInfo.InvariantCulture) : "—")} |");

    private static string Verdict(AiEvalVerdictV1 verdict) => verdict switch
    {
        AiEvalVerdictV1.Passed => "geçti",
        AiEvalVerdictV1.Failed => "kaldı",
        AiEvalVerdictV1.Withheld => "gönderilmedi",
        _ => verdict.ToString(),
    };

    private static string Check(bool? value) => value switch
    {
        true => "✓",
        false => "✗",
        null => "—",
    };
}
