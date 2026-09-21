using GoymenERP.Api.Shell;
using GoymenERP.BuildingBlocks.Shell;
using GoymenERP.Modules.Counterparty.Infrastructure.Shell;
using GoymenERP.Modules.Finance.Infrastructure.Shell;
using GoymenERP.Modules.Organization.Infrastructure.Shell;
using GoymenERP.Modules.Product.Infrastructure.Shell;

namespace GoymenERP.Api.IntegrationTests;

/// <summary>
/// FIN-UI-1: Finance's provider (published since FIN-UI-1c) through the REAL <see cref="ShellCapabilityRegistry"/> — its
/// owner, route, path, screen-id and control rules — alone and beside the other module providers, so
/// that registering it in the host cannot fail at start-up. No database; the host registration and
/// its authority constant are the central writer's.
/// </summary>
public sealed class FinanceShellCapabilityRegistryTests
{
    [Fact]
    public void The_registry_accepts_the_Finance_provider_with_one_screen_per_route_and_the_approval_as_a_control()
    {
        var provider = new FinanceShellCapabilityProvider();
        var registry = new ShellCapabilityRegistry([provider],
            new ShellCapabilityAuthorityCatalog([ShellCapabilityRegistry.ComputeCandidateRevision([provider])]));

        Assert.Equal(
            [
                ("finance.bank-accounts.approve", "F-04", "finance.bank-accounts.list"),
                ("finance.bank-accounts.list", "F-04", null),
                ("finance.bank-accounts.propose", "F-04-NEW", null),
                ("finance.receipts.detail", "F-02-DETAIL", null),
                ("finance.receipts.draft", "F-02-NEW", null),
                ("finance.receipts.list", "F-02", null),
            ],
            registry.Registrations.Select(registration => (registration.Declaration.CapabilityId,
                registration.Declaration.ScreenId, registration.Declaration.ParentCapabilityId)));
        Assert.All(registry.Registrations, registration =>
        {
            Assert.Equal(FinanceShellCapabilityProvider.OwnerId, registration.Owner);
            Assert.True(registration.Declaration.ImplementationAvailable);
        });
    }

    [Fact]
    public void The_Finance_provider_sits_beside_the_other_module_providers_without_a_collision()
    {
        // Route ids, paths and screen ids are unique across providers too; the combined set must hold.
        IShellCapabilityProvider[] providers =
        [
            new CounterpartyShellCapabilityProvider(), new OrganizationShellCapabilityProvider(),
            new ProductShellCapabilityProvider(), new FinanceShellCapabilityProvider(),
        ];

        var registry = new ShellCapabilityRegistry(providers,
            new ShellCapabilityAuthorityCatalog([ShellCapabilityRegistry.ComputeCandidateRevision(providers)]));

        Assert.Equal(6, registry.Registrations.Count(registration => registration.Owner == FinanceShellCapabilityProvider.OwnerId));
    }

    [Fact]
    public void Adding_the_Finance_provider_without_approving_its_revision_fails_closed()
    {
        // Registering the provider alone, without the authority constant, refuses the whole registry:
        // the two must land together (central).
        IShellCapabilityProvider[] providers =
        [
            new CounterpartyShellCapabilityProvider(), new OrganizationShellCapabilityProvider(),
            new ProductShellCapabilityProvider(), new FinanceShellCapabilityProvider(),
        ];

        Assert.Throws<InvalidOperationException>(() => new ShellCapabilityRegistry(providers,
            new ShellCapabilityAuthorityCatalog([ShellCapabilityRegistryAuthorities.CounterpartyV15PublishedListCreateAndDetailOrganizationV11AndProductV10])));
    }
}
