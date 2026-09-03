using System.Text.Json;
using GoymenERP.Api.Shell;
using GoymenERP.BuildingBlocks.Shell;
using GoymenERP.Modules.Counterparty.Infrastructure.Shell;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;

namespace GoymenERP.Api.IntegrationTests;

public sealed class NormalProviderRuntimeAttestationTests
{
    private const string SecretSentinel = "must-never-appear-secret-value";

    [Fact]
    public void ExactNormalProviderWithoutDisposableKeys_CreatesBoundAttestation()
    {
        var provider = new CounterpartyShellCapabilityProvider();
        var registry = CreateRegistry(provider);

        var attestation = NormalProviderRuntimeAttestation.Create(
            [provider],
            Configuration(
                new KeyValuePair<string, string?>(
                    "Unrelated:Secret",
                    SecretSentinel)),
            registry);

        Assert.Equal(
            NormalProviderRuntimeAttestation.SchemaVersion,
            attestation.SchemaVersion);
        Assert.Equal("normal", attestation.ProviderOrigin);
        Assert.Equal(1, attestation.ResolvedProviderCount);
        Assert.Equal(
            typeof(CounterpartyShellCapabilityProvider).FullName,
            attestation.ResolvedProviderType);
        Assert.Empty(attestation.OverrideKeysPresent);
        Assert.True(Guid.TryParseExact(
            attestation.ProcessInstanceId,
            "D",
            out var parsedProcessInstanceId));
        Assert.NotEqual(Guid.Empty, parsedProcessInstanceId);
        Assert.Equal(
            parsedProcessInstanceId.ToString("D"),
            attestation.ProcessInstanceId);
        Assert.Equal(Environment.ProcessId, attestation.ProcessId);
        Assert.Equal(registry.Revision, attestation.RegistryRevision);

        using var document = JsonDocument.Parse(
            JsonSerializer.Serialize(attestation));
        var root = document.RootElement;
        Assert.Equal(
            [
                "schemaVersion",
                "providerOrigin",
                "resolvedProviderCount",
                "resolvedProviderType",
                "overrideKeysPresent",
                "processInstanceId",
                "processId",
                "registryRevision",
            ],
            root.EnumerateObject().Select(static property => property.Name).ToArray());
        Assert.Equal(
            "C01-NORMAL-PROVIDER-ATTESTATION-V1",
            root.GetProperty("schemaVersion").GetString());
        Assert.Empty(root.GetProperty("overrideKeysPresent").EnumerateArray());
        Assert.DoesNotContain(
            SecretSentinel,
            root.GetRawText(),
            StringComparison.Ordinal);
    }

    [Fact]
    public void RepeatedCreateCalls_UseSameProcessOwnedIdentityAndProcessId()
    {
        var provider = new CounterpartyShellCapabilityProvider();
        var configuration = Configuration();
        var registry = CreateRegistry(provider);

        var first = NormalProviderRuntimeAttestation.Create(
            [provider],
            configuration,
            registry);
        var second = NormalProviderRuntimeAttestation.Create(
            [provider],
            configuration,
            registry);

        Assert.Equal(first.ProcessInstanceId, second.ProcessInstanceId);
        Assert.Equal(Environment.ProcessId, first.ProcessId);
        Assert.Equal(first.ProcessId, second.ProcessId);
        Assert.True(Guid.TryParseExact(
            first.ProcessInstanceId,
            "D",
            out var processInstanceId));
        Assert.NotEqual(Guid.Empty, processInstanceId);
        Assert.Equal(
            processInstanceId.ToString("D"),
            first.ProcessInstanceId);
    }

    [Fact]
    public void MissingProvider_FailsClosed()
    {
        var canonicalProvider = new CounterpartyShellCapabilityProvider();

        Assert.Throws<InvalidOperationException>(() =>
            NormalProviderRuntimeAttestation.Create(
                [],
                Configuration(),
                CreateRegistry(canonicalProvider)));
    }

    [Fact]
    public void MultipleProviders_FailClosed()
    {
        var canonicalProvider = new CounterpartyShellCapabilityProvider();

        Assert.Throws<InvalidOperationException>(() =>
            NormalProviderRuntimeAttestation.Create(
                [canonicalProvider, new UnknownProvider()],
                Configuration(),
                CreateRegistry(canonicalProvider)));
    }

    [Fact]
    public void UnknownProvider_FailsClosed()
    {
        var unknownProvider = new UnknownProvider();

        Assert.Throws<InvalidOperationException>(() =>
            NormalProviderRuntimeAttestation.Create(
                [unknownProvider],
                Configuration(),
                CreateRegistry(unknownProvider)));
    }

    [Fact]
    public void C01DisposableProvider_CannotAttestAsNormal()
    {
        var disposableProvider = new C01E2eShellCapabilityProvider();

        Assert.Throws<InvalidOperationException>(() =>
            NormalProviderRuntimeAttestation.Create(
                [disposableProvider],
                Configuration(),
                CreateRegistry(disposableProvider)));
    }

    [Fact]
    public void EnableConfigurationKey_FailsClosed() =>
        AssertDisposableConfigurationKeyRejected(
            C01E2eShellCapabilityActivation.EnableConfigurationKey);

    [Fact]
    public void DisposableConfirmationConfigurationKey_FailsClosed() =>
        AssertDisposableConfigurationKeyRejected(
            C01E2eShellCapabilityActivation.DisposableConfirmationConfigurationKey);

    [Fact]
    public void TestedCommitConfigurationKey_FailsClosed() =>
        AssertDisposableConfigurationKeyRejected(
            C01E2eShellCapabilityActivation.TestedCommitConfigurationKey);

    [Fact]
    public void RunIdConfigurationKey_FailsClosed() =>
        AssertDisposableConfigurationKeyRejected(
            C01E2eShellCapabilityActivation.RunIdConfigurationKey);

    [Fact]
    public void ComposeProjectConfigurationKey_FailsClosed() =>
        AssertDisposableConfigurationKeyRejected(
            C01E2eShellCapabilityActivation.ComposeProjectConfigurationKey);

    [Fact]
    public void DisposableConfigurationKeyMatching_IsCaseInsensitive()
    {
        AssertDisposableConfigurationKeyRejected(
            C01E2eShellCapabilityActivation.EnableConfigurationKey.ToUpperInvariant());
    }

    [Fact]
    public async Task MissingOutputPath_IsNoOpEvenOutsideTesting()
    {
        var services = new ServiceCollection();
        services.AddSingleton<IConfiguration>(Configuration(
            new KeyValuePair<string, string?>("Unrelated:Secret", SecretSentinel)));
        services.AddSingleton<IHostEnvironment>(
            new TestHostEnvironment("Production"));
        await using var serviceProvider = services.BuildServiceProvider();

        var attestation = await NormalProviderRuntimeAttestation
            .WriteIfRequestedAsync(serviceProvider);

        Assert.Null(attestation);
    }

    [Fact]
    public async Task ConfiguredOutputOutsideTesting_FailsClosed()
    {
        using var temporaryDirectory = new TemporaryDirectory();
        var outputPath = Path.Combine(temporaryDirectory.Path, "runtime.json");
        await using var serviceProvider = CreateWriterServices(
            "Production",
            Configuration(new KeyValuePair<string, string?>(
                NormalProviderRuntimeAttestation.OutputPathConfigurationKey,
                outputPath)),
            []);

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            NormalProviderRuntimeAttestation.WriteIfRequestedAsync(serviceProvider));

        Assert.Contains("Testing", exception.Message, StringComparison.Ordinal);
        Assert.False(File.Exists(outputPath));
    }

    [Fact]
    public async Task ExactTestingConfiguration_WritesNewAtomicJsonArtifact()
    {
        using var temporaryDirectory = new TemporaryDirectory();
        var outputPath = Path.Combine(temporaryDirectory.Path, "runtime.json");
        var provider = new CounterpartyShellCapabilityProvider();
        await using var serviceProvider = CreateWriterServices(
            "Testing",
            Configuration(
                new KeyValuePair<string, string?>(
                    NormalProviderRuntimeAttestation.OutputPathConfigurationKey,
                    outputPath),
                new KeyValuePair<string, string?>(
                    "Unrelated:Secret",
                    SecretSentinel)),
            [provider],
            CreateRegistry(provider));

        var attestation = await NormalProviderRuntimeAttestation
            .WriteIfRequestedAsync(serviceProvider);

        Assert.NotNull(attestation);
        var bytes = await File.ReadAllBytesAsync(outputPath);
        using var document = JsonDocument.Parse(bytes);
        Assert.Equal(
            attestation.ProcessInstanceId,
            document.RootElement.GetProperty("processInstanceId").GetString());
        Assert.Equal(
            attestation.ProcessId,
            document.RootElement.GetProperty("processId").GetInt32());
        Assert.Empty(
            document.RootElement
                .GetProperty("overrideKeysPresent")
                .EnumerateArray());
        Assert.DoesNotContain(
            SecretSentinel,
            document.RootElement.GetRawText(),
            StringComparison.Ordinal);
        Assert.Empty(Directory.EnumerateFiles(
            temporaryDirectory.Path,
            "*.tmp",
            SearchOption.TopDirectoryOnly));
    }

    [Fact]
    public async Task ExistingOutputFile_IsNeverOverwritten()
    {
        using var temporaryDirectory = new TemporaryDirectory();
        var outputPath = Path.Combine(temporaryDirectory.Path, "runtime.json");
        const string existingContent = "pre-existing-attestation";
        await File.WriteAllTextAsync(outputPath, existingContent);
        var provider = new CounterpartyShellCapabilityProvider();
        await using var serviceProvider = CreateWriterServices(
            "Testing",
            OutputConfiguration(outputPath),
            [provider],
            CreateRegistry(provider));

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            NormalProviderRuntimeAttestation.WriteIfRequestedAsync(serviceProvider));

        Assert.Equal(existingContent, await File.ReadAllTextAsync(outputPath));
        Assert.Empty(Directory.EnumerateFiles(
            temporaryDirectory.Path,
            "*.tmp",
            SearchOption.TopDirectoryOnly));
    }

    [Fact]
    public async Task PreCanceledWrite_LeavesNoFinalOrTemporaryArtifact()
    {
        using var temporaryDirectory = new TemporaryDirectory();
        var outputPath = Path.Combine(temporaryDirectory.Path, "runtime.json");
        var provider = new CounterpartyShellCapabilityProvider();
        await using var serviceProvider = CreateWriterServices(
            "Testing",
            OutputConfiguration(outputPath),
            [provider],
            CreateRegistry(provider));
        using var cancellation = new CancellationTokenSource();
        await cancellation.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            NormalProviderRuntimeAttestation.WriteIfRequestedAsync(
                serviceProvider,
                cancellation.Token));

        Assert.False(File.Exists(outputPath));
        Assert.Empty(Directory.EnumerateFiles(
            temporaryDirectory.Path,
            "*.tmp",
            SearchOption.TopDirectoryOnly));
    }

    [Fact]
    public async Task WriterWithDisposableOverride_FailsWithoutArtifactOrSecretLeak()
    {
        using var temporaryDirectory = new TemporaryDirectory();
        var outputPath = Path.Combine(temporaryDirectory.Path, "runtime.json");
        var provider = new CounterpartyShellCapabilityProvider();
        await using var serviceProvider = CreateWriterServices(
            "Testing",
            Configuration(
                new KeyValuePair<string, string?>(
                    NormalProviderRuntimeAttestation.OutputPathConfigurationKey,
                    outputPath),
                new KeyValuePair<string, string?>(
                    C01E2eShellCapabilityActivation.TestedCommitConfigurationKey,
                    SecretSentinel)),
            [provider],
            CreateRegistry(provider));

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            NormalProviderRuntimeAttestation.WriteIfRequestedAsync(serviceProvider));

        Assert.DoesNotContain(
            SecretSentinel,
            exception.ToString(),
            StringComparison.Ordinal);
        Assert.False(File.Exists(outputPath));
    }

    [Fact]
    public async Task WriterWithUnknownProvider_FailsWithoutArtifact()
    {
        using var temporaryDirectory = new TemporaryDirectory();
        var outputPath = Path.Combine(temporaryDirectory.Path, "runtime.json");
        var provider = new UnknownProvider();
        await using var serviceProvider = CreateWriterServices(
            "Testing",
            OutputConfiguration(outputPath),
            [provider],
            CreateRegistry(provider));

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            NormalProviderRuntimeAttestation.WriteIfRequestedAsync(serviceProvider));

        Assert.False(File.Exists(outputPath));
    }

    [Fact]
    public void RegistryFromDifferentProviderSet_FailsClosed()
    {
        var provider = new CounterpartyShellCapabilityProvider();

        Assert.Throws<InvalidOperationException>(() =>
            NormalProviderRuntimeAttestation.Create(
                [provider],
                Configuration(),
                CreateRegistry()));
    }

    private static void AssertDisposableConfigurationKeyRejected(string key)
    {
        var provider = new CounterpartyShellCapabilityProvider();
        var exception = Assert.Throws<InvalidOperationException>(() =>
            NormalProviderRuntimeAttestation.Create(
                [provider],
                Configuration(
                    new KeyValuePair<string, string?>(key, SecretSentinel)),
                CreateRegistry(provider)));

        Assert.DoesNotContain(
            SecretSentinel,
            exception.ToString(),
            StringComparison.Ordinal);
    }

    private static ShellCapabilityRegistry CreateRegistry(
        params IShellCapabilityProvider[] providers)
    {
        var revision = ShellCapabilityRegistry.ComputeCandidateRevision(providers);
        return new ShellCapabilityRegistry(
            providers,
            new ShellCapabilityAuthorityCatalog([revision]));
    }

    private static IConfiguration Configuration(
        params KeyValuePair<string, string?>[] values) =>
        new ConfigurationBuilder()
            .AddInMemoryCollection(values)
            .Build();

    private static IConfiguration OutputConfiguration(string outputPath) =>
        Configuration(new KeyValuePair<string, string?>(
            NormalProviderRuntimeAttestation.OutputPathConfigurationKey,
            outputPath));

    private static ServiceProvider CreateWriterServices(
        string environmentName,
        IConfiguration configuration,
        IReadOnlyCollection<IShellCapabilityProvider> providers,
        ShellCapabilityRegistry? registry = null)
    {
        var services = new ServiceCollection();
        services.AddSingleton(configuration);
        services.AddSingleton<IHostEnvironment>(
            new TestHostEnvironment(environmentName));
        foreach (var provider in providers)
        {
            services.AddSingleton<IShellCapabilityProvider>(provider);
        }

        if (registry is not null)
        {
            services.AddSingleton(registry);
        }

        return services.BuildServiceProvider();
    }

    private sealed class TestHostEnvironment(string environmentName) : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = environmentName;

        public string ApplicationName { get; set; } =
            "GoymenERP.Api.IntegrationTests";

        public string ContentRootPath { get; set; } = AppContext.BaseDirectory;

        public IFileProvider ContentRootFileProvider { get; set; } =
            new NullFileProvider();
    }

    private sealed class TemporaryDirectory : IDisposable
    {
        internal TemporaryDirectory()
        {
            Path = System.IO.Path.Combine(
                System.IO.Path.GetTempPath(),
                "GoymenERP.NormalProviderRuntimeAttestationTests",
                Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(Path);
        }

        internal string Path { get; }

        public void Dispose()
        {
            Directory.Delete(Path, recursive: true);
        }
    }

    private sealed class UnknownProvider : IShellCapabilityProvider
    {
        public string Owner => "TEST-UNKNOWN";

        public string ContractVersion => "1.0";

        public IReadOnlyCollection<ShellCapabilityDeclaration> GetDeclarations() => [];
    }
}
