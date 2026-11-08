using GoymenERP.Modules.Sales.Domain.Invoices;

namespace GoymenERP.Modules.Sales.Application.Contracts;

// S5 API-35 (Satış hazırlık planı v1 §3.3) — Fatura çekirdeği 3: the official document match. SAT-D89 (number, ETTN, customer,
// currency exact; the official value is authoritative), SAT-D229 + Sinan S1 (tolerance 0.05 TL on the TL equivalent, Satış's own
// configuration), SAT-D106/D204 (a larger difference is approved separately, never by the preparer), EK20 (the match is Satış's
// own work: no open item, no Finance write).

/// <summary>
/// API-35 body. <paramref name="ApproveDifference"/> is the explicit approval of a TL difference above the tolerance
/// (<paramref name="DifferenceReason"/> is then required); it is refused when the difference is within the tolerance.
/// </summary>
public sealed record MatchSalesInvoiceRequest(string ExpectedVersion, string OfficialInvoiceNo, string Ettn, bool ApproveDifference = false,
    string? DifferenceReason = null);

/// <summary>The recorded match. Amounts are exact invariant text, all TL equivalents (Sinan S1).</summary>
public sealed record SalesInvoiceMatchView(string OfficialInvoiceNo, string Ettn, string InvoiceAmount, string OfficialAmount,
    string Difference, string Tolerance, string MatchedBy, DateTimeOffset MatchedAtUtc, string? DifferenceApprovedBy, string? DifferenceReason);

/// <summary>How the official-invoice provider answered one lookup; anything but <see cref="Found"/> is read fail-closed.</summary>
public enum SalesOfficialInvoiceOutcomeV1
{
    /// <summary>The provider cannot answer (not wired, failed, malformed): never read as "not found".</summary>
    Unavailable = 0,

    /// <summary>The provider answered: no official document of this company has this number and ETTN together.</summary>
    NotFound = 1,

    Found = 2,
}

public sealed record SalesOfficialInvoiceAnswerV1(SalesOfficialInvoiceOutcomeV1 Outcome, SalesOfficialInvoiceFacts? Invoice)
{
    public static SalesOfficialInvoiceAnswerV1 Unavailable { get; } = new(SalesOfficialInvoiceOutcomeV1.Unavailable, null);

    public static SalesOfficialInvoiceAnswerV1 NotFound { get; } = new(SalesOfficialInvoiceOutcomeV1.NotFound, null);
}

/// <summary>
/// API-35 (SAT-D89): the Sales-declared read port the owner of the official-invoice import answers with the company's official
/// document (number, ETTN, customer, currency, payable amount and its TL equivalent). The owner implements it over its own
/// company-filtered data and registers it in its own module; Sales never reads those tables. The port carries NO authority — the
/// match command measures its own names — and CREATES NOTHING. Until an owner registers it the default adapter answers
/// <see cref="SalesOfficialInvoiceOutcomeV1.Unavailable"/> and the match fails closed (503).
/// </summary>
public interface ISalesOfficialInvoiceReadPortV1
{
    Task<SalesOfficialInvoiceAnswerV1> LookupAsync(Guid companyId, string officialNo, string ettn, CancellationToken cancellationToken = default);
}

/// <summary>
/// SAT-D229 (Sinan S1): the amount tolerance of the official match, measured on the TL equivalent, inclusive. 0.05 TL is the
/// decided default of Satış's own configuration surface; the Ayarlar link is deferred (SAT-D209), so the value is a configured
/// option, never a literal in the comparison. The tolerance in force is stored on every match.
/// </summary>
public sealed class SalesInvoiceMatchTolerancePolicy
{
    public const decimal DefaultToleranceAmount = 0.05m;

    public SalesInvoiceMatchTolerancePolicy(decimal toleranceAmount = DefaultToleranceAmount)
    {
        ToleranceAmount = toleranceAmount >= 0m
            ? toleranceAmount
            : throw new ArgumentOutOfRangeException(nameof(toleranceAmount), "The tolerance cannot be negative.");
    }

    public decimal ToleranceAmount { get; }
}

/// <summary>
/// S5 (Sinan, Issue #66, 7 November: VUK 232, 12,000 TL): the withholding (tevkifat) threshold evaluated at finalisation, measured on
/// the invoice's TL net amount, strictly below refuses. The value is a configured option of Satış's own surface (SAT-D209: no
/// Ayarlar table or port), never a literal in the check; it is marked "mali musavir teyidi bekliyor" - the advisor's confirmation of
/// the value and the basis is a human gate before go-live.
/// </summary>
public sealed class SalesInvoiceWithholdingThresholdPolicy
{
    public const decimal DefaultThresholdAmount = 12000.00m;

    public SalesInvoiceWithholdingThresholdPolicy(decimal thresholdAmount = DefaultThresholdAmount)
    {
        ThresholdAmount = thresholdAmount >= 0m
            ? thresholdAmount
            : throw new ArgumentOutOfRangeException(nameof(thresholdAmount), "The threshold cannot be negative.");
    }

    public decimal ThresholdAmount { get; }
}

/// <summary>
/// Diagnostics seam of the finalisation (host-implemented, logging only): a Finance hand-off failure after the commit is reported
/// here and never changes the answer. Carries identifiers and the exception, no amounts and no secrets.
/// </summary>
public interface ISalesInvoiceDiagnosticsV1
{
    void FinanceHandOffFailed(Guid companyId, Guid invoiceId, string stage, Exception exception);
}
