namespace GoymenERP.Modules.Accounting.Application.Contracts;

// B-04b (KLN-D6, D10, D12, D14, D15, D17, D19; SAT-D208, D223, D226): the Accounting-owned, versioned acceptance port through
// which a FINALISED source document (first: a sales invoice) becomes ONE draft voucher in Accounting's own tables.
// The producing lane keeps the document "waiting for Accounting" until this port answers Admitted; Accounting never
// writes the producer's tables and the producer never writes Accounting's. Accounting does not recompute an amount: it
// uses the producer's line amounts (rounding and the last-line closing of KLN-D14 / SAT-D223 are the producer's
// deterministic calculator) and only checks that they are consistent and that the voucher balances.

/// <summary>The kinds of source document this port version accepts.</summary>
public static class AccountingSourceDocumentKindsV1
{
    /// <summary>A finalised sales invoice. Key: <c>sales-invoice-accounting:{invoiceId:D}:{finalizedVersion}</c>.</summary>
    public const string SalesInvoice = "sales-invoice";

    /// <summary>The idempotency key a producer must use for one finalised sales invoice version.</summary>
    public static string SalesInvoiceKey(Guid invoiceId, long finalizedVersion) =>
        FormattableString.Invariant($"sales-invoice-accounting:{invoiceId:D}:{finalizedVersion}");
}

/// <summary>
/// The four amounts of one invoice, as exact decimal text (dot separator, no sign, no grouping). Payable is
/// net + VAT − withholding (KLN-D6); the producer states it, Accounting only checks it.
/// </summary>
public sealed record AccountingSourceDocumentTotalsV1(string Net, string Vat, string Withholding, string Payable);

/// <summary>
/// One invoice line, in the FUNCTIONAL currency (TL equivalent at the invoice's own fixed rate, SAT-D226 — Accounting
/// never fetches a rate). <paramref name="VatRateCode"/> is the line's VAT-rate group (an exemption or 0% is its own
/// group, SAT-D223). <paramref name="ItemType"/>, <paramref name="LinePurpose"/> and <paramref name="IsExport"/> are the
/// optional selectors the posting account mapping uses to pick the revenue account (KLN-D7, D11, D18).
/// </summary>
public sealed record AccountingSourceDocumentLineV1(
    int LineNumber,
    string? ItemType,
    string? LinePurpose,
    bool? IsExport,
    string? VatRateCode,
    string Net,
    string Vat,
    string Withholding);

/// <summary>
/// One admission offer. <paramref name="CompanyId"/> and <paramref name="ActorId"/> are the caller's server context (the owner
/// fences them against its own context). <paramref name="SourceActorId"/> is the user who FINALISED the document
/// (KLN-D10, tracked as the source actor; KLN-D17: that user cannot approve the voucher). <paramref name="SourceDocumentId"/> is the
/// stable id the producer generated once at finalisation. The idempotency key is
/// <see cref="AccountingSourceDocumentKindsV1.SalesInvoiceKey"/>; the same key with a different document content is refused,
/// never replayed. <paramref name="ExchangeRate"/> is the invoice's single fixed rate for a foreign-currency invoice and null for
/// a TL invoice; <paramref name="DocumentTotals"/> are in the invoice currency and <paramref name="FunctionalTotals"/> in TL
/// (equal when the rate is null). The correlation ids and the actor are the call's context, not the document's content.
/// </summary>
public sealed record AccountingSourceDocumentAdmissionRequestV1(
    Guid CompanyId,
    string ActorId,
    string CorrelationId,
    string? CausationId,
    string IdempotencyKey,
    string DocumentKind,
    Guid SourceDocumentId,
    Guid InvoiceId,
    long FinalizedVersion,
    Guid SourceActorId,
    Guid PartyId,
    Guid CurrencyId,
    string? ExchangeRate,
    DateOnly DocumentDate,
    DateOnly? DueDate,
    AccountingSourceDocumentTotalsV1 DocumentTotals,
    AccountingSourceDocumentTotalsV1 FunctionalTotals,
    IReadOnlyList<AccountingSourceDocumentLineV1> Lines);

/// <summary>What Accounting answered. Only <see cref="Admitted"/> moves the producer's ledger to admitted.</summary>
public enum AccountingSourceDocumentAdmissionOutcomeV1
{
    /// <summary>Accounting could not answer (fail-closed: no authority measurement, a failed read or a lost race). Nothing is known; re-drive.</summary>
    Unavailable = 0,

    /// <summary>One draft voucher exists for the key (first admission or replay).</summary>
    Admitted = 1,

    /// <summary>Accounting did not create a voucher. <see cref="AccountingSourceDocumentAdmissionAnswerV1.Retryable"/> says whether the same key may succeed later.</summary>
    Refused = 2,
}

/// <summary>
/// The answer. <paramref name="Reason"/> is a stable code of at most
/// <see cref="AccountingSourceDocumentAdmissionAnswerV1.ReasonMaximumLength"/> characters
/// (<see cref="AccountingSourceDocumentAdmissionReasonsV1"/>). <paramref name="Retryable"/> is true for a refusal that is
/// waiting on Accounting's own configuration (an account mapping: "eşleme bekliyor") — the producer re-drives the SAME key
/// after Accounting has configured it; a content refusal is final for that key (a changed invoice is a new finalised
/// version).
/// </summary>
public sealed record AccountingSourceDocumentAdmissionAnswerV1(
    AccountingSourceDocumentAdmissionOutcomeV1 Outcome,
    string Reason,
    Guid? VoucherDraftId,
    bool IsReplay,
    bool Retryable)
{
    public const int ReasonMaximumLength = 64;

    public static AccountingSourceDocumentAdmissionAnswerV1 Unavailable { get; } =
        new(AccountingSourceDocumentAdmissionOutcomeV1.Unavailable, AccountingSourceDocumentAdmissionReasonsV1.Unavailable,
            null, false, true);

    public static AccountingSourceDocumentAdmissionAnswerV1 Admitted(Guid voucherDraftId, bool isReplay) =>
        new(AccountingSourceDocumentAdmissionOutcomeV1.Admitted, AccountingSourceDocumentAdmissionReasonsV1.Admitted,
            voucherDraftId, isReplay, false);

    public static AccountingSourceDocumentAdmissionAnswerV1 Refused(string reason, bool isReplay = false, bool retryable = false) =>
        new(AccountingSourceDocumentAdmissionOutcomeV1.Refused, reason, null, isReplay, retryable);
}

/// <summary>The stable reason codes (each at most 64 characters).</summary>
public static class AccountingSourceDocumentAdmissionReasonsV1
{
    public const string Admitted = "admitted";
    public const string Unavailable = "unavailable";

    /// <summary>The caller's authority was not granted. A setup or grant gap, never an Accounting decision.</summary>
    public const string AuthorityDenied = "authority_denied";

    public const string RequestInvalid = "request_invalid";
    public const string DocumentKindUnsupported = "document_kind_unsupported";
    public const string IdempotencyKeyInvalid = "idempotency_key_invalid";

    /// <summary>The key (or the invoice version) was used with another document content.</summary>
    public const string IdempotencyConflict = "idempotency_conflict";

    public const string DocumentInvalid = "document_invalid";
    public const string AmountInvalid = "amount_invalid";
    public const string TotalsMismatch = "totals_mismatch";
    public const string ExchangeRateInvalid = "exchange_rate_invalid";
    public const string LineInvalid = "line_invalid";
    public const string WithholdingExceedsVat = "withholding_exceeds_vat";
    public const string MappingQueryInvalid = "mapping_query_invalid";
    public const string VoucherUnbalanced = "voucher_unbalanced";

    /// <summary>No active, effective or complete account mapping answers a line yet (eşleme bekliyor). Retryable.</summary>
    public const string MappingPending = "mapping_pending";

    /// <summary>Two mapping rows answer equally specifically (eşleme bekliyor until configuration is fixed). Retryable.</summary>
    public const string MappingAmbiguous = "mapping_ambiguous";
}

public enum AccountingSourceDocumentAuthorityDecisionV1
{
    /// <summary>No measurement is composed. Fail closed.</summary>
    Unavailable = 0,
    Denied = 1,
    Allowed = 2,
}

/// <param name="Decision">The fresh measurement.</param>
/// <param name="PermissionCode">The permission that was measured (audit envelope); no grant is declared here.</param>
/// <param name="RoleCodes">The caller's role codes for the audit envelope.</param>
public sealed record AccountingSourceDocumentAuthorityAnswerV1(
    AccountingSourceDocumentAuthorityDecisionV1 Decision,
    string PermissionCode,
    IReadOnlyCollection<string> RoleCodes)
{
    public static AccountingSourceDocumentAuthorityAnswerV1 Unavailable { get; } =
        new(AccountingSourceDocumentAuthorityDecisionV1.Unavailable, string.Empty, []);

    public static AccountingSourceDocumentAuthorityAnswerV1 Denied { get; } =
        new(AccountingSourceDocumentAuthorityDecisionV1.Denied, string.Empty, []);
}

/// <summary>
/// Host-owned fresh authority measurement for one admission (the Finance <c>IFinanceInvoiceOpenItemAdmissionAuthorityV1</c>
/// pattern): for a sales invoice the host measures the PRODUCER's finalisation permission (e.g. <c>sales.invoices.finalize</c>)
/// company-wide for the caller. No permission string and no grant is declared by Accounting here, and an unmapped
/// composition stays unavailable (the port answers <see cref="AccountingSourceDocumentAdmissionOutcomeV1.Unavailable"/>).
/// </summary>
public interface IAccountingSourceDocumentAdmissionAuthorityV1
{
    Task<AccountingSourceDocumentAuthorityAnswerV1> CheckAsync(
        Guid companyId,
        string actorId,
        string documentKind,
        Guid sourceDocumentId,
        CancellationToken cancellationToken);
}

/// <summary>
/// The Accounting side of B-04b. It writes ONE draft voucher (and its lines) to Accounting's own tables and never anything of
/// the producer. Declared in <c>Application.Contracts</c> and versioned (V1): the producer's host adapts its own
/// port to this one; Sales' code does not reference Accounting. Synchronous and idempotent.
/// <list type="bullet">
/// <item>The same key and content never produces a second voucher: it replays the first answer.</item>
/// <item>A missing, inactive, not-yet-effective or ambiguous account mapping is fail-closed: no voucher, a retryable
/// <see cref="AccountingSourceDocumentAdmissionOutcomeV1.Refused"/> with the waiting row visible to Accounting
/// ("eşleme bekliyor"). The account is never guessed.</item>
/// <item>A date without an OPEN period still admits the draft (KLN-D15: not refused); it carries no period until one
/// is open and the approval binds it ("dönem bekliyor").</item>
/// <item>A content refusal is recorded once and replayed for the key (the content of a finalised version is immutable).</item>
/// </list>
/// Not an HTTP surface; no permission is registered and no grant is made here.
/// </summary>
public interface IAccountingSourceDocumentAdmissionPortV1
{
    Task<AccountingSourceDocumentAdmissionAnswerV1> AdmitAsync(
        AccountingSourceDocumentAdmissionRequestV1 request,
        CancellationToken cancellationToken = default);
}
