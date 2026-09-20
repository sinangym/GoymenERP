namespace GoymenERP.Modules.Accounting.Application.Contracts;

/// <summary>What the period owner answered about ONE date of the caller's company.</summary>
public enum AccountingPeriodStatusOutcomeV1
{
    /// <summary>The caller may not read the company's periods, or its context changed while the owner answered.</summary>
    Denied = 0,

    /// <summary>No period of the company covers the date.</summary>
    None = 1,

    /// <summary>A period covers the date and it is OPEN.</summary>
    Open = 2,

    /// <summary>A period covers the date and it is NOT open — proposed (Ö4: not yet approved) or closed.</summary>
    NotOpen = 3,
}

/// <param name="Outcome">The owner's answer.</param>
/// <param name="PeriodId">The covering period, when one exists. Never a name or an amount.</param>
/// <param name="Status">The stored status as text (<c>Open</c> / <c>Proposed</c> / <c>Closed</c>), when one exists.</param>
public sealed record AccountingPeriodStatusAnswerV1(
    AccountingPeriodStatusOutcomeV1 Outcome, Guid? PeriodId, string? Status);

/// <summary>
/// The period owner's narrow answer for one date of the caller's company (FIN-T05; the same port serves
/// Logistics' period-status read, #367 K4.5 — designed once for both consumers).
/// <list type="bullet">
/// <item><b>It decides no date.</b> The caller says which date it is asking about; which date DECIDES a
/// record's period is the consumer's business rule, not this port's. Sinan's K1 (#66 5748258299) maps the
/// manual bank receipt to its <c>occurredOn</c>; that mapping lives in Finance and is explicitly not a
/// general rule for every accounting source.</item>
/// <item><b>It carries no money.</b> A period id and a status; no journal, no amount, no queue.</item>
/// <item><b>The caller's own authority.</b> The adapter reads under the ACTOR's narrow
/// <c>accounting.periods.read</c>, never under an authority of its own: a port that reads under its own
/// name widens privilege (#366's lesson, where two adapters re-checked the company but not the actor).
/// A caller without that name is <see cref="AccountingPeriodStatusOutcomeV1.Denied"/> — which the consumer
/// must not present as "the period is not open"; the two are different answers.</item>
/// </list>
/// </summary>
public interface IAccountingPeriodStatusPortV1
{
    Task<AccountingPeriodStatusAnswerV1> GetStatusAsync(DateOnly on, CancellationToken cancellationToken = default);
}
