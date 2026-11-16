namespace GoymenERP.Modules.Inventory.Infrastructure.Persistence;

/// <summary>An actual physical entry whose cost is not yet readable. Append-only; no provisional zero enters an average.</summary>
internal sealed class PendingMovementCostRow : IInventoryTenantRow
{
    public Guid CompanyId { get; set; }
    public Guid MovementId { get; set; }
    public int LineNumber { get; set; }
    public Guid CurrencyId { get; set; }
    public Guid LabelId { get; set; }
    public Guid ProductId { get; set; }
    public Guid LocationId { get; set; }
    public decimal QuantityKg { get; set; }
    public string Reason { get; set; } = string.Empty;
    public DateTimeOffset RecordedAtUtc { get; set; }
}

/// <summary>One recognition of a pending entry; original physical facts and exit stamps remain unchanged.</summary>
internal sealed class MovementCostRecognitionRow : IInventoryTenantRow
{
    public Guid Id { get; set; }
    public Guid CompanyId { get; set; }
    public Guid MovementId { get; set; }
    public int LineNumber { get; set; }
    public Guid CurrencyId { get; set; }
    public Guid? GenerationId { get; set; }
    public decimal UnitCostPerKg { get; set; }
    public decimal RemainingQuantityKg { get; set; }
    public decimal ConsumedQuantityKg { get; set; }
    public string SourceVersion { get; set; } = string.Empty;
    public string ResultJson { get; set; } = string.Empty;
    public DateTimeOffset RecordedAtUtc { get; set; }
    public string CorrelationId { get; set; } = string.Empty;
}

/// <summary>Current pointer only. Every generation transition is also append-only in ManufacturingCostGenerationHistory.</summary>
internal sealed class ManufacturingCostGenerationRow : IInventoryTenantRow
{
    public Guid Id { get; set; }
    public Guid CompanyId { get; set; }
    public Guid ManufacturingOrderId { get; set; }
    public string ClosedOrderVersion { get; set; } = string.Empty;
    public long Version { get; set; }
    public int State { get; set; }
    public string OutputFactsJson { get; set; } = string.Empty;
    public string LastResultJson { get; set; } = string.Empty;
    public DateTimeOffset CreatedAtUtc { get; set; }
    public DateTimeOffset UpdatedAtUtc { get; set; }
    public byte[] RowVersion { get; set; } = [];
}

internal sealed class ManufacturingCostGenerationHistoryRow : IInventoryTenantRow
{
    public Guid CompanyId { get; set; }
    public Guid GenerationId { get; set; }
    public long Version { get; set; }
    public string InputFingerprint { get; set; } = string.Empty;
    public string InputEvidenceJson { get; set; } = string.Empty;
    public string ResultJson { get; set; } = string.Empty;
    public string ActorId { get; set; } = string.Empty;
    public string CorrelationId { get; set; } = string.Empty;
    public DateTimeOffset RecordedAtUtc { get; set; }
}
