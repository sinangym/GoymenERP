using GoymenERP.BuildingBlocks.Security;
using GoymenERP.BuildingBlocks.Shell;
using GoymenERP.Modules.Manufacturing.Infrastructure.Shell;

namespace GoymenERP.Manufacturing.UnitTests;

/// <summary>
/// The staged Manufacturing shell provider (MFG-UI card D1–D3): one route and one control on it,
/// the frontend's ids and route (<c>src/manufacturing/capabilities.ts</c> read from the repository,
/// so a drift is red here), company-wide scope, and <c>ImplementationAvailable: false</c> until the
/// browser suite accepts the screen.
/// </summary>
public sealed class ManufacturingShellCapabilityProviderContractTests
{
    private const string FrontendCapabilities = "src/frontend/goymen-erp-web/src/manufacturing/capabilities.ts";

    [Fact]
    public void The_order_route_and_its_fact_control_are_staged_with_the_frontends_ids()
    {
        var source = File.ReadAllText(Path.Combine(FindRepositoryRoot(), FrontendCapabilities));
        var provider = new ManufacturingShellCapabilityProvider();
        var declarations = provider.GetDeclarations();

        Assert.Equal(("MANUFACTURING", "1.0"), (provider.Owner, provider.ContractVersion));
        Assert.Equal(2, declarations.Count);
        foreach (var declaration in declarations)
        {
            Assert.Contains($"'{declaration.CapabilityId}'", source);
            Assert.Contains($"'{declaration.RoutePath}'", source);
            Assert.Equal(
                ("manufacturing.order-detail", "/uretim/emir/:orderId", "MFG-01"),
                (declaration.RouteId, declaration.RoutePath, declaration.ScreenId));
            Assert.Equal(
                (ShellCapabilityRoutePrecedence.ParameterizedAfterStatic, ShellCapabilityContextRequirement.SingleCompany,
                    ShellCapabilityScopeRequirement.CompanyWide),
                (declaration.RoutePrecedence, declaration.ContextRequirement, declaration.ScopeRequirement));
            Assert.Null(declaration.GlobalProductOperation);
            Assert.False(declaration.ImplementationAvailable);
        }

        var detail = Assert.Single(declarations, item => item.ParentCapabilityId is null);
        Assert.Equal("manufacturing.orders.detail", detail.CapabilityId);
        Assert.Equal([ErpPermissions.ManufacturingOrdersRead], detail.RequiredPermissions);

        // A control: the same route, its parent's permission plus the write one (card D3).
        var facts = Assert.Single(declarations, item => item.ParentCapabilityId is not null);
        Assert.Equal(("manufacturing.orders.facts", "manufacturing.orders.detail"), (facts.CapabilityId, facts.ParentCapabilityId));
        Assert.Equal([ErpPermissions.ManufacturingOrdersRead, ErpPermissions.ManufacturingFactsRecord], facts.RequiredPermissions);
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
