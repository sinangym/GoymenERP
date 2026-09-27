using GoymenERP.Modules.Inventory.Domain.Stock;

namespace GoymenERP.Modules.Inventory.Application;

/// <summary>
/// Fail-closed safety gate for Sales-sourced physical stock (Finance review of #479, P2-2): a Sales-order
/// allocation, and the definite shipment that consumes one, may use only a position that THIS company both
/// owns and holds. Once customer-owned (or fason-held) Free positions can exist, anything else would let a
/// customer's goods be reserved and shipped as a company sale (AT-030, DEC-STK-001).
/// <para>
/// This is a gate, not a policy. It decides nothing about customer-owned or fason commercial links, returns to
/// the owner, or who may ship another party's goods: those stay with the open business decision #66/5830378162.
/// </para>
/// </summary>
public static class InventorySalesOwnershipGuardV1
{
    // TODO(#66/5830378162): when the customer-owned/fason commercial-link grain is decided, replace this blanket
    // refusal with the accepted rule (e.g. return-to-owner or fason shipment flows). Until then every non-company
    // owner or custodian is refused for Sales-sourced stock.

    /// <summary>True only when the owner AND the custodian are the server-resolved company itself.</summary>
    public static bool IsCompanyOwnedAndHeld(
        StockPartyKind ownerKind,
        Guid ownerId,
        StockPartyKind custodianKind,
        Guid custodianId,
        Guid companyId) =>
        companyId != Guid.Empty
        && ownerKind == StockPartyKind.Company && ownerId == companyId
        && custodianKind == StockPartyKind.Company && custodianId == companyId;
}
