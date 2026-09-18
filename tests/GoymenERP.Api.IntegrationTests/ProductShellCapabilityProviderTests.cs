using System.Net;
using System.Net.Http.Json;
using System.Text.RegularExpressions;
using GoymenERP.Api.ProductRuntime;
using GoymenERP.Api.Shell;
using GoymenERP.BuildingBlocks.Security;
using GoymenERP.BuildingBlocks.Shell;
using GoymenERP.Modules.Counterparty.Infrastructure.Shell;
using GoymenERP.Modules.Identity.Domain.Assignments;
using GoymenERP.Modules.Identity.Infrastructure.Persistence;
using GoymenERP.Modules.Organization.Infrastructure.Shell;
using GoymenERP.Modules.Product.Infrastructure.Shell;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;

namespace GoymenERP.Api.IntegrationTests;

/// <summary>
/// Product's real shell capability provider: Product's four screens (U-01 list and 360 detail, U-02
/// create and basic edit) are published to the shell and granted only from the global Product
/// authority. The registry and C-01 checks need no database; the granted/denied answer is measured
/// through the real host on a real SQL Server with a synthetic, isolated Product grant (never seeded).
/// </summary>
public sealed class ProductShellCapabilityProviderTests
{
    private static readonly Guid ActorId = Guid.Parse("20000000-0000-0000-0000-000000000003");
    private static readonly Guid ActorExternalId = Guid.Parse("21000000-0000-0000-0000-000000000003");
    private static readonly Guid SystemRoleId = Guid.Parse("30000000-0000-0000-0000-000000000003");
    private static readonly Guid CompanyA = Guid.Parse("10000000-0000-0000-0000-000000000001");

    private static readonly string[] ProductCapabilities =
        ["product.items.create", "product.items.detail", "product.items.edit", "product.items.list"];

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
            new ShellCapabilityAuthorityCatalog([ShellCapabilityRegistryAuthorities.CounterpartyV15AndOrganizationV10])));
        Assert.NotEqual(ShellCapabilityRegistryAuthorities.CounterpartyV15AndOrganizationV10,
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

    [SqlServerFact]
    public async Task Product_screens_follow_the_global_Product_authority_through_the_real_provider()
    {
        var baseConnection = Environment.GetEnvironmentVariable(SqlServerRelationalAcceptanceTests.ConnectionStringEnvironmentVariable)!;
        var databaseName = "goymen_erp_ci_" + Guid.NewGuid().ToString("N");
        var connection = new SqlConnectionStringBuilder(baseConnection) { InitialCatalog = databaseName }.ConnectionString;
        // The real host with its real providers; only the Product runtime is configured, as the pilot is.
        var factory = new SqlServerRelationalApiFactory(baseConnection, services =>
        {
            services.RemoveAll<ProductRuntimeOptions>();
            services.AddSingleton(new ProductRuntimeOptions(connection,
                new ProductPilotIssuerBinding(ActorId, ActorExternalId), Enumerable.Range(1, 32).Select(value => (byte)value).ToArray()));
        }, databaseName: databaseName);
        try
        {
            using var startup = factory.CreateClient();
            using var administrator = factory.CreateClientFor("group-bootstrap-admin", CompanyA);
            using var companyOnly = factory.CreateClientFor("company-a-user", CompanyA);

            // No Product grant yet: the company administrator role alone opens no Product screen.
            Assert.Empty(ProductOnly(await ReadAsync(administrator)));

            await using (var scope = factory.Services.CreateAsyncScope())
            {
                var identity = scope.ServiceProvider.GetRequiredService<IdentityDbContext>();
                foreach (var operation in Enum.GetValues<ProductMasterOperation>())
                    identity.Set<ProductMasterAssignment>().Add(new ProductMasterAssignment(Guid.NewGuid(), ActorId,
                        SystemRoleId, operation, ActorId, ActorExternalId, Guid.NewGuid(),
                        "Synthetic isolated SQL fixture; not a Sinan or production grant", DateTimeOffset.UtcNow.AddMinutes(-1), null));
                await identity.SaveChangesAsync();
            }

            // Granted: all four screens; the other actor, without a Product grant, none.
            Assert.Equal(ProductCapabilities, ProductOnly(await ReadAsync(administrator)));
            Assert.Empty(ProductOnly(await ReadAsync(companyOnly)));

            await using (var scope = factory.Services.CreateAsyncScope())
            {
                var identity = scope.ServiceProvider.GetRequiredService<IdentityDbContext>();
                foreach (var grant in await identity.Set<ProductMasterAssignment>().Where(row => row.UserId == ActorId && row.IsActive).ToArrayAsync())
                    grant.Revoke(Guid.NewGuid(), "Isolated Product shell revocation", DateTimeOffset.UtcNow);
                await identity.SaveChangesAsync();
            }

            // Revoked: none again, while the company administrator role stays assigned.
            Assert.Empty(ProductOnly(await ReadAsync(administrator)));
        }
        finally
        {
            await factory.DisposeAsync();
            if (!Regex.IsMatch(databaseName, "\\Agoymen_erp_ci_[0-9a-f]{32}\\z", RegexOptions.CultureInvariant))
                throw new InvalidOperationException("Refusing cleanup outside this test's generated database identity.");
            await using var master = new SqlConnection(new SqlConnectionStringBuilder(baseConnection) { InitialCatalog = "master" }.ConnectionString);
            await master.OpenAsync();
            await using var drop = master.CreateCommand();
            drop.CommandText = $"IF DB_ID(N'{databaseName}') IS NOT NULL BEGIN ALTER DATABASE [{databaseName}] SET SINGLE_USER WITH ROLLBACK IMMEDIATE; DROP DATABASE [{databaseName}]; END;";
            await drop.ExecuteNonQueryAsync();
        }
    }

    private static async Task<ShellCapabilitySnapshot> ReadAsync(HttpClient client) =>
        (await client.GetFromJsonAsync<ShellCapabilitySnapshot>("/api/v1/shell/capabilities"))!;

    private static string[] ProductOnly(ShellCapabilitySnapshot snapshot) =>
        [.. snapshot.Capabilities.Where(capability => capability.StartsWith("product.", StringComparison.Ordinal)).Order(StringComparer.Ordinal)];

    private sealed class TestingEnvironment : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = "Testing";
        public string ApplicationName { get; set; } = "GoymenERP.Api.IntegrationTests";
        public string ContentRootPath { get; set; } = AppContext.BaseDirectory;
        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
    }
}
