using GoymenERP.Modules.Purchasing.Domain.Invoices;

namespace GoymenERP.Modules.Purchasing.Application.Contracts;

public sealed record PurchaseIncomingInvoiceVersionRequestV1(string ExpectedVersion);
public sealed record PurchaseIncomingInvoiceVarianceApprovalRequestV1(string ExpectedVersion, string Reason);

/// <summary>Owner result, projected to exact string money/quantity fields by the HTTP boundary.</summary>
public sealed record PurchaseIncomingInvoiceOperationResultV1(PurchaseIncomingInvoiceSnapshot Invoice,
    bool IsReplay, string OriginalCorrelationId, string? FinanceOutcome = null);

public interface IPurchaseIncomingInvoiceWorkflowV1
{
    Task<PurchaseIncomingInvoiceSnapshot> ReadAsync(Guid invoiceId, CancellationToken cancellationToken = default);
    Task<PurchaseIncomingInvoiceOperationResultV1> ApproveVarianceAsync(Guid invoiceId,
        PurchaseIncomingInvoiceVarianceApprovalRequestV1 request, PurchasingCommandMetadata metadata,
        CancellationToken cancellationToken = default);
    Task<PurchaseIncomingInvoiceOperationResultV1> FinalizeAsync(Guid invoiceId,
        PurchaseIncomingInvoiceVersionRequestV1 request, PurchasingCommandMetadata metadata,
        CancellationToken cancellationToken = default);
    Task<PurchaseIncomingInvoiceOperationResultV1> RetryFinanceAsync(Guid invoiceId,
        PurchaseIncomingInvoiceVersionRequestV1 request, PurchasingCommandMetadata metadata,
        CancellationToken cancellationToken = default);
}

public interface IPurchaseIncomingInvoiceDiagnosticsV1
{
    void FinanceHandoffFailed(Guid companyId, Guid invoiceId, string phase, Exception exception);
}
