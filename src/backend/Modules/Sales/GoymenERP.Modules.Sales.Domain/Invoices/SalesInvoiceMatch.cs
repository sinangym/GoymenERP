namespace GoymenERP.Modules.Sales.Domain.Invoices;

/// <summary>
/// The official invoice as the issuing program's import states it (API-35, SAT-D89): the official number, the ETTN, the customer, the
/// currency and the payable amount in the invoice currency and its TL equivalent — and, since SAT-D241 (EK48), the TL equivalents of the
/// VAT and the withholding (tevkifat) totals, compared separately from the payable amount. The official value is authoritative — Sales
/// does not correct it; it only decides whether the approved invoice agrees with it.
/// </summary>
public sealed record SalesOfficialInvoiceFacts(
    string OfficialNo,
    string Ettn,
    Guid CustomerPartyId,
    Guid CurrencyId,
    decimal PayableAmount,
    decimal PayableFunctionalAmount,
    decimal VatFunctionalAmount,
    decimal WithholdingFunctionalAmount);

/// <summary>
/// SAT-D241: the VAT and the withholding TL equivalents of the invoice and the official document and their absolute differences, each
/// compared with the same tolerance as the payable amount (SAT-D229 / SAT-D232). Null on a match recorded before SAT-D241 (the
/// comparison was not made then; such a match never finalises — <see cref="SalesInvoiceMatchFacts.HasOpenDifference"/>).
/// </summary>
public sealed record SalesInvoiceTaxMatchFacts(
    decimal InvoiceVatAmount,
    decimal OfficialVatAmount,
    decimal VatDifference,
    decimal InvoiceWithholdingAmount,
    decimal OfficialWithholdingAmount,
    decimal WithholdingDifference)
{
    public bool IsConsistent => InvoiceVatAmount >= 0m && OfficialVatAmount >= 0m && InvoiceWithholdingAmount >= 0m
        && OfficialWithholdingAmount >= 0m && VatDifference == Math.Abs(InvoiceVatAmount - OfficialVatAmount)
        && WithholdingDifference == Math.Abs(InvoiceWithholdingAmount - OfficialWithholdingAmount);
}

/// <summary>The fields in which an approved invoice does not equal the official document (exact match, SAT-D89).</summary>
public sealed record SalesInvoiceMatchEvaluation(
    bool CustomerMatches,
    bool CurrencyMatches,
    decimal InvoiceFunctionalAmount,
    decimal OfficialFunctionalAmount,
    decimal Difference,
    SalesInvoiceTaxMatchFacts Taxes)
{
    public bool ExactFieldsMatch => CustomerMatches && CurrencyMatches;

    /// <summary>The payable difference exceeds the (inclusive) tolerance.</summary>
    public bool PayableExceeds(decimal tolerance) => Difference > tolerance;

    /// <summary>SAT-D241: the VAT difference exceeds the same tolerance, measured on its own.</summary>
    public bool VatExceeds(decimal tolerance) => Taxes.VatDifference > tolerance;

    /// <summary>SAT-D241: the withholding difference exceeds the same tolerance, measured on its own.</summary>
    public bool WithholdingExceeds(decimal tolerance) => Taxes.WithholdingDifference > tolerance;

    /// <summary>SAT-D241: any one of the three exceeding sends the match to the separate approval (SAT-D89), the others notwithstanding.</summary>
    public bool Exceeds(decimal tolerance) => PayableExceeds(tolerance) || VatExceeds(tolerance) || WithholdingExceeds(tolerance);
}

/// <summary>
/// The recorded official match of an approved invoice (API-35): the official number and ETTN, the TL equivalents that were
/// compared (Sinan S1, SAT-D229), the absolute difference, the tolerance that applied then, who matched and when, and — only when a
/// difference exceeded the tolerance — the person who approved it (SAT-D106) with the reason. Since SAT-D241 the VAT and withholding
/// comparison is recorded beside it (<see cref="Taxes"/>; null only on a match recorded before). All amounts are TL equivalents.
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
    string? DifferenceReason,
    SalesInvoiceTaxMatchFacts? Taxes)
{
    public bool DifferenceWasApproved => DifferenceApprovedBy is not null;

    /// <summary>The largest of the recorded payable, VAT and withholding differences.</summary>
    public decimal LargestDifference => Taxes is null
        ? Difference
        : Math.Max(Difference, Math.Max(Taxes.VatDifference, Taxes.WithholdingDifference));

    /// <summary>A recorded difference exceeded the tolerance (SAT-D229 / SAT-D241), so the match carries an approval.</summary>
    public bool Exceeded => LargestDifference > Tolerance;

    /// <summary>
    /// The match leaves something open for the finalisation (NEEDS-SINAN #3): any non-zero payable, VAT or withholding difference, or a
    /// match recorded before SAT-D241 that never compared VAT and withholding (fail-closed).
    /// </summary>
    public bool HasOpenDifference => Taxes is null || LargestDifference != 0m;
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
        if (State is not (SalesInvoiceState.Approved or SalesInvoiceState.Finalized) || Match is not null || !MatchIsConsistent(facts))
        {
            throw new InvalidOperationException("Kayıtlı resmî fatura eşleşmesi okunamadı.");
        }

        Match = facts;
    }

    /// <summary>The TL equivalent of the invoice payable amount: the frozen functional total, or the total itself for TL.</summary>
    public decimal PayableFunctionalAmount => FunctionalTotals?.Payable.Amount ?? Totals.Payable.Amount;

    /// <summary>The TL equivalent of the invoice VAT total (SAT-D241): the frozen functional total, or the total itself for TL.</summary>
    public decimal VatFunctionalAmount => FunctionalTotals?.Vat.Amount ?? Totals.Vat.Amount;

    /// <summary>The TL equivalent of the invoice withholding total (SAT-D241): the frozen functional total, or the total itself for TL.</summary>
    public decimal WithholdingFunctionalAmount => FunctionalTotals?.Withholding.Amount ?? Totals.Withholding.Amount;

    /// <summary>
    /// Compares the approved invoice with the official document without changing anything: the customer and the currency are exact;
    /// the payable, VAT and withholding differences are measured separately, each on the TL equivalents (Sinan S1, SAT-D241).
    /// </summary>
    public SalesInvoiceMatchEvaluation EvaluateMatch(SalesOfficialInvoiceFacts official)
    {
        ArgumentNullException.ThrowIfNull(official);
        var invoiceAmount = PayableFunctionalAmount;
        var invoiceVat = VatFunctionalAmount;
        var invoiceWithholding = WithholdingFunctionalAmount;
        return new SalesInvoiceMatchEvaluation(
            Header.PartyId == official.CustomerPartyId,
            Header.CurrencyId == official.CurrencyId,
            invoiceAmount,
            official.PayableFunctionalAmount,
            Math.Abs(invoiceAmount - official.PayableFunctionalAmount),
            new SalesInvoiceTaxMatchFacts(
                invoiceVat,
                official.VatFunctionalAmount,
                Math.Abs(invoiceVat - official.VatFunctionalAmount),
                invoiceWithholding,
                official.WithholdingFunctionalAmount,
                Math.Abs(invoiceWithholding - official.WithholdingFunctionalAmount)));
    }

    /// <summary>
    /// Records the official match of an <see cref="SalesInvoiceState.Approved"/> invoice once. The customer and the currency must be
    /// equal; payable, VAT and withholding TL differences within the (inclusive) tolerance are accepted as they are; if any one of them
    /// is larger (SAT-D241), the match needs the explicit approval of a person other than the preparer and submitter, with a reason
    /// (SAT-D106/D204). Nothing else changes.
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

        var exceeds = evaluation.Exceeds(tolerance);
        string? approver = null;
        string? reason = null;
        if (exceeds)
        {
            if (!approveDifference)
            {
                throw new SalesRuleException(SalesRuleCodes.InvoiceMatchDifferenceNotApproved,
                    "Ödenecek tutar, KDV ya da tevkifat farkı toleransı aşıyor; ayrı ve açık onay gerekir.");
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
            evaluation.OfficialFunctionalAmount, evaluation.Difference, tolerance, actor, now.ToUniversalTime(), approver, reason,
            evaluation.Taxes);
        Match = facts;
        return facts;
    }

    private static bool MatchIsConsistent(SalesInvoiceMatchFacts facts)
    {
        var within = !facts.Exceeded;
        var approvalShape = within
            ? facts.DifferenceApprovedBy is null && facts.DifferenceReason is null
            : facts.DifferenceApprovedBy is not null && facts.DifferenceReason is not null;
        return facts.Difference >= 0m && facts.Tolerance >= 0m && approvalShape && facts.Taxes is not { IsConsistent: false }
            && !string.IsNullOrWhiteSpace(facts.OfficialNo) && facts.Ettn.Length == EttnLength;
    }
}
