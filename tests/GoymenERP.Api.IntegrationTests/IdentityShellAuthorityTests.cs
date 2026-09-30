using GoymenERP.Api.Shell;
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

namespace GoymenERP.Api.IntegrationTests;

/// <summary>SY-01/SY-02: the normal host with Identity 1.0 beside every published module.</summary>
public sealed class IdentityShellAuthorityTests
{
    [Fact]
    public void Normal_host_revision_matches_the_exact_integrated_provider_tuple()
    {
        IShellCapabilityProvider[] providers =
        [
            new CounterpartyShellCapabilityProvider(),
            new OrganizationShellCapabilityProvider(),
            new ProductShellCapabilityProvider(),
            new SalesShellCapabilityProvider(),
            new FinanceShellCapabilityProvider(),
            new ManufacturingShellCapabilityProvider(),
            new LogisticsShellCapabilityProvider(),
            new PurchasingShellCapabilityProvider(),
            new AccountingShellCapabilityProvider(),
            new InventoryShellCapabilityProvider(),
            new IdentityShellCapabilityProvider(),
        ];
        var actual = ShellCapabilityRegistry.ComputeCandidateRevision(providers);

        Assert.True(string.Equals(
                ShellCapabilityRegistryAuthorities.CounterpartyV15PublishedListCreateAndDetailOrganizationV13ProductV12PublishedSalesV14PublishedFinanceV12PublishedManufacturingV15PublishedLogisticsV12PublishedPurchasingV14PublishedAccountingV13PublishedInventoryV15AndPublishedIdentityV10,
                actual,
                StringComparison.Ordinal),
            $"Identity normal-host authority drifted. Actual: {actual}");
    }
}
