namespace GoymenERP.Modules.Finance.Contracts;

/// <summary>
/// FIN-S6 (F-005 D3): how Finance answered one lookup of an official sales invoice record by its official number and
/// ETTN. Anything but <see cref="Approved"/> carries no facts: only an approved record is the official fact (F-005 D1).
/// </summary>
public enum FinanceOfficialSalesInvoiceReadOutcomeV1
{
    /// <summary>Finance cannot answer (another company, a store fault, a stored row it cannot read): never "not found".</summary>
    Unavailable = 0,

    /// <summary>No live record of this company has this official number and this ETTN together.</summary>
    NotFound = 1,

    /// <summary>A live record exists but still waits for its second-person approval: it is not yet the official fact.</summary>
    PendingApproval = 2,

    /// <summary>The approved record (FNS-D20: it never changes afterwards).</summary>
    Approved = 3,
}

/// <summary>
/// The approved official sales invoice as Finance recorded it. Amounts are the exact, normalised decimal text Finance
/// stored (DEC-FIN-014): <see cref="PayableAmount"/> is in <see cref="CurrencyId"/>, the functional amounts are the
/// company's functional (TL) equivalents. The tax base is carried for Finance-side readers; the Sales match does not
/// compare it (FNS-D21). <see cref="Ettn"/> is the lowercase "D" GUID text.
/// </summary>
public sealed record FinanceOfficialSalesInvoiceFactsV1(
    Guid InvoiceId,
    Guid CompanyId,
    string OfficialNo,
    string Ettn,
    Guid CustomerPartyId,
    Guid CurrencyId,
    string PayableAmount,
    string PayableFunctionalAmount,
    string TaxBaseFunctionalAmount,
    string VatFunctionalAmount,
    string WithholdingFunctionalAmount,
    Guid? PdfDocumentId,
    int? PdfDocumentVersionNo,
    string? PdfSha256,
    string Version,
    DateTimeOffset ApprovedAtUtc,
    DateOnly? InvoiceDate = null,
    string SourceKind = "manual",
    Guid? SourceBatchId = null,
    string? SourceChecksum = null,
    bool HasPendingManual = false);

public sealed record FinanceOfficialSalesInvoiceReadAnswerV1(
    FinanceOfficialSalesInvoiceReadOutcomeV1 Outcome,
    FinanceOfficialSalesInvoiceFactsV1? Invoice)
{
    public static FinanceOfficialSalesInvoiceReadAnswerV1 Unavailable { get; } =
        new(FinanceOfficialSalesInvoiceReadOutcomeV1.Unavailable, null);

    public static FinanceOfficialSalesInvoiceReadAnswerV1 NotFound { get; } =
        new(FinanceOfficialSalesInvoiceReadOutcomeV1.NotFound, null);

    public static FinanceOfficialSalesInvoiceReadAnswerV1 PendingApproval { get; } =
        new(FinanceOfficialSalesInvoiceReadOutcomeV1.PendingApproval, null);
}

/// <summary>
/// FIN-S6 (F-005 D3): the Finance-owned read of one official sales invoice record in the server-resolved company, by the
/// official number and the ETTN together (the Sales match key, SAT-D89). It reads Finance's own company-filtered store
/// only; a rejected or withdrawn record (FNS-D39) is not live and reads as <see cref="FinanceOfficialSalesInvoiceReadOutcomeV1.NotFound"/>.
/// The port carries NO authority — the consumer measures its own permission around it — and CREATES NOTHING (no attempt,
/// audit or version effect). A company other than the resolved one answers
/// <see cref="FinanceOfficialSalesInvoiceReadOutcomeV1.Unavailable"/>, never another company's record.
/// </summary>
public interface IFinanceOfficialSalesInvoiceReadPortV1
{
    Task<FinanceOfficialSalesInvoiceReadAnswerV1> LookupAsync(Guid companyId, string officialNo, string ettn,
        CancellationToken cancellationToken = default);
}
