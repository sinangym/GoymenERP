using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace GoymenERP.Api.Ai.Evaluation;

/// <summary>A reviewer's mark of one sampled answer.</summary>
public enum AiEvalHumanVerdictV1
{
    Correct = 1,
    Incorrect = 2,
}

/// <summary>
/// One sampled answer as the reviewer sees it. The reviewer fills <see cref="HumanVerdict"/> with <c>dogru</c> or
/// <c>yanlis</c> and may add a <see cref="Note"/>; every other field must come back unchanged.
/// </summary>
public sealed record AiEvalReviewSheetItemV1(
    string ItemId,
    string Category,
    string Question,
    string ExpectedAnswerSummary,
    AiEvalVerdictV1 AutomaticVerdict,
    IReadOnlyList<string> AutomaticReasons,
    string? GatewayStatus,
    string? AnswerText,
    string? HumanVerdict,
    string? Note);

/// <summary>The human review sheet of one candidate (format <see cref="AiEvalHumanReviewV1.Format"/>).</summary>
public sealed record AiEvalReviewSheetV1(
    string Format,
    string Contract,
    string Candidate,
    string Seed,
    int SamplePercent,
    int EvaluatedItems,
    IReadOnlyList<AiEvalReviewSheetItemV1> Items);

public sealed record AiEvalHumanDisagreementV1(string ItemId, string Category, AiEvalVerdictV1 Automatic, AiEvalHumanVerdictV1 Human,
    string? Note);

/// <summary>
/// AI-D47 difference report: the automatic score and the human mark on the same sample, their agreement, the difference of
/// the two pass rates in percentage points (human − automatic), and every disagreement in both directions.
/// </summary>
public sealed record AiEvalHumanReviewResultV1(
    string Contract,
    string Candidate,
    string Seed,
    int Sampled,
    AiEvalRateV1 AutomaticOnSample,
    AiEvalRateV1 HumanOnSample,
    AiEvalRateV1 Agreement,
    double? DifferencePoints,
    int AutomaticPassedHumanIncorrect,
    int AutomaticFailedHumanCorrect,
    IReadOnlyList<AiEvalHumanDisagreementV1> Disagreements);

/// <summary>
/// WP-A2 human review of the provider comparison (AI-D47, YOL-D21 detail): of every candidate's answers a random
/// <see cref="DecisionSamplePercent"/> % (rounded up, never zero when something was evaluated) is marked right/wrong by a
/// person and the difference to the automatic score is reported. The sample is reproducible: items are ordered by
/// SHA-256 of the seed and the item id, independent of the runtime's random generator; the candidate is not part of the
/// key, so two candidates compared on the same items are reviewed on the same sample. Withheld items (personal data, not
/// sent to an external provider) are never sampled. The sheet holds SENTETIK questions and the candidate's composed text
/// for the reviewer; it is a local working file, not a published report.
/// </summary>
public static class AiEvalHumanReviewV1
{
    public const string Format = "goymen.ai.eval-human-review.v1";

    /// <summary>AI-D47: the share of each candidate's answers a person marks.</summary>
    public const int DecisionSamplePercent = 10;

    public const string CorrectMark = "dogru";
    public const string IncorrectMark = "yanlis";
    public const int MaximumSeedLength = 100;

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        Converters = { new JsonStringEnumConverter(allowIntegerValues: false) },
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
    };

    /// <summary>Items in a sample of <paramref name="evaluated"/> answers: <see cref="DecisionSamplePercent"/> %, rounded up.</summary>
    public static int SampleSize(int evaluated)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(evaluated);
        return (evaluated * DecisionSamplePercent + 99) / 100;
    }

    /// <summary>The review sheet of one run, sampled over its evaluated (not withheld) items.</summary>
    public static AiEvalReviewSheetV1 CreateSheet(AiEvalReportV1 report, AiEvalSetV1 set, string seed)
    {
        ArgumentNullException.ThrowIfNull(report);
        var evaluated = report.Items.Where(item => item.Verdict != AiEvalVerdictV1.Withheld).Select(item => item.ItemId).ToArray();
        return Sheet(report, set, seed, evaluated);
    }

    /// <summary>The review sheets of a comparison: both candidates on the same sample of the compared items.</summary>
    public static (AiEvalReviewSheetV1 First, AiEvalReviewSheetV1 Second) CreateSheets(AiEvalComparisonV1 comparison, AiEvalSetV1 set,
        string seed)
    {
        ArgumentNullException.ThrowIfNull(comparison);
        var withheld = comparison.First.Items.Concat(comparison.Second.Items)
            .Where(item => item.Verdict == AiEvalVerdictV1.Withheld)
            .Select(item => item.ItemId)
            .ToHashSet(StringComparer.Ordinal);
        var common = comparison.First.Items.Select(item => item.ItemId).Where(id => !withheld.Contains(id)).ToArray();
        return (Sheet(comparison.First, set, seed, common), Sheet(comparison.Second, set, seed, common));
    }

    public static string ToJson(AiEvalReviewSheetV1 sheet) => JsonSerializer.Serialize(sheet, Json);

    public static string ToJson(AiEvalHumanReviewResultV1 result) => JsonSerializer.Serialize(result, Json);

    /// <summary>Strict reader of a (marked) sheet: unknown fields, a wrong format or a missing field are refused.</summary>
    public static AiEvalReviewSheetV1 ParseSheet(string json)
    {
        ArgumentNullException.ThrowIfNull(json);
        AiEvalReviewSheetV1? sheet;
        try
        {
            sheet = JsonSerializer.Deserialize<AiEvalReviewSheetV1>(json, Json);
        }
        catch (JsonException exception)
        {
            throw new InvalidDataException("The review sheet is not a valid v1 sheet.", exception);
        }

        if (sheet is null || sheet.Format != Format || sheet.Items is null || string.IsNullOrWhiteSpace(sheet.Candidate)
            || string.IsNullOrWhiteSpace(sheet.Seed)
            || sheet.Items.Any(item => item is null || string.IsNullOrWhiteSpace(item.ItemId) || item.AutomaticReasons is null
                                       || !Enum.IsDefined(item.AutomaticVerdict)))
            throw new InvalidDataException("The review sheet is not a valid v1 sheet.");
        return sheet;
    }

    /// <summary>
    /// Compares the issued sheet with the reviewer's marked copy. The marked copy must be the issued sheet with only
    /// <c>humanVerdict</c> and <c>note</c> filled: same format, contract, candidate, seed, percent, items in the same order
    /// with the same automatic verdicts and reasons, and every item marked <c>dogru</c> or <c>yanlis</c>. Anything else is
    /// refused with every problem listed (no partial report).
    /// </summary>
    public static AiEvalHumanReviewResultV1 Compare(AiEvalReviewSheetV1 issued, AiEvalReviewSheetV1 marked)
    {
        ArgumentNullException.ThrowIfNull(issued);
        ArgumentNullException.ThrowIfNull(marked);
        var problems = new List<string>();
        if (marked.Format != issued.Format || marked.Contract != issued.Contract || marked.Candidate != issued.Candidate
            || marked.Seed != issued.Seed || marked.SamplePercent != issued.SamplePercent || marked.EvaluatedItems != issued.EvaluatedItems)
            problems.Add("sayfa_basligi_degisti");
        if (marked.Items.Count != issued.Items.Count) problems.Add("madde_sayisi_degisti");

        var marks = new List<(AiEvalReviewSheetItemV1 Item, AiEvalHumanVerdictV1 Human)>();
        foreach (var (expected, actual) in issued.Items.Zip(marked.Items))
        {
            if (actual.ItemId != expected.ItemId || actual.AutomaticVerdict != expected.AutomaticVerdict
                || !actual.AutomaticReasons.SequenceEqual(expected.AutomaticReasons, StringComparer.Ordinal)
                || actual.Category != expected.Category || actual.AnswerText != expected.AnswerText)
            {
                problems.Add($"{expected.ItemId}:madde_degisti");
                continue;
            }

            switch (actual.HumanVerdict)
            {
                case CorrectMark:
                    marks.Add((actual, AiEvalHumanVerdictV1.Correct));
                    break;
                case IncorrectMark:
                    marks.Add((actual, AiEvalHumanVerdictV1.Incorrect));
                    break;
                default:
                    problems.Add($"{expected.ItemId}:isaret_yok_veya_gecersiz");
                    break;
            }
        }

        if (problems.Count > 0)
            throw new InvalidDataException($"The marked review sheet is refused: {string.Join(", ", problems)}.");

        var disagreements = marks
            .Where(mark => (mark.Item.AutomaticVerdict == AiEvalVerdictV1.Passed) != (mark.Human == AiEvalHumanVerdictV1.Correct))
            .Select(mark => new AiEvalHumanDisagreementV1(mark.Item.ItemId, mark.Item.Category, mark.Item.AutomaticVerdict, mark.Human,
                mark.Item.Note))
            .ToArray();
        var automatic = new AiEvalRateV1(marks.Count(mark => mark.Item.AutomaticVerdict == AiEvalVerdictV1.Passed), marks.Count);
        var human = new AiEvalRateV1(marks.Count(mark => mark.Human == AiEvalHumanVerdictV1.Correct), marks.Count);
        double? difference = automatic.Rate is { } a && human.Rate is { } h
            ? Math.Round((h - a) * 100, 2, MidpointRounding.AwayFromZero)
            : null;
        return new AiEvalHumanReviewResultV1(issued.Contract, issued.Candidate, issued.Seed, marks.Count, automatic, human,
            new AiEvalRateV1(marks.Count - disagreements.Length, marks.Count), difference,
            disagreements.Count(item => item.Automatic == AiEvalVerdictV1.Passed),
            disagreements.Count(item => item.Automatic != AiEvalVerdictV1.Passed),
            disagreements);
    }

    public static string ToMarkdown(AiEvalHumanReviewResultV1 result)
    {
        ArgumentNullException.ThrowIfNull(result);
        var text = new StringBuilder();
        text.AppendLine(CultureInfo.InvariantCulture, $"# İnsan incelemesi farkı — {result.Candidate}");
        text.AppendLine();
        text.AppendLine(CultureInfo.InvariantCulture,
            $"AI-D47: cevapların %{DecisionSamplePercent}'u (tohum `{result.Seed}`, {result.Sampled} madde) bir kişi tarafından işaretlendi.");
        text.AppendLine();
        text.AppendLine("| Ölçüt | Değer | Oran |");
        text.AppendLine("|---|---|---|");
        Row(text, "Otomatik puan (örneklemde geçti)", result.AutomaticOnSample);
        Row(text, "İnsan işareti (örneklemde doğru)", result.HumanOnSample);
        Row(text, "Uyum", result.Agreement);
        text.AppendLine(CultureInfo.InvariantCulture,
            $"| Fark (insan − otomatik) | {(result.DifferencePoints is { } points ? points.ToString("0.##", CultureInfo.InvariantCulture) + " puan" : "—")} | |");
        text.AppendLine(CultureInfo.InvariantCulture, $"| Otomatik geçti, insan yanlış dedi | {result.AutomaticPassedHumanIncorrect} | |");
        text.AppendLine(CultureInfo.InvariantCulture, $"| Otomatik kaldı, insan doğru dedi | {result.AutomaticFailedHumanCorrect} | |");
        text.AppendLine();
        text.AppendLine("## Uyuşmayan maddeler");
        text.AppendLine();
        if (result.Disagreements.Count == 0)
        {
            text.AppendLine("Yok.");
            return text.ToString();
        }

        text.AppendLine("| Madde | Kategori | Otomatik | İnsan |");
        text.AppendLine("|---|---|---|---|");
        foreach (var item in result.Disagreements)
        {
            text.AppendLine(CultureInfo.InvariantCulture,
                $"| {item.ItemId} | {item.Category} | {(item.Automatic == AiEvalVerdictV1.Passed ? "geçti" : "kaldı")} | {(item.Human == AiEvalHumanVerdictV1.Correct ? "doğru" : "yanlış")} |");
        }

        return text.ToString();
    }

    private static AiEvalReviewSheetV1 Sheet(AiEvalReportV1 report, AiEvalSetV1 set, string seed, IReadOnlyList<string> population)
    {
        ArgumentNullException.ThrowIfNull(set);
        ArgumentException.ThrowIfNullOrWhiteSpace(seed);
        if (seed.Length > MaximumSeedLength) throw new ArgumentOutOfRangeException(nameof(seed));

        var size = SampleSize(population.Count);
        var chosen = population
            .OrderBy(id => SampleKey(seed, id), StringComparer.Ordinal)
            .ThenBy(id => id, StringComparer.Ordinal)
            .Take(size)
            .ToHashSet(StringComparer.Ordinal);
        var questions = set.Items.ToDictionary(item => item.Id, StringComparer.Ordinal);
        var items = report.Items
            .Where(item => chosen.Contains(item.ItemId))
            .Select(item =>
            {
                var question = questions.TryGetValue(item.ItemId, out var source)
                    ? source
                    : throw new ArgumentException($"Item {item.ItemId} is not in the evaluation set.", nameof(set));
                return new AiEvalReviewSheetItemV1(item.ItemId, item.Category, question.Question, question.ExpectedAnswerSummary,
                    item.Verdict, item.Reasons, item.GatewayStatus?.ToString(), item.AnswerText, null, null);
            })
            .ToArray();
        return new AiEvalReviewSheetV1(Format, report.Contract, report.Candidate, seed, DecisionSamplePercent, population.Count, items);
    }

    private static string SampleKey(string seed, string itemId) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes($"{seed}\n{itemId}")));

    private static void Row(StringBuilder text, string label, AiEvalRateV1 rate) =>
        text.AppendLine(CultureInfo.InvariantCulture,
            $"| {label} | {rate.Passed} / {rate.Evaluated} | {(rate.Rate is { } value ? value.ToString("P1", CultureInfo.InvariantCulture) : "—")} |");
}
