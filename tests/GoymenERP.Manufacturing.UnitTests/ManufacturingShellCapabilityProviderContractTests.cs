using GoymenERP.BuildingBlocks.Security;
using GoymenERP.BuildingBlocks.Shell;
using GoymenERP.Modules.Manufacturing.Infrastructure.Shell;

namespace GoymenERP.Manufacturing.UnitTests;

/// <summary>
/// The published Manufacturing shell provider (MFG-UI card D1–D3; MFG-UI-F; MFG-ORD): four routes — the
/// order screen/list and plan create/detail — two controls on the order screen and two on plan detail. The six
/// already-consumed ids and all routes stay pinned to <c>src/manufacturing/capabilities.ts</c>; the two new
/// plan controls are pinned below until the owner consumer lands them.
/// company-wide scope, and <c>ImplementationAvailable: true</c> on all of them. Each control is its
/// parent with only its id, parent and permissions changed, so the registry's control rule (same route,
/// context and scope; the parent's permissions kept; never available under a staged parent) holds by
/// construction.
/// </summary>
public sealed class ManufacturingShellCapabilityProviderContractTests
{
    private const string FrontendCapabilities = "src/frontend/goymen-erp-web/src/manufacturing/capabilities.ts";

    [Fact]
    public void The_routes_and_controls_are_published_with_the_existing_frontend_boundary()
    {
        var source = File.ReadAllText(Path.Combine(FindRepositoryRoot(), FrontendCapabilities));
        var provider = new ManufacturingShellCapabilityProvider();
        var declarations = provider.GetDeclarations();

        Assert.Equal(("MANUFACTURING", "1.2"), (provider.Owner, provider.ContractVersion));
        Assert.Equal(8, declarations.Count);
        foreach (var declaration in declarations)
        {
            if (declaration.CapabilityId is not ManufacturingShellCapabilityProvider.PlanApproveCapabilityId
                and not ManufacturingShellCapabilityProvider.PlanOrderCreateCapabilityId)
            {
                Assert.Contains($"'{declaration.CapabilityId}'", source);
            }

            Assert.Contains($"'{declaration.RoutePath}'", source);
            Assert.Equal(
                (ShellCapabilityContextRequirement.SingleCompany, ShellCapabilityScopeRequirement.CompanyWide),
                (declaration.ContextRequirement, declaration.ScopeRequirement));
            Assert.Null(declaration.GlobalProductOperation);
            Assert.True(declaration.ImplementationAvailable);
        }

        var detail = Assert.Single(declarations, item => item.CapabilityId == "manufacturing.orders.detail");
        Assert.Equal(
            ("manufacturing.order-detail", "/uretim/emir/:orderId", "MFG-01",
                ShellCapabilityRoutePrecedence.ParameterizedAfterStatic),
            (detail.RouteId, detail.RoutePath, detail.ScreenId, detail.RoutePrecedence));
        Assert.Null(detail.ParentCapabilityId);
        Assert.Equal([ErpPermissions.ManufacturingOrdersRead], detail.RequiredPermissions);

        // The list is its own route and its own screen: static, so it never matches as an order id.
        var list = Assert.Single(declarations, item => item.CapabilityId == "manufacturing.orders.list");
        Assert.Equal(
            ("manufacturing.order-list", "/uretim/emirler", "MFG-02", ShellCapabilityRoutePrecedence.Static),
            (list.RouteId, list.RoutePath, list.ScreenId, list.RoutePrecedence));
        Assert.Null(list.ParentCapabilityId);
        Assert.Equal([ErpPermissions.ManufacturingOrdersRead], list.RequiredPermissions);

        var planCreate = Assert.Single(declarations, item => item.CapabilityId == "manufacturing.plans.create");
        Assert.Equal(
            ("manufacturing.plan-create", "/uretim/plan/yeni", "MFG-PLAN-CREATE",
                ShellCapabilityRoutePrecedence.Static),
            (planCreate.RouteId, planCreate.RoutePath, planCreate.ScreenId, planCreate.RoutePrecedence));
        Assert.Null(planCreate.ParentCapabilityId);
        Assert.Equal([ErpPermissions.ManufacturingPlansWrite], planCreate.RequiredPermissions);

        var planDetail = Assert.Single(declarations, item => item.CapabilityId == "manufacturing.plans.detail");
        Assert.Equal(
            ("manufacturing.plan-detail", "/uretim/plan/:planId", "MFG-PLAN-DETAIL",
                ShellCapabilityRoutePrecedence.ParameterizedAfterStatic),
            (planDetail.RouteId, planDetail.RoutePath, planDetail.ScreenId, planDetail.RoutePrecedence));
        Assert.Null(planDetail.ParentCapabilityId);
        Assert.Equal([ErpPermissions.ManufacturingPlansRead], planDetail.RequiredPermissions);

        // The controls: the same route as the order screen, its permission plus their own.
        AssertControl(declarations, detail, "manufacturing.orders.facts",
            [ErpPermissions.ManufacturingOrdersRead, ErpPermissions.ManufacturingFactsRecord]);
        AssertControl(declarations, detail, "manufacturing.orders.close",
            [ErpPermissions.ManufacturingOrdersRead, ErpPermissions.ManufacturingOrdersClose]);
        AssertControl(declarations, planDetail, "manufacturing.plans.approve",
            [ErpPermissions.ManufacturingPlansRead, ErpPermissions.ManufacturingPlansApprove]);
        AssertControl(declarations, planDetail, "manufacturing.plans.order-create",
            [ErpPermissions.ManufacturingPlansRead, ErpPermissions.ManufacturingOrdersCreate]);

        // Closing is not recording: neither control's permissions contain the other's own permission.
        var facts = Assert.Single(declarations, item => item.CapabilityId == "manufacturing.orders.facts");
        var close = Assert.Single(declarations, item => item.CapabilityId == "manufacturing.orders.close");
        Assert.DoesNotContain(ErpPermissions.ManufacturingOrdersClose, facts.RequiredPermissions);
        Assert.DoesNotContain(ErpPermissions.ManufacturingFactsRecord, close.RequiredPermissions);
    }

    private static void AssertControl(IReadOnlyCollection<ShellCapabilityDeclaration> declarations,
        ShellCapabilityDeclaration parent, string capabilityId, string[] permissions)
    {
        var control = Assert.Single(declarations, item => item.CapabilityId == capabilityId);
        Assert.Equal(parent.CapabilityId, control.ParentCapabilityId);
        Assert.Equal(permissions, control.RequiredPermissions);
        Assert.Equal(
            parent with
            {
                CapabilityId = control.CapabilityId,
                ParentCapabilityId = control.ParentCapabilityId,
                RequiredPermissions = control.RequiredPermissions,
            },
            control);
        Assert.Superset(parent.RequiredPermissions.ToHashSet(StringComparer.Ordinal),
            control.RequiredPermissions.ToHashSet(StringComparer.Ordinal));
    }

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "GoymenERP.slnx")))
        {
            directory = directory.Parent;
        }

        return directory?.FullName ?? throw new InvalidOperationException("Repository root not found.");
    }
}
