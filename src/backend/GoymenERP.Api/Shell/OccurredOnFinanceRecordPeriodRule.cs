using GoymenERP.Modules.Accounting.Application.Contracts;
using GoymenERP.Modules.Finance.Contracts;

namespace GoymenERP.Api.Shell;

/// <summary>
/// FIN-T05: the period gate of a definitive manual bank receipt, as Sinan decided it on 20 September 2026
/// (#66 <c>5748258299</c>, K1 — closes <c>5745134466</c>).
/// <list type="bullet">
/// <item><b><c>occurredOn</c> decides.</b> The real money event's transaction date determines the open-period
/// check.</item>
/// <item><b><c>valueOn</c> does NOT move the period.</b> It is the value date and is carried on the record
/// (Ö5 keeps the three dates apart), but it is never asked about here.</item>
/// <item><b><c>recordedAtUtc</c> is the server's audit moment</b>, nothing more: an event that happened in a
/// past period and is recorded today does NOT move into today's period.</item>
/// <item><b>A closed period goes to controlled correction</b> — there is no silent date change and no
/// automatic reopening, so this rule simply refuses and records nothing.</item>
/// </list>
/// <para>
/// <b>Scope, deliberately narrow.</b> This is the date mapping for THIS source only. It lives here, in one
/// host class that can see both modules, precisely so that it does not become a general rule every
/// accounting source inherits — the user's decision says the mapping is for this source and warns against
/// generalising it. Accounting's port is asked "what is the status of the period covering this date" and
/// never learns which date matters.
/// </para>
/// <para>
/// <b>Two refusals that must not look alike</b> (the LOG-4b lesson): a port that could not answer — the actor
/// lacks <c>accounting.periods.read</c>, or its context changed mid-read — is
/// <see cref="RecordPeriodAnswer.RuleUnavailableCode"/>, "I could not decide". A period that exists and is
/// not open, or no period at all, is <see cref="RecordPeriodAnswer.PeriodNotOpenCode"/>, "I decided, and the
/// answer is no". Collapsing them would leave a recorder unable to tell a missing grant from a closed month.
/// </para>
/// </summary>
public sealed class OccurredOnFinanceRecordPeriodRule(IAccountingPeriodStatusPortV1 periods) : IFinanceRecordPeriodRule
{
    public async Task<RecordPeriodAnswer> CheckAsync(FinanceContext context, DateOnly occurredOn, DateOnly? valueOn,
        DateTimeOffset recordedAtUtc, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);
        cancellationToken.ThrowIfCancellationRequested();

        // K1: the transaction date and only it. `valueOn` and `recordedAtUtc` are carried by the record and
        // are deliberately not consulted — asking with either would move a back-dated event into another
        // period, which the decision refuses in those words.
        var answer = await periods.GetStatusAsync(occurredOn, cancellationToken);
        return answer.Outcome switch
        {
            AccountingPeriodStatusOutcomeV1.Open => new RecordPeriodAnswer(RecordPeriodOutcome.Open, null),
            AccountingPeriodStatusOutcomeV1.NotOpen or AccountingPeriodStatusOutcomeV1.None =>
                new RecordPeriodAnswer(RecordPeriodOutcome.NotOpen, RecordPeriodAnswer.PeriodNotOpenCode),
            // Denied, and anything a future owner adds: the rule could not decide, so nothing is recorded.
            _ => RecordPeriodAnswer.Unavailable,
        };
    }
}
