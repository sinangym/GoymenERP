namespace GoymenERP.Modules.Purchasing.Infrastructure.Persistence;

/// <summary>
/// ALC-S5 (B-19; SAT-D306, SAT-D392, SAT-D393, SAT-D396): one return to supplier of one goods acceptance (label) of an order line.
/// The source block, kg, cost basis and unit cost are a snapshot of the request; only Status, Version, RowVersion and the decision
/// facts (DecidedBy, DecidedAtUtc, DecisionReason) move after the first commit, once (PendingCostException → Recorded or Rejected).
/// Never deleted.
/// </summary>
internal sealed class PurchaseSupplierReturnRow : IPurchasingTenantRow
{
    public Guid CompanyId { get; set; }
    public Guid ReturnId { get; set; }
    public Guid ReturnLineId { get; set; }
    public Guid OrderId { get; set; }
    public Guid LineId { get; set; }
    public Guid GoodsAcceptanceId { get; set; }
    public Guid MovementId { get; set; }
    public int Status { get; set; }
    public long Version { get; set; }
    public byte[] RowVersion { get; set; } = [];
    public string SourceBlock { get; set; } = string.Empty;
    public decimal QuantityKg { get; set; }
    public int CostBasis { get; set; }
    public decimal UnitCostTryPerKg { get; set; }
    public bool ReplacementExpected { get; set; }
    public string Reason { get; set; } = string.Empty;
    public DateTimeOffset OccurredAtUtc { get; set; }
    public string RequestedBy { get; set; } = string.Empty;
    public DateTimeOffset RequestedAtUtc { get; set; }
    public string? DecidedBy { get; set; }
    public DateTimeOffset? DecidedAtUtc { get; set; }
    public string? DecisionReason { get; set; }
    public string CorrelationId { get; set; } = string.Empty;
}

/// <summary>
/// ALC-S5: the supplier return's idempotency attempts (ADR-011 §2/§7), the <see cref="PurchaseAttemptRow"/> shape with the return as
/// the recorded result. Append only.
/// </summary>
internal sealed class PurchaseSupplierReturnAttemptRow : IPurchasingTenantRow
{
    public Guid CompanyId { get; set; }
    public string CommandName { get; set; } = string.Empty;
    public string IdempotencyKeyHash { get; set; } = string.Empty;
    public string RequestHash { get; set; } = string.Empty;
    public string ActorId { get; set; } = string.Empty;
    public int Outcome { get; set; }
    public Guid? ReturnId { get; set; }
    public string? RefusalJson { get; set; }
    public string CorrelationId { get; set; } = string.Empty;
    public DateTimeOffset RecordedAtUtc { get; set; }
}
