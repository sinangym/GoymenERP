using GoymenERP.Api.Shell;
using GoymenERP.BuildingBlocks.Security;
using GoymenERP.BuildingBlocks.Shell;
using GoymenERP.Modules.Accounting.Infrastructure.Shell;
using GoymenERP.Modules.Counterparty.Infrastructure.Shell;
using GoymenERP.Modules.Finance.Infrastructure.Shell;
using GoymenERP.Modules.Identity.Infrastructure.Shell;
using GoymenERP.Modules.Inventory.Infrastructure.Shell;
using GoymenERP.Modules.Logistics.Infrastructure.Shell;
using GoymenERP.Modules.Manufacturing.Infrastructure.Shell;
using GoymenERP.Modules.Organization.Infrastructure.Shell;
using GoymenERP.Modules.Product.Infrastructure.Shell;
using GoymenERP.Modules.Purchasing.Infrastructure.Shell;
using GoymenERP.Modules.Sales.Infrastructure.Shell;
using GoymenERP.Modules.Settings.Infrastructure.Shell;
using GoymenERP.Modules.Work.Domain;
using GoymenERP.Modules.Work.Infrastructure.Shell;
using Microsoft.Extensions.DependencyInjection;

namespace GoymenERP.Api.IntegrationTests;

/// <summary>WP9: Work's shell provider declares only Work permissions, grants nothing, and the actual host accepts it.</summary>
public sealed class WorkShellCapabilityProviderTests
{
    [Fact]
    public void Work_declares_the_my_tasks_route_and_its_assign_control_with_Work_permissions_only()
    {
        var provider = new WorkShellCapabilityProvider();
        Assert.Equal(("WORK", "1.0"), (provider.Owner, provider.ContractVersion));
        // TEKNIK_TUZAKLAR 1: records with a collection member compare by reference, so each declaration is projected.
        static string Comparable(ShellCapabilityDeclaration declaration) => string.Join('\u001f',
            declaration.CapabilityId, declaration.RouteId, declaration.RoutePath, declaration.ScreenId, declaration.RoutePrecedence.ToString(),
            declaration.ContextRequirement.ToString(), declaration.ScopeRequirement.ToString(),
            declaration.ImplementationAvailable ? "available" : "staged", declaration.ParentCapabilityId ?? "<no-parent>",
            string.Join(',', declaration.RequiredPermissions));
        Assert.Equal(new[]
            {
                "work.tasks.mine\u001fwork.tasks-mine\u001f/gorevlerim\u001fW-TASKS-01\u001fStatic\u001fSingleCompany\u001fCompanyWide\u001favailable\u001f<no-parent>\u001fwork.tasks.complete",
                "work.tasks.assign\u001fwork.tasks-mine\u001f/gorevlerim\u001fW-TASKS-01\u001fStatic\u001fSingleCompany\u001fCompanyWide\u001favailable\u001fwork.tasks.mine\u001fwork.tasks.complete,work.tasks.assign",
            },
            provider.GetDeclarations().Select(Comparable).ToArray());
        Assert.All(provider.GetDeclarations().SelectMany(declaration => declaration.RequiredPermissions), permission =>
        {
            Assert.Contains(permission, WorkPermissionManifestV1.Names);
            Assert.True(ErpPermissions.IsKnown(permission));
            Assert.DoesNotContain(permission, ErpPermissions.Critical);
        });
    }

    [Fact]
    public void The_actual_host_including_Work_uses_its_exact_provider_authority()
    {
        IShellCapabilityProvider[] providers =
        [
            new CounterpartyShellCapabilityProvider(), new OrganizationShellCapabilityProvider(),
            new ProductShellCapabilityProvider(), new SalesShellCapabilityProvider(),
            new FinanceShellCapabilityProvider(), new ManufacturingShellCapabilityProvider(),
            new LogisticsShellCapabilityProvider(), new PurchasingShellCapabilityProvider(),
            new AccountingShellCapabilityProvider(), new InventoryShellCapabilityProvider(),
            new IdentityShellCapabilityProvider(), new ProductPriceListShellCapabilityProvider(),
            new SettingsShellCapabilityProvider(), new ProductPaintMembershipShellCapabilityProvider(),
            new WorkShellCapabilityProvider(),
        ];
        var actual = ShellCapabilityRegistry.ComputeCandidateRevision(providers);
        Assert.True(string.Equals(ShellCapabilityRegistryAuthorities.FullNormalHostWithSettingsPaintMembershipsAndWork, actual,
            StringComparison.Ordinal), $"Work normal-host authority drifted. Actual: {actual}");
        var services = new ServiceCollection();
        foreach (var provider in providers) services.AddSingleton(typeof(IShellCapabilityProvider), provider.GetType());
        var configured = C01E2eShellCapabilityActivation.Configure(services,
            new Microsoft.Extensions.Hosting.Internal.HostingEnvironment { EnvironmentName = "Testing" },
            new Microsoft.Extensions.Configuration.ConfigurationBuilder().Build());
        Assert.Equal(actual, configured);
        _ = new ShellCapabilityRegistry(providers, new ShellCapabilityAuthorityCatalog([actual]));
    }
}
