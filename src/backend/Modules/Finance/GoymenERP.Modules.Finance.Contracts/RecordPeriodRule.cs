namespace GoymenERP.Modules.Finance.Contracts;

/// <summary>How a record's period stands for the rule that decides it.</summary>
public enum RecordPeriodOutcome
{
    /// <summary>No rule decides the period yet (Q1 unanswered): nothing is recorded.</summary>
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
/// FIN-T05a: the period gate of a Record (Ö5: "yetkili açık dönemde kayıt"). Ö5 keeps the transaction date, the
/// recording instant and the value date apart but does not say WHICH of them decides a recorded receipt's period;
/// that is the open NEEDS-BUSINESS Q1 (#66 5745134466). So the rule receives every fact a record carries and decides
/// nothing itself until Q1 names the date: the only registered rule reads nothing and answers
/// <see cref="RecordPeriodAnswer.Unavailable"/>, and every Record is refused with
/// <see cref="RecordPeriodAnswer.RuleUnavailableCode"/>. Q1's answer changes one binding (which date, read through
/// Accounting's period port), not the command.
/// </summary>
public interface IFinanceRecordPeriodRule
{
    Task<RecordPeriodAnswer> CheckAsync(FinanceContext context, DateOnly occurredOn, DateOnly? valueOn,
        DateTimeOffset recordedAtUtc, CancellationToken cancellationToken);
}

/// <summary>The rule while Q1 is open: reads nothing, decides nothing, answers Unavailable.</summary>
public sealed class UndecidedFinanceRecordPeriodRule : IFinanceRecordPeriodRule
{
    public Task<RecordPeriodAnswer> CheckAsync(FinanceContext context, DateOnly occurredOn, DateOnly? valueOn,
        DateTimeOffset recordedAtUtc, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(RecordPeriodAnswer.Unavailable);
    }
}
