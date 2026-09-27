using GoymenERP.Modules.Inventory.Application;

namespace GoymenERP.Modules.Manufacturing.Application;

/// <summary>
/// The fail-closed guard at the Manufacturing input-issue entry: until the owner decides how a customer-owned /
/// fason input carries its commercial link, a parent roll is issued into production only when the exact Inventory
/// position the issue consumes is owned AND custodied by the context company.
/// <para>
/// Why here. Inventory now carries any owner/custodian through the issue (the physical context is Inventory's
/// fact, never repainted), and plan-time roll selection is the only other place that refuses customer-owned rolls.
/// A later change that let a plan reference a customer roll would otherwise have Manufacturing process customer
/// material with no commercial link — which would silently answer NEEDS-SINAN #66/5830378162 (planned-input
/// snapshot, plan-level snapshot or Inventory-position link; mixed contexts). This guard answers nothing about
/// that grain: it only keeps the public issue path closed.
/// </para>
/// <para>
/// The refusal is the existing, already-declared public 409 of this route,
/// <see cref="InventoryManufacturingRealizationCodesV1.ParentLabelContextConflict"/> — the same code (and the same
/// user text) the issue answered before the customer-context aggregate, so no client, contract or OpenAPI shape
/// changes. It is applied inside the owner transaction, before the commit, so a refused issue leaves no
/// Manufacturing fact, no Inventory movement and no command record.
/// </para>
/// <para>
/// TODO(#66/5830378162): replace this unconditional refusal with an explicit, owner-approved decision flag and
/// the decided commercial-link snapshot once NEEDS-SINAN is answered. No such flag exists yet, and none is added
/// here: enabling customer-owned or third-party-custodied input is a Business decision, not a technical default.
/// </para>
/// </summary>
public static class ManufacturingCustomerOwnedInputGuardV1
{
    /// <summary>The Inventory party-kind name of the company's own stock, as the position snapshot carries it.</summary>
    public const string CompanyPartyKind = "Company";

    /// <summary>The public refusal code of a refused issue (409), already declared on the input-issue route.</summary>
    public const string RefusalCode = InventoryManufacturingRealizationCodesV1.ParentLabelContextConflict;

    /// <summary>
    /// True only when the issued position is owned and custodied by <paramref name="companyId"/> itself. Anything
    /// else — a Counterparty owner (customer material, fason), a Counterparty custodian (third-party custody, e.g.
    /// an external depot), another company, an unknown kind or a missing snapshot — is refused.
    /// </summary>
    public static bool Admits(Guid companyId, ManufacturingInventoryPositionSnapshotV1? position) =>
        companyId != Guid.Empty
        && position is not null
        && string.Equals(position.OwnerKind, CompanyPartyKind, StringComparison.Ordinal)
        && position.OwnerId == companyId
        && string.Equals(position.CustodianKind, CompanyPartyKind, StringComparison.Ordinal)
        && position.CustodianId == companyId;
}
