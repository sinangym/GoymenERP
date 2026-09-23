using GoymenERP.Api.Shell;
using GoymenERP.BrowserSuiteFixtureTool;
using GoymenERP.BuildingBlocks.Shell;
using GoymenERP.Modules.Counterparty.Infrastructure.Shell;
using GoymenERP.Modules.Product.Infrastructure.Fixtures;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.DependencyInjection;

namespace GoymenERP.Api.IntegrationTests;

// These tests set and remove process environment variables, which every host in this process reads:
// they run alone, after the parallel collections.
[CollectionDefinition(nameof(ProcessEnvironmentCollection), DisableParallelization = true)]
public sealed class ProcessEnvironmentCollection;

/// <summary>
/// #331 (u02 run 35411370499): the browser-suite runner's in-process host, started the way the real
/// "Materialize the suite's fixture" step starts it, with the run's C-01 envelope variables in the
/// process. Program reads the shell and fixture-writer gates before it builds the app, so the host must
/// start exactly as its container would: no envelope for a normal-profile suite, the staged profile for a
/// staged one, and the fixture writers registered in both.
/// </summary>
[Collection(nameof(ProcessEnvironmentCollection))]
public sealed class BrowserSuiteHostEnvelopeTests
{
    private const string Commit = "0123456789abcdef0123456789abcdef01234567";
    private const string RunId = "35411370499";
    private const string ComposeProject = "goymen-c01-35411370499-1";

    // What the workflow exports to every step of a run (the runner's own settings included).
    private static readonly IReadOnlyDictionary<string, string> ProcessEnvelope = new Dictionary<string, string>
    {
        ["GOYMEN_ERP_C01_E2E_DISPOSABLE"] = "CONFIRMED",
        ["GOYMEN_ERP_C01_E2E_TESTED_COMMIT"] = Commit,
        ["GOYMEN_ERP_C01_E2E_RUN_ID"] = RunId,
        ["GOYMEN_ERP_C01_E2E_COMPOSE_PROJECT"] = ComposeProject,
        ["GOYMEN_ERP_C01_E2E_FIXTURE_EVIDENCE"] = "artifacts/browser-fixture-evidence.json",
        ["GOYMEN_ERP_C01_E2E_SQLSERVER_CONNECTION"] = "Server=127.0.0.1,11433;Database=runner-only;",
    };

    [Fact]
    public void S04_output_validation_accepts_only_its_closed_product_scalar_contract()
    {
        BrowserSuiteSeedOutputValidator.Validate(
            "s04",
            new Dictionary<string, string>
            {
                ["productVersion"] = "2",
                ["productUnitProfileVersion"] = "1",
                ["productCode"] = "SYNS04-1",
                ["productName"] = "SENTETIK browser-suite:s04 sales candidate",
            },
            new HashSet<string>(StringComparer.Ordinal));

        Assert.Throws<InvalidOperationException>(() => BrowserSuiteSeedOutputValidator.Validate(
            "s03",
            new Dictionary<string, string> { ["productVersion"] = "2" },
            new HashSet<string>(StringComparer.Ordinal)));
    }

    [Theory]
    [InlineData("productVersion", "0")]
    [InlineData("productVersion", "02")]
    [InlineData("productUnitProfileVersion", "1.5")]
    [InlineData("productCode", "SYNS04-0")]
    [InlineData("productCode", "OTHER-1")]
    [InlineData("productName", "SENTETIK browser-suite:s04 sales candidate ")]
    [InlineData("unexpected", "2")]
    public void S04_output_validation_rejects_values_outside_its_closed_product_scalar_contract(
        string key,
        string value)
    {
        Assert.Throws<InvalidOperationException>(() => BrowserSuiteSeedOutputValidator.Validate(
            "s04",
            new Dictionary<string, string> { [key] = value },
            new HashSet<string>(StringComparer.Ordinal)));
    }

    [Fact]
    public void Pur01_output_validation_accepts_only_its_closed_product_scalar_contract()
    {
        BrowserSuiteSeedOutputValidator.Validate(
            "pur01",
            new Dictionary<string, string>
            {
                ["productVersion"] = "2",
                ["productUnitProfileVersion"] = "1",
                ["productCode"] = "SYNPUR01-1",
                ["productName"] = "SENTETIK browser-suite:pur01 purchase candidate",
            },
            new HashSet<string>(StringComparer.Ordinal));

        foreach (var (key, value) in new[]
                 {
                     ("productVersion", "02"),
                     ("productUnitProfileVersion", "0"),
                     ("productCode", "SYNPUR01-0"),
                     ("productName", "SENTETIK browser-suite:pur01 purchase candidate "),
                 })
        {
            Assert.Throws<InvalidOperationException>(() => BrowserSuiteSeedOutputValidator.Validate(
                "pur01",
                new Dictionary<string, string> { [key] = value },
                new HashSet<string>(StringComparer.Ordinal)));
        }
    }

    [Fact]
    public void R14_output_validation_accepts_only_its_closed_product_scalar_contract()
    {
        BrowserSuiteSeedOutputValidator.Validate(
            "r14",
            new Dictionary<string, string>
            {
                ["productVersion"] = "2",
                ["productUnitProfileVersion"] = "1",
                ["productCode"] = "SYNR14-1",
                ["productName"] = "SENTETIK browser-suite:r14 stock candidate",
            },
            new HashSet<string>(StringComparer.Ordinal));

        foreach (var (key, value) in new[]
                 {
                     ("productVersion", "02"),
                     ("productUnitProfileVersion", "0"),
                     ("productCode", "SYNR14-0"),
                     ("productName", "SENTETIK browser-suite:r14 stock candidate "),
                 })
        {
            Assert.Throws<InvalidOperationException>(() => BrowserSuiteSeedOutputValidator.Validate(
                "r14",
                new Dictionary<string, string> { [key] = value },
                new HashSet<string>(StringComparer.Ordinal)));
        }
    }

    [Fact]
    public void A_normal_application_host_does_not_register_product_technical_fixture_services()
    {
        WithoutProcessEnvelope(() =>
        {
            using var host = new GoymenErpApiFactory();
            using (host.CreateClient())
            {
                // Creating the first client builds and starts Program.cs as a normal Testing host.
            }

            var registered = host.Services.GetRequiredService<IServiceProviderIsService>();
            Assert.False(registered.IsService(typeof(C01E2eProductTechnicalCardFixtureWriter)));
            Assert.False(registered.IsService(typeof(IProductTechnicalCardFixtureStore)));
            Assert.False(registered.IsService(typeof(C01E2eProductSalesCandidateFixtureWriter)));
            Assert.False(registered.IsService(typeof(IProductSalesCandidateFixtureStore)));
            Assert.False(registered.IsService(typeof(C01E2eProductPurchaseCandidateFixtureWriter)));
            Assert.False(registered.IsService(typeof(IProductPurchaseCandidateFixtureStore)));
            Assert.False(registered.IsService(typeof(C01E2eProductStockCandidateFixtureWriter)));
            Assert.False(registered.IsService(typeof(IProductStockCandidateFixtureStore)));
        });
    }

    [Fact]
    public void A_normal_profile_host_starts_without_the_envelope_the_process_carries_and_opens_the_writers()
    {
        WithProcessEnvelope(() =>
        {
            using var host = StartedHost(BaseApiEnvironment());

            Assert.All(ProcessEnvelope.Keys, name => Assert.Null(Environment.GetEnvironmentVariable(name)));
            var providers = ProviderTypes(host);
            Assert.Contains(typeof(CounterpartyShellCapabilityProvider), providers);
            Assert.DoesNotContain(typeof(C01E2eShellCapabilityProvider), providers);
            AssertFixtureWritersRegistered(host);
        });
    }

    [Fact]
    public void A_staged_profile_host_gets_its_profile_from_the_container_environment()
    {
        WithProcessEnvelope(() =>
        {
            var api = BaseApiEnvironment();
            foreach (var name in new[]
                     {
                         "GOYMEN_ERP_C01_E2E_DISPOSABLE", "GOYMEN_ERP_C01_E2E_TESTED_COMMIT",
                         "GOYMEN_ERP_C01_E2E_RUN_ID", "GOYMEN_ERP_C01_E2E_COMPOSE_PROJECT",
                     })
            {
                api[name] = ProcessEnvelope[name];
            }

            api["C01E2e__EnableListCapability"] = "true";
            api["C01E2e__Profile"] = C01E2eShellCapabilityActivation.CariListDetailProfile;
            using var host = StartedHost(api);

            // The container carries these four, so the process keeps them; the other two are removed.
            Assert.Equal(Commit, Environment.GetEnvironmentVariable("GOYMEN_ERP_C01_E2E_TESTED_COMMIT"));
            Assert.Null(Environment.GetEnvironmentVariable("GOYMEN_ERP_C01_E2E_SQLSERVER_CONNECTION"));
            Assert.Equal([typeof(C01E2eCariListDetailShellCapabilityProvider)], ProviderTypes(host));
            AssertFixtureWritersRegistered(host);
        });
    }

    private static Microsoft.AspNetCore.Mvc.Testing.WebApplicationFactory<Program> StartedHost(
        Dictionary<string, string> apiEnvironment)
    {
        var runner = BrowserSuiteHost.Create(
            FindRepositoryRoot(),
            apiEnvironment,
            "Server=testing;Database=testing;User Id=testing;Password=testing;",
            new BrowserSuiteRunBinding(Commit, RunId, ComposeProject));
        var databaseName = $"browser-suite-host-{Guid.NewGuid():N}";
        var host = runner.WithWebHostBuilder(builder => builder.ConfigureServices(
            services => GoymenErpApiFactory.UseInMemoryDatabases(services, databaseName)));
        using (host.CreateClient())
        {
            // Creating the first client builds and starts the host (Program.cs as-is), as the runner does.
        }

        return host;
    }

    private static Type[] ProviderTypes(Microsoft.AspNetCore.Mvc.Testing.WebApplicationFactory<Program> host) =>
        host.Services.GetServices<IShellCapabilityProvider>().Select(provider => provider.GetType()).ToArray();

    private static void AssertFixtureWritersRegistered(Microsoft.AspNetCore.Mvc.Testing.WebApplicationFactory<Program> host)
    {
        var registered = host.Services.GetRequiredService<IServiceProviderIsService>();
        Assert.True(registered.IsService(typeof(C01E2eProductMasterAssignmentFixtureWriter)));
        Assert.True(registered.IsService(typeof(C01E2eProductCodeGroupFixtureWriter)));
        Assert.True(registered.IsService(typeof(C01E2eProductSalesCandidateFixtureWriter)));
        Assert.True(registered.IsService(typeof(IProductSalesCandidateFixtureStore)));
        Assert.True(registered.IsService(typeof(C01E2eProductPurchaseCandidateFixtureWriter)));
        Assert.True(registered.IsService(typeof(IProductPurchaseCandidateFixtureStore)));
        Assert.True(registered.IsService(typeof(C01E2eProductStockCandidateFixtureWriter)));
        Assert.True(registered.IsService(typeof(IProductStockCandidateFixtureStore)));
    }

    // The normal-profile container's identity keys (infra/docker-compose.browser-normal.yml); Testing
    // selects DevelopmentBootstrap itself. The database and cursor key are the test host's own.
    private static Dictionary<string, string> BaseApiEnvironment() => new(StringComparer.Ordinal)
    {
        ["ASPNETCORE_ENVIRONMENT"] = "Testing",
        ["BootstrapIdentity__HeaderName"] = "X-Development-User",
        ["BootstrapIdentity__Users__0__UserId"] = "group-bootstrap-admin",
        ["BootstrapIdentity__Users__0__DisplayName"] = "C01 E2E Group Administrator",
        ["BootstrapIdentity__Users__1__UserId"] = "company-a-user",
        ["BootstrapIdentity__Users__1__DisplayName"] = "C01 E2E Unauthorized User",
    };

    private static void WithProcessEnvelope(Action test)
    {
        var saved = ProcessEnvelope.Keys
            .Concat(["C01E2e__EnableListCapability", "C01E2e__Profile"])
            .ToDictionary(name => name, name => Environment.GetEnvironmentVariable(name));
        try
        {
            foreach (var (name, value) in ProcessEnvelope)
            {
                Environment.SetEnvironmentVariable(name, value);
            }

            test();
        }
        finally
        {
            foreach (var (name, value) in saved)
            {
                Environment.SetEnvironmentVariable(name, value);
            }
        }
    }

    private static void WithoutProcessEnvelope(Action test)
    {
        var saved = ProcessEnvelope.Keys
            .Concat(["C01E2e__EnableListCapability", "C01E2e__Profile"])
            .ToDictionary(name => name, name => Environment.GetEnvironmentVariable(name));
        try
        {
            foreach (var name in saved.Keys)
            {
                Environment.SetEnvironmentVariable(name, null);
            }

            test();
        }
        finally
        {
            foreach (var (name, value) in saved)
            {
                Environment.SetEnvironmentVariable(name, value);
            }
        }
    }

    private static string FindRepositoryRoot()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory);
             directory is not null;
             directory = directory.Parent)
        {
            if (File.Exists(Path.Combine(directory.FullName, "src", "backend", "GoymenERP.Api", "Program.cs")))
            {
                return directory.FullName;
            }
        }

        throw new DirectoryNotFoundException("GöymenERP repository kökü bulunamadı.");
    }
}
