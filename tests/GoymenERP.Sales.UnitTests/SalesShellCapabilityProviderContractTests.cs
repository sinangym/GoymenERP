using GoymenERP.BuildingBlocks.Shell;
using GoymenERP.Modules.Sales.Application.Contracts;
using GoymenERP.Modules.Sales.Infrastructure.Shell;

namespace GoymenERP.Sales.UnitTests;

/// <summary>
/// The staged Sales shell provider: three declarations, the frontend lane's ids and routes
/// (<c>src/sales/capabilities.ts</c> read from the repository, so a drift is red here), the Sales
/// permission names, and <c>ImplementationAvailable: false</c> until the browser suite accepts the
/// screens (the Cari pattern; a separate exact-head change flips them).
/// </summary>
public sealed class SalesShellCapabilityProviderContractTests
{
    private const string FrontendCapabilities = "src/frontend/goymen-erp-web/src/sales/capabilities.ts";

    [Fact]
    public void The_three_screens_are_declared_with_the_frontends_ids_and_routes()
    {
        var source = File.ReadAllText(Path.Combine(FindRepositoryRoot(), FrontendCapabilities));
        var provider = new SalesShellCapabilityProvider();
        var declarations = provider.GetDeclarations();

        Assert.Equal(("SALES", "1.0"), (provider.Owner, provider.ContractVersion));
        Assert.Equal(3, declarations.Count);
        foreach (var declaration in declarations)
        {
            Assert.Contains($"'{declaration.CapabilityId}'", source);
            Assert.Contains($"'{declaration.RoutePath}'", source);
            Assert.Equal(ShellCapabilityScopeRequirement.CompanyWide, declaration.ScopeRequirement);
            Assert.Null(declaration.GlobalProductOperation);
            Assert.Null(declaration.ParentCapabilityId);
            Assert.False(declaration.ImplementationAvailable);
        }

        var list = Assert.Single(declarations, item => item.CapabilityId == SalesShellCapabilityProvider.ListCapabilityId);
        Assert.Equal(("/satis/siparisler", ShellCapabilityRoutePrecedence.Static, ShellCapabilityContextRequirement.SingleOrAuthorizedMultiCompanyRead),
            (list.RoutePath, list.RoutePrecedence, list.ContextRequirement));
        Assert.Equal([SalesPermissionManifestV1.DraftsRead], list.RequiredPermissions);

        var create = Assert.Single(declarations, item => item.CapabilityId == SalesShellCapabilityProvider.CreateCapabilityId);
        Assert.Equal(("/satis/siparisler/yeni", ShellCapabilityRoutePrecedence.Static, ShellCapabilityContextRequirement.SingleCompany),
            (create.RoutePath, create.RoutePrecedence, create.ContextRequirement));
        Assert.Equal([SalesPermissionManifestV1.DraftsCreate], create.RequiredPermissions);

        var detail = Assert.Single(declarations, item => item.CapabilityId == SalesShellCapabilityProvider.DetailCapabilityId);
        Assert.Equal(("/satis/siparisler/:draftId", ShellCapabilityRoutePrecedence.ParameterizedAfterStatic), (detail.RoutePath, detail.RoutePrecedence));
        Assert.Equal([SalesPermissionManifestV1.DraftsRead], detail.RequiredPermissions);
    }

    [Fact]
    public void The_permission_names_are_the_manifests_and_spelled_as_the_card_decided()
    {
        Assert.Equal("sales.drafts.create", SalesPermissionManifestV1.DraftsCreate);
        Assert.Equal("sales.drafts.read", SalesPermissionManifestV1.DraftsRead);
        Assert.Equal(2, SalesPermissionManifestV1.Names.Count);
        Assert.Equal(new SalesDraftServiceBindings(), new SalesDraftServiceBindings(SalesPermissionManifestV1.DraftsCreate, SalesPermissionManifestV1.DraftsRead));
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
