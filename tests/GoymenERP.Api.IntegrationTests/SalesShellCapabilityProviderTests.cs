using System.Net;
using System.Net.Http.Json;
using GoymenERP.Api.Shell;
using GoymenERP.BuildingBlocks.Shell;
using GoymenERP.Modules.Counterparty.Infrastructure.Shell;
using GoymenERP.Modules.Organization.Infrastructure.Shell;
using GoymenERP.Modules.Product.Infrastructure.Shell;
using GoymenERP.Modules.Sales.Application.Contracts;
using GoymenERP.Modules.Sales.Infrastructure.Shell;
using Microsoft.Extensions.DependencyInjection;

namespace GoymenERP.Api.IntegrationTests;

/// <summary>
/// Sales's real shell capability provider, published (S03-c): the three draft screens (S-02 list,
/// S-03 create, S-04 detail) are published to the shell and open only for an actor holding the Sales
/// permission. These checks need no database; the granted/denied answer through the real host on a real
/// SQL Server is <see cref="SalesShellCapabilitySqlServerRelationalAcceptanceTests"/>.
/// </summary>
public sealed class SalesShellCapabilityProviderTests
{
    private static readonly Guid CompanyA = Guid.Parse("10000000-0000-0000-0000-000000000001");

    [Fact]
    public void The_registry_accepts_the_published_provider_with_its_pinned_ids_routes_and_permissions()
    {
        var provider = new SalesShellCapabilityProvider();
        var registry = new ShellCapabilityRegistry([provider],
            new ShellCapabilityAuthorityCatalog([ShellCapabilityRegistry.ComputeCandidateRevision([provider])]));

        Assert.Equal("SALES", provider.Owner);
        // The three screens (one permission each, no parent) and the SALES-A1 approve CONTROL of the
        // detail (the detail's route; read + approve; parent = the detail) — the Finance approval pattern.
        Assert.Equal(
            [
                ("sales.drafts.create", "/satis/siparisler/yeni", SalesPermissionManifestV1.DraftsCreate, (string?)null),
                ("sales.drafts.detail", "/satis/siparisler/:draftId", SalesPermissionManifestV1.DraftsRead, null),
                ("sales.drafts.list", "/satis/siparisler", SalesPermissionManifestV1.DraftsRead, null),
                ("sales.orders.approve", "/satis/siparisler/:draftId",
                    $"{SalesPermissionManifestV1.DraftsRead}+{SalesPermissionManifestV1.OrdersApprove}", "sales.drafts.detail"),
            ],
            registry.Registrations.Select(registration => (registration.Declaration.CapabilityId,
                registration.Declaration.RoutePath, string.Join('+', registration.Declaration.RequiredPermissions),
                registration.Declaration.ParentCapabilityId)));
        Assert.All(registry.Registrations, registration =>
        {
            // Company permissions, published; never a global Product operation.
            Assert.True(registration.Declaration.ImplementationAvailable);
            Assert.Null(registration.Declaration.GlobalProductOperation);
        });
    }

    [Fact]
    public void A_registry_with_the_published_Sales_provider_stays_fail_closed_under_the_staged_authority()
    {
        // The catalog only accepts the revisions it names: publishing the Sales screens without approving
        // the new revision refuses the whole registry instead of serving an unreviewed set.
        IShellCapabilityProvider[] providers =
        [
            new CounterpartyShellCapabilityProvider(), new OrganizationShellCapabilityProvider(),
            new ProductShellCapabilityProvider(), new SalesShellCapabilityProvider(),
        ];

        Assert.Throws<InvalidOperationException>(() => new ShellCapabilityRegistry(providers,
            new ShellCapabilityAuthorityCatalog([ShellCapabilityRegistryAuthorities.CounterpartyV15OrganizationV10AndProductV10])));
        Assert.NotEqual(ShellCapabilityRegistryAuthorities.CounterpartyV15OrganizationV10AndProductV10,
            ShellCapabilityRegistry.ComputeCandidateRevision(providers));
    }

    [Fact]
    public async Task The_normal_host_registers_the_published_Sales_provider_under_an_accepted_revision_and_grants_nothing_ungranted()
    {
        await using var factory = new GoymenErpApiFactory();
        var providers = factory.Services.GetServices<IShellCapabilityProvider>().ToArray();
        Assert.Contains(providers, provider => provider is SalesShellCapabilityProvider);
        var revision = ShellCapabilityRegistry.ComputeCandidateRevision(providers);
        // The value printed here is the one the central authority constant must pin.
        Assert.True(factory.Services.GetRequiredService<ShellCapabilityAuthorityCatalog>().Accepts(revision),
            $"The host's shell authority catalog does not accept the registry revision {revision}.");

        // A published declaration grants nothing: this actor holds no Sales permission, so no Sales screen.
        using var client = factory.CreateClientFor(TestUsers.CompanyA, CompanyA);
        using var response = await client.GetAsync("/api/v1/shell/capabilities");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var snapshot = (await response.Content.ReadFromJsonAsync<ShellCapabilitySnapshot>())!;
        Assert.DoesNotContain(snapshot.Capabilities, capability => capability.StartsWith("sales.", StringComparison.Ordinal));
    }
}
