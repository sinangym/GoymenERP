namespace GoymenERP.Modules.Inventory.Infrastructure.Persistence;

/// <summary>SAT-D338: immutable physical refusal evidence, not a movement, reservation, or retention queue.</summary>
internal sealed class NegativeStockAttemptRow : IInventoryTenantRow
{
    public Guid Id { get; set; }
    public Guid CompanyId { get; set; }
    public Guid MovementId { get; set; }
    public string ActorId { get; set; } = "";
    public string KeyHash { get; set; } = "";
    public string PayloadHash { get; set; } = "";
    public string PermissionCode { get; set; } = "";
    public string CorrelationId { get; set; } = "";
    public string SourceChannel { get; set; } = "";
    public DateTimeOffset RefusedAtUtc { get; set; }
    public Guid ProductId { get; set; }
    public Guid LocationId { get; set; }
    public Guid LabelId { get; set; }
    public string LabelCode { get; set; } = "";
    public int MovementKind { get; set; }
    public int LineNumber { get; set; }
    public string SourceKind { get; set; } = "";
    public Guid SourceId { get; set; }
    public Guid? SourceLineId { get; set; }
    public decimal RequestedKg { get; set; }
    public decimal AvailableKg { get; set; }
    public decimal MissingKg { get; set; }
    // Null is legacy, never an invented pre-validation observation.
    public long? ObservedCountBoundaryVersion { get; set; }
}
internal sealed class NegativeStockAttemptResolutionRow : IInventoryTenantRow
{
    public Guid CompanyId { get; set; }
    public Guid AttemptId { get; set; }
    public Guid StockCountId { get; set; }
    public DateTimeOffset DecidedAtUtc { get; set; }
    public string DecidedBy { get; set; } = "";
    public long? CountBoundaryVersion { get; set; }
}

/// <summary>Technical transaction mutex, not a stock quantity, authority, or business count version.</summary>
internal sealed class NegativeStockValidationGuardRow : IInventoryTenantRow
{
    public Guid CompanyId { get; set; }
    public long Version { get; set; }
}

/// <summary>The immutable, approved count which advanced this exact product/location boundary.</summary>
internal sealed class NegativeStockCountBoundaryRow : IInventoryTenantRow
{
    public Guid CompanyId { get; set; }
    public Guid ProductId { get; set; }
    public Guid LocationId { get; set; }
    public long Version { get; set; }
    public Guid StockCountId { get; set; }
    public DateTimeOffset DecidedAtUtc { get; set; }
    public string DecidedBy { get; set; } = "";
}
