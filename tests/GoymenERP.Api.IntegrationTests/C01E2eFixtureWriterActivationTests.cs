using GoymenERP.Api.Shell;
using GoymenERP.Modules.Accounting.Infrastructure.Fixtures;
using GoymenERP.Modules.Identity.Domain.Assignments;
using GoymenERP.Modules.Identity.Infrastructure.Fixtures;
using GoymenERP.Modules.Identity.Infrastructure.Persistence;
using GoymenERP.Modules.Product.Infrastructure.Fixtures;
using GoymenERP.Modules.Settings.Application;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using GoymenERP.Modules.Settings.Application.Contracts.V1;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;

namespace GoymenERP.Api.IntegrationTests;

/// <summary>
/// BROWSER-CI a2 part 2 (decision W1): the gated fixture writers exist only inside the fixture runner's
/// in-process host. No key: nothing registered. Any key without the whole gate: startup refused. A
/// writer constructed by hand outside the gate: refused.
/// </summary>
public sealed class C01E2eFixtureWriterActivationTests
{
    private const string Commit = "0123456789abcdef0123456789abcdef01234567";

    private static readonly Dictionary<string, string?> Gate = new()
    {
        [C01E2eFixtureWriterActivation.HostConfigurationKey] = "CONFIRMED",
        [C01E2eFixtureWriterActivation.TestedCommitConfigurationKey] = Commit,
        [C01E2eFixtureWriterActivation.RunIdConfigurationKey] = "35384984300",
        [C01E2eFixtureWriterActivation.ComposeProjectConfigurationKey] = "goymen-c01-35384984300-1",
    };

    private static readonly Dictionary<string, string?> ApiReadGate = new()
    {
        [BrowserSuiteApiReadFixtureActivation.ProfileConfigurationKey] =
            BrowserSuiteApiReadFixtureActivation.ManufacturingProfile,
        [BrowserSuiteApiReadFixtureActivation.DisposableConfigurationKey] = "CONFIRMED",
        [BrowserSuiteApiReadFixtureActivation.TestedCommitConfigurationKey] = Commit,
        [BrowserSuiteApiReadFixtureActivation.RunIdConfigurationKey] = "35384984300",
        [BrowserSuiteApiReadFixtureActivation.ComposeProjectConfigurationKey] = "goymen-c01-35384984300-1",
    };

    [Fact]
    public void Without_any_key_nothing_is_registered()
    {
        var services = new ServiceCollection();

        Assert.False(C01E2eFixtureWriterActivation.Configure(
            services, new TestHostEnvironment("Testing"), Configuration(new Dictionary<string, string?>())));
        Assert.DoesNotContain(services, descriptor =>
            descriptor.ImplementationType == typeof(C01E2eManufacturingToleranceFixtureProvider));
        Assert.DoesNotContain(services, descriptor =>
            descriptor.ServiceType == typeof(C01E2eProductMasterAssignmentFixtureWriter)
            || descriptor.ServiceType == typeof(IProductMasterAssignmentFixtureStore)
            || descriptor.ServiceType == typeof(C01E2eProductTechnicalCardFixtureWriter)
            || descriptor.ServiceType == typeof(IProductTechnicalCardFixtureStore)
            || descriptor.ServiceType == typeof(C01E2eProductSalesCandidateFixtureWriter)
            || descriptor.ServiceType == typeof(IProductStockDisplayQualifiedFixtureStore)
            || descriptor.ServiceType == typeof(C01E2eProductPurchaseSalesStockJourneyFixtureWriter)
            || descriptor.ServiceType == typeof(IProductPurchaseSalesStockJourneyFixtureStore)
            || descriptor.ServiceType == typeof(C01E2eAccountingOpenPeriodFixtureWriter)
            || descriptor.ServiceType == typeof(IAccountingOpenPeriodFixtureStore)
            || descriptor.ServiceType == typeof(IAccountingPostedJournalFixtureStore));
    }

    [Fact]
    public void Normal_api_without_the_read_fixture_gate_keeps_the_approved_Settings_provider()
    {
        var services = new ServiceCollection();
        services.AddScoped<ISettingsManufacturingToleranceProviderV1, ApprovedSettingsPrerequisiteProvider>();

        Assert.False(BrowserSuiteApiReadFixtureActivation.Configure(
            services, new TestHostEnvironment("Testing"), Configuration(new Dictionary<string, string?>())));
        var provider = Assert.Single(services, descriptor =>
            descriptor.ServiceType == typeof(ISettingsManufacturingToleranceProviderV1));
        Assert.Equal(typeof(ApprovedSettingsPrerequisiteProvider), provider.ImplementationType);
    }

    [Fact]
    public void Exact_disposable_mfg_api_gate_replaces_only_the_read_only_tolerance_provider()
    {
        var services = new ServiceCollection();
        services.AddScoped<ISettingsManufacturingToleranceProviderV1, ApprovedSettingsPrerequisiteProvider>();

        Assert.True(BrowserSuiteApiReadFixtureActivation.Configure(
            services, new TestHostEnvironment("Testing"), Configuration(ApiReadGate)));
        var provider = Assert.Single(services, descriptor =>
            descriptor.ServiceType == typeof(ISettingsManufacturingToleranceProviderV1));
        Assert.Equal(typeof(C01E2eManufacturingToleranceFixtureProvider), provider.ImplementationType);
        Assert.DoesNotContain(services, descriptor =>
            descriptor.ServiceType == typeof(C01E2eProductMasterAssignmentFixtureWriter)
            || descriptor.ServiceType == typeof(IProductMasterAssignmentFixtureStore)
            || descriptor.ServiceType == typeof(C01E2eProductPurchaseSalesStockJourneyFixtureWriter)
            || descriptor.ServiceType == typeof(IProductPurchaseSalesStockJourneyFixtureStore));
    }

    [Fact]
    public void A_partial_or_non_Testing_api_read_fixture_gate_fails_closed()
    {
        var partial = new Dictionary<string, string?>
        {
            [BrowserSuiteApiReadFixtureActivation.ProfileConfigurationKey] =
                BrowserSuiteApiReadFixtureActivation.ManufacturingProfile,
        };
        Assert.Throws<InvalidOperationException>(() => BrowserSuiteApiReadFixtureActivation.Configure(
            new ServiceCollection(), new TestHostEnvironment("Testing"), Configuration(partial)));
        Assert.Throws<InvalidOperationException>(() => BrowserSuiteApiReadFixtureActivation.Configure(
            new ServiceCollection(), new TestHostEnvironment("Production"), Configuration(ApiReadGate)));
    }

    [Fact]
    public void The_whole_gate_registers_the_writer_and_identitys_port()
    {
        var services = new ServiceCollection();

        Assert.True(C01E2eFixtureWriterActivation.Configure(
            services, new TestHostEnvironment("Testing"), Configuration(Gate)));
        var tolerance = Assert.Single(services, descriptor =>
            descriptor.ServiceType == typeof(ISettingsManufacturingToleranceProviderV1));
        Assert.Equal(typeof(C01E2eManufacturingToleranceFixtureProvider), tolerance.ImplementationType);
        Assert.Single(services, descriptor =>
            descriptor.ServiceType == typeof(C01E2eProductMasterAssignmentFixtureWriter));
        Assert.Single(services, descriptor =>
            descriptor.ServiceType == typeof(IProductMasterAssignmentFixtureStore));
        Assert.Single(services, descriptor =>
            descriptor.ServiceType == typeof(C01E2eProductTechnicalCardFixtureWriter));
        Assert.Single(services, descriptor =>
            descriptor.ServiceType == typeof(IProductTechnicalCardFixtureStore));
        Assert.Single(services, descriptor =>
            descriptor.ServiceType == typeof(C01E2eProductSalesCandidateFixtureWriter));
        Assert.Single(services, descriptor =>
            descriptor.ServiceType == typeof(IProductStockDisplayQualifiedFixtureStore));
        Assert.DoesNotContain(services, descriptor =>
            descriptor.ServiceType == typeof(IProductSalesCandidateFixtureStore));
        Assert.Single(services, descriptor =>
            descriptor.ServiceType == typeof(C01E2eProductPurchaseSalesStockJourneyFixtureWriter));
        Assert.Single(services, descriptor =>
            descriptor.ServiceType == typeof(IProductPurchaseSalesStockJourneyFixtureStore));
        Assert.Single(services, descriptor =>
            descriptor.ServiceType == typeof(C01E2eAccountingOpenPeriodFixtureWriter));
        Assert.Single(services, descriptor =>
            descriptor.ServiceType == typeof(IAccountingOpenPeriodFixtureStore));
        // acc01 ACC01-09: the posted-journal port, only behind the same gate.
        Assert.Single(services, descriptor =>
            descriptor.ServiceType == typeof(IAccountingPostedJournalFixtureStore));
    }

    [Fact]
    public async Task The_s04_product_writer_returns_the_stock_display_qualified_output()
    {
        var expected = new ProductStockDisplayQualifiedFixtureOutput(
            Guid.Parse("b6b00000-0000-4000-8000-000000000401"), "2",
            Guid.Parse("c5ef643e-a6e8-51f3-89f2-8e68b00cb983"),
            Guid.Parse("4a790fdc-2a6b-5126-835f-d3a13d28b71c"), "1", "SYNS04G-1",
            "SENTETIK browser-suite:s04 stock-display-qualified sales candidate");
        var writer = new C01E2eProductSalesCandidateFixtureWriter(
            new TestHostEnvironment("Testing"),
            Configuration(Gate),
            new RecordingStockDisplayQualifiedStore(expected));

        var actual = await writer.EnsureAsync(CancellationToken.None);

        Assert.Equal(expected, actual);
    }

    public static TheoryData<string, string, string?> BrokenGates => new()
    {
        { "outside Testing", "Development", null },
        { "production", "Production", null },
        { "not confirmed", "Testing", C01E2eFixtureWriterActivation.HostConfigurationKey + "=confirmed" },
        { "host missing", "Testing", C01E2eFixtureWriterActivation.HostConfigurationKey + "=" },
        { "uppercase commit", "Testing", C01E2eFixtureWriterActivation.TestedCommitConfigurationKey + "=" + Commit.ToUpperInvariant() },
        { "short commit", "Testing", C01E2eFixtureWriterActivation.TestedCommitConfigurationKey + "=" + Commit[..39] },
        { "commit missing", "Testing", C01E2eFixtureWriterActivation.TestedCommitConfigurationKey + "=" },
        { "run id not numeric", "Testing", C01E2eFixtureWriterActivation.RunIdConfigurationKey + "=35x" },
        { "run id missing", "Testing", C01E2eFixtureWriterActivation.RunIdConfigurationKey + "=" },
        { "wrong project prefix", "Testing", C01E2eFixtureWriterActivation.ComposeProjectConfigurationKey + "=goymen-browser-35384984300-1" },
        { "another run's project", "Testing", C01E2eFixtureWriterActivation.ComposeProjectConfigurationKey + "=goymen-c01-1-1" },
        { "project without attempt", "Testing", C01E2eFixtureWriterActivation.ComposeProjectConfigurationKey + "=goymen-c01-35384984300-" },
        { "project missing", "Testing", C01E2eFixtureWriterActivation.ComposeProjectConfigurationKey + "=" },
    };

    [Theory]
    [MemberData(nameof(BrokenGates))]
    public void Any_broken_gate_registers_nothing_and_refuses_construction(
        string label,
        string environmentName,
        string? change)
    {
        var values = new Dictionary<string, string?>(Gate);
        if (change is not null)
        {
            var (key, value) = (change[..change.IndexOf('=')], change[(change.IndexOf('=') + 1)..]);
            values[key] = value.Length == 0 ? null : value;
        }

        var configuration = Configuration(values);
        var environment = new TestHostEnvironment(environmentName);
        var services = new ServiceCollection();

        Assert.Throws<InvalidOperationException>(() =>
            C01E2eFixtureWriterActivation.Configure(services, environment, configuration));
        Assert.DoesNotContain(services, descriptor =>
            descriptor.ServiceType == typeof(C01E2eProductMasterAssignmentFixtureWriter)
            || descriptor.ServiceType == typeof(C01E2eProductTechnicalCardFixtureWriter)
            || descriptor.ImplementationType == typeof(C01E2eManufacturingToleranceFixtureProvider)
            || descriptor.ServiceType == typeof(IProductTechnicalCardFixtureStore)
            || descriptor.ServiceType == typeof(C01E2eProductSalesCandidateFixtureWriter)
            || descriptor.ServiceType == typeof(IProductStockDisplayQualifiedFixtureStore)
            || descriptor.ServiceType == typeof(C01E2eProductPurchaseSalesStockJourneyFixtureWriter)
            || descriptor.ServiceType == typeof(IProductPurchaseSalesStockJourneyFixtureStore)
            || descriptor.ServiceType == typeof(C01E2eAccountingOpenPeriodFixtureWriter)
            || descriptor.ServiceType == typeof(IAccountingOpenPeriodFixtureStore)
            || descriptor.ServiceType == typeof(IAccountingPostedJournalFixtureStore));
        Assert.Throws<InvalidOperationException>(() =>
            new C01E2eProductMasterAssignmentFixtureWriter(environment, configuration, new RecordingStore()));
        Assert.Throws<InvalidOperationException>(() =>
            new C01E2eProductTechnicalCardFixtureWriter(
                environment,
                configuration,
                new RecordingTechnicalCardStore()));
        Assert.Throws<InvalidOperationException>(() =>
            new C01E2eProductSalesCandidateFixtureWriter(
                environment,
                configuration,
                new RecordingStockDisplayQualifiedStore()));
        Assert.Throws<InvalidOperationException>(() =>
            new C01E2eProductPurchaseSalesStockJourneyFixtureWriter(
                environment,
                configuration,
                new RecordingPurchaseSalesStockJourneyStore()));
        Assert.Throws<InvalidOperationException>(() =>
            new C01E2eAccountingOpenPeriodFixtureWriter(
                environment,
                configuration,
                new RecordingAccountingOpenPeriodStore()));
        Assert.False(string.IsNullOrEmpty(label));
    }

    [Fact]
    public async Task The_writer_requires_a_suite_labelled_decision_reference()
    {
        var store = new RecordingStore();
        var writer = new C01E2eProductMasterAssignmentFixtureWriter(
            new TestHostEnvironment("Testing"), Configuration(Gate), store);

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            writer.EnsureAsync(Row(decisionReference: "Sinan decision 42"), CancellationToken.None));
        await writer.EnsureAsync(Row(), CancellationToken.None);
        Assert.Single(store.Rows);
    }

    [Fact]
    public async Task The_store_adds_once_accepts_an_identical_rerun_and_refuses_a_different_row()
    {
        await using var provider = new ServiceCollection()
            .AddDbContext<IdentityDbContext>(options => options.UseInMemoryDatabase(Guid.NewGuid().ToString("N")))
            .AddProductMasterAssignmentFixtureStore()
            .BuildServiceProvider();

        await using (var scope = provider.CreateAsyncScope())
        {
            await scope.ServiceProvider.GetRequiredService<IProductMasterAssignmentFixtureStore>()
                .EnsureAsync(Row(), CancellationToken.None);
        }

        await using (var scope = provider.CreateAsyncScope())
        {
            await scope.ServiceProvider.GetRequiredService<IProductMasterAssignmentFixtureStore>()
                .EnsureAsync(Row(), CancellationToken.None);
            await Assert.ThrowsAsync<InvalidOperationException>(() =>
                scope.ServiceProvider.GetRequiredService<IProductMasterAssignmentFixtureStore>()
                    .EnsureAsync(Row(operation: ProductMasterOperation.DraftCreate), CancellationToken.None));
            Assert.Equal(1, await scope.ServiceProvider.GetRequiredService<IdentityDbContext>()
                .Set<ProductMasterAssignment>().CountAsync());
        }
    }

    private static ProductMasterAssignmentFixtureRow Row(
        ProductMasterOperation operation = ProductMasterOperation.Read,
        string decisionReference = "SENTETIK browser-suite:u02 Product fixture decision") =>
        new(
            Guid.Parse("b5000000-0000-0000-0000-000000000001"),
            Guid.Parse("20000000-0000-0000-0000-000000000003"),
            Guid.Parse("30000000-0000-0000-0000-000000000003"),
            operation,
            Guid.Parse("20000000-0000-0000-0000-000000000003"),
            Guid.Parse("21000000-0000-0000-0000-000000000003"),
            Guid.Parse("b5100000-0000-0000-0000-000000000001"),
            decisionReference,
            new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero));

    private static IConfiguration Configuration(IReadOnlyDictionary<string, string?> values) =>
        new ConfigurationBuilder().AddInMemoryCollection(values).Build();

    private sealed class RecordingStore : IProductMasterAssignmentFixtureStore
    {
        public List<ProductMasterAssignmentFixtureRow> Rows { get; } = [];

        public Task EnsureAsync(ProductMasterAssignmentFixtureRow row, CancellationToken cancellationToken)
        {
            Rows.Add(row);
            return Task.CompletedTask;
        }
    }

    private sealed class RecordingTechnicalCardStore : IProductTechnicalCardFixtureStore
    {
        public Task EnsureAsync(CancellationToken cancellationToken) => Task.CompletedTask;
    }

    private sealed class RecordingStockDisplayQualifiedStore(
        ProductStockDisplayQualifiedFixtureOutput? output = null) : IProductStockDisplayQualifiedFixtureStore
    {
        public Task<ProductStockDisplayQualifiedFixtureOutput> EnsureAsync(CancellationToken cancellationToken) =>
            Task.FromResult(output ?? new ProductStockDisplayQualifiedFixtureOutput(
                Guid.Parse("b6b00000-0000-4000-8000-000000000401"), "2",
                Guid.Parse("c5ef643e-a6e8-51f3-89f2-8e68b00cb983"),
                Guid.Parse("4a790fdc-2a6b-5126-835f-d3a13d28b71c"), "1", "SYNS04G-1",
                "SENTETIK browser-suite:s04 stock-display-qualified sales candidate"));
    }

    private sealed class RecordingPurchaseSalesStockJourneyStore
        : IProductPurchaseSalesStockJourneyFixtureStore
    {
        public Task<ProductPurchaseSalesStockJourneyFixtureOutput> EnsureAsync(
            CancellationToken cancellationToken) =>
            Task.FromResult(new ProductPurchaseSalesStockJourneyFixtureOutput(
                ApprovedUnitProfileCatalog.GdcCompanyId,
                Guid.Parse("b6900000-0000-4000-8000-000000000401"),
                "2",
                Guid.Parse("c5ef643e-a6e8-51f3-89f2-8e68b00cb983"),
                Guid.Parse("4a790fdc-2a6b-5126-835f-d3a13d28b71c"),
                "1",
                new DateOnly(2026, 9, 18),
                "SYNPL04-1",
                "SENTETIK browser-suite:purchase-to-delivery candidate"));
    }

    private sealed class RecordingAccountingOpenPeriodStore : IAccountingOpenPeriodFixtureStore
    {
        public Task EnsureAsync(CancellationToken cancellationToken) => Task.CompletedTask;
    }

    private sealed class TestHostEnvironment(string environmentName) : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = environmentName;

        public string ApplicationName { get; set; } = "GoymenERP.Api.IntegrationTests";

        public string ContentRootPath { get; set; } = AppContext.BaseDirectory;

        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
    }
}
