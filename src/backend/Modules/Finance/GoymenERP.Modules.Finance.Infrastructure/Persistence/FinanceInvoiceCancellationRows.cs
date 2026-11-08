namespace GoymenERP.Modules.Finance.Infrastructure.Persistence;

/// <summary>
/// F2b / API-11 (SAT-D96): the receipt of one invoice cancellation — idempotency (key hash, payload hash, actor, stored
/// answer) and the cancellation facts. Append-only; at most one per invoice and source kind.
/// </summary>
internal sealed class FinanceInvoiceCancellationRow : IFinanceTenantRow
{
    public Guid Id { get; set; }
    public Guid CompanyId { get; set; }
    public string SourceKind { get; set; } = string.Empty;
    public Guid InvoiceId { get; set; }
    public string InvoiceVersion { get; set; } = string.Empty;
    public string Reason { get; set; } = string.Empty;
    public string IdempotencyKeyHash { get; set; } = string.Empty;
    public string PayloadHash { get; set; } = string.Empty;
    public string ActorId { get; set; } = string.Empty;
    public string ResultJson { get; set; } = string.Empty;
    public string CorrelationId { get; set; } = string.Empty;
    public string? CausationId { get; set; }
    public DateTimeOffset CancelledAtUtc { get; set; }
}

/// <summary>
/// F2b: one open item closed by a cancellation — the reversal fact the party ledger shows (the item's original amount,
/// on the cancellation day). Append-only; an open item is reversed at most once.
/// </summary>
internal sealed class FinanceInvoiceCancellationItemRow : IFinanceTenantRow
{
    public Guid CompanyId { get; set; }
    public Guid CancellationId { get; set; }
    public Guid OpenItemId { get; set; }
    public string ReversedAmountText { get; set; } = string.Empty;
}
