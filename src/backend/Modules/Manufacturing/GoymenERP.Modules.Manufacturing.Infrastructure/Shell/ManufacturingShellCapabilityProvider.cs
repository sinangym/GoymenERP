using GoymenERP.BuildingBlocks.Security;
using GoymenERP.BuildingBlocks.Shell;

namespace GoymenERP.Modules.Manufacturing.Infrastructure.Shell;

/// <summary>
/// Publishes the Manufacturing order screen to the shell (MFG-UI card, coordinator-approved) —
/// STAGED: both declarations are <c>ImplementationAvailable: false</c> until the browser suite
/// <c>mfg</c> accepts the screen, when a separate exact-head change flips them (the Cari pattern,
/// card D1).
/// <list type="bullet">
/// <item><c>manufacturing.orders.detail</c> → <c>/uretim/emir/:orderId</c> on
/// <see cref="ErpPermissions.ManufacturingOrdersRead"/>.</item>
/// <item><c>manufacturing.orders.facts</c>: the fact forms inside that screen, a control on the same
/// route (<c>ParentCapabilityId</c>), adding <see cref="ErpPermissions.ManufacturingFactsRecord"/>.
/// Without it the forms are not drawn (card D3); the endpoints enforce either way.</item>
/// </list>
/// Company-wide scope only, as the API requires (K-1): the routes do not filter by work center.
/// <c>MFG-01</c> is a technical sub-view id, like ORG-01/02; the canonical production screens
/// (PR-09/13/14/15) are <c>DECISION_ONLY_REFERENCE_MISSING</c>, and this is not their locked reference.
/// The ids and route are the frontend's (<c>src/manufacturing/capabilities.ts</c>); a contract test
/// pins them to that file. The host registers this provider in the central writer's round, together
/// with its accepted authority revision.
/// </summary>
public sealed class ManufacturingShellCapabilityProvider : IShellCapabilityProvider
{
    public const string OwnerId = "MANUFACTURING";
    public const string OwnerContractVersion = "1.0";

    public const string DetailCapabilityId = "manufacturing.orders.detail";
    public const string FactsCapabilityId = "manufacturing.orders.facts";
    public const string DetailRouteId = "manufacturing.order-detail";
    public const string DetailRoutePath = "/uretim/emir/:orderId";
    public const string DetailScreenId = "MFG-01";

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
            ShellCapabilityContextRequirement.SingleCompany,
            ShellCapabilityScopeRequirement.CompanyWide,
            [ErpPermissions.ManufacturingOrdersRead],
            ImplementationAvailable: false);

        return
        [
            detail,
            detail with
            {
                CapabilityId = FactsCapabilityId,
                ParentCapabilityId = DetailCapabilityId,
                RequiredPermissions = [ErpPermissions.ManufacturingOrdersRead, ErpPermissions.ManufacturingFactsRecord],
            },
        ];
    }
}
