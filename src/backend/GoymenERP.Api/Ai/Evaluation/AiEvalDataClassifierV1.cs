using System.Text.RegularExpressions;
using GoymenERP.Api.Ai.Knowledge;
using GoymenERP.BuildingBlocks.Ai;

namespace GoymenERP.Api.Ai.Evaluation;

/// <summary>Data class of an evaluation item and why (AI-D44, AI-D42).</summary>
public sealed record AiEvalItemClassificationV1(AiDataClassV1 DataClass, bool HoldsPersonalData, IReadOnlyList<string> Signals);

/// <summary>
/// WP-A2 deterministic classification of an evaluation item before anything is sent (AI-D44: a free question is Internal
/// by default and rises to Confidential ("Gizli") when it carries personal data such as a national id, phone, IBAN or
/// e-mail; AI-D42: a contact person's name, phone, e-mail or national id is Confidential). Signals are the shared outbound
/// text screen (<see cref="AiOutboundTextScreenV1"/>: IBAN, e-mail, digit run, currency amount — fail-closed, every finding
/// raises the class), the decision index's personal name terms, and an expected source that the index marks as a
/// personal data candidate. A Confidential item never goes to an external provider (YOL-D21: the provider comparison uses
/// only questions without personal data). It is a screen, not a guarantee; it only ever raises the class.
/// </summary>
public sealed partial class AiEvalDataClassifierV1(AiDecisionIndex? decisionIndex)
{
    public const string SignalPersonalName = "kisi_adi";
    public const string SignalPersonalSource = "kisisel_veri_adayi_kaynak";

    public AiEvalItemClassificationV1 Classify(AiEvalItemV1 item)
    {
        ArgumentNullException.ThrowIfNull(item);
        var signals = new List<string>();
        signals.AddRange(AiOutboundTextScreenV1.Screen(item.Question).Findings.Select(finding => finding.ToString()));

        if (decisionIndex is not null)
        {
            var folded = AiDecisionIndex.Fold(item.Question);
            if (decisionIndex.PersonalNameTerms.Any(term => ContainsWord(folded, AiDecisionIndex.Fold(term))))
                signals.Add(SignalPersonalName);

            if (item.ExpectedSourceIds.Any(id => decisionIndex.Find(id) is { } record
                    && string.Equals(record.DataClassCandidate, AiDecisionIndex.PersonalDataCandidate, StringComparison.Ordinal)))
                signals.Add(SignalPersonalSource);
        }

        var personal = signals.Count > 0;
        return new AiEvalItemClassificationV1(personal ? AiDataClassV1.Confidential : AiDataClassV1.Internal, personal,
            signals.AsReadOnly());
    }

    private static bool ContainsWord(string foldedText, string foldedTerm) =>
        foldedTerm.Length > 0
        && Regex.IsMatch(foldedText, $"(?<![\\p{{L}}\\p{{N}}]){Regex.Escape(foldedTerm)}(?![\\p{{L}}\\p{{N}}])",
            RegexOptions.CultureInvariant);
}
