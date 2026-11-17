using GoymenERP.Modules.Sales.Domain.Invoices;

namespace GoymenERP.Modules.Sales.Application.Contracts;

public static class SalesSubcontractInvoiceLifecycleCommandsV1
{
    public const string Submit = "sales.subcontract-service-invoice.submit.v1";
    public const string Approve = "sales.subcontract-service-invoice.approve.v1";
    public const string Reject = "sales.subcontract-service-invoice.reject.v1";
    public const string Cancel = "sales.subcontract-service-invoice.cancel.v1";
    public const string Match = "sales.subcontract-service-invoice.match.v1";
    public const string Finalize = "sales.subcontract-service-invoice.finalize.v1";
    public const string ResendFinance = "sales.subcontract-service-invoice.resend-finance.v1";
    public static bool IsKnown(string name) => name is Submit or Approve or Reject or Cancel or Match or Finalize or ResendFinance;
}

public sealed record SalesSubcontractInvoiceReplayV1(SalesSubcontractServiceInvoice? Invoice,
    SalesDraftTerminalRefusal? Refusal, string OriginalCorrelationId);

public interface ISalesSubcontractInvoiceLifecycleStoreV1
{
    Task<SalesSubcontractInvoiceReplayV1?> FindLifecycleReplayAsync(SalesDraftWriteEnvelope envelope,
        string requestHash, string commandName, CancellationToken cancellationToken = default);
    Task<SalesSubcontractServiceInvoiceStoreResult?> UpdateLifecycleAsync(SalesDraftWriteEnvelope envelope,
        string requestHash, string commandName, Guid invoiceId, Action<SalesSubcontractServiceInvoice, DateTimeOffset> mutate,
        SalesDraftStateAudit audit, CancellationToken cancellationToken = default);
    Task<SalesSubcontractInvoiceReplayV1> RecordLifecycleRefusalAsync(SalesDraftWriteEnvelope envelope,
        string requestHash, string commandName, SalesDraftTerminalRefusal refusal, CancellationToken cancellationToken = default);
    Task<Guid?> FindOfficialClaimAsync(string officialNo, string ettn, CancellationToken cancellationToken = default);
}

public interface ISalesSubcontractInvoiceLifecycleServiceV1
{
    Task<SalesSubcontractServiceInvoiceCommandResult> SubmitSubcontractInvoiceAsync(Guid invoiceId,
        SalesDraftStateCommandRequest request, SalesCommandMetadata metadata, CancellationToken cancellationToken = default);
    Task<SalesSubcontractServiceInvoiceCommandResult> ApproveSubcontractInvoiceAsync(Guid invoiceId,
        SalesDraftStateCommandRequest request, SalesCommandMetadata metadata, CancellationToken cancellationToken = default);
    Task<SalesSubcontractServiceInvoiceCommandResult> RejectSubcontractInvoiceAsync(Guid invoiceId,
        SalesDraftStateCommandRequest request, SalesCommandMetadata metadata, CancellationToken cancellationToken = default);
    Task<SalesSubcontractServiceInvoiceCommandResult> CancelSubcontractInvoiceAsync(Guid invoiceId,
        SalesDraftStateCommandRequest request, SalesCommandMetadata metadata, CancellationToken cancellationToken = default);
    Task<SalesSubcontractServiceInvoiceCommandResult> MatchSubcontractInvoiceAsync(Guid invoiceId,
        MatchSalesInvoiceRequest request, SalesCommandMetadata metadata, CancellationToken cancellationToken = default);
    Task<SalesSubcontractServiceInvoiceCommandResult> FinalizeSubcontractInvoiceAsync(Guid invoiceId,
        SalesDraftStateCommandRequest request, SalesCommandMetadata metadata, CancellationToken cancellationToken = default);
    Task<SalesSubcontractServiceInvoiceCommandResult> ResendSubcontractInvoiceToFinanceAsync(Guid invoiceId,
        SalesDraftStateCommandRequest request, SalesCommandMetadata metadata, CancellationToken cancellationToken = default);
}
