using System.Text.Json.Serialization;

namespace GoymenERP.Modules.Accounting.Application;

/// <summary>SAT-D422: an explicit invoice-line choice, independent of the Product card's current default.</summary>
public enum InvoiceLineCogsClassificationV1 { TradeGoods = 1, Manufactured = 2 }
public enum FinalizedInvoiceLineSourceOutcomeV1 { Available = 1, NotFound = 2, Unavailable = 3 }

/// <summary>
/// Sales-owned immutable commercial facts. The invoice's finalization identity/version is used rather than the mutable
/// Finance-admission attempt counter. CommercialProofJson is owner-produced canonical evidence; Accounting never changes
/// an invoice amount, quantity, tax, shipment or stock position through this correction.
/// </summary>
public sealed record FinalizedInvoiceLineSourceV1(Guid CompanyId, Guid InvoiceId, Guid InvoiceLineId,
    Guid FinalizationAdmissionId, long FinalizedVersion, Guid ProductId, Guid ShipmentId,
    Guid DispatchNoteId, int DispatchNoteLineNo, int InvoiceLineNo, DateOnly InvoiceDate, string OfficialInvoiceNumber,
    Guid Ettn, string CommercialProofJson, string Fingerprint);

public sealed record FinalizedInvoiceLineSourceReadV1(FinalizedInvoiceLineSourceOutcomeV1 Outcome,
    Guid CompanyId, Guid InvoiceId, Guid InvoiceLineId, FinalizedInvoiceLineSourceV1? Source);
public sealed record FinalizedInvoiceCogsSelectionV1(Guid CompanyId, Guid InvoiceId, string OfficialInvoiceNumber,
    Guid Ettn, IReadOnlyList<FinalizedInvoiceLineReferenceV1> Lines);
public sealed record FinalizedInvoiceCogsSelectionReadV1(FinalizedInvoiceLineSourceOutcomeV1 Outcome, Guid CompanyId,
    FinalizedInvoiceCogsSelectionV1? Selection);

/// <summary>No commercial price fields are disclosed by a permission that only authorizes an Accounting correction.</summary>
public sealed record FinalizedInvoiceLineReferenceV1(Guid CompanyId, Guid InvoiceId, Guid InvoiceLineId,
    Guid FinalizationAdmissionId, long FinalizedVersion, Guid ProductId, Guid ShipmentId,
    Guid DispatchNoteId, int DispatchNoteLineNo, int InvoiceLineNo, DateOnly InvoiceDate, string OfficialInvoiceNumber, Guid Ettn, string Fingerprint)
{
    public static FinalizedInvoiceLineReferenceV1 From(FinalizedInvoiceLineSourceV1 source) => new(source.CompanyId, source.InvoiceId,
        source.InvoiceLineId, source.FinalizationAdmissionId, source.FinalizedVersion, source.ProductId, source.ShipmentId,
        source.DispatchNoteId, source.DispatchNoteLineNo, source.InvoiceLineNo, source.InvoiceDate, source.OfficialInvoiceNumber, source.Ettn, source.Fingerprint);
}

/// <summary>Actual Sales owner read only; the Accounting command fences its existing reversal authority.</summary>
public interface IAccountingFinalizedInvoiceLineSourceV1
{
    Task<FinalizedInvoiceCogsSelectionReadV1> FindAsync(Guid companyId, string officialInvoiceNumber, CancellationToken cancellationToken = default);
    Task<FinalizedInvoiceLineSourceReadV1> GetAsync(Guid companyId, Guid invoiceId, Guid invoiceLineId,
        CancellationToken cancellationToken = default);
}

/// <summary>Exact posted source component, never a share inferred from invoice net or an aggregate journal amount.</summary>
public sealed record InvoiceLineCogsPostedEvidenceV1(Guid CompanyId, Guid InvoiceId, Guid InvoiceLineId,
    long FinalizedVersion, Guid JournalId, int CogsJournalLineNumber, int CounterJournalLineNumber,
    Guid CogsAccountId, Guid CounterAccountId, Guid CurrencyId, string Amount, string SourceFingerprint);

public sealed record InvoiceLineCogsAccountV1(Guid AccountId, string Code, string Name);
public sealed record InvoiceLineCogsAccountPlanV1(Guid PackageId, long PackageVersion, long? CatalogVersion,
    string SourceVersion, string SourceSha256, InvoiceLineCogsAccountV1 Account);
public sealed record InvoiceLineCogsPeriodOptionV1(Guid PeriodId, DateOnly StartsOn, DateOnly EndsOn);
public sealed record InvoiceLineCogsOptionsV1(Guid CompanyId, IReadOnlyList<InvoiceLineCogsAccountV1> Accounts,
    IReadOnlyList<InvoiceLineCogsPeriodOptionV1> OpenPeriods);

public sealed record InvoiceLineCogsRevisionViewV1(long Version, Guid ProposalId,
    InvoiceLineCogsClassificationV1 Classification, InvoiceLineCogsAccountPlanV1 AccountPlan,
    Guid PreparedBy, Guid ApprovedBy, DateTimeOffset ApprovedAtUtc, Guid PeriodId,
    DateOnly TransactionDate, DateOnly AccountingDate, Guid? ReversingCorrectionJournalId,
    Guid? ReplacementCorrectionJournalId, string Reason);

public sealed record InvoiceLineCogsCorrectionViewV1(Guid ProposalId, Guid CompanyId, Guid InvoiceId,
    Guid InvoiceLineId, long FinalizedVersion, long Version, long ExpectedLineVersion, string Status,
    InvoiceLineCogsClassificationV1 Classification, InvoiceLineCogsAccountPlanV1 AccountPlan,
    Guid PeriodId, DateOnly TransactionDate, DateOnly AccountingDate, DateOnly? ValueDate,
    Guid PreparedBy, DateTimeOffset PreparedAtUtc, Guid? ApprovedBy, DateTimeOffset? ApprovedAtUtc,
    string Reason, InvoiceLineCogsPostedEvidenceV1? PostedEvidence,
    Guid? ReversingCorrectionJournalId, Guid? ReplacementCorrectionJournalId);

public sealed record InvoiceLineCogsHistoryV1(Guid CompanyId, Guid InvoiceId, Guid InvoiceLineId,
    FinalizedInvoiceLineReferenceV1 Source, long LineVersion, InvoiceLineCogsRevisionViewV1? Current,
    IReadOnlyList<InvoiceLineCogsRevisionViewV1> Revisions, IReadOnlyList<InvoiceLineCogsCorrectionViewV1> Proposals,
    string PostingEvidenceState, InvoiceLineCogsPostedEvidenceV1? PostedEvidence,
    InvoiceLineCogsProductReferenceV1? ProductReference = null);

/// <summary>Today's Product default is reference data, never proof of the invoice line's former classification.</summary>
public sealed record InvoiceLineCogsProductReferenceV1(Guid CompanyId, Guid ProductId, string Outcome,
    InvoiceLineCogsClassificationV1? Classification, string? ProductVersion, string? ClassificationVersion);
public interface IAccountingInvoiceLineProductReferenceV1
{
    Task<InvoiceLineCogsProductReferenceV1> GetAsync(Guid companyId, Guid productId, CancellationToken cancellationToken = default);
}

/// <summary>
/// A line-bound posting source is explicit: SourceId is InvoiceId and PostingPurpose also binds line and finalization version.
/// An invoice-total journal cannot be split into line COGS by this workflow.
/// The canonical component has COGS debit line 1 and its counter-account credit line 2; later corrections retain that
/// exact component identity. This contract does not authorize an initial posting or choose its recognition date.
/// </summary>
public static class InvoiceLineCogsPostingTraceV1
{
    public const string Purpose = "sales-invoice-line-cogs";
    public static string For(Guid invoiceLineId, long finalizedVersion) => invoiceLineId != Guid.Empty && finalizedVersion > 0
        ? Purpose + ":" + invoiceLineId.ToString("D") + ":" + finalizedVersion.ToString(System.Globalization.CultureInfo.InvariantCulture)
        : throw new ArgumentException("A COGS trace needs its invoice line and finalization version.");
}

/// <summary>SAT-D423/FNS-D112: explicit actual account and open period/date, with existing reversal authority.</summary>
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record PrepareInvoiceLineCogsCorrectionV1([property: JsonRequired] Guid ProposalId,
    [property: JsonRequired] long ExpectedFinalizedVersion, [property: JsonRequired] long ExpectedLineVersion,
    [property: JsonRequired] InvoiceLineCogsClassificationV1 Classification, [property: JsonRequired] Guid CogsAccountId,
    [property: JsonRequired] Guid PeriodId, [property: JsonRequired] DateOnly TransactionDate,
    [property: JsonRequired] DateOnly AccountingDate, [property: JsonRequired] string Reason, DateOnly? ValueDate = null);

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record ApproveInvoiceLineCogsCorrectionV1([property: JsonRequired] long ExpectedVersion);

public interface IInvoiceLineCogsCorrectionStoreV1
{
    Task<InvoiceLineCogsOptionsV1> OptionsAsync(CancellationToken cancellationToken = default);
    Task RecordAttemptAsync(AccountingCommandRecord record, Func<CancellationToken, Task> freshFence, CancellationToken cancellationToken = default);
    Task<InvoiceLineCogsHistoryV1> ReadAsync(FinalizedInvoiceLineSourceV1 source, CancellationToken cancellationToken = default);
    Task<InvoiceLineCogsCorrectionViewV1?> FindAsync(Guid proposalId, CancellationToken cancellationToken = default);
    Task<InvoiceLineCogsCorrectionViewV1> PrepareAsync(FinalizedInvoiceLineSourceV1 source,
        PrepareInvoiceLineCogsCorrectionV1 request, Guid actorId, Func<CancellationToken, Task> freshFence,
        AccountingCommandMetadata metadata, CancellationToken cancellationToken = default);
    Task<InvoiceLineCogsCorrectionViewV1> ApproveAsync(FinalizedInvoiceLineSourceV1 source, Guid proposalId,
        long expectedVersion, Guid actorId, Func<CancellationToken, Task> freshFence,
        AccountingCommandMetadata metadata, CancellationToken cancellationToken = default);
}

public static class InvoiceLineCogsCorrectionCodesV1
{
    public const string RequestInvalid = "accounting_invoice_line_cogs_correction_request_invalid";
    public const string NotFound = "accounting_invoice_line_cogs_correction_not_found";
    public const string SourceUnavailable = "accounting_invoice_line_cogs_correction_source_unavailable";
    public const string SourceChanged = "accounting_invoice_line_cogs_correction_source_changed";
    public const string VersionConflict = "accounting_invoice_line_cogs_correction_version_conflict";
    public const string SelfApproval = "accounting_invoice_line_cogs_correction_self_approval";
    public const string NotPrepared = "accounting_invoice_line_cogs_correction_not_prepared";
    public const string OpenPeriodRequired = "accounting_invoice_line_cogs_correction_open_period_required";
    public const string AccountPlanUnavailable = "accounting_invoice_line_cogs_correction_account_plan_unavailable";
    public const string AccountNotInActivePlan = "accounting_invoice_line_cogs_correction_account_not_in_active_plan";
    public const string PostingEvidenceInvalid = "accounting_invoice_line_cogs_correction_posting_evidence_invalid";
}
