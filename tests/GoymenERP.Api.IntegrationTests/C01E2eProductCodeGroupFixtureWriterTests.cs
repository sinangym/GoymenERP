using GoymenERP.Api.Shell;
using GoymenERP.Modules.Product.Infrastructure.Fixtures;
using GoymenERP.Modules.Product.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;

namespace GoymenERP.Api.IntegrationTests;

/// <summary>
/// The gated Product code-group fixture writer (W1/C, u02): the same gate as the ProductMasterAssignment
/// writer. A writer constructed outside the whole gate is refused; a group whose name does not name its
/// suite is refused; Product's port adds once, accepts an identical re-run and refuses a different group
/// under the same id. What SQL Server enforces is the suite run's (SUITE_FIXTURE line).
/// </summary>
public sealed class C01E2eProductCodeGroupFixtureWriterTests
{
    private const string Commit = "0123456789abcdef0123456789abcdef01234567";

    private static readonly Dictionary<string, string?> Gate = new()
    {
        [C01E2eFixtureWriterActivation.HostConfigurationKey] = "CONFIRMED",
        [C01E2eFixtureWriterActivation.TestedCommitConfigurationKey] = Commit,
        [C01E2eFixtureWriterActivation.RunIdConfigurationKey] = "35384984300",
        [C01E2eFixtureWriterActivation.ComposeProjectConfigurationKey] = "goymen-c01-35384984300-1",
    };

    public static TheoryData<string, string, string?> BrokenGates => new()
    {
        { "outside Testing", "Development", null },
        { "production", "Production", null },
        { "not confirmed", "Testing", C01E2eFixtureWriterActivation.HostConfigurationKey + "=confirmed" },
        { "host missing", "Testing", C01E2eFixtureWriterActivation.HostConfigurationKey + "=" },
        { "uppercase commit", "Testing", C01E2eFixtureWriterActivation.TestedCommitConfigurationKey + "=" + Commit.ToUpperInvariant() },
        { "commit missing", "Testing", C01E2eFixtureWriterActivation.TestedCommitConfigurationKey + "=" },
        { "run id not numeric", "Testing", C01E2eFixtureWriterActivation.RunIdConfigurationKey + "=35x" },
        { "another run's project", "Testing", C01E2eFixtureWriterActivation.ComposeProjectConfigurationKey + "=goymen-c01-1-1" },
        { "project missing", "Testing", C01E2eFixtureWriterActivation.ComposeProjectConfigurationKey + "=" },
    };

    [Theory]
    [MemberData(nameof(BrokenGates))]
    public void Outside_the_whole_gate_the_writer_cannot_be_constructed(string label, string environmentName, string? change)
    {
        var values = new Dictionary<string, string?>(Gate);
        if (change is not null)
        {
            var (key, value) = (change[..change.IndexOf('=')], change[(change.IndexOf('=') + 1)..]);
            values[key] = value.Length == 0 ? null : value;
        }

        Assert.Throws<InvalidOperationException>(() => new C01E2eProductCodeGroupFixtureWriter(
            new TestHostEnvironment(environmentName), Configuration(values), new RecordingStore()));
        Assert.False(string.IsNullOrEmpty(label));
    }

    [Fact]
    public async Task The_writer_requires_a_suite_labelled_group_name()
    {
        var store = new RecordingStore();
        var writer = new C01E2eProductCodeGroupFixtureWriter(new TestHostEnvironment("Testing"), Configuration(Gate), store);

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            writer.EnsureAsync(Row(name: "Sac ve Bobin"), CancellationToken.None));
        await writer.EnsureAsync(Row(), CancellationToken.None);
        Assert.Single(store.Rows);
    }

    [Fact]
    public async Task Products_port_adds_once_accepts_an_identical_rerun_and_refuses_a_different_group()
    {
        await using var provider = new ServiceCollection()
            .AddDbContext<ProductDbContext>(options => options.UseInMemoryDatabase(Guid.NewGuid().ToString("N")))
            .AddProductCodeGroupFixtureStore()
            .BuildServiceProvider();

        await using (var scope = provider.CreateAsyncScope())
        {
            await scope.ServiceProvider.GetRequiredService<IProductCodeGroupFixtureStore>().EnsureAsync(Row(), CancellationToken.None);
        }

        await using (var scope = provider.CreateAsyncScope())
        {
            var store = scope.ServiceProvider.GetRequiredService<IProductCodeGroupFixtureStore>();
            await store.EnsureAsync(Row(), CancellationToken.None);
            await Assert.ThrowsAsync<InvalidOperationException>(() =>
                store.EnsureAsync(Row(prefix: "OTHER"), CancellationToken.None));
            // The domain's own rules still apply: a prefix that is not letters and digits is refused.
            await Assert.ThrowsAsync<ArgumentException>(() =>
                store.EnsureAsync(Row(id: Guid.Parse("b6000000-0000-4000-8000-000000000299"), prefix: "SU-02"), CancellationToken.None));
            var groups = await scope.ServiceProvider.GetRequiredService<ProductDbContext>().CodeGroups.ToListAsync();
            var group = Assert.Single(groups);
            Assert.Equal(("SU02", "SENTETIK browser-suite:u02 kod grubu"), (group.Prefix, group.Name));
        }
    }

    private static ProductCodeGroupFixtureRow Row(Guid? id = null, string name = "SENTETIK browser-suite:u02 kod grubu",
        string prefix = "SU02") =>
        new(id ?? Guid.Parse("b6000000-0000-4000-8000-000000000201"), name, prefix);

    private static IConfiguration Configuration(IReadOnlyDictionary<string, string?> values) =>
        new ConfigurationBuilder().AddInMemoryCollection(values).Build();

    private sealed class RecordingStore : IProductCodeGroupFixtureStore
    {
        public List<ProductCodeGroupFixtureRow> Rows { get; } = [];

        public Task EnsureAsync(ProductCodeGroupFixtureRow row, CancellationToken cancellationToken)
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
