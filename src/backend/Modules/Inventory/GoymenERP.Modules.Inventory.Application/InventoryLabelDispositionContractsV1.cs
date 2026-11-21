namespace GoymenERP.Modules.Inventory.Application;

/// <summary>
/// F-009 D1 (URT-D31, §4.1): where a label's kilograms went, read from Inventory's own movements, balances and
/// allocations. Manufacturing reads it before an output correction to show the reversible kilograms and the reason
/// the rest is locked; the reversal command re-checks the same figures inside its transaction, so this read is never
/// the decision source (TOCTOU). Company-filtered, untracked and side-effect free; it carries no authority — the
/// caller checks its own permission around the call. Every kilogram value is an invariant <c>0.###</c> string.
/// </summary>
public interface IInventoryLabelDispositionReadPortV1
{
    /// <summary>
    /// The disposition of <paramref name="labelId"/> under <paramref name="companyId"/>. A company other than the
    /// server-resolved one, or an unknown label, reads as <see cref="InventoryLabelDispositionStatusV1.NotFound"/>;
    /// stored figures that do not reconcile read as <see cref="InventoryLabelDispositionStatusV1.Unreadable"/>, and
    /// the caller must then refuse (fail-closed).
    /// </summary>
    Task<InventoryLabelDispositionReadV1> GetAsync(
        Guid companyId,
        Guid labelId,
        CancellationToken cancellationToken = default);
}

public enum InventoryLabelDispositionStatusV1
{
    Value = 1,
    NotFound = 2,
    Unreadable = 3,
}

public sealed record InventoryLabelDispositionReadV1(
    InventoryLabelDispositionStatusV1 Status,
    InventoryLabelDispositionV1? Disposition)
{
    public static InventoryLabelDispositionReadV1 NotFound { get; } =
        new(InventoryLabelDispositionStatusV1.NotFound, null);

    public static InventoryLabelDispositionReadV1 Unreadable { get; } =
        new(InventoryLabelDispositionStatusV1.Unreadable, null);
}

/// <summary>Whether the label still has physical kilograms anywhere (free or blocked).</summary>
public enum InventoryLabelStockStateV1
{
    InStock = 1,
    OutOfStock = 2,
}

/// <summary>
/// The manufacturing output this label was received from. Null on <see cref="InventoryLabelDispositionV1.Output"/>
/// when the label was not received by a manufacturing output declaration.
/// </summary>
public sealed record InventoryLabelOutputOriginV1(
    Guid ManufacturingOrderId,
    Guid OutputId,
    Guid ReceiptMovementId,
    string ProducedKg,
    string ReversedKg,
    string FreeUnallocatedAtOutputPositionKg,
    string BlockedAtOutputPositionKg);

/// <summary>
/// The movement totals of the label by business reason (both directions, gross: a customer return does not reduce
/// <see cref="ShippedKg"/>, it is its own figure), its current balances and its active allocations.
/// <see cref="BlockedKg"/> is the quality-blocked part of <see cref="PhysicalKg"/>; blocked kilograms are never free.
/// <see cref="OtherInKg"/> and <see cref="OtherOutKg"/> hold every movement with no named reason here (for example a
/// goods receipt or an opening count). <see cref="LabelVersion"/> is the expected version of a following command.
/// </summary>
public sealed record InventoryLabelDispositionV1(
    Guid CompanyId,
    Guid LabelId,
    Guid ProductId,
    string LabelVersion,
    InventoryLabelStockStateV1 State,
    InventoryLabelOutputOriginV1? Output,
    string ShippedKg,
    string CustomerReturnedKg,
    string ManufacturingIssuedKg,
    string ManufacturingReturnedKg,
    string TransferDispatchedKg,
    string TransferAcceptedKg,
    string ScrappedKg,
    string CountAdjustedInKg,
    string CountAdjustedOutKg,
    string OtherInKg,
    string OtherOutKg,
    string PhysicalKg,
    string BlockedKg,
    string AllocatedKg);

/// <summary>
/// F-009 D5 / URT-D34 (§6, Inventory answer): the Free kilograms a label can still be allocated, for Manufacturing's
/// selection list and missing-input list before it allocates. Side-effect free and never the decision source: the
/// allocation command re-checks inside its transaction and stays all-or-nothing. Company-filtered and untracked; it
/// carries no authority.
/// </summary>
public interface IInventoryFreeQuantityReadPortV1
{
    /// <summary>
    /// <see cref="InventoryFreeQuantityV1.FreeKg"/> is the Free balance minus the active allocations on it, summed
    /// over the label's Free positions. The plan-input allocation command additionally needs the label's Free stock in
    /// exactly one position; <see cref="InventoryFreeQuantityV1.FreePositionCount"/> lets the caller see that.
    /// </summary>
    Task<InventoryFreeQuantityReadV1> GetAsync(
        Guid companyId,
        Guid labelId,
        CancellationToken cancellationToken = default);
}

public sealed record InventoryFreeQuantityReadV1(
    InventoryLabelDispositionStatusV1 Status,
    InventoryFreeQuantityV1? Value)
{
    public static InventoryFreeQuantityReadV1 NotFound { get; } =
        new(InventoryLabelDispositionStatusV1.NotFound, null);

    public static InventoryFreeQuantityReadV1 Unreadable { get; } =
        new(InventoryLabelDispositionStatusV1.Unreadable, null);
}

public sealed record InventoryFreeQuantityV1(
    Guid CompanyId,
    Guid LabelId,
    Guid ProductId,
    string LabelVersion,
    string FreeKg,
    int FreePositionCount);
