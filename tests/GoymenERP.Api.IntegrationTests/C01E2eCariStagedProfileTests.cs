using System.Net;
using GoymenERP.Api.Shell;
using GoymenERP.BuildingBlocks.Shell;
using GoymenERP.Modules.Counterparty.Infrastructure.Shell;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;

namespace GoymenERP.Api.IntegrationTests;

/// <summary>
/// BROWSER-CI stage 2: the disposable "cari-list-detail" profile. It exists only inside the exact
/// C-01 envelope, opens the Cari list and detail and nothing else, and has its own reviewed
/// authority. The canonical list-only C-01 is unchanged when the profile key is absent.
/// </summary>
public sealed class C01E2eCariStagedProfileTests
{
    [Fact]
    public void The_profile_opens_exactly_list_and_detail_under_its_own_authority()
    {
        var services = CounterpartyOnly();

        var authority = C01E2eShellCapabilityActivation.Configure(services, new TestingEnvironment(),
            Configuration(C01E2eShellCapabilityActivation.CariListDetailProfile));

        Assert.Equal(ShellCapabilityRegistryAuthorities.C01DisposableCariListDetailV1, authority);
        var descriptor = Assert.Single(services, item => item.ServiceType == typeof(IShellCapabilityProvider));
        Assert.Equal(typeof(C01E2eCariListDetailShellCapabilityProvider), descriptor.ImplementationType);
        using var container = services.BuildServiceProvider();
        var registry = new ShellCapabilityRegistry(container.GetServices<IShellCapabilityProvider>(),
            new ShellCapabilityAuthorityCatalog([authority]));
        Assert.Equal(
            new[] { CounterpartyShellCapabilityProvider.DetailCapabilityId, CounterpartyShellCapabilityProvider.ListCapabilityId },
            registry.Registrations.Where(item => item.Declaration.ImplementationAvailable)
                .Select(item => item.Declaration.CapabilityId).Order(StringComparer.Ordinal));
    }

    [Fact]
    public void The_profile_authority_is_the_registry_revision_of_its_provider()
    {
        // On failure xUnit prints the actual revision: the constant is never guessed.
        Assert.Equal(ShellCapabilityRegistryAuthorities.C01DisposableCariListDetailV1,
            ShellCapabilityRegistry.ComputeCandidateRevision([new C01E2eCariListDetailShellCapabilityProvider()]));
    }

    [Fact]
    public void The_detail_keeps_the_canonical_declaration_except_its_availability()
    {
        var canonical = new CounterpartyShellCapabilityProvider().GetDeclarations()
            .Single(item => item.CapabilityId == CounterpartyShellCapabilityProvider.DetailCapabilityId);
        var staged = new C01E2eCariListDetailShellCapabilityProvider().GetDeclarations()
            .Single(item => item.CapabilityId == CounterpartyShellCapabilityProvider.DetailCapabilityId);

        Assert.True(staged.ImplementationAvailable);
        Assert.Equal(canonical with { ImplementationAvailable = true }, staged, DeclarationComparer.Instance);
    }

    [Fact]
    public void Without_the_profile_key_the_canonical_list_only_C01_is_unchanged()
    {
        var services = CounterpartyOnly();

        var authority = C01E2eShellCapabilityActivation.Configure(services, new TestingEnvironment(), Configuration(null));

        Assert.Equal(ShellCapabilityRegistryAuthorities.C01DisposableListOnlyV1, authority);
        Assert.Equal(typeof(C01E2eShellCapabilityProvider),
            Assert.Single(services, item => item.ServiceType == typeof(IShellCapabilityProvider)).ImplementationType);
    }

    [Theory]
    [InlineData("cari-list")]
    [InlineData("CARI-LIST-DETAIL")]
    [InlineData("")]
    public void An_unknown_profile_fails_startup(string profile)
    {
        Assert.Throws<InvalidOperationException>(() => C01E2eShellCapabilityActivation.Configure(
            CounterpartyOnly(), new TestingEnvironment(), Configuration(profile)));
    }

    [Fact]
    public void The_profile_key_alone_requests_activation_and_fails_without_the_envelope()
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            [C01E2eShellCapabilityActivation.ProfileConfigurationKey] = C01E2eShellCapabilityActivation.CariListDetailProfile,
        }).Build();

        Assert.Throws<InvalidOperationException>(() => C01E2eShellCapabilityActivation.Configure(
            CounterpartyOnly(), new TestingEnvironment(), configuration));
    }

    [Fact]
    public async Task A_development_header_naming_an_unconfigured_actor_is_refused_even_in_Testing()
    {
        // Suite actors exist only when the suite profile configures them: an arbitrary header value
        // never authenticates.
        await using var factory = new GoymenErpApiFactory();
        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Development-User", "browser-suite-unconfigured-actor");

        using var response = await client.GetAsync("/api/v1/identity/me");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    private static ServiceCollection CounterpartyOnly()
    {
        var services = new ServiceCollection();
        services.AddSingleton<IShellCapabilityProvider, CounterpartyShellCapabilityProvider>();
        return services;
    }

    private static IConfiguration Configuration(string? profile)
    {
        var values = C01E2eShellCapabilityActivationTests.ExactEnvelope();
        if (profile is not null)
        {
            values[C01E2eShellCapabilityActivation.ProfileConfigurationKey] = profile;
        }

        return new ConfigurationBuilder().AddInMemoryCollection(values).Build();
    }

    private sealed class DeclarationComparer : IEqualityComparer<ShellCapabilityDeclaration>
    {
        public static DeclarationComparer Instance { get; } = new();

        public bool Equals(ShellCapabilityDeclaration? x, ShellCapabilityDeclaration? y) =>
            x is not null && y is not null
            && x.CapabilityId == y.CapabilityId && x.RouteId == y.RouteId && x.RoutePath == y.RoutePath
            && x.ScreenId == y.ScreenId && x.RoutePrecedence == y.RoutePrecedence
            && x.ContextRequirement == y.ContextRequirement && x.ScopeRequirement == y.ScopeRequirement
            && x.RequiredPermissions.SequenceEqual(y.RequiredPermissions)
            && x.ImplementationAvailable == y.ImplementationAvailable
            && x.GlobalProductOperation == y.GlobalProductOperation && x.ParentCapabilityId == y.ParentCapabilityId;

        public int GetHashCode(ShellCapabilityDeclaration obj) => obj.CapabilityId.GetHashCode(StringComparison.Ordinal);
    }

    private sealed class TestingEnvironment : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = "Testing";
        public string ApplicationName { get; set; } = "GoymenERP.Api.IntegrationTests";
        public string ContentRootPath { get; set; } = AppContext.BaseDirectory;
        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
    }
}
