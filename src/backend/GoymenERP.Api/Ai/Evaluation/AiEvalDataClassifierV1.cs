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
/// raises the class) and an expected source that the index marks as a personal data candidate. WP-A4 (plan D2, AI-D36)
/// masks person names at index generation and the packaged index carries no name list, so person names in the question are
/// screened without the index by <see cref="AiEvalPersonNameScreenV1"/> (host-supplied known names, given name + surname,
/// honorific; AI-D44, AI-D49). A Confidential item never goes to an external provider (YOL-D21: the provider comparison uses
/// only questions without personal data). It is a screen, not a guarantee; it only ever raises the class.
/// </summary>
public sealed class AiEvalDataClassifierV1(AiDecisionIndex? decisionIndex, AiEvalPersonNameScreenV1? personNames = null)
{
    public const string SignalPersonalSource = "kisisel_veri_adayi_kaynak";

    private readonly AiEvalPersonNameScreenV1 names = personNames ?? new AiEvalPersonNameScreenV1();

    public AiEvalItemClassificationV1 Classify(AiEvalItemV1 item)
    {
        ArgumentNullException.ThrowIfNull(item);
        var signals = new List<string>();
        signals.AddRange(AiOutboundTextScreenV1.Screen(item.Question).Findings.Select(finding => finding.ToString()));
        signals.AddRange(names.Screen(item.Question));

        if (decisionIndex is not null)
        {
            if (item.ExpectedSourceIds.Any(id => decisionIndex.Find(id) is { } record
                    && string.Equals(record.DataClassCandidate, AiDecisionIndex.PersonalDataCandidate, StringComparison.Ordinal)))
                signals.Add(SignalPersonalSource);
        }

        var personal = signals.Count > 0;
        return new AiEvalItemClassificationV1(personal ? AiDataClassV1.Confidential : AiDataClassV1.Internal, personal,
            signals.AsReadOnly());
    }
}
