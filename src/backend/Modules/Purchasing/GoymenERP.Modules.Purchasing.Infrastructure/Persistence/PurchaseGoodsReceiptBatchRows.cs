namespace GoymenERP.Modules.Purchasing.Infrastructure.Persistence;

/// <summary>
/// ALC-S16 (F-027; KLN-D43/KLN-D45): one bulk goods receipt of one kg order line. The kg comparison (expected = the line's
/// remaining kg at submission, received = Σ labels), its outcome and the Ayarlar tolerance in force are a snapshot of the
/// submission; only Status, Version, RowVersion and the decision facts (DecidedBy, DecidedAtUtc, DecisionReason) move after the
/// first commit, once (PendingApproval → Posted or Rejected). Never deleted.
/// </summary>
internal sealed class PurchaseGoodsReceiptBatchRow : IPurchasingTenantRow
{
    public Guid CompanyId { get; set; }
    public Guid BatchId { get; set; }
    public Guid OrderId { get; set; }
    public Guid LineId { get; set; }
    public Guid ProductId { get; set; }
    public string ProductVersion { get; set; } = string.Empty;
    public Guid UnitId { get; set; }
    public Guid LocationId { get; set; }
    public DateTimeOffset OccurredAtUtc { get; set; }
    public int Status { get; set; }
    public long Version { get; set; }
    public byte[] RowVersion { get; set; } = [];
    public decimal ExpectedQuantityKg { get; set; }
    public decimal ReceivedQuantityKg { get; set; }
    public int VarianceOutcome { get; set; }
    public decimal? TolerancePercent { get; set; }
    public bool VarianceAcknowledged { get; set; }

    /// <summary>
    /// AK-3b (KLN-D119, SAT-D437): the kg per piece recorded on the order line at approval, for a bulk receipt of a piece-unit line;
    /// null for a kilogram line and on older rows. When set, ExpectedQuantityKg is the theoretical kg of the received pieces.
    /// </summary>
    public decimal? KilogramsPerUnit { get; set; }
    public string PreparedBy { get; set; } = string.Empty;
    public DateTimeOffset PreparedAtUtc { get; set; }
    public string? DecidedBy { get; set; }
    public DateTimeOffset? DecidedAtUtc { get; set; }
    public string? DecisionReason { get; set; }
    public string CorrelationId { get; set; } = string.Empty;
}

/// <summary>
/// ALC-S16 (KLN-D41): one label of a bulk receipt. <see cref="LabelCode"/> is our code (the entered code, the supplier's label
/// number, or a generated one); it is unique in the company among unreleased labels. <see cref="SupplierLabelNumber"/> keeps the
/// supplier's original number apart and searchable. Only <see cref="Released"/> moves, once, when the batch is rejected (the code is
/// free again; the row stays as history). Never deleted.
/// </summary>
internal sealed class PurchaseGoodsReceiptBatchLabelRow : IPurchasingTenantRow
{
    public Guid CompanyId { get; set; }
    public Guid BatchId { get; set; }
    public int LineNo { get; set; }
    public Guid AcceptanceId { get; set; }
    public Guid LotId { get; set; }
    public Guid LabelId { get; set; }
    public Guid MovementId { get; set; }
    public string LabelCode { get; set; } = string.Empty;
    public int CodeSource { get; set; }
    public long? GeneratedNumber { get; set; }
    public string? SupplierLabelNumber { get; set; }
    public decimal MeasuredQuantityKg { get; set; }

    /// <summary>AK-3b (KLN-D119): the label's whole piece count on a piece-unit line; null on a kilogram line and on older rows.</summary>
    public int? PieceCount { get; set; }
    public string? HeatNumber { get; set; }
    public string? CoilNumber { get; set; }
    public bool Released { get; set; }
}

/// <summary>
/// ALC-S16: the bulk receipt's idempotency attempts (ADR-011 §2/§7), the <see cref="PurchaseAttemptRow"/> shape with the batch as the
/// recorded result. Append only.
/// </summary>
internal sealed class PurchaseGoodsReceiptBatchAttemptRow : IPurchasingTenantRow
{
    public Guid CompanyId { get; set; }
    public string CommandName { get; set; } = string.Empty;
    public string IdempotencyKeyHash { get; set; } = string.Empty;
    public string RequestHash { get; set; } = string.Empty;
    public string ActorId { get; set; } = string.Empty;
    public int Outcome { get; set; }
    public Guid? BatchId { get; set; }
    public string? RefusalJson { get; set; }
    public string CorrelationId { get; set; } = string.Empty;
    public DateTimeOffset RecordedAtUtc { get; set; }
}
