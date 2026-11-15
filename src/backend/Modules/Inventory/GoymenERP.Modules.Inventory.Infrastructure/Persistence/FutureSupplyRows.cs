namespace GoymenERP.Modules.Inventory.Infrastructure.Persistence;

/// <summary>
/// F-017 STOK §3.2 (F017-b): one future supply allocation — part of an approved purchase general line's not-yet-received quantity
/// held for one approved Sales order line. Quantities are in <see cref="PurchaseUnitId"/> (the purchase line's own unit). A row is
/// never deleted: a release lowers <see cref="RemainingQuantity"/> and raises <see cref="ReleasedQuantity"/> in place (same id,
/// same <see cref="CreatedAtUtc"/> and <see cref="ManualSequence"/>); consumed = allocated − remaining − released (F017-c).
/// </summary>
internal sealed class FutureSupplyAllocationRow : IInventoryTenantRow
{
    public Guid Id { get; set; }
    public Guid CompanyId { get; set; }
    public string SourceKind { get; set; } = string.Empty;
    public Guid PurchaseOrderId { get; set; }
    public Guid PurchaseOrderLineId { get; set; }
    public Guid PurchaseUnitId { get; set; }
    public Guid ProductId { get; set; }
    public string ProductVersion { get; set; } = string.Empty;
    public string DemandKind { get; set; } = string.Empty;
    public Guid DemandId { get; set; }
    public Guid DemandLineId { get; set; }
    public decimal AllocatedQuantity { get; set; }
    public decimal RemainingQuantity { get; set; }
    public decimal ReleasedQuantity { get; set; }

    /// <summary>The consumption order a user may set (F017-c); null means creation order.</summary>
    public int? ManualSequence { get; set; }

    public string Reason { get; set; } = string.Empty;
    public int Status { get; set; }
    public string? ReleaseReasonCode { get; set; }
    public string? ReleaseOrigin { get; set; }
    public string? ReleaseReason { get; set; }
    public long Version { get; set; }
    public byte[] RowVersion { get; set; } = [];

    /// <summary>The purchase order version the source answered at creation; an observation only, never a lock.</summary>
    public long ObservedPurchaseOrderVersion { get; set; }

    public DateTimeOffset CreatedAtUtc { get; set; }
    public DateTimeOffset UpdatedAtUtc { get; set; }
    public DateTimeOffset? ReleasedAtUtc { get; set; }
}

/// <summary>
/// F-017 STOK §3.2 step 4: the per purchase line ledger. <see cref="ActiveAllocated"/> is Σ remaining of the line's active
/// allocations, kept in the same save as every allocation change; <see cref="AllocatableSnapshot"/> is the purchase line's
/// allocatable quantity the last successful create saw (written only after the check passed). The row version makes two
/// writers of the same line conflict even if the application lock were missing.
/// </summary>
internal sealed class FutureSupplyLineRow : IInventoryTenantRow
{
    public Guid CompanyId { get; set; }
    public Guid PurchaseOrderId { get; set; }
    public Guid PurchaseOrderLineId { get; set; }
    public Guid PurchaseUnitId { get; set; }
    public decimal AllocatableSnapshot { get; set; }
    public decimal ActiveAllocated { get; set; }
    public long Version { get; set; }
    public byte[] RowVersion { get; set; } = [];
    public DateTimeOffset CreatedAtUtc { get; set; }
    public DateTimeOffset UpdatedAtUtc { get; set; }
}
