using GoymenERP.Modules.Inventory.Domain.Stock;

namespace GoymenERP.Modules.Inventory.Application;

/// <summary>
/// LOJ-D2/D5 (STK-S3): the fail-closed ownership gate of a subcontract ("fason teslim") delivery. The goods go back only to
/// the counterparty that owns them: a position passes only when it is owned by THAT counterparty and held by the
/// server-resolved company itself. Company-owned stock (that would be a sale, LOJ-D3), another counterparty's goods, and
/// goods held by a third party never pass.
/// <para>
/// This is a pure predicate, not yet a write path: the Sales gate (<see cref="InventorySalesOwnershipGuardV1"/>) still
/// guards every allocation and definite shipment until the subcontract allocation demand kind and its writer are wired.
/// </para>
/// </summary>
public static class InventoryContractProcessingOwnershipGuardV1
{
    /// <summary>True only when the owner is <paramref name="counterpartyId"/> and the custodian is the company itself.</summary>
    public static bool IsCounterpartyOwnedAndCompanyHeld(
        StockPartyKind ownerKind,
        Guid ownerId,
        StockPartyKind custodianKind,
        Guid custodianId,
        Guid companyId,
        Guid counterpartyId) =>
        companyId != Guid.Empty
        && counterpartyId != Guid.Empty
        && counterpartyId != companyId
        && ownerKind == StockPartyKind.Counterparty && ownerId == counterpartyId
        && custodianKind == StockPartyKind.Company && custodianId == companyId;
}
