using GoymenERP.Modules.Inventory.Domain.Stock;

namespace GoymenERP.Modules.Inventory.Application;

/// <summary>LOJ-D2/D5: the recipient Party owns the goods; this company holds them. An account is never compared here.</summary>
public static class InventorySubcontractOwnershipGuardV1
{
    public static bool IsCustomerOwnedAndHeld(StockPartyKind ownerKind, Guid ownerId,
        StockPartyKind custodianKind, Guid custodianId, Guid companyId, Guid customerPartyId) =>
        companyId != Guid.Empty && customerPartyId != Guid.Empty
        && ownerKind == StockPartyKind.Counterparty && ownerId == customerPartyId
        && custodianKind == StockPartyKind.Company && custodianId == companyId;
}
