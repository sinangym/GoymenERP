namespace GoymenERP.Modules.Purchasing.Infrastructure.Persistence;

/// <summary>ALC-S6 immutable net adjustment, distinct from immutable original acceptance shares.</summary>
internal sealed class PurchaseReceiptAdjustmentRow : IPurchasingTenantRow
{
    public Guid CompanyId { get; set; }
    public Guid AdjustmentId { get; set; }
    public Guid OrderId { get; set; }
    public Guid AcceptanceId { get; set; }
    public Guid LineId { get; set; }
    public Guid? BatchId { get; set; }
    public int? BatchLineNo { get; set; }
    public decimal UndoQuantity { get; set; }
    public bool IsSupplierReturn { get; set; }
    public bool ReplacementExpected { get; set; }
    public Guid? NewAcceptanceId { get; set; }
    public decimal NewAcceptedQuantity { get; set; }
    public long OrderVersionAfter { get; set; }
    public bool Revived { get; set; }
    public decimal NetOpenedQuantity { get; set; }
    public string RecordedBy { get; set; } = string.Empty;
    public string Reason { get; set; } = string.Empty;
    public DateTimeOffset RecordedAtUtc { get; set; }
    public string CorrelationId { get; set; } = string.Empty;
    public string ResultJson { get; set; } = string.Empty;
}

/// <summary>One append-only command result and final order version for every correction in the batch.</summary>
internal sealed class PurchaseReceiptAdjustmentBatchRow : IPurchasingTenantRow
{
    public Guid CompanyId { get; set; }
    public Guid BatchId { get; set; }
    public Guid OrderId { get; set; }
    public long OrderVersionAfter { get; set; }
    public int LineCount { get; set; }
    public string RecordedBy { get; set; } = string.Empty;
    public string Reason { get; set; } = string.Empty;
    public DateTimeOffset RecordedAtUtc { get; set; }
    public string CorrelationId { get; set; } = string.Empty;
    public string ResultJson { get; set; } = string.Empty;
}

/// <summary>The first and subsequent cancellations remain immutable after ALS-D92 revival.</summary>
internal sealed class PurchaseOrderCancellationHistoryRow : IPurchasingTenantRow
{
    public Guid CompanyId { get; set; }
    public Guid OrderId { get; set; }
    public long OrderVersionAfter { get; set; }
    public string RecordedBy { get; set; } = string.Empty;
    public string Reason { get; set; } = string.Empty;
    public DateTimeOffset RecordedAtUtc { get; set; }
}
