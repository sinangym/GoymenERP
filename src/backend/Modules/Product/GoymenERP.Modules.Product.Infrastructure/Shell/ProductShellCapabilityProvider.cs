using GoymenERP.BuildingBlocks.Security;
using GoymenERP.BuildingBlocks.Shell;

namespace GoymenERP.Modules.Product.Infrastructure.Shell;

/// <summary>
/// Publishes Product's four screens to the shell (U-01 list and 360 detail, U-02 create and basic
/// edit), so an actor holding the global Product authority reaches them in the normal runtime.
/// <list type="bullet">
/// <item><b>Authority is not company permission.</b> Every declaration carries its exact
/// <see cref="GroupSharedMasterOperation"/> and no company permission: the shell grants it from the
/// server's global Product authority snapshot (explicit Product master assignments), never from a
/// company role. The central registry pins these ids, routes and operations and refuses anything
/// else (owner <c>PRODUCT</c>, single-company context, any granted scope, no parent control).</item>
/// <item><b>No new permission name.</b> The operations are the ones the Product runtime already
/// enforces on every API call; publishing a capability decides nothing the API does not.</item>
/// <item><b>Unconfigured runtime shows nothing.</b> Without the Product runtime's issuer
/// configuration there is no Product authority service, so no actor is granted any of these.</item>
/// </list>
/// The ids and routes are the frontend lane's (<c>src/product/capabilities.ts</c>) and the shell's
/// (<c>src/shell/capabilities.ts</c>); a drift test pins those two together.
/// </summary>
public sealed class ProductShellCapabilityProvider : IShellCapabilityProvider
{
    public const string OwnerId = "PRODUCT";
    public const string OwnerContractVersion = "1.0";

    public const string ListCapabilityId = "product.items.list";
    public const string DetailCapabilityId = "product.items.detail";
    public const string CreateCapabilityId = "product.items.create";
    public const string EditCapabilityId = "product.items.edit";

    public string Owner => OwnerId;

    public string ContractVersion => OwnerContractVersion;

    public IReadOnlyCollection<ShellCapabilityDeclaration> GetDeclarations() =>
    [
        Declaration(ListCapabilityId, "product.list", "/urun", "U-01",
            ShellCapabilityRoutePrecedence.Static, GroupSharedMasterOperation.Read),
        Declaration(DetailCapabilityId, "product.detail", "/urun/:productId", "U-01-360",
            ShellCapabilityRoutePrecedence.ParameterizedAfterStatic, GroupSharedMasterOperation.Read),
        Declaration(CreateCapabilityId, "product.create", "/urun/yeni", "U-02",
            ShellCapabilityRoutePrecedence.Static, GroupSharedMasterOperation.DraftCreate),
        Declaration(EditCapabilityId, "product.edit", "/urun/:productId/duzenle", "U-02-EDIT",
            ShellCapabilityRoutePrecedence.ParameterizedAfterStatic, GroupSharedMasterOperation.DraftBasicEdit),
    ];

    private static ShellCapabilityDeclaration Declaration(string capabilityId, string routeId, string routePath,
        string screenId, ShellCapabilityRoutePrecedence precedence, GroupSharedMasterOperation operation) =>
        new ShellCapabilityDeclaration(
            capabilityId,
            routeId,
            routePath,
            screenId,
            precedence,
            ShellCapabilityContextRequirement.SingleCompany,
            ShellCapabilityScopeRequirement.AnyGrantedScope,
            RequiredPermissions: [],
            ImplementationAvailable: true,
            GlobalProductOperation: operation);
}
