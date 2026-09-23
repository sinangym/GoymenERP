namespace GoymenERP.Modules.Inventory.Application;

/// <summary>Company-scoped physical label discovery for draft references, not stock availability.</summary>
public interface IInventoryParentRollCandidatesV1
{
    Task<InventoryParentRollCandidatesV1> SearchAsync(InventoryParentRollQueryV1 query,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// Revalidates a draft reference after the consumer's recorded-command replay lookup.
/// Does not reserve stock, compare planned kg, or authorize production use.
/// Success matches server CompanyId and the selection's LabelId/ExpectedLabelVersion exactly,
/// with DraftSelectable=true, DraftRefusalCode=null and ProductionUseRequiresRevalidation=true.
/// Nonselectable references throw 422. ReadConflict is retryable/unrecorded; VersionConflict is terminal.
/// </summary>
public interface IInventoryParentRollValidationV1
{
    Task<InventoryParentRollReferenceV1> ValidateAsync(InventoryParentRollSelectionV1 selection,
        CancellationToken cancellationToken = default);
}

public sealed record InventoryParentRollQueryV1(string? Q = null, int Limit = 25);
public sealed record InventoryParentRollSelectionV1(Guid LabelId, string ExpectedLabelVersion);
public sealed record InventoryParentRollCandidatesV1(
    IReadOnlyList<InventoryParentRollReferenceV1> Items, bool SearchWindowFull);

/// <summary>
/// Quantities are lossless kg text. UnblockedPhysicalKg means BlockState.Free physical buckets only;
/// no allocation/hold deduction or available/usable quantity is represented. Positions preserve
/// owner, custodian and location independently. Null heat/coil remains unknown, not a type test.
/// Customer-owned positions remain visible but require explicit production/commercial context.
/// ProductionUseRequiresRevalidation is always true, including for complete/unblocked own stock.
/// </summary>
public sealed record InventoryParentRollReferenceV1(
    Guid CompanyId, Guid LabelId, string LabelVersion, string LabelCode, Guid ProductId,
    InventoryLotV1 Lot, string? HeatNumber, string? CoilNumber,
    string DocumentStatus, IReadOnlyList<string> RequiredDocuments, IReadOnlyList<string> AttachedDocuments,
    string PhysicalKg, string UnblockedPhysicalKg, string BlockedPhysicalKg,
    IReadOnlyList<InventoryPositionV1> Positions,
    bool DraftSelectable, string? DraftRefusalCode, bool ProductionUseRequiresRevalidation);

public static class InventoryParentRollCodesV1
{
    public const string Unauthenticated = "inventory_parent_roll_unauthenticated";
    public const string Forbidden = "inventory_parent_roll_forbidden";
    public const string InvalidQuery = "inventory_parent_roll_invalid_query";
    public const string InvalidVersion = "inventory_label_version_invalid";
    public const string InvalidReference = "inventory_parent_roll_invalid_reference";
    public const string VersionConflict = "inventory_label_version_conflict";
    public const string ReadConflict = "inventory_parent_roll_read_conflict";
    public const string ContextRequired = "inventory_parent_roll_context_required";
    public const string NoPhysicalPosition = "inventory_parent_roll_no_physical_position";
    public const string Unavailable = "inventory_parent_roll_unavailable";
}

/// <summary>Closed read/refusal channel. Denials and missing/foreign ids disclose no record facts.</summary>
public sealed class InventoryParentRollException(int statusCode, string code) : Exception(code)
{
    public int StatusCode { get; } = statusCode;
    public string Code { get; } = code;
}
