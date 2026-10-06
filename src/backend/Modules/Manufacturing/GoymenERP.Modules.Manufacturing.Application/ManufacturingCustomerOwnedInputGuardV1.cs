using GoymenERP.Modules.Inventory.Application;
using GoymenERP.Modules.Manufacturing.Domain.Plans;

namespace GoymenERP.Modules.Manufacturing.Application;

/// <summary>
/// The fail-closed guard at the Manufacturing input-issue entry. A legacy/company input must be owned and custodied
/// by the context company. A customer/tolling input must be owned by the exact captured Counterparty party and
/// custodied by the context company.
/// <para>
/// Why here. Inventory now carries any owner/custodian through the issue (the physical context is Inventory's
/// fact, never repainted), and the planned input carries the exact immutable commercial context captured after
/// Counterparty authorization. The guard does not query or refresh that captured provenance during realization.
/// </para>
/// <para>
/// The refusal is the existing, already-declared public 409 of this route,
/// <see cref="InventoryManufacturingRealizationCodesV1.ParentLabelContextConflict"/> — the same code (and the same
/// user text) the issue answered before the customer-context aggregate, so no client, contract or OpenAPI shape
/// changes. It is applied inside the owner transaction, before the commit, so a refused issue leaves no
/// Manufacturing fact, no Inventory movement and no command record.
/// </para>
/// </summary>
public static class ManufacturingCustomerOwnedInputGuardV1
{
    /// <summary>The Inventory party-kind name of the company's own stock, as the position snapshot carries it.</summary>
    public const string CompanyPartyKind = "Company";

    /// <summary>The Inventory party-kind name used for a captured Counterparty owner.</summary>
    public const string CounterpartyPartyKind = "Counterparty";

    /// <summary>The public refusal code of a refused issue (409), already declared on the input-issue route.</summary>
    public const string RefusalCode = InventoryManufacturingRealizationCodesV1.ParentLabelContextConflict;

    /// <summary>
    /// Matches the Inventory position to the planned input's immutable context. Third-party custody, another
    /// company, another Counterparty, unknown kinds and missing snapshots are refused.
    /// </summary>
    public static bool Admits(Guid companyId, ManufacturingInventoryPositionSnapshotV1? position,
        PlanCommercialContextSnapshotV1? commercialContext = null)
    {
        if (companyId == Guid.Empty || position is null
            || !string.Equals(position.CustodianKind, CompanyPartyKind, StringComparison.Ordinal)
            || position.CustodianId != companyId)
        {
            return false;
        }

        return commercialContext is null
            ? string.Equals(position.OwnerKind, CompanyPartyKind, StringComparison.Ordinal)
              && position.OwnerId == companyId
            : string.Equals(position.OwnerKind, CounterpartyPartyKind, StringComparison.Ordinal)
              && position.OwnerId == commercialContext.PartyId;
    }
}
