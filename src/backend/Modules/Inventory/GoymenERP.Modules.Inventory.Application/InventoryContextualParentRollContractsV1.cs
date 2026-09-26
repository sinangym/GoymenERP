using GoymenERP.Modules.Inventory.Domain.Stock;

namespace GoymenERP.Modules.Inventory.Application;

/// <summary>
/// In-process owner port for a consumer that has already authorized an exact commercial stock
/// context. It does not change the public parent-roll candidate or validation contracts and does
/// not accept a location, role, account/version, reservation or availability assertion.
/// </summary>
public interface IInventoryContextualParentRollReferencesV1
{
    Task<InventoryParentRollCandidatesV1> SearchAsync(InventoryContextualParentRollQueryV1 query,
        CancellationToken cancellationToken = default);

    Task<InventoryParentRollReferenceV1> ValidateAsync(InventoryContextualParentRollSelectionV1 selection,
        CancellationToken cancellationToken = default);
}

public sealed record InventoryContextualParentRollQueryV1(
    InventoryParentRollPositionContextV1 PositionContext,
    string? Q = null,
    int Limit = 25);

public sealed record InventoryContextualParentRollSelectionV1(
    Guid LabelId,
    string ExpectedLabelVersion,
    InventoryParentRollPositionContextV1 PositionContext);

/// <summary>
/// Exact Inventory-owned identities supplied after consumer authorization. Location remains an
/// output of the stored positions and can never be forced by this input.
/// </summary>
public sealed record InventoryParentRollPositionContextV1(
    StockPartyKind OwnerKind,
    Guid OwnerId,
    StockPartyKind CustodianKind,
    Guid CustodianId);
