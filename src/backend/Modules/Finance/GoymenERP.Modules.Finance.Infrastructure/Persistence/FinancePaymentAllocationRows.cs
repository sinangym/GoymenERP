namespace GoymenERP.Modules.Finance.Infrastructure.Persistence;

/// <summary>
/// F3a / API-20 (DEC-FIN-006): one distribution of a RECORDED manual bank payment (money out) to one open payable
/// item. It is not a second money event: the payment's outgoing money event is the only fact; this row records how
/// that fact settles a supplier item. Only <see cref="Status"/>, the release trace and the version ever change.
/// </summary>
internal sealed class FinancePaymentAllocationRow : IFinanceTenantRow
{
    public Guid Id { get; set; }
    public Guid CompanyId { get; set; }
    public Guid PaymentId { get; set; }
    public Guid OpenItemId { get; set; }
    public Guid CurrencyId { get; set; }
    public string AmountText { get; set; } = string.Empty;
    public string Reason { get; set; } = string.Empty;
    public int Status { get; set; }
    public long Version { get; set; }
    public string CreatedBy { get; set; } = string.Empty;
    public DateTimeOffset CreatedAtUtc { get; set; }
    public string? ReleasedBy { get; set; }
    public DateTimeOffset? ReleasedAtUtc { get; set; }
    public string? ReleaseReason { get; set; }
}

/// <summary>
/// One concurrency aggregate per recorded payment: its unallocated amount is the payment amount minus its active
/// allocations. Create/release update this row and the target open item in the same save; the version keeps two
/// targets from consuming the same remaining payment amount concurrently.
/// </summary>
internal sealed class FinancePaymentAllocationStateRow : IFinanceTenantRow
{
    public Guid CompanyId { get; set; }
    public Guid PaymentId { get; set; }
    public Guid CurrencyId { get; set; }
    public string OriginalAmountText { get; set; } = string.Empty;
    public string UnallocatedAmountText { get; set; } = string.Empty;
    public long Version { get; set; }
    public string UpdatedBy { get; set; } = string.Empty;
    public DateTimeOffset UpdatedAtUtc { get; set; }
}
