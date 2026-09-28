using GoymenERP.BuildingBlocks.Shell;
using GoymenERP.Modules.Sales.Application.Contracts;

namespace GoymenERP.Modules.Sales.Infrastructure.Shell;

/// <summary>
/// Publishes the three Sales draft screens of #260 (S-02 list, S-03 create, S-04 detail) to the
/// shell — PUBLISHED (S03-c): every declaration is <c>ImplementationAvailable: true</c> since the
/// draft API (#321) landed; the registry never publishes a staged declaration, so the browser suite
/// <c>s03</c> is the acceptance of this flip, not its precondition (the Product #316 precedent; the
/// Sales documents carry no activation envelope rule). A declaration grants nothing: the screens open
/// only for an actor holding the permission, and the grant is a human gate. Company permissions,
/// the Counterparty way: the list and the detail share
/// <see cref="SalesPermissionManifestV1.DraftsRead"/>, the create screen needs
/// <see cref="SalesPermissionManifestV1.DraftsCreate"/>; selecting a product on it still needs the
/// Product owner's narrow selection read, measured by that owner. The ids and routes are the
/// frontend lane's (<c>src/sales/capabilities.ts</c>); a contract test pins them to that file.
/// The central registry requires registered names, so the host registers this provider only after
/// the central catalogue knows them, together with its own accepted revision (one module = one row
/// in the optional providers and one normal-host authority).
/// </summary>
public sealed class SalesShellCapabilityProvider : IShellCapabilityProvider
{
    public const string OwnerId = "SALES";
    public const string OwnerContractVersion = "1.4";

    public const string ListCapabilityId = "sales.drafts.list";
    public const string ListRouteId = "sales.drafts.list";
    public const string ListRoutePath = "/satis/siparisler";
    public const string ListScreenId = "S-02";
    public const string CreateCapabilityId = "sales.drafts.create";
    public const string CreateRouteId = "sales.drafts.create";
    public const string CreateRoutePath = "/satis/siparisler/yeni";
    public const string CreateScreenId = "S-03";
    public const string DetailCapabilityId = "sales.drafts.detail";
    public const string DetailRouteId = "sales.drafts.detail";
    public const string DetailRoutePath = "/satis/siparisler/:draftId";
    public const string DetailScreenId = "S-04";

    /// <summary>
    /// SALES-A1: the approve / reject control on the S-04 detail (the Finance bank-account approval
    /// pattern): the detail's route and access plus <see cref="SalesPermissionManifestV1.OrdersApprove"/>.
    /// A control, not a route (<c>ParentCapabilityId</c> = the detail). The submit button needs no
    /// control of its own: it follows the create capability the shell already publishes.
    /// </summary>
    public const string ApproveCapabilityId = "sales.orders.approve";

    /// <summary>K3: the separate commercial-revision control on S-04; create and approve never confer it.</summary>
    public const string PriceWriteCapabilityId = "sales.orders.price.write";

    /// <summary>DEC-SAL-003: the delivery-schedule update CONTROL of the S-04 detail (read + the schedule-write name).</summary>
    public const string DeliveryScheduleWriteCapabilityId = "sales.orders.delivery-schedule.write";

    /// <summary>
    /// DEC-SAL-005: the cancel CONTROL of the S-04 detail (read + the cancel name) — the impact preview and the cancellation of
    /// an approved order. Create, approve, price and schedule never confer it (shell contract 1.3).
    /// </summary>
    public const string CancelCapabilityId = "sales.orders.cancel";

    /// <summary>
    /// DEC-SAL-002: the quantity-revision CONTROL of the S-04 detail (read + the quantity-write name). Create, approve, price,
    /// schedule and cancel never confer it (shell contract 1.4).
    /// </summary>
    public const string QuantityWriteCapabilityId = "sales.orders.quantity.write";

    public string Owner => OwnerId;

    public string ContractVersion => OwnerContractVersion;

    public IReadOnlyCollection<ShellCapabilityDeclaration> GetDeclarations()
    {
        var detail = new ShellCapabilityDeclaration(
            DetailCapabilityId,
            DetailRouteId,
            DetailRoutePath,
            DetailScreenId,
            ShellCapabilityRoutePrecedence.ParameterizedAfterStatic,
            ShellCapabilityContextRequirement.SingleOrAuthorizedMultiCompanyRead,
            ShellCapabilityScopeRequirement.CompanyWide,
            [SalesPermissionManifestV1.DraftsRead],
            ImplementationAvailable: true);
        return
        [
        new ShellCapabilityDeclaration(
            ListCapabilityId,
            ListRouteId,
            ListRoutePath,
            ListScreenId,
            ShellCapabilityRoutePrecedence.Static,
            ShellCapabilityContextRequirement.SingleOrAuthorizedMultiCompanyRead,
            ShellCapabilityScopeRequirement.CompanyWide,
            [SalesPermissionManifestV1.DraftsRead],
            ImplementationAvailable: true),
        new ShellCapabilityDeclaration(
            CreateCapabilityId,
            CreateRouteId,
            CreateRoutePath,
            CreateScreenId,
            ShellCapabilityRoutePrecedence.Static,
            ShellCapabilityContextRequirement.SingleCompany,
            ShellCapabilityScopeRequirement.CompanyWide,
            [SalesPermissionManifestV1.DraftsCreate],
            ImplementationAvailable: true),
        detail,
        // SALES-A1: the decision is a control of the detail: exactly the detail's route, screen, precedence,
        // context and scope (the registry requires a control to keep its parent's), plus the approve name.
        // The command re-authorizes the company on the server on every send.
        detail with
        {
            CapabilityId = ApproveCapabilityId,
            ParentCapabilityId = DetailCapabilityId,
            RequiredPermissions = [SalesPermissionManifestV1.DraftsRead, SalesPermissionManifestV1.OrdersApprove],
        },
        detail with
        {
            CapabilityId = PriceWriteCapabilityId,
            ParentCapabilityId = DetailCapabilityId,
            RequiredPermissions = [SalesPermissionManifestV1.DraftsRead, SalesPermissionManifestV1.OrdersPriceWrite],
        },
        // DEC-SAL-003: the planner's delivery-schedule update is a control of the detail, like the price revision.
        detail with
        {
            CapabilityId = DeliveryScheduleWriteCapabilityId,
            ParentCapabilityId = DetailCapabilityId,
            RequiredPermissions = [SalesPermissionManifestV1.DraftsRead, SalesPermissionManifestV1.OrdersDeliveryScheduleWrite],
        },
        // DEC-SAL-005: the impact preview and the cancellation are a control of the detail, like the schedule update.
        detail with
        {
            CapabilityId = CancelCapabilityId,
            ParentCapabilityId = DetailCapabilityId,
            RequiredPermissions = [SalesPermissionManifestV1.DraftsRead, SalesPermissionManifestV1.OrdersCancel],
        },
        // DEC-SAL-002: the quantity revision is a control of the detail, like the price revision.
        detail with
        {
            CapabilityId = QuantityWriteCapabilityId,
            ParentCapabilityId = DetailCapabilityId,
            RequiredPermissions = [SalesPermissionManifestV1.DraftsRead, SalesPermissionManifestV1.OrdersQuantityWrite],
        },
        ];
    }
}
