using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;
using GoymenERP.BuildingBlocks.Shell;
using GoymenERP.Modules.Counterparty.Infrastructure.Shell;
using Microsoft.Extensions.DependencyInjection;

namespace GoymenERP.Api.Shell;

internal static class NormalProviderRuntimeAttestation
{
    internal const string SchemaVersion =
        "C01-NORMAL-PROVIDER-ATTESTATION-V1";
    internal const string NormalProviderOrigin = "normal";
    internal const string OutputPathConfigurationKey =
        "NormalProviderAttestation:OutputPath";

    internal static NormalProviderRuntimeAttestationRecord Create(
        IEnumerable<IShellCapabilityProvider> resolvedProviders,
        IConfiguration configuration,
        ShellCapabilityRegistry registry)
    {
        ArgumentNullException.ThrowIfNull(resolvedProviders);
        ArgumentNullException.ThrowIfNull(configuration);
        ArgumentNullException.ThrowIfNull(registry);

        var providerArray = resolvedProviders.ToArray();
        var presentOverrideKeys = FindPresentOverrideKeys(configuration);
        if (presentOverrideKeys.Count > 0)
        {
            throw new InvalidOperationException(
                "Normal provider runtime attestation disposable C-01 "
                + $"configuration key içeremez: {string.Join(", ", presentOverrideKeys)}.");
        }

        if (providerArray.Length != 1)
        {
            throw new InvalidOperationException(
                "Normal provider runtime attestation tam olarak bir shell capability "
                + "provider gerektirir.");
        }

        var provider = providerArray[0];
        if (provider is null
            || provider.GetType() != typeof(CounterpartyShellCapabilityProvider))
        {
            throw new InvalidOperationException(
                "Normal provider runtime attestation yalnız exact normal shell "
                + "capability provider tipini kabul eder.");
        }

        var expectedRegistryRevision =
            ShellCapabilityRegistry.ComputeCandidateRevision(providerArray);
        if (!string.Equals(
                registry.Revision,
                expectedRegistryRevision,
                StringComparison.Ordinal)
            || !IsCanonicalRegistryRevision(registry.Revision))
        {
            throw new InvalidOperationException(
                "Normal provider runtime attestation registry revision ile resolved "
                + "provider setini eşleyemedi.");
        }

        var resolvedProviderType = provider.GetType().FullName
            ?? throw new InvalidOperationException(
                "Normal provider runtime attestation provider tip adını çözemedi.");

        return new NormalProviderRuntimeAttestationRecord(
            SchemaVersion,
            NormalProviderOrigin,
            providerArray.Length,
            resolvedProviderType,
            EmptyOverrideKeys,
            ProcessInstanceId,
            Environment.ProcessId,
            registry.Revision);
    }

    internal static async Task<NormalProviderRuntimeAttestationRecord?>
        WriteIfRequestedAsync(
            IServiceProvider services,
            CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(services);

        var configuration = services.GetRequiredService<IConfiguration>();
        if (!HasConfigurationKey(configuration, OutputPathConfigurationKey))
        {
            return null;
        }

        var environment = services.GetRequiredService<IHostEnvironment>();
        if (!environment.IsEnvironment("Testing"))
        {
            throw new InvalidOperationException(
                "Normal provider runtime attestation yalnız Testing ortamında "
                + "yazılabilir.");
        }

        if (!TryGetCanonicalOutputPath(
                configuration[OutputPathConfigurationKey],
                out var outputPath))
        {
            throw new InvalidOperationException(
                "Normal provider runtime attestation geçerli bir absolute output "
                + "path gerektirir.");
        }

        var attestation = Create(
            services.GetServices<IShellCapabilityProvider>(),
            configuration,
            services.GetRequiredService<ShellCapabilityRegistry>());
        var bytes = JsonSerializer.SerializeToUtf8Bytes(attestation);
        if (!await TryWriteNewFileAtomicallyAsync(
                outputPath,
                bytes,
                cancellationToken))
        {
            throw new InvalidOperationException(
                "Normal provider runtime attestation yeni output dosyasına "
                + "yazılamadı.");
        }

        return attestation;
    }

    private static IReadOnlyList<string> FindPresentOverrideKeys(
        IConfiguration configuration)
    {
        var keys = configuration
            .AsEnumerable()
            .Select(static entry => entry.Key)
            .Where(key => DisposableConfigurationKeys.Contains(
                key,
                StringComparer.OrdinalIgnoreCase))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(static key => key, StringComparer.Ordinal)
            .ToArray();
        return Array.AsReadOnly(keys);
    }

    private static bool HasConfigurationKey(
        IConfiguration configuration,
        string expectedKey) =>
        configuration
            .AsEnumerable()
            .Select(static entry => entry.Key)
            .Any(key => string.Equals(
                key,
                expectedKey,
                StringComparison.OrdinalIgnoreCase));

    private static bool TryGetCanonicalOutputPath(
        string? configuredPath,
        out string outputPath)
    {
        outputPath = string.Empty;
        try
        {
            if (string.IsNullOrWhiteSpace(configuredPath)
                || !string.Equals(
                    configuredPath,
                    configuredPath.Trim(),
                    StringComparison.Ordinal)
                || !Path.IsPathFullyQualified(configuredPath)
                || configuredPath.IndexOf('\0') >= 0)
            {
                return false;
            }

            var fullPath = Path.GetFullPath(configuredPath);
            var pathComparison = OperatingSystem.IsWindows()
                ? StringComparison.OrdinalIgnoreCase
                : StringComparison.Ordinal;
            if (!string.Equals(configuredPath, fullPath, pathComparison)
                || string.IsNullOrWhiteSpace(Path.GetFileName(fullPath)))
            {
                return false;
            }

            outputPath = fullPath;
            return true;
        }
        catch (Exception exception) when (
            exception is IOException
            or UnauthorizedAccessException
            or NotSupportedException
            or ArgumentException)
        {
            return false;
        }
    }

    private static async Task<bool> TryWriteNewFileAtomicallyAsync(
        string outputPath,
        byte[] bytes,
        CancellationToken cancellationToken)
    {
        var directory = Path.GetDirectoryName(outputPath);
        if (string.IsNullOrWhiteSpace(directory)
            || !Directory.Exists(directory)
            || File.Exists(outputPath)
            || Directory.Exists(outputPath))
        {
            return false;
        }

        var temporaryPath = Path.Combine(
            directory,
            $".{Path.GetFileName(outputPath)}.{ProcessInstanceId}.tmp");
        var temporaryFileCreated = false;
        try
        {
            await using (var stream = new FileStream(
                             temporaryPath,
                             FileMode.CreateNew,
                             FileAccess.Write,
                             FileShare.None,
                             bufferSize: 4096,
                             FileOptions.Asynchronous | FileOptions.WriteThrough))
            {
                temporaryFileCreated = true;
                await stream.WriteAsync(bytes, cancellationToken);
                await stream.FlushAsync(cancellationToken);
                stream.Flush(flushToDisk: true);
            }

            File.Move(temporaryPath, outputPath, overwrite: false);
            temporaryFileCreated = false;
            return true;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception exception) when (
            exception is IOException
            or UnauthorizedAccessException
            or NotSupportedException
            or ArgumentException)
        {
            return false;
        }
        finally
        {
            if (temporaryFileCreated)
            {
                try
                {
                    File.Delete(temporaryPath);
                }
                catch (Exception exception) when (
                    exception is IOException
                    or UnauthorizedAccessException
                    or NotSupportedException)
                {
                    // Only the temp file successfully created above is cleaned up.
                }
            }
        }
    }

    private static bool IsCanonicalRegistryRevision(string revision)
    {
        const string prefix = "sha256:";
        const int sha256Base64UrlLength = 43;

        if (!revision.StartsWith(prefix, StringComparison.Ordinal)
            || revision.Length != prefix.Length + sha256Base64UrlLength)
        {
            return false;
        }

        foreach (var character in revision.AsSpan(prefix.Length))
        {
            if (character is not (>= 'A' and <= 'Z')
                and not (>= 'a' and <= 'z')
                and not (>= '0' and <= '9')
                and not '-'
                and not '_')
            {
                return false;
            }
        }

        return true;
    }

    private static readonly string[] DisposableConfigurationKeys =
    [
        C01E2eShellCapabilityActivation.EnableConfigurationKey,
        C01E2eShellCapabilityActivation.DisposableConfirmationConfigurationKey,
        C01E2eShellCapabilityActivation.TestedCommitConfigurationKey,
        C01E2eShellCapabilityActivation.RunIdConfigurationKey,
        C01E2eShellCapabilityActivation.ComposeProjectConfigurationKey,
    ];

    private static readonly IReadOnlyList<string> EmptyOverrideKeys =
        Array.AsReadOnly(Array.Empty<string>());

    private static readonly string ProcessInstanceId = Guid.NewGuid()
        .ToString("D", CultureInfo.InvariantCulture);
}

internal sealed record NormalProviderRuntimeAttestationRecord
{
    internal NormalProviderRuntimeAttestationRecord(
        string schemaVersion,
        string providerOrigin,
        int resolvedProviderCount,
        string resolvedProviderType,
        IReadOnlyList<string> overrideKeysPresent,
        string processInstanceId,
        int processId,
        string registryRevision)
    {
        SchemaVersion = schemaVersion;
        ProviderOrigin = providerOrigin;
        ResolvedProviderCount = resolvedProviderCount;
        ResolvedProviderType = resolvedProviderType;
        OverrideKeysPresent = overrideKeysPresent;
        ProcessInstanceId = processInstanceId;
        ProcessId = processId;
        RegistryRevision = registryRevision;
    }

    [JsonPropertyName("schemaVersion")]
    [JsonPropertyOrder(0)]
    public string SchemaVersion { get; }

    [JsonPropertyName("providerOrigin")]
    [JsonPropertyOrder(1)]
    public string ProviderOrigin { get; }

    [JsonPropertyName("resolvedProviderCount")]
    [JsonPropertyOrder(2)]
    public int ResolvedProviderCount { get; }

    [JsonPropertyName("resolvedProviderType")]
    [JsonPropertyOrder(3)]
    public string ResolvedProviderType { get; }

    [JsonPropertyName("overrideKeysPresent")]
    [JsonPropertyOrder(4)]
    public IReadOnlyList<string> OverrideKeysPresent { get; }

    [JsonPropertyName("processInstanceId")]
    [JsonPropertyOrder(5)]
    public string ProcessInstanceId { get; }

    [JsonPropertyName("processId")]
    [JsonPropertyOrder(6)]
    public int ProcessId { get; }

    [JsonPropertyName("registryRevision")]
    [JsonPropertyOrder(7)]
    public string RegistryRevision { get; }
}
