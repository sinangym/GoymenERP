using GoymenERP.Modules.Inventory.Domain.Movements;

namespace GoymenERP.Modules.Inventory.Application;

public enum InventoryManufacturingCostReadOutcomeV1 { Available = 1, Unreadable = 2 }
public enum InventoryMovementCostStampStatusV1 { Known = 1, Unavailable = 2 }
public enum InventoryIssueCostCurrencyStatusV1 { FunctionalCurrency = 1, Converted = 2, RateMissing = 3, Unreadable = 4 }

/// <summary>
/// A cost actually captured with one physical movement. TL and USD are independent immutable facts.
/// USD is production display only (URT-D82); it does not authorize a posting in Accounting.
/// LedgerVersion names the source read at staging time. Missing legacy stamps are Unavailable, never today's average.
/// </summary>
public sealed record InventoryMovementCostStampV1(InventoryMovementCostStampStatusV1 Status, Guid CurrencyId,
    decimal? UnitCostPerKg, decimal? Amount, long? LedgerVersion, string StampVersion, string? Reason);

/// <summary>
/// URT-D55/FNS-D73 entry-currency proof, captured at issue time. This is separate from the TL moving average;
/// no comparison or reconstruction of TL as FX times foreign cost is implied. A missing proof stays explicit.
/// </summary>
public sealed record InventoryIssueCostCurrencyV1(InventoryIssueCostCurrencyStatusV1 Status,
    Guid? OriginalCurrencyId, decimal? ForeignUnitCostPerKg, decimal? ExchangeRate,
    string SourceVersion, string? Reason);

public sealed record InventoryManufacturingCostIssueV1(Guid FactId, Guid MovementId, string SourceKind,
    StockMovementKind MovementKind, Guid ParentLabelId, Guid ProductId, string ProductVersion, decimal QuantityKg,
    InventoryMovementCostStampV1 Tl, InventoryMovementCostStampV1 Usd, InventoryIssueCostCurrencyV1 Currency);

public sealed record InventoryManufacturingCostReturnV1(Guid FactId, Guid MovementId, string SourceKind,
    StockMovementKind MovementKind, Guid ParentLabelId, decimal QuantityKg, Guid? SourceIssueMovementId,
    InventoryMovementCostStampV1 Tl, InventoryMovementCostStampV1 Usd);

/// <summary>Original physical declaration, not Manufacturing's later net corrected kg (which its owner supplies).</summary>
public sealed record InventoryManufacturingCostOutputV1(Guid FactId, Guid ReceiptMovementId, string SourceKind,
    StockMovementKind MovementKind, Guid ParentLabelId, Guid LabelId, string LabelCode,
    Guid ProductId, string ProductVersion, decimal DeclaredQuantityKg);

public sealed record InventoryManufacturingCostReadV1(InventoryManufacturingCostReadOutcomeV1 Outcome,
    Guid CompanyId, Guid ManufacturingOrderId, IReadOnlyList<InventoryManufacturingCostIssueV1> Issues,
    IReadOnlyList<InventoryManufacturingCostReturnV1> Returns, IReadOnlyList<InventoryManufacturingCostOutputV1> Outputs,
    string SourceVersion, string? Reason = null);

/// <summary>
/// Inventory owner read of its actual movement facts and immutable cost stamps for one company/order.
/// The consumer owns Manufacturing authority and cross-checks its own fact IDs and quantities.
/// Available with empty lists means a successful real empty read; Unreadable never masquerades as empty success.
/// </summary>
public interface IInventoryManufacturingCostReadPortV1
{
    Task<InventoryManufacturingCostReadV1> GetAsync(Guid companyId, Guid manufacturingOrderId,
        CancellationToken cancellationToken = default);
}
