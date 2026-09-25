namespace GoymenERP.Modules.Finance.Contracts;

/// <summary>How a record's period stands for the rule that decides it.</summary>
public enum RecordPeriodOutcome
{
    /// <summary>The rule could not decide — no rule is registered, or the period owner did not answer (a missing
    /// <c>accounting.periods.read</c>, or a context that changed mid-read). Nothing is recorded, and this is NOT
    /// "the period is not open".</summary>
    Unavailable = 0,

    /// <summary>The deciding date falls in an OPEN period of the company: the record may proceed.</summary>
    Open = 1,

    /// <summary>The deciding date's period is not open (none, proposed or closed): nothing is recorded.</summary>
    NotOpen = 2,
}

/// <summary>The rule's answer, with the stable code a refusal carries.</summary>
public sealed record RecordPeriodAnswer(RecordPeriodOutcome Outcome, string? Code)
{
    public const string RuleUnavailableCode = "finance_period_rule_unavailable";
    public const string PeriodNotOpenCode = "finance_period_not_open";

    public static RecordPeriodAnswer Unavailable { get; } = new RecordPeriodAnswer(RecordPeriodOutcome.Unavailable, RuleUnavailableCode);
}

/// <summary>
/// FIN-T05: the period gate of a Record (Ö5: "yetkili açık dönemde kayıt"). Ö5 keeps the transaction date, the
/// recording instant and the value date apart; WHICH of them decides was answered by Sinan on 20 September 2026
/// (#66 <c>5748258299</c>, K1 — closes <c>5745134466</c>): <b>occurredOn decides</b>, valueOn does not move the
/// period, recordedAtUtc is only the audit moment, and a closed period goes to controlled correction with no
/// silent date change and no automatic reopening.
/// <para>
/// The rule still receives every fact a record carries, because the interface belongs to Finance while the
/// mapping belongs to ONE host class (<c>OccurredOnFinanceRecordPeriodRule</c>) that can also see Accounting's
/// period port. That is deliberate: the decision is a date mapping for THIS source, and keeping it out of the
/// shared contract is what stops it becoming a general rule every accounting source inherits.
/// </para>
/// <para>
/// <see cref="UndecidedFinanceRecordPeriodRule"/> remains the fallback for a composition that registers no rule:
/// it decides nothing and every Record is refused with <see cref="RecordPeriodAnswer.RuleUnavailableCode"/> —
/// which is also the answer when the period owner could not be read at all. That refusal must never be shown as
/// "the period is not open"; the two are different answers.
/// </para>
/// </summary>
public interface IFinanceRecordPeriodRule
{
    Task<RecordPeriodAnswer> CheckAsync(FinanceContext context, DateOnly occurredOn, DateOnly? valueOn,
        DateTimeOffset recordedAtUtc, CancellationToken cancellationToken);
}

/// <summary>
/// The fallback when a composition registers no rule (and the rule the module itself can offer, since Finance
/// cannot see Accounting): reads nothing, decides nothing, answers Unavailable. The host registers the real
/// <c>OccurredOnFinanceRecordPeriodRule</c> before the module's <c>TryAdd</c>, so this one is not used there.
/// </summary>
public sealed class UndecidedFinanceRecordPeriodRule : IFinanceRecordPeriodRule
{
    public Task<RecordPeriodAnswer> CheckAsync(FinanceContext context, DateOnly occurredOn, DateOnly? valueOn,
        DateTimeOffset recordedAtUtc, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(RecordPeriodAnswer.Unavailable);
    }
}
