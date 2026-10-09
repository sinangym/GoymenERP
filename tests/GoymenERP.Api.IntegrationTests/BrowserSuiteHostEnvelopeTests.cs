using GoymenERP.Api.Observability;
using GoymenERP.Api.Shell;
using GoymenERP.BrowserSuiteFixtureTool;
using GoymenERP.BuildingBlocks.Shell;
using GoymenERP.Modules.Accounting.Application;
using GoymenERP.Modules.Accounting.Infrastructure.Persistence;
using GoymenERP.Modules.Counterparty.Infrastructure.Shell;
using GoymenERP.Modules.Finance.Infrastructure.Fixtures;
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
                ["allocationLabelVersion"] = "3",
                ["productCode"] = "SYNS04G-1",
                ["productName"] = "SENTETIK browser-suite:s04 stock-display-qualified sales candidate",
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
    [InlineData("allocationLabelVersion", "0")]
    [InlineData("allocationLabelVersion", "03")]
    [InlineData("allocationLabelVersion", "3.5")]
    [InlineData("productCode", "SYNS04G-0")]
    [InlineData("productCode", "SYNS04-1")]
    [InlineData("productCode", "OTHER-1")]
    [InlineData("productName", "SENTETIK browser-suite:s04 stock-display-qualified sales candidate ")]
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
                ["productCode"] = "SAC-1",
                ["productName"] = "SENTETIK browser-suite:r14 genuine S235JR 3 mm x 248 mm kaplamasiz sac",
            },
            new HashSet<string>(StringComparer.Ordinal));

        foreach (var (key, value) in new[]
                 {
                     ("productVersion", "02"),
                     ("productUnitProfileVersion", "0"),
                     ("productCode", "SAC-2"),
                     ("productName", "SENTETIK browser-suite:r14 genuine S235JR 3 mm x 248 mm kaplamasiz sac "),
                 })
        {
            Assert.Throws<InvalidOperationException>(() => BrowserSuiteSeedOutputValidator.Validate(
                "r14",
                new Dictionary<string, string> { [key] = value },
                new HashSet<string>(StringComparer.Ordinal)));
        }
    }

    [Theory]
    [InlineData("productCode", "10000000-0000-0000-0000-000000000001")]
    [InlineData("productName", "r14-recorder")]
    public void R14_product_identity_validation_does_not_accept_guid_or_actor_fallback(
        string key,
        string value)
    {
        Assert.Throws<InvalidOperationException>(() => BrowserSuiteSeedOutputValidator.Validate(
            "r14",
            new Dictionary<string, string> { [key] = value },
            new HashSet<string>(["r14-recorder"], StringComparer.Ordinal)));
    }

    [Fact]
    public void Acc01_output_validation_accepts_only_its_closed_normal_journal_scalar_contract()
    {
        BrowserSuiteSeedOutputValidator.Validate(
            "acc01",
            new Dictionary<string, string>
            {
                ["normalSourceLabel"] = "Kaynak ref. ...0000ac01 - 2026-09-24",
                ["normalOccurredOn"] = "2026-09-24",
                ["normalAmount"] = "10.25",
                ["normalDebitLabel"] = "SENTETIK-100 - Sentetik borc hesabi (Borç)",
                ["normalCreditLabel"] = "SENTETIK-120 - Sentetik alacak hesabi (Alacak)",
            },
            new HashSet<string>(StringComparer.Ordinal));

        BrowserSuiteSeedOutputValidator.Validate(
            "acc01",
            new Dictionary<string, string>
            {
                ["normalSourceLabel"] = "Kaynak ref. ...0000ac02 - 2026-09-24",
            },
            new HashSet<string>(StringComparer.Ordinal));
    }

    [Theory]
    [InlineData("normalSourceLabel", "Kaynak ref. ...0000AC01 - 2026-09-24")]
    [InlineData("normalOccurredOn", "2026-09-25")]
    [InlineData("normalAmount", "10.250")]
    [InlineData("normalDebitLabel", "SENTETIK-100 - Sentetik borc hesabi")]
    [InlineData("normalCreditLabel", "SENTETIK-120 - Sentetik alacak hesabi ")]
    [InlineData("unexpected", "10.25")]
    public void Acc01_output_validation_rejects_values_outside_its_closed_normal_journal_scalar_contract(
        string key,
        string value)
    {
        Assert.Throws<InvalidOperationException>(() => BrowserSuiteSeedOutputValidator.Validate(
            "acc01",
            new Dictionary<string, string> { [key] = value },
            new HashSet<string>(StringComparer.Ordinal)));
    }

    [Theory]
    [InlineData("normalSourceLabel")]
    [InlineData("normalOccurredOn")]
    [InlineData("normalAmount")]
    [InlineData("normalDebitLabel")]
    [InlineData("normalCreditLabel")]
    public void Acc01_output_validation_does_not_accept_guid_or_actor_fallback(string key)
    {
        foreach (var value in new[]
                 {
                     "10000000-0000-0000-0000-000000000001",
                     "acc01-preparer",
                 })
        {
            Assert.Throws<InvalidOperationException>(() => BrowserSuiteSeedOutputValidator.Validate(
                "acc01",
                new Dictionary<string, string> { [key] = value },
                new HashSet<string>(["acc01-preparer"], StringComparer.Ordinal)));
        }
    }

    [Fact]
    public void Mfg_output_validation_accepts_only_its_closed_fixture_scalar_contract()
    {
        BrowserSuiteSeedOutputValidator.Validate(
            "mfg",
            new Dictionary<string, string>
            {
                ["issueRollCode"] = "SYN-MFG-ROLL-2",
                ["replayRollCode"] = "SYN-MFG-ROLL-3",
                ["conflictRollCode"] = "SYN-MFG-ROLL-4",
                ["listRollCode"] = "SYN-MFG-ROLL-5",
                ["closeRollCode"] = "SYN-MFG-ROLL-6",
                ["planCreateRollCode"] = "SYN-MFG-ROLL-7",
                ["planReplayRollCode"] = "SYN-MFG-ROLL-8",
                ["planRefusalRollCode"] = "SYN-MFG-ROLL-9",
                ["outputUiRollId"] = "10000000-0000-0000-0000-000000000015",
                ["outputUiRollCode"] = "SYN-MFG-ROLL-10",
                ["outputProductVersion"] = "2",
                ["outputRequiredDocuments"] = "[]",
                ["outputOwnerKind"] = "Company",
                ["outputCustodianKind"] = "Company",
                ["outputInitialBlock"] = "Free",
            },
            new HashSet<string>(StringComparer.Ordinal));
    }

    [Theory]
    [InlineData("issueRollCode", "SYN-MFG-ROLL-3")]
    [InlineData("planRefusalRollCode", "SYN-MFG-ROLL-10")]
    [InlineData("outputUiRollCode", "SYN-MFG-ROLL-11")]
    [InlineData("outputProductVersion", "02")]
    [InlineData("outputRequiredDocuments", "[\"MTC\"]")]
    [InlineData("outputOwnerKind", "Counterparty")]
    [InlineData("outputCustodianKind", "company")]
    [InlineData("outputInitialBlock", "Blocked")]
    [InlineData("unexpected", "SYN-MFG-ROLL-2")]
    public void Mfg_output_validation_rejects_values_outside_its_closed_fixture_scalar_contract(
        string key,
        string value)
    {
        Assert.Throws<InvalidOperationException>(() => BrowserSuiteSeedOutputValidator.Validate(
            "mfg",
            new Dictionary<string, string> { [key] = value },
                    new HashSet<string>(StringComparer.Ordinal)));
    }

    [Theory]
    [InlineData("10000000-0000-0000-0000-000000000015")]
    [InlineData("mfg-recorder")]
    public void Mfg_output_validation_does_not_accept_guid_or_actor_fallback_for_output_ui_code(
        string value)
    {
        Assert.Throws<InvalidOperationException>(() => BrowserSuiteSeedOutputValidator.Validate(
            "mfg",
            new Dictionary<string, string> { ["outputUiRollCode"] = value },
            new HashSet<string>(["mfg-recorder"], StringComparer.Ordinal)));
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
            Assert.False(registered.IsService(typeof(C01E2eProductStockDisplayQualifiedFixtureWriter)));
            Assert.False(registered.IsService(typeof(IProductStockDisplayQualifiedFixtureStore)));
            Assert.False(registered.IsService(typeof(C01E2eProductPurchaseCandidateFixtureWriter)));
            Assert.False(registered.IsService(typeof(IProductPurchaseCandidateFixtureStore)));
            Assert.False(registered.IsService(typeof(C01E2eProductStockCandidateFixtureWriter)));
            Assert.False(registered.IsService(typeof(IProductStockCandidateFixtureStore)));
            Assert.False(registered.IsService(typeof(C01E2eFinanceNormalJournalSourceFixtureWriter)));
            Assert.False(registered.IsService(typeof(IFinanceRecordedReceiptFixtureStore)));
            Assert.False(registered.IsService(typeof(C01E2eApprovedNormalJournalSourceBindingCatalog)));
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
    public void Exact_c01_container_envelope_registers_the_request_evidence_recorder_only_there()
    {
        WithoutProcessEnvelope(() =>
        {
            var directory = Path.Combine(Path.GetTempPath(), $"c01-host-evidence-{Guid.NewGuid():N}");
            Directory.CreateDirectory(directory);
            try
            {
                var api = BaseApiEnvironment();
                api["C01E2e__EnableListCapability"] = "true";
                api["GOYMEN_ERP_C01_E2E_DISPOSABLE"] = "CONFIRMED";
                api["GOYMEN_ERP_C01_E2E_TESTED_COMMIT"] = Commit;
                api["GOYMEN_ERP_C01_E2E_RUN_ID"] = RunId;
                api["GOYMEN_ERP_C01_E2E_COMPOSE_PROJECT"] = ComposeProject;
                api["GOYMEN_ERP_C01_E2E_DATABASE_NAME"] = $"GoymenErpC01_{RunId}_1";
                api["GOYMEN_ERP_C01_E2E_REQUEST_EVIDENCE_PATH"] =
                    Path.Combine(directory, "requests.jsonl");
                api["GOYMEN_ERP_C01_E2E_EVIDENCE_ISSUER"] = "goymenerp-api-c01-testing-v1";
                api["GOYMEN_ERP_C01_E2E_CONTAINER_IDENTITY"] = Environment.MachineName;
                api["GOYMEN_ERP_C01_E2E_PROCESS_ID"] = Environment.ProcessId.ToString();
                api["GOYMEN_ERP_C01_E2E_PROCESS_START_IDENTITY"] =
                    $"test-start-{Environment.ProcessId}";
                using var host = StartedHost(api);

                Assert.IsType<C01E2eRequestEvidenceRecorder>(
                    host.Services.GetRequiredService<C01E2eRequestEvidenceRecorder>());
            }
            finally
            {
                Directory.Delete(directory, recursive: true);
            }
        });
    }

    [Fact]
    public void Normal_host_does_not_register_the_c01_request_evidence_recorder()
    {
        WithoutProcessEnvelope(() =>
        {
            using var host = StartedHost(BaseApiEnvironment());
            Assert.False(host.Services.GetRequiredService<IServiceProviderIsService>()
                .IsService(typeof(C01E2eRequestEvidenceRecorder)));
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

    [Fact]
    public void An_acc01_disposable_api_opens_only_the_exact_read_binding()
    {
        WithoutProcessEnvelope(() =>
        {
            using var baseHost = new GoymenErpApiFactory();
            using var host = baseHost.WithWebHostBuilder(builder =>
            {
                builder.UseSetting("BrowserSuiteApi:Profile", "acc01");
                builder.UseSetting("BrowserSuiteApi:Disposable", "CONFIRMED");
                builder.UseSetting("BrowserSuiteApi:TestedCommit", Commit);
                builder.UseSetting("BrowserSuiteApi:RunId", RunId);
                builder.UseSetting("BrowserSuiteApi:ComposeProject", ComposeProject);
            });
            using (host.CreateClient())
            {
                // Build Program as a normal API test host with only the exact acc01 read gate.
            }

            var registered = host.Services.GetRequiredService<IServiceProviderIsService>();
            Assert.False(registered.IsService(typeof(C01E2eFinanceNormalJournalSourceFixtureWriter)));
            Assert.False(registered.IsService(typeof(IFinanceRecordedReceiptFixtureStore)));
            Assert.False(registered.IsService(typeof(IFinanceRecordedReceiptFixtureReadStore)));
            Assert.True(registered.IsService(typeof(IApprovedAccountingSourceCatalogStoreV1)));
            Assert.IsType<ApprovedAccountingSourceCatalogStoreV1>(
                host.Services.GetRequiredService<IApprovedNormalJournalSourceBindingCatalogV1>());
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
        Assert.True(registered.IsService(typeof(C01E2eProductStockDisplayQualifiedFixtureWriter)));
        Assert.True(registered.IsService(typeof(IProductStockDisplayQualifiedFixtureStore)));
        Assert.False(registered.IsService(typeof(IProductSalesCandidateFixtureStore)));
        Assert.True(registered.IsService(typeof(C01E2eProductPurchaseCandidateFixtureWriter)));
        Assert.True(registered.IsService(typeof(IProductPurchaseCandidateFixtureStore)));
        Assert.True(registered.IsService(typeof(C01E2eProductStockCandidateFixtureWriter)));
        Assert.True(registered.IsService(typeof(IProductStockCandidateFixtureStore)));
        Assert.True(registered.IsService(typeof(C01E2eFinanceNormalJournalSourceFixtureWriter)));
        Assert.True(registered.IsService(typeof(IFinanceRecordedReceiptFixtureStore)));
        Assert.False(registered.IsService(typeof(C01E2eApprovedNormalJournalSourceBindingCatalog)));
        Assert.True(registered.IsService(typeof(IApprovedAccountingSourceCatalogStoreV1)));
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
