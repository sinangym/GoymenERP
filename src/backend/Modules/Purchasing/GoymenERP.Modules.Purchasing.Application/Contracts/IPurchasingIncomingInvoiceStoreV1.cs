using GoymenERP.Modules.Purchasing.Domain.Invoices;

namespace GoymenERP.Modules.Purchasing.Application.Contracts;

public static class PurchaseIncomingInvoiceCommandsV1
{
    public const string Record = "purchasing.incoming-invoice.record.v1";
    public const string Match = "purchasing.incoming-invoice.match.v1";
    public const string ApproveVariance = "purchasing.incoming-invoice.approve-variance.v1";
    public const string Finalize = "purchasing.incoming-invoice.finalize.v1";
    public const string AcknowledgeFinance = "purchasing.incoming-invoice.acknowledge-finance.v1";
    public static bool IsKnown(string value) => value is Record or Match or ApproveVariance or Finalize or AcknowledgeFinance;
}

public sealed record PurchasingIncomingInvoiceWriteResult(PurchaseIncomingInvoiceSnapshot? Invoice,
    PurchasingTerminalRefusal? Refusal, bool IsReplay, string OriginalCorrelationId);

/// <summary>Purchasing-owned append-only invoice history. Caller supplies a fresh permission fence before reads and before save.
/// Receipt and order bindings are verified under the same general-line locks used by supplier revisions.</summary>
public interface IPurchasingIncomingInvoiceStoreV1
{
    Task<PurchaseIncomingInvoiceSnapshot?> FindIncomingInvoiceAsync(Guid invoiceId, CancellationToken cancellationToken = default);
    Task<PurchasingIncomingInvoiceWriteResult?> FindIncomingInvoiceAttemptAsync(string command,
        PurchasingWriteEnvelope envelope, string requestHash, CancellationToken cancellationToken = default);
    Task<PurchasingIncomingInvoiceWriteResult> RecordIncomingInvoiceAsync(PurchaseIncomingInvoice invoice,
        PurchasingWriteEnvelope envelope, string requestHash, Func<CancellationToken, Task> requireStillAuthorized,
        CancellationToken cancellationToken = default);
    /// <summary>ALS-D125: records with an audit note (the duplicate warning reason) in the same save. A store that cannot carry the
    /// note refuses instead of dropping it (fail closed); without a note it is the plain record.</summary>
    Task<PurchasingIncomingInvoiceWriteResult> RecordIncomingInvoiceAsync(PurchaseIncomingInvoice invoice,
        PurchasingWriteEnvelope envelope, string requestHash, Func<CancellationToken, Task> requireStillAuthorized, string? auditNote,
        CancellationToken cancellationToken = default) => auditNote is null
        ? RecordIncomingInvoiceAsync(invoice, envelope, requestHash, requireStillAuthorized, cancellationToken)
        : throw new NotSupportedException("This store cannot record an audit note.");
    Task<PurchasingIncomingInvoiceWriteResult?> MutateIncomingInvoiceAsync(Guid invoiceId, string command,
        PurchasingWriteEnvelope envelope, string requestHash, IReadOnlyList<Guid> orderIdsToLock,
        Func<PurchaseIncomingInvoice, CancellationToken, Task> mutate,
        Func<CancellationToken, Task> requireStillAuthorized, CancellationToken cancellationToken = default);
    Task<PurchasingIncomingInvoiceWriteResult> RecordIncomingInvoiceRefusalAsync(string command,
        PurchasingWriteEnvelope envelope, string requestHash, PurchasingTerminalRefusal refusal,
        Func<CancellationToken, Task> requireStillAuthorized, CancellationToken cancellationToken = default);
}
