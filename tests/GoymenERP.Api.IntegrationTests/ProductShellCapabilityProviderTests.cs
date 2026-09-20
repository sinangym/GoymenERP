using System.Net;
using System.Net.Http.Json;
using GoymenERP.Api.Shell;
using GoymenERP.BuildingBlocks.Security;
using GoymenERP.BuildingBlocks.Shell;
using GoymenERP.Modules.Counterparty.Infrastructure.Shell;
using GoymenERP.Modules.Organization.Infrastructure.Shell;
using GoymenERP.Modules.Product.Infrastructure.Shell;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;

namespace GoymenERP.Api.IntegrationTests;

/// <summary>
/// Product's real shell capability provider: Product's four screens (U-01 list and 360 detail, U-02
/// create and basic edit) are published to the shell and granted only from the global Product
/// authority. These checks need no database; the granted/denied answer through the real host on a real
/// SQL Server is <see cref="ProductShellCapabilitySqlServerRelationalAcceptanceTests"/>.
/// </summary>
public sealed class ProductShellCapabilityProviderTests
{
    private static readonly Guid CompanyA = Guid.Parse("10000000-0000-0000-0000-000000000001");

    [Fact]
    public void The_registry_accepts_the_real_provider_with_its_pinned_ids_routes_and_operations()
    {
        var provider = new ProductShellCapabilityProvider();
        var registry = new ShellCapabilityRegistry([provider],
            new ShellCapabilityAuthorityCatalog([ShellCapabilityRegistry.ComputeCandidateRevision([provider])]));

        Assert.Equal("PRODUCT", provider.Owner);
        Assert.Equal(
            [
                ("product.items.create", "/urun/yeni", GroupSharedMasterOperation.DraftCreate),
                ("product.items.detail", "/urun/:productId", GroupSharedMasterOperation.Read),
                ("product.items.edit", "/urun/:productId/duzenle", GroupSharedMasterOperation.DraftBasicEdit),
                ("product.items.list", "/urun", GroupSharedMasterOperation.Read),
            ],
            registry.Registrations.Select(registration => (registration.Declaration.CapabilityId,
                registration.Declaration.RoutePath, registration.Declaration.GlobalProductOperation!.Value)));
        Assert.All(registry.Registrations, registration =>
        {
            // Global Product authority, never a company permission.
            Assert.Empty(registration.Declaration.RequiredPermissions);
            Assert.True(registration.Declaration.ImplementationAvailable);
            Assert.Null(registration.Declaration.ParentCapabilityId);
        });
    }

    [Fact]
    public void A_registry_with_the_Product_provider_stays_fail_closed_under_the_old_authority()
    {
        // The catalog only accepts the revisions it names: adding Product without approving the new
        // revision refuses the whole registry instead of serving an unreviewed set.
        IShellCapabilityProvider[] providers =
            [new CounterpartyShellCapabilityProvider(), new OrganizationShellCapabilityProvider(), new ProductShellCapabilityProvider()];

        Assert.Throws<InvalidOperationException>(() => new ShellCapabilityRegistry(providers,
            new ShellCapabilityAuthorityCatalog([ShellCapabilityRegistryAuthorities.CounterpartyV15PublishedCreateAndDetailAndOrganizationV11])));
        Assert.NotEqual(ShellCapabilityRegistryAuthorities.CounterpartyV15PublishedCreateAndDetailAndOrganizationV11,
            ShellCapabilityRegistry.ComputeCandidateRevision(providers));
    }

    [Fact]
    public async Task The_normal_host_registers_the_Product_provider_under_an_accepted_revision_and_grants_nothing_unconfigured()
    {
        await using var factory = new GoymenErpApiFactory();
        var providers = factory.Services.GetServices<IShellCapabilityProvider>().ToArray();
        Assert.Contains(providers, provider => provider is ProductShellCapabilityProvider);
        var revision = ShellCapabilityRegistry.ComputeCandidateRevision(providers);
        // The value printed here is the one the central authority constant must pin.
        Assert.True(factory.Services.GetRequiredService<ShellCapabilityAuthorityCatalog>().Accepts(revision),
            $"The host's shell authority catalog does not accept the registry revision {revision}.");

        // This host has no Product runtime configuration: no Product authority, so no Product screen.
        using var client = factory.CreateClientFor(TestUsers.CompanyA, CompanyA);
        using var response = await client.GetAsync("/api/v1/shell/capabilities");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var snapshot = (await response.Content.ReadFromJsonAsync<ShellCapabilitySnapshot>())!;
        Assert.DoesNotContain(snapshot.Capabilities, capability => capability.StartsWith("product.", StringComparison.Ordinal));
    }

    [Fact]
    public void The_C01_disposable_stack_stays_list_only_when_the_Product_provider_is_registered()
    {
        var services = new ServiceCollection();
        services.AddSingleton<IShellCapabilityProvider, CounterpartyShellCapabilityProvider>();
        services.AddSingleton<IShellCapabilityProvider, OrganizationShellCapabilityProvider>();
        services.AddSingleton<IShellCapabilityProvider, ProductShellCapabilityProvider>();

        var authority = C01E2eShellCapabilityActivation.Configure(services, new TestingEnvironment(),
            new ConfigurationBuilder().AddInMemoryCollection(C01E2eShellCapabilityActivationTests.ExactEnvelope()).Build());

        Assert.Equal(ShellCapabilityRegistryAuthorities.C01DisposableListOnlyV1, authority);
        var descriptor = Assert.Single(services, item => item.ServiceType == typeof(IShellCapabilityProvider));
        Assert.Equal(typeof(C01E2eShellCapabilityProvider), descriptor.ImplementationType);
    }

    private sealed class TestingEnvironment : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = "Testing";
        public string ApplicationName { get; set; } = "GoymenERP.Api.IntegrationTests";
        public string ContentRootPath { get; set; } = AppContext.BaseDirectory;
        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
    }
}
