using GoymenERP.Api.Shell;
using GoymenERP.Modules.Identity.Domain.Assignments;
using GoymenERP.Modules.Identity.Infrastructure.Fixtures;
using GoymenERP.Modules.Identity.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
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

    [Fact]
    public void Without_any_key_nothing_is_registered()
    {
        var services = new ServiceCollection();

        Assert.False(C01E2eFixtureWriterActivation.Configure(
            services, new TestHostEnvironment("Testing"), Configuration([])));
        Assert.DoesNotContain(services, descriptor =>
            descriptor.ServiceType == typeof(C01E2eProductMasterAssignmentFixtureWriter)
            || descriptor.ServiceType == typeof(IProductMasterAssignmentFixtureStore));
    }

    [Fact]
    public void The_whole_gate_registers_the_writer_and_identitys_port()
    {
        var services = new ServiceCollection();

        Assert.True(C01E2eFixtureWriterActivation.Configure(
            services, new TestHostEnvironment("Testing"), Configuration(Gate)));
        Assert.Single(services, descriptor =>
            descriptor.ServiceType == typeof(C01E2eProductMasterAssignmentFixtureWriter));
        Assert.Single(services, descriptor =>
            descriptor.ServiceType == typeof(IProductMasterAssignmentFixtureStore));
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
            descriptor.ServiceType == typeof(C01E2eProductMasterAssignmentFixtureWriter));
        Assert.Throws<InvalidOperationException>(() =>
            new C01E2eProductMasterAssignmentFixtureWriter(environment, configuration, new RecordingStore()));
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

    private sealed class TestHostEnvironment(string environmentName) : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = environmentName;

        public string ApplicationName { get; set; } = "GoymenERP.Api.IntegrationTests";

        public string ContentRootPath { get; set; } = AppContext.BaseDirectory;

        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
    }
}
