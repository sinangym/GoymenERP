namespace GoymenERP.Modules.Accounting.Infrastructure.Persistence;

/// <summary>Append-only cancellation receipt. Also fences late finalization delivery and approval.</summary>
internal sealed class AccountingSourceInvoiceCancellationRow : IAccountingTenantRow
{
    public Guid CompanyId { get; set; }
    public Guid CancellationId { get; set; }
    public string DocumentKind { get; set; } = "";
    public Guid SourceDocumentId { get; set; }
    public Guid InvoiceId { get; set; }
    public long FinalizedVersion { get; set; }
    public Guid CancelledBy { get; set; }
    public DateTimeOffset CancelledAtUtc { get; set; }
    public string Reason { get; set; } = "";
    public string RequestHash { get; set; } = "";
    public string CorrelationId { get; set; } = "";
    public DateTimeOffset RecordedAtUtc { get; set; }
}
