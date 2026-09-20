using GoymenERP.BuildingBlocks.Shell;
using GoymenERP.Modules.Sales.Application.Contracts;
using GoymenERP.Modules.Sales.Infrastructure.Shell;

namespace GoymenERP.Sales.UnitTests;

/// <summary>
/// The published Sales shell provider: three screens plus the SALES-A1 approve control, the frontend
/// lane's ids and routes (<c>src/sales/capabilities.ts</c> read from the repository, so a drift is red
/// here), the Sales permission names, and <c>ImplementationAvailable: true</c> (S03-c; the registry
/// never publishes a staged declaration, so the browser suite s03 accepts the flip rather than
/// preceding it).
/// </summary>
public sealed class SalesShellCapabilityProviderContractTests
{
    private const string FrontendCapabilities = "src/frontend/goymen-erp-web/src/sales/capabilities.ts";

    [Fact]
    public void The_three_screens_and_the_approve_control_are_declared_with_the_frontends_ids_and_routes()
    {
        var source = File.ReadAllText(Path.Combine(FindRepositoryRoot(), FrontendCapabilities));
        var provider = new SalesShellCapabilityProvider();
        var declarations = provider.GetDeclarations();

        Assert.Equal(("SALES", "1.0"), (provider.Owner, provider.ContractVersion));
        Assert.Equal(4, declarations.Count);
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
        // D2 (Sinan 5749046401): deciding one's OWN submitted order is a SEPARATE name, registered to nobody (deny-first).
        Assert.Equal("sales.orders.approve.own", SalesPermissionManifestV1.OrdersApproveOwn);
        Assert.Equal(6, SalesPermissionManifestV1.Names.Count);
        Assert.Equal(new SalesDraftServiceBindings(),
            new SalesDraftServiceBindings(SalesPermissionManifestV1.DraftsCreate, SalesPermissionManifestV1.DraftsRead, SalesPermissionManifestV1.OrdersApprove,
                SalesPermissionManifestV1.OrdersPriceWrite, SalesPermissionManifestV1.OrdersPriceRead, SalesPermissionManifestV1.OrdersApproveOwn));
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
