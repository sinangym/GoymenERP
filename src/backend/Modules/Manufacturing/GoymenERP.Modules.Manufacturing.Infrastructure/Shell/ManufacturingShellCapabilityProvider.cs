using GoymenERP.BuildingBlocks.Security;
using GoymenERP.BuildingBlocks.Shell;

namespace GoymenERP.Modules.Manufacturing.Infrastructure.Shell;

/// <summary>
/// Publishes the Manufacturing order and plan screens to the shell (MFG-UI card, coordinator-approved) —
/// PUBLISHED: every declaration is <c>ImplementationAvailable: true</c>, after the corresponding
/// browser suite accepted the staged screen (the S03-c / FIN-UI-1c pattern). The screen opens to whoever
/// holds the permission in the context company; publishing grants nothing (every grant is a human gate).
/// <list type="bullet">
/// <item><c>manufacturing.orders.detail</c> → <c>/uretim/emir/:orderId</c> on
/// <see cref="ErpPermissions.ManufacturingOrdersRead"/>.</item>
/// <item><c>manufacturing.orders.facts</c>: the fact forms inside that screen, a control on the same
/// route (<c>ParentCapabilityId</c>), adding <see cref="ErpPermissions.ManufacturingFactsRecord"/>.
/// Without it the forms are not drawn (card D3); the endpoints enforce either way.</item>
/// <item><c>manufacturing.plans.create</c> → <c>/uretim/plan/yeni</c> on
/// <see cref="ErpPermissions.ManufacturingPlansWrite"/>.</item>
/// <item><c>manufacturing.plans.detail</c> → <c>/uretim/plan/:planId</c> on
/// <see cref="ErpPermissions.ManufacturingPlansRead"/>.</item>
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
    public const string OwnerContractVersion = "1.1";

    public const string DetailCapabilityId = "manufacturing.orders.detail";
    public const string FactsCapabilityId = "manufacturing.orders.facts";
    public const string ListCapabilityId = "manufacturing.orders.list";
    public const string CloseCapabilityId = "manufacturing.orders.close";
    public const string DetailRouteId = "manufacturing.order-detail";
    public const string DetailRoutePath = "/uretim/emir/:orderId";
    public const string DetailScreenId = "MFG-01";
    public const string ListRouteId = "manufacturing.order-list";
    public const string ListRoutePath = "/uretim/emirler";
    public const string ListScreenId = "MFG-02";
    public const string PlanCreateCapabilityId = "manufacturing.plans.create";
    public const string PlanCreateRouteId = "manufacturing.plan-create";
    public const string PlanCreateRoutePath = "/uretim/plan/yeni";
    public const string PlanCreateScreenId = "MFG-PLAN-CREATE";
    public const string PlanDetailCapabilityId = "manufacturing.plans.detail";
    public const string PlanDetailRouteId = "manufacturing.plan-detail";
    public const string PlanDetailRoutePath = "/uretim/plan/:planId";
    public const string PlanDetailScreenId = "MFG-PLAN-DETAIL";

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
            ImplementationAvailable: true);

        var list = new ShellCapabilityDeclaration(
            ListCapabilityId,
            ListRouteId,
            ListRoutePath,
            ListScreenId,
            ShellCapabilityRoutePrecedence.Static,
            ShellCapabilityContextRequirement.SingleCompany,
            ShellCapabilityScopeRequirement.CompanyWide,
            [ErpPermissions.ManufacturingOrdersRead],
            ImplementationAvailable: true);

        var planCreate = new ShellCapabilityDeclaration(
            PlanCreateCapabilityId,
            PlanCreateRouteId,
            PlanCreateRoutePath,
            PlanCreateScreenId,
            ShellCapabilityRoutePrecedence.Static,
            ShellCapabilityContextRequirement.SingleCompany,
            ShellCapabilityScopeRequirement.CompanyWide,
            [ErpPermissions.ManufacturingPlansWrite],
            ImplementationAvailable: true);

        var planDetail = new ShellCapabilityDeclaration(
            PlanDetailCapabilityId,
            PlanDetailRouteId,
            PlanDetailRoutePath,
            PlanDetailScreenId,
            ShellCapabilityRoutePrecedence.ParameterizedAfterStatic,
            ShellCapabilityContextRequirement.SingleCompany,
            ShellCapabilityScopeRequirement.CompanyWide,
            [ErpPermissions.ManufacturingPlansRead],
            ImplementationAvailable: true);

        return
        [
            detail,
            detail with
            {
                CapabilityId = FactsCapabilityId,
                ParentCapabilityId = DetailCapabilityId,
                RequiredPermissions = [ErpPermissions.ManufacturingOrdersRead, ErpPermissions.ManufacturingFactsRecord],
            },
            // MFG-ORD: the close is a control of the order screen, like the fact forms — the same route
            // and context, its own permission on top of the read. Recording a fact never closes an order.
            detail with
            {
                CapabilityId = CloseCapabilityId,
                ParentCapabilityId = DetailCapabilityId,
                RequiredPermissions = [ErpPermissions.ManufacturingOrdersRead, ErpPermissions.ManufacturingOrdersClose],
            },
            list,
            planCreate,
            planDetail,
        ];
    }
}
