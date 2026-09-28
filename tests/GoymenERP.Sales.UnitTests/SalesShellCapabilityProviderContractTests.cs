using GoymenERP.BuildingBlocks.Shell;
using GoymenERP.Modules.Sales.Application.Contracts;
using GoymenERP.Modules.Sales.Infrastructure.Shell;

namespace GoymenERP.Sales.UnitTests;

/// <summary>
/// The published Sales shell provider: three screens plus the approve, commercial-revision, delivery-schedule and cancel controls, the frontend
/// lane's ids and routes (<c>src/sales/capabilities.ts</c> read from the repository, so a drift is red
/// here), the Sales permission names, and <c>ImplementationAvailable: true</c> (S03-c; the registry
/// never publishes a staged declaration, so the browser suite s03 accepts the flip rather than
/// preceding it).
/// </summary>
public sealed class SalesShellCapabilityProviderContractTests
{
    private const string FrontendCapabilities = "src/frontend/goymen-erp-web/src/sales/capabilities.ts";

    [Fact]
    public void The_three_screens_and_the_two_controls_are_declared_with_the_frontends_ids_and_routes()
    {
        var source = File.ReadAllText(Path.Combine(FindRepositoryRoot(), FrontendCapabilities));
        var provider = new SalesShellCapabilityProvider();
        var declarations = provider.GetDeclarations();

        // DEC-SAL-003 (1.2): the delivery-schedule control joins the approve and commercial-revision controls;
        // DEC-SAL-005 (1.3): the cancel control joins them; DEC-SAL-002 (1.4): the quantity-revision control.
        Assert.Equal(("SALES", "1.4"), (provider.Owner, provider.ContractVersion));
        Assert.Equal(8, declarations.Count);
        foreach (var declaration in declarations)
        {
            Assert.Contains($"'{declaration.CapabilityId}'", source);
            Assert.Contains($"'{declaration.RoutePath}'", source);
            Assert.Equal(ShellCapabilityScopeRequirement.CompanyWide, declaration.ScopeRequirement);
            Assert.Null(declaration.GlobalProductOperation);
            // S03-c: published to the shell (the grant stays a human gate; an ungranted actor still sees nothing).
            Assert.True(declaration.ImplementationAvailable);
        }

        var list = Assert.Single(declarations, item => item.CapabilityId == SalesShellCapabilityProvider.ListCapabilityId);
        Assert.Equal(("/satis/siparisler", ShellCapabilityRoutePrecedence.Static, ShellCapabilityContextRequirement.SingleOrAuthorizedMultiCompanyRead),
            (list.RoutePath, list.RoutePrecedence, list.ContextRequirement));
        Assert.Equal([SalesPermissionManifestV1.DraftsRead], list.RequiredPermissions);
        Assert.Null(list.ParentCapabilityId);

        var create = Assert.Single(declarations, item => item.CapabilityId == SalesShellCapabilityProvider.CreateCapabilityId);
        Assert.Equal(("/satis/siparisler/yeni", ShellCapabilityRoutePrecedence.Static, ShellCapabilityContextRequirement.SingleCompany),
            (create.RoutePath, create.RoutePrecedence, create.ContextRequirement));
        Assert.Equal([SalesPermissionManifestV1.DraftsCreate], create.RequiredPermissions);
        Assert.Null(create.ParentCapabilityId);

        var detail = Assert.Single(declarations, item => item.CapabilityId == SalesShellCapabilityProvider.DetailCapabilityId);
        Assert.Equal(("/satis/siparisler/:draftId", ShellCapabilityRoutePrecedence.ParameterizedAfterStatic), (detail.RoutePath, detail.RoutePrecedence));
        Assert.Equal([SalesPermissionManifestV1.DraftsRead], detail.RequiredPermissions);
        Assert.Null(detail.ParentCapabilityId);

        // SALES-A1: the decision is a control of the detail (the Finance bank-account approval pattern):
        // exactly the detail's route, screen, precedence, context and scope (the registry's control rule),
        // the read plus the approve name — nothing of its own.
        var approve = Assert.Single(declarations, item => item.CapabilityId == SalesShellCapabilityProvider.ApproveCapabilityId);
        Assert.Equal("sales.orders.approve", approve.CapabilityId);
        Assert.Equal((detail.RouteId, detail.RoutePath, detail.ScreenId, detail.RoutePrecedence, detail.ContextRequirement, detail.ScopeRequirement),
            (approve.RouteId, approve.RoutePath, approve.ScreenId, approve.RoutePrecedence, approve.ContextRequirement, approve.ScopeRequirement));
        Assert.Equal(SalesShellCapabilityProvider.DetailCapabilityId, approve.ParentCapabilityId);
        Assert.Equal([SalesPermissionManifestV1.DraftsRead, SalesPermissionManifestV1.OrdersApprove], approve.RequiredPermissions);

        var price = Assert.Single(declarations, item => item.CapabilityId == SalesShellCapabilityProvider.PriceWriteCapabilityId);
        Assert.Equal("sales.orders.price.write", price.CapabilityId);
        Assert.Equal((detail.RouteId, detail.RoutePath, detail.ScreenId, detail.RoutePrecedence, detail.ContextRequirement, detail.ScopeRequirement),
            (price.RouteId, price.RoutePath, price.ScreenId, price.RoutePrecedence, price.ContextRequirement, price.ScopeRequirement));
        Assert.Equal(SalesShellCapabilityProvider.DetailCapabilityId, price.ParentCapabilityId);
        Assert.Equal([SalesPermissionManifestV1.DraftsRead, SalesPermissionManifestV1.OrdersPriceWrite], price.RequiredPermissions);

        // DEC-SAL-003: the planner's delivery-date update is a control of the detail too — the read plus its own name.
        var schedule = Assert.Single(declarations, item => item.CapabilityId == SalesShellCapabilityProvider.DeliveryScheduleWriteCapabilityId);
        Assert.Equal("sales.orders.delivery-schedule.write", schedule.CapabilityId);
        Assert.Equal((detail.RouteId, detail.RoutePath, detail.ScreenId, detail.RoutePrecedence, detail.ContextRequirement, detail.ScopeRequirement),
            (schedule.RouteId, schedule.RoutePath, schedule.ScreenId, schedule.RoutePrecedence, schedule.ContextRequirement, schedule.ScopeRequirement));
        Assert.Equal(SalesShellCapabilityProvider.DetailCapabilityId, schedule.ParentCapabilityId);
        Assert.Equal([SalesPermissionManifestV1.DraftsRead, SalesPermissionManifestV1.OrdersDeliveryScheduleWrite], schedule.RequiredPermissions);

        // DEC-SAL-005: the impact preview and the cancellation are a control of the detail — the read plus the cancel name.
        var cancel = Assert.Single(declarations, item => item.CapabilityId == SalesShellCapabilityProvider.CancelCapabilityId);
        Assert.Equal("sales.orders.cancel", cancel.CapabilityId);
        Assert.Equal((detail.RouteId, detail.RoutePath, detail.ScreenId, detail.RoutePrecedence, detail.ContextRequirement, detail.ScopeRequirement),
            (cancel.RouteId, cancel.RoutePath, cancel.ScreenId, cancel.RoutePrecedence, cancel.ContextRequirement, cancel.ScopeRequirement));
        Assert.Equal(SalesShellCapabilityProvider.DetailCapabilityId, cancel.ParentCapabilityId);
        Assert.Equal([SalesPermissionManifestV1.DraftsRead, SalesPermissionManifestV1.OrdersCancel], cancel.RequiredPermissions);

        // DEC-SAL-002: the quantity revision is a control of the detail — the read plus its own name.
        var quantity = Assert.Single(declarations, item => item.CapabilityId == SalesShellCapabilityProvider.QuantityWriteCapabilityId);
        Assert.Equal("sales.orders.quantity.write", quantity.CapabilityId);
        Assert.Equal((detail.RouteId, detail.RoutePath, detail.ScreenId, detail.RoutePrecedence, detail.ContextRequirement, detail.ScopeRequirement),
            (quantity.RouteId, quantity.RoutePath, quantity.ScreenId, quantity.RoutePrecedence, quantity.ContextRequirement, quantity.ScopeRequirement));
        Assert.Equal(SalesShellCapabilityProvider.DetailCapabilityId, quantity.ParentCapabilityId);
        Assert.Equal([SalesPermissionManifestV1.DraftsRead, SalesPermissionManifestV1.OrdersQuantityWrite], quantity.RequiredPermissions);
    }

    [Fact]
    public void The_permission_names_are_the_manifests_and_spelled_as_the_card_decided()
    {
        Assert.Equal("sales.drafts.create", SalesPermissionManifestV1.DraftsCreate);
        Assert.Equal("sales.drafts.read", SalesPermissionManifestV1.DraftsRead);
        // SALES-A1 (Business 5737989917): the decision's own name, company-wide; its class and grant are the central writer's and a human's.
        Assert.Equal("sales.orders.approve", SalesPermissionManifestV1.OrdersApprove);
        // K3 (Sinan 5748258299): the SEPARATE price-write name; the create name does not confer it, and it confers no approval.
        Assert.Equal("sales.orders.price.write", SalesPermissionManifestV1.OrdersPriceWrite);
        // K3 P2 (Sinan 5748258299; D5 5749046401): the SEPARATE, NARROW price-read name — the draft read does not confer it,
        // and it confers nothing adjacent (no cost, margin, bank or Finance read).
        Assert.Equal("sales.orders.price.read", SalesPermissionManifestV1.OrdersPriceRead);
        // DEC-SAL-003: the SEPARATE planning/shipping name; the create, approve and price names do not confer it.
        Assert.Equal("sales.orders.delivery-schedule.write", SalesPermissionManifestV1.OrdersDeliveryScheduleWrite);
        // DEC-SAL-005: the SEPARATE cancel name; no other Sales name confers it and it confers none of them.
        Assert.Equal("sales.orders.cancel", SalesPermissionManifestV1.OrdersCancel);
        // DEC-SAL-002: the SEPARATE quantity-revision name; no other Sales name confers it and it confers no approval.
        Assert.Equal("sales.orders.quantity.write", SalesPermissionManifestV1.OrdersQuantityWrite);
        Assert.Equal((9, 8), (SalesPermissionManifestV1.Version, SalesPermissionManifestV1.Names.Count));
        Assert.Equal(new SalesDraftServiceBindings(),
            new SalesDraftServiceBindings(SalesPermissionManifestV1.DraftsCreate, SalesPermissionManifestV1.DraftsRead, SalesPermissionManifestV1.OrdersApprove,
                SalesPermissionManifestV1.OrdersPriceWrite, SalesPermissionManifestV1.OrdersPriceRead, SalesPermissionManifestV1.OrdersDeliveryScheduleWrite,
                SalesPermissionManifestV1.OrdersCancel, SalesPermissionManifestV1.OrdersQuantityWrite));
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
