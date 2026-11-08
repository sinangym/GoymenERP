namespace GoymenERP.Modules.Sales.Domain.Invoices;

/// <summary>
/// The official invoice as the issuing program's import states it (API-35, SAT-D89): the official number, the ETTN, the customer, the
/// currency and the payable amount in the invoice currency and its TL equivalent. The official value is authoritative — Sales does
/// not correct it; it only decides whether the approved invoice agrees with it.
/// </summary>
public sealed record SalesOfficialInvoiceFacts(
    string OfficialNo,
    string Ettn,
    Guid CustomerPartyId,
    Guid CurrencyId,
    decimal PayableAmount,
    decimal PayableFunctionalAmount);

/// <summary>The fields in which an approved invoice does not equal the official document (exact match, SAT-D89).</summary>
public sealed record SalesInvoiceMatchEvaluation(
    bool CustomerMatches,
    bool CurrencyMatches,
    decimal InvoiceFunctionalAmount,
    decimal OfficialFunctionalAmount,
    decimal Difference)
{
    public bool ExactFieldsMatch => CustomerMatches && CurrencyMatches;
}

/// <summary>
/// The recorded official match of an approved invoice (API-35): the official number and ETTN, the TL equivalents that were
/// compared (Sinan S1, SAT-D229), the absolute difference, the tolerance that applied then, who matched and when, and — only when the
/// difference exceeded the tolerance — the person who approved it (SAT-D106) with the reason. All amounts are TL equivalents.
/// </summary>
public sealed record SalesInvoiceMatchFacts(
    string OfficialNo,
    string Ettn,
    decimal InvoiceFunctionalAmount,
    decimal OfficialFunctionalAmount,
    decimal Difference,
    decimal Tolerance,
    string MatchedBy,
    DateTimeOffset MatchedAtUtc,
    string? DifferenceApprovedBy,
    string? DifferenceReason)
{
    public bool DifferenceWasApproved => DifferenceApprovedBy is not null;
}

public sealed partial class SalesInvoice
{
    public const int MaximumOfficialNoLength = 64;
    public const int EttnLength = 36;

    /// <summary>
    /// The official match, null until API-35 records it. Recording it does not change <see cref="State"/> or <see cref="Version"/>:
    /// an approved invoice is frozen by the database (TR_Invoices_Guard), and the match is its own append-only record.
    /// </summary>
    public SalesInvoiceMatchFacts? Match { get; private set; }

    /// <summary>Attaches the stored match when the aggregate is restored from the store.</summary>
    public void RestoreMatch(SalesInvoiceMatchFacts facts)
    {
        ArgumentNullException.ThrowIfNull(facts);
        if (State != SalesInvoiceState.Approved || Match is not null || !MatchIsConsistent(facts))
        {
            throw new InvalidOperationException("Kayıtlı resmî fatura eşleşmesi okunamadı.");
        }

        Match = facts;
    }

    /// <summary>The TL equivalent of the invoice payable amount: the frozen functional total, or the total itself for TL.</summary>
    public decimal PayableFunctionalAmount => FunctionalTotals?.Payable.Amount ?? Totals.Payable.Amount;

    /// <summary>
    /// Compares the approved invoice with the official document without changing anything: the customer and the currency are exact;
    /// the difference is measured on the TL equivalents (Sinan S1).
    /// </summary>
    public SalesInvoiceMatchEvaluation EvaluateMatch(SalesOfficialInvoiceFacts official)
    {
        ArgumentNullException.ThrowIfNull(official);
        var invoiceAmount = PayableFunctionalAmount;
        return new SalesInvoiceMatchEvaluation(
            Header.PartyId == official.CustomerPartyId,
            Header.CurrencyId == official.CurrencyId,
            invoiceAmount,
            official.PayableFunctionalAmount,
            Math.Abs(invoiceAmount - official.PayableFunctionalAmount));
    }

    /// <summary>
    /// Records the official match of an <see cref="SalesInvoiceState.Approved"/> invoice once. The customer and the currency must be
    /// equal; a TL difference within the (inclusive) tolerance is accepted as it is; a larger one needs the explicit approval of a
    /// person other than the preparer and submitter, with a reason (SAT-D106/D204). Nothing else changes.
    /// </summary>
    public SalesInvoiceMatchFacts RecordMatch(long expectedVersion, string actorId, SalesOfficialInvoiceFacts official, decimal tolerance,
        bool approveDifference, string? differenceReason, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(official);
        if (tolerance < 0m)
        {
            throw new ArgumentOutOfRangeException(nameof(tolerance));
        }

        RequireVersion(expectedVersion);
        var actor = RequireActor(actorId);
        RequireState(SalesInvoiceState.Approved, "Yalnız onaylı fatura resmî belgeyle eşleştirilir.");
        if (Match is not null)
        {
            throw StateConflict("Fatura zaten resmî belgeyle eşleştirildi.");
        }

        var evaluation = EvaluateMatch(official);
        if (!evaluation.ExactFieldsMatch)
        {
            throw new SalesRuleException(SalesRuleCodes.InvoiceMatchMismatch,
                "Resmî belgenin müşterisi ya da para birimi faturayla aynı değil.");
        }

        var exceeds = evaluation.Difference > tolerance;
        string? approver = null;
        string? reason = null;
        if (exceeds)
        {
            if (!approveDifference)
            {
                throw new SalesRuleException(SalesRuleCodes.InvoiceMatchDifferenceNotApproved,
                    "Tutar farkı toleransı aşıyor; ayrı ve açık onay gerekir.");
            }

            RequireNotPreparer(actor);
            reason = RequireReason(differenceReason) ?? throw new SalesRuleException(SalesRuleCodes.InvoiceMatchDifferenceReasonRequired,
                "Tutar farkı onayı için neden yazılır.");
            approver = actor;
        }
        else if (approveDifference)
        {
            throw new SalesRuleException(SalesRuleCodes.InvoiceMatchDifferenceNotApplicable,
                "Fark toleransın içinde; onay istenemez.");
        }

        var facts = new SalesInvoiceMatchFacts(official.OfficialNo, official.Ettn, evaluation.InvoiceFunctionalAmount,
            evaluation.OfficialFunctionalAmount, evaluation.Difference, tolerance, actor, now.ToUniversalTime(), approver, reason);
        Match = facts;
        return facts;
    }

    private static bool MatchIsConsistent(SalesInvoiceMatchFacts facts)
    {
        var within = facts.Difference <= facts.Tolerance;
        var approvalShape = within
            ? facts.DifferenceApprovedBy is null && facts.DifferenceReason is null
            : facts.DifferenceApprovedBy is not null && facts.DifferenceReason is not null;
        return facts.Difference >= 0m && facts.Tolerance >= 0m && approvalShape
            && !string.IsNullOrWhiteSpace(facts.OfficialNo) && facts.Ettn.Length == EttnLength;
    }
}
