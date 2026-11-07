namespace GoymenERP.Modules.Finance.Contracts;

// F2b / API-11 (SAT-D96, FNS-D14): when the producing module records that a finalised invoice was cancelled outside the ERP
// (the "resmî belge iptal" command; e-Fatura RED / e-Arşiv iptal), Finance closes that invoice's open items with a
// reversal — nothing is deleted. The reversal is its own fact: the open item keeps its original amount, its status becomes
// Cancelled and its outstanding amount zero, and the party ledger shows a reversing row on the cancellation day.

/// <summary>
/// The cancellation of one admitted invoice. <paramref name="CancellationId"/> is the producer's id for this cancellation
/// (the receipt's id); <paramref name="InvoiceVersion"/> is the admitted version being cancelled; <paramref name="Reason"/>
/// is the producer's short reason text, kept on the receipt and the audit.
/// </summary>
public sealed record FinanceInvoiceOpenItemCancellationV1(
    Guid CancellationId,
    FinanceInvoiceProducerV1 Producer,
    Guid InvoiceId,
    string InvoiceVersion,
    string Reason);

/// <summary>
/// The Finance side of the invoice cancellation (API-11). Synchronous, idempotent and in-process (no HTTP surface), like
/// <see cref="IFinanceInvoiceOpenItemAdmissionPortV1"/>; the producer commits its own cancellation first and then calls
/// this with the same key until it is answered (ALS-D64 "ayrı adım").
/// <list type="bullet">
/// <item>The same key, payload and actor replay the first answer; the same <c>CancellationId</c> under a new key replays
/// when the payload is the same and is refused otherwise.</item>
/// <item>An invoice Finance never admitted, or another version of it, is refused; an invoice already cancelled under
/// another <c>CancellationId</c> is refused with <c>finance_invoice_already_cancelled</c>.</item>
/// <item>If any of the invoice's open items has an active allocation (a receipt or payment is distributed to it), the
/// cancellation is refused with <c>finance_invoice_cancellation_has_allocations</c>. What should happen to an allocated
/// invoice is not decided (Issue #66 NEEDS-SINAN); until it is, the allocation is released first by its own operation.</item>
/// </list>
/// Fenced by the host-owned <see cref="IFinanceInvoiceOpenItemCancellationAuthorityV1"/> before any read and right before
/// the save; the caller's company and actor must equal the server context.
/// </summary>
public interface IFinanceInvoiceOpenItemCancellationPortV1
{
    Task<FinanceInvoiceOpenItemCancellationResultV1> CancelAsync(
        FinanceContext context,
        CommandMetadata metadata,
        FinanceInvoiceOpenItemCancellationV1 cancellation,
        CancellationToken cancellationToken);
}

/// <summary>The answer: the open items closed by this cancellation (all of the invoice's items on success).</summary>
public sealed record FinanceInvoiceOpenItemCancellationResultV1(
    CommandOutcome Outcome,
    IReadOnlyList<Guid> OpenItemIds,
    bool IsReplay,
    string OriginalCorrelationId,
    IReadOnlyList<ValidationIssue> Issues);

/// <summary>
/// Host-owned fresh authority measurement for one invoice cancellation (the F-03 pattern). No permission string or grant
/// is declared here; which grant answers it is decided when a producer is wired, and an unmapped composition must remain
/// unavailable.
/// </summary>
public interface IFinanceInvoiceOpenItemCancellationAuthorityV1
{
    Task<PermissionDecision> CheckAsync(
        FinanceContext context,
        FinanceInvoiceProducerV1 producer,
        Guid invoiceId,
        CancellationToken cancellationToken);
}
