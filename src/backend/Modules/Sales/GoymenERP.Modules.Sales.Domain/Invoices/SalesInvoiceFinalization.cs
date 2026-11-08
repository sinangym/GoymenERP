namespace GoymenERP.Modules.Sales.Domain.Invoices;

/// <summary>Where the finalised invoice's open items stand with Finance (SAT-D208). Sales never calls a Waiting invoice admitted.</summary>
public enum SalesInvoiceFinanceStatus
{
    /// <summary>"Finans kabulü bekliyor": Sales committed; Finance has not yet answered Succeeded.</summary>
    Waiting = 1,

    /// <summary>Finance admitted the open items (terminal).</summary>
    Admitted = 2,
}

/// <summary>
/// One instalment as offered to Finance: the identifiers are generated once at finalisation and persisted, so a re-drive offers the
/// very same ids (Finance replays or refuses a changed payload). TL amount only on a foreign-currency invoice (SAT-D227).
/// </summary>
public sealed record SalesInvoiceFinalInstalment(Guid OpenItemId, Guid InstallmentId, int SequenceNo, DateOnly DueDate, decimal Amount,
    decimal? FunctionalAmount);

/// <summary>The finalisation facts and the Finance hand-off ledger of a finalised invoice (API-36).</summary>
public sealed record SalesInvoiceFinalization(
    Guid AdmissionId,
    long FinalizedVersion,
    string FinalizedBy,
    DateTimeOffset FinalizedAtUtc,
    SalesInvoiceFinanceStatus FinanceStatus,
    DateTimeOffset? AdmittedAtUtc,
    int AttemptCount,
    DateTimeOffset? LastAttemptAtUtc,
    string? LastOutcome,
    IReadOnlyList<SalesInvoiceFinalInstalment> Plan)
{
    public const int MaximumOutcomeLength = 64;
}

public sealed partial class SalesInvoice
{
    /// <summary>The finalisation, null until API-36 records it.</summary>
    public SalesInvoiceFinalization? Finalization { get; private set; }

    /// <summary>
    /// Finalises a matched invoice: <c>Approved → Finalized</c> (version + 1). The official match must already be recorded; the TL
    /// split of the instalments is the calculator's (SAT-D227). The finalised invoice offers its open items to Finance with a
    /// <see cref="SalesInvoiceFinanceStatus.Waiting"/> status; nothing here claims Finance accepted them.
    /// </summary>
    public SalesInvoiceFinalization Finalize(long expectedVersion, string actorId, Func<Guid> newId, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(newId);
        RequireVersion(expectedVersion);
        var actor = RequireActor(actorId);
        RequireState(SalesInvoiceState.Approved, "Yalnız onaylı fatura kesinleştirilir.");
        if (Match is null)
        {
            throw new SalesRuleException(SalesRuleCodes.InvoiceMatchRequired, "Fatura resmî belgeyle eşleşmeden kesinleştirilmez.");
        }

        var split = SalesInvoiceInstalmentCalculator.FunctionalAmounts(instalments, Header.Rate, Header.CurrencyId, FunctionalTotals?.Payable);
        var plan = new List<SalesInvoiceFinalInstalment>(instalments.Count);
        for (var index = 0; index < instalments.Count; index++)
        {
            var item = instalments[index];
            plan.Add(new SalesInvoiceFinalInstalment(Identity.Require(newId(), "openItemId"), Identity.Require(newId(), "installmentId"),
                item.SequenceNo, item.DueDate, item.Amount.Amount, split[index]));
        }

        var finalization = new SalesInvoiceFinalization(Identity.Require(newId(), "admissionId"), Version + 1, actor, now.ToUniversalTime(),
            SalesInvoiceFinanceStatus.Waiting, null, 0, null, null, plan);
        State = SalesInvoiceState.Finalized;
        Finalization = finalization;
        Advance();
        return finalization;
    }

    /// <summary>
    /// Records one hand-off attempt (no version step: the invoice is frozen, the ledger is its own record). An admitted invoice is
    /// never offered or recorded again.
    /// </summary>
    public SalesInvoiceFinalization RecordFinanceAttempt(bool admitted, string outcome, DateTimeOffset now)
    {
        var current = Finalization ?? throw StateConflict("Fatura kesinleştirilmedi.");
        if (State != SalesInvoiceState.Finalized || current.FinanceStatus == SalesInvoiceFinanceStatus.Admitted)
        {
            throw StateConflict("Finans kabulü zaten tamamlandı.");
        }

        var text = RequireText(outcome, SalesInvoiceFinalization.MaximumOutcomeLength, nameof(outcome));
        var at = now.ToUniversalTime();
        Finalization = current with
        {
            FinanceStatus = admitted ? SalesInvoiceFinanceStatus.Admitted : SalesInvoiceFinanceStatus.Waiting,
            AdmittedAtUtc = admitted ? at : null,
            AttemptCount = checked(current.AttemptCount + 1),
            LastAttemptAtUtc = at,
            LastOutcome = text,
        };
        return Finalization;
    }

    /// <summary>Attaches the stored finalisation when the aggregate is restored from the store.</summary>
    public void RestoreFinalization(SalesInvoiceFinalization facts)
    {
        ArgumentNullException.ThrowIfNull(facts);
        var payable = FunctionalTotals?.Payable.Amount;
        var admitted = facts.FinanceStatus == SalesInvoiceFinanceStatus.Admitted;
        var split = facts.Plan.Where(item => item.FunctionalAmount is not null).Sum(item => item.FunctionalAmount!.Value);
        if (State != SalesInvoiceState.Finalized || Finalization is not null || Match is null || facts.FinalizedVersion != Version
            || facts.AttemptCount < 0 || facts.Plan.Count != instalments.Count || admitted != facts.AdmittedAtUtc.HasValue
            || (facts.AttemptCount == 0) != (facts.LastAttemptAtUtc is null) || (admitted && facts.AttemptCount == 0)
            || (payable is null ? facts.Plan.Any(item => item.FunctionalAmount is not null) : split != payable)
            || facts.Plan.Select((item, index) => item.SequenceNo != instalments[index].SequenceNo || item.Amount != instalments[index].Amount.Amount
                || item.DueDate != instalments[index].DueDate).Any(mismatch => mismatch))
        {
            throw new InvalidOperationException("Kayıtlı fatura kesinleştirmesi okunamadı.");
        }

        Finalization = facts;
    }
}
