namespace GoymenERP.Modules.Inventory.Application;

public enum InventoryManufacturingCostGenerationStateV1 { Pending = 1, Unreadable = 2, Recognized = 3, Reversed = 4 }

/// <summary>Physical identity and net mass come from the Manufacturing owner; cost is null while its source is unreadable.</summary>
public sealed record InventoryManufacturingCostGenerationOutputV1(Guid OutputId, Guid ReceiptMovementId, Guid LabelId,
    decimal NetQuantityKg, decimal? TlUnitCostPerKg, decimal? UsdUnitCostPerKg, decimal? TlAmount = null);

/// <summary>
/// A close may stage Pending/Unreadable without any amount. URT-D67 recognizes money only once the real cost becomes readable;
/// URT-D58 permits re-reading those sources later. ExpectedGenerationVersion is "0" for the first record, then the exact
/// current generation version. Later recognition preserves the same original physical output set and closed order version.
/// EvidenceJson is the immutable Manufacturing calculator/policy/monthly-price/input-stamp provenance, never a caller price.
/// </summary>
public sealed record InventoryManufacturingCostGenerationCommandV1(Guid CompanyId, Guid ManufacturingOrderId,
    Guid GenerationId, string ExpectedGenerationVersion, string ClosedOrderVersion,
    InventoryManufacturingCostGenerationStateV1 State, string InputFingerprint, string CalculatorVersion,
    string EvidenceJson, IReadOnlyList<InventoryManufacturingCostGenerationOutputV1> Outputs,
    string Reason, DateTimeOffset OccurredAtUtc);

public sealed record InventoryManufacturingCostGenerationReversalV1(Guid CompanyId, Guid ManufacturingOrderId,
    Guid GenerationId, string ExpectedGenerationVersion, string ExpectedOrderVersion,
    string Reason, DateTimeOffset OccurredAtUtc);

/// <summary>All physical positions are explicit; only company-owned/held positions enter the local moving average (KLN-D84).</summary>
public sealed record InventoryManufacturingCostPositionEffectV1(Guid LocationId, decimal QuantityKg, decimal TlValue,
    bool IncludedInMovingAverage);

/// <summary>Exact effect actually written to the 4dp average pool. The difference from the authoritative amount is
/// immutable arithmetic provenance, never an automatic accounting entry or a discarded cent.</summary>
public sealed record InventoryManufacturingCostPoolEffectV1(Guid CurrencyId, Guid LocationId, Guid ProductId,
    decimal QuantityKg, decimal BeforeQuantityKg, decimal? BeforeAverageCostPerKg,
    decimal AfterQuantityKg, decimal AfterAverageCostPerKg, decimal AuthoritativeValue,
    decimal RecordedPoolValueEffect, decimal AverageRoundingDifference);

/// <summary>
/// Original output quantity = remaining physical quantity + consumed quantity, with explicit TL effects.
/// Consumed TL is a separate SMM cost-difference voucher proposal; USD is display only. Old issues are never rewritten.
/// </summary>
public sealed record InventoryManufacturingCostGenerationOutputResultV1(Guid OutputId, Guid ReceiptMovementId, Guid LabelId,
    decimal NetQuantityKg, decimal RemainingQuantityKg, decimal ConsumedQuantityKg,
    decimal? TlUnitCostPerKg, decimal? RemainingTlValue, decimal? ConsumedTlDifference,
    decimal? UsdUnitCostPerKg, IReadOnlyList<InventoryManufacturingCostPositionEffectV1> Positions,
    decimal? OriginalTlAmount = null, IReadOnlyList<InventoryManufacturingCostPoolEffectV1>? PoolEffects = null);

public sealed record InventoryManufacturingCostGenerationResultV1(Guid CompanyId, Guid ManufacturingOrderId,
    Guid GenerationId, string GenerationVersion, string ClosedOrderVersion, InventoryManufacturingCostGenerationStateV1 State,
    string InputFingerprint, IReadOnlyList<InventoryManufacturingCostGenerationOutputResultV1> Outputs,
    Guid? CostDifferenceEventId, bool IsReplay, string OriginalCorrelationId);

/// <summary>
/// Single-use Inventory participant enlisted in Manufacturing's existing open DbConnection/DbTransaction. Inventory writes
/// its own immutable generation history, moving-average effects and outbox. Caller owns commit/rollback; no nested commit.
/// A pending source never becomes a new physical-close prerequisite. A recognized generation is reversed by its exact id/version.
/// </summary>
public interface IInventoryManufacturingCostGenerationParticipantV1 : IAsyncDisposable
{
    Task<InventoryManufacturingCostGenerationResultV1?> ReadActiveAsync(Guid manufacturingOrderId,
        CancellationToken cancellationToken = default);

    Task<InventoryManufacturingCostGenerationResultV1> StageAsync(InventoryManufacturingCostGenerationCommandV1 command,
        InventoryCommandMetadata metadata, CancellationToken cancellationToken = default);

    Task<InventoryManufacturingCostGenerationResultV1> ReverseAsync(InventoryManufacturingCostGenerationReversalV1 command,
        InventoryCommandMetadata metadata, CancellationToken cancellationToken = default);
}

public sealed class InventoryManufacturingCostGenerationException(int status, string code) : Exception(code)
{
    public int Status { get; } = status;
    public string Code { get; } = code;
}
