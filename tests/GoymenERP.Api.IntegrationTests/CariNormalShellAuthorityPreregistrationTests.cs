using GoymenERP.Api.Shell;
using GoymenERP.BuildingBlocks.Shell;
using GoymenERP.Modules.Accounting.Infrastructure.Shell;
using GoymenERP.Modules.Counterparty.Infrastructure.Shell;
using GoymenERP.Modules.Finance.Infrastructure.Shell;
using GoymenERP.Modules.Logistics.Infrastructure.Shell;
using GoymenERP.Modules.Manufacturing.Infrastructure.Shell;
using GoymenERP.Modules.Organization.Infrastructure.Shell;
using GoymenERP.Modules.Product.Infrastructure.Shell;
using GoymenERP.Modules.Purchasing.Infrastructure.Shell;
using GoymenERP.Modules.Sales.Infrastructure.Shell;

namespace GoymenERP.Api.IntegrationTests;

public sealed class CariNormalShellAuthorityPreregistrationTests
{
    [Fact]
    public void Three_flag_normal_authorities_move_while_disposable_envelopes_keep_their_accepted_revisions()
    {
        IShellCapabilityProvider[] optionalProviders =
        [
            new OrganizationShellCapabilityProvider(),
            new ProductShellCapabilityProvider(),
            new SalesShellCapabilityProvider(),
            new FinanceShellCapabilityProvider(),
            new ManufacturingShellCapabilityProvider(),
            new LogisticsShellCapabilityProvider(),
            new PurchasingShellCapabilityProvider(),
            new AccountingShellCapabilityProvider(),
        ];

        for (var optionalCount = 0; optionalCount <= optionalProviders.Length; optionalCount++)
        {
            IShellCapabilityProvider[] current =
            [
                new CounterpartyShellCapabilityProvider(),
                .. optionalProviders.Take(optionalCount),
            ];
            IShellCapabilityProvider[] obsoleteTwoFlag =
            [
                new ListStagedCounterpartyProvider(),
                .. optionalProviders.Take(optionalCount),
            ];

            Assert.NotEqual(
                ShellCapabilityRegistry.ComputeCandidateRevision(obsoleteTwoFlag),
                ShellCapabilityRegistry.ComputeCandidateRevision(current));
        }

        Assert.Equal(
            ShellCapabilityRegistryAuthorities.C01DisposableListOnlyV1,
            ShellCapabilityRegistry.ComputeCandidateRevision([new C01E2eShellCapabilityProvider()]));
        Assert.Equal(
            ShellCapabilityRegistryAuthorities.C01DisposableCariListDetailV1,
            ShellCapabilityRegistry.ComputeCandidateRevision([new C01E2eCariListDetailShellCapabilityProvider()]));
        Assert.Equal(
            ShellCapabilityRegistryAuthorities.C01DisposableLogisticsInstructionsV1,
            ShellCapabilityRegistry.ComputeCandidateRevision(
            [
                new C01E2eShellCapabilityProvider(),
                new C01E2eLogisticsInstructionsShellCapabilityProvider(),
            ]));
    }

    private sealed class ListStagedCounterpartyProvider : IShellCapabilityProvider
    {
        public string Owner => CounterpartyShellCapabilityProvider.OwnerId;

        public string ContractVersion => CounterpartyShellCapabilityProvider.OwnerContractVersion;

        public IReadOnlyCollection<ShellCapabilityDeclaration> GetDeclarations() =>
            Array.AsReadOnly(new CounterpartyShellCapabilityProvider().GetDeclarations()
                .Select(declaration => declaration.CapabilityId == CounterpartyShellCapabilityProvider.ListCapabilityId
                    ? declaration with { ImplementationAvailable = false }
                    : declaration)
                .ToArray());
    }
}
