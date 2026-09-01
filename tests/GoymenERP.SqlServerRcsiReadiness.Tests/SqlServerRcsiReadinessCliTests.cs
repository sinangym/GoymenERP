using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using GoymenERP.BuildingBlocks.SqlServer;
using Microsoft.Data.SqlClient;

namespace GoymenERP.SqlServerRcsiReadiness.Tests;

public sealed class SqlServerRcsiReadinessCliTests : IDisposable
{
    private const string ConnectionEnvironment = "GOYMEN_ERP_RCSI_PROBE_CONNECTION";
    private const string DatabaseName = "GöymenERP_Üretim";
    private const string LogicalEndpointASha256 =
        "75a99409b81404222ad7347d4dad424dfab7b355844a736531e2105eb621c05f";
    private const string LogicalEndpointBSha256 =
        "f67561664930dd93b930601f71be9bcd6ce230064681b7b7f03695df0c745fe1";
    private static readonly DateTimeOffset ObservedAtUtc =
        new(2026, 9, 1, 10, 20, 30, TimeSpan.Zero);
    private readonly RuntimePayloadFixture _payload = RuntimePayloadFixture.CreateBuiltTool();

    public void Dispose() => _payload.Dispose();

    [Fact]
    public async Task ValidInvocation_WritesCanonicalUtf8BytesAndBindsExecutedPayload()
    {
        var directory = CreateTemporaryDirectory();
        var outputPath = Path.Combine(directory, "ready.json");
        try
        {
            await using var standardOutput = new MemoryStream();
            using var standardError = new StringWriter();
            var exitCode = await RunAsync(
                ValidArguments(outputPath),
                SafeEnvironment,
                standardOutput,
                standardError,
                ReadyProbe);

            Assert.Equal(0, exitCode);
            var fileBytes = await File.ReadAllBytesAsync(outputPath);
            Assert.Equal(fileBytes, standardOutput.ToArray());
            Assert.False(fileBytes.AsSpan().StartsWith(new byte[] { 0xEF, 0xBB, 0xBF }));
            Assert.Empty(standardError.ToString());
            using var document = JsonDocument.Parse(fileBytes);
            var root = document.RootElement;
            Assert.Equal(DatabaseName, root.GetProperty("expectedDatabase").GetString());
            Assert.Equal(_payload.SourceRevisionId, root.GetProperty("testedCommit").GetString());
            Assert.Equal(
                _payload.ToolAssemblySha256,
                root.GetProperty("expectedToolAssemblySha256").GetString());
            Assert.Equal(
                _payload.ToolAssemblySha256,
                root.GetProperty("observedToolAssemblySha256").GetString());
            Assert.Equal(
                _payload.ManifestSha256,
                root.GetProperty("runtimePayloadManifestSha256").GetString());
            var json = Encoding.UTF8.GetString(fileBytes);
            Assert.DoesNotContain("SuperSecret", json, StringComparison.Ordinal);
            Assert.DoesNotContain("secret-host", json, StringComparison.Ordinal);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public async Task ConcurrentFreshInvocations_ToSamePathHaveOneWinnerAndNoResidue()
    {
        var directory = CreateTemporaryDirectory();
        var outputPath = Path.Combine(directory, "winner.json");
        var entered = 0;
        var bothEntered = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        try
        {
            async Task<SqlServerRcsiReadinessEvidence> Probe(
                SqlConnection _,
                SqlServerRcsiReadinessBinding binding,
                CancellationToken cancellationToken)
            {
                if (Interlocked.Increment(ref entered) == 2)
                {
                    bothEntered.TrySetResult();
                }

                await bothEntered.Task.WaitAsync(cancellationToken);
                return ReadyEvidence(binding);
            }

            var first = InvokeConcurrent(
                outputPath,
                "11111111-1111-1111-1111-111111111111",
                Probe);
            var second = InvokeConcurrent(
                outputPath,
                "22222222-2222-2222-2222-222222222222",
                Probe);
            var results = await Task.WhenAll(first, second);

            Assert.Equal([0, 5], results.Select(item => item.ExitCode).Order().ToArray());
            var winner = Assert.Single(results, item => item.ExitCode == 0);
            var loser = Assert.Single(results, item => item.ExitCode == 5);
            var fileBytes = await File.ReadAllBytesAsync(outputPath);
            Assert.Equal(fileBytes, winner.StandardOutput);
            using var winnerJson = JsonDocument.Parse(fileBytes);
            Assert.Equal(winner.RunId, winnerJson.RootElement.GetProperty("runId").GetString());
            using var loserJson = JsonDocument.Parse(loser.StandardOutput);
            Assert.Equal(
                "EVIDENCE_WRITE_FAILED",
                loserJson.RootElement.GetProperty("reasonCode").GetString());
            Assert.Empty(Directory.GetFiles(directory, "*.tmp"));
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public async Task StaleOutput_IsExitFiveWithoutSecretProbeOverwriteOrTemp()
    {
        var directory = CreateTemporaryDirectory();
        var outputPath = Path.Combine(directory, "ready.json");
        await File.WriteAllTextAsync(outputPath, "stale-ready");
        var secretRead = false;
        var probeStarted = false;
        try
        {
            await using var output = new MemoryStream();
            using var error = new StringWriter();
            var exitCode = await RunAsync(
                ValidArguments(outputPath),
                name =>
                {
                    if (name == ConnectionEnvironment)
                    {
                        secretRead = true;
                    }

                    return null;
                },
                output,
                error,
                (_, _, _) =>
                {
                    probeStarted = true;
                    throw new InvalidOperationException();
                });

            Assert.Equal(5, exitCode);
            Assert.False(secretRead);
            Assert.False(probeStarted);
            Assert.Equal("stale-ready", await File.ReadAllTextAsync(outputPath));
            Assert.Empty(Directory.GetFiles(directory, "*.tmp"));
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public async Task ProbeTimeout_IsDeterministicExitFive()
    {
        var directory = CreateTemporaryDirectory();
        var outputPath = Path.Combine(directory, "timeout.json");
        try
        {
            await using var output = new MemoryStream();
            using var error = new StringWriter();
            var stopwatch = Stopwatch.StartNew();
            var exitCode = await RunAsync(
                ValidArguments(outputPath, timeoutSeconds: "1"),
                SafeEnvironment,
                output,
                error,
                static async (_, _, token) =>
                {
                    await Task.Delay(Timeout.InfiniteTimeSpan, token);
                    throw new InvalidOperationException();
                });

            Assert.Equal(5, exitCode);
            Assert.InRange(stopwatch.Elapsed, TimeSpan.FromMilliseconds(500), TimeSpan.FromSeconds(5));
            Assert.Equal(await File.ReadAllBytesAsync(outputPath), output.ToArray());
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public async Task CallerCancellation_PropagatesWithoutEvidenceOrSecretDisclosure()
    {
        var directory = CreateTemporaryDirectory();
        var outputPath = Path.Combine(directory, "cancelled.json");
        using var cancellationSource = new CancellationTokenSource();
        try
        {
            await using var output = new MemoryStream();
            using var error = new StringWriter();
            var invocation = RunAsync(
                ValidArguments(outputPath),
                SafeEnvironment,
                output,
                error,
                static async (_, _, token) =>
                {
                    await Task.Delay(Timeout.InfiniteTimeSpan, token);
                    throw new InvalidOperationException();
                },
                cancellationSource.Token);

            await cancellationSource.CancelAsync();

            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => invocation);
            Assert.False(File.Exists(outputPath));
            Assert.Empty(output.ToArray());
            Assert.Empty(error.ToString());
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public async Task MalformedConnectionSecret_IsExitTwoWithoutEchoingSecret()
    {
        var directory = CreateTemporaryDirectory();
        var outputPath = Path.Combine(directory, "invalid-secret.json");
        const string rawSecret = "Server=secret-host;Password=SuperSecret;malformed";
        try
        {
            await using var output = new MemoryStream();
            using var error = new StringWriter();
            var probeStarted = false;
            var exitCode = await RunAsync(
                ValidArguments(outputPath),
                name => name == ConnectionEnvironment ? rawSecret : null,
                output,
                error,
                (_, _, _) =>
                {
                    probeStarted = true;
                    throw new InvalidOperationException();
                });

            Assert.Equal(2, exitCode);
            Assert.False(probeStarted);
            var fileBytes = await File.ReadAllBytesAsync(outputPath);
            Assert.Equal(fileBytes, output.ToArray());
            var combined = Encoding.UTF8.GetString(fileBytes) + error;
            Assert.DoesNotContain(rawSecret, combined, StringComparison.Ordinal);
            Assert.DoesNotContain("SuperSecret", combined, StringComparison.Ordinal);
            Assert.DoesNotContain("secret-host", combined, StringComparison.Ordinal);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    public static TheoryData<string> InvalidArgumentCases => new()
    {
        "timeout-zero", "timeout-negative", "timeout-overflow", "unsafe-env",
        "empty-run", "invalid-run", "invalid-commit", "missing-endpoint",
        "invalid-endpoint", "invalid-tool-hash", "duplicate-scalar",
        "unknown-option", "missing-option", "bad-output"
    };

    [Theory]
    [MemberData(nameof(InvalidArgumentCases))]
    public async Task InvalidArgumentMatrix_IsExitTwoBeforeSecretProbeOrArtifact(string scenario)
    {
        var directory = CreateTemporaryDirectory();
        var outputPath = Path.Combine(directory, "invalid.json");
        var arguments = ValidArguments(outputPath).ToList();
        MutateInvalidArguments(arguments, scenario);
        var secretRead = false;
        var probeStarted = false;
        var artifactVerificationStarted = false;
        try
        {
            await using var output = new MemoryStream();
            using var error = new StringWriter();
            var exitCode = await RunAsync(
                arguments.ToArray(),
                name =>
                {
                    if (name == ConnectionEnvironment)
                    {
                        secretRead = true;
                    }

                    return null;
                },
                output,
                error,
                (_, _, _) =>
                {
                    probeStarted = true;
                    throw new InvalidOperationException();
                },
                verifyRuntimePayload: (
                    string? _,
                    string? _,
                    string? _,
                    out SqlServerRcsiReadinessToolArtifactIdentity identity) =>
                {
                    artifactVerificationStarted = true;
                    identity = null!;
                    return false;
                });

            Assert.Equal(2, exitCode);
            Assert.False(secretRead);
            Assert.False(probeStarted);
            Assert.False(artifactVerificationStarted);
            Assert.Empty(Directory.GetFiles(directory));
            Assert.Empty(error.ToString());
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Theory]
    [InlineData("commit")]
    [InlineData("tool-hash")]
    [InlineData("manifest-hash")]
    public async Task ArtifactBindingMismatch_IsExitTwoBeforeSecretProbeOrOutput(string mismatch)
    {
        var directory = CreateTemporaryDirectory();
        var outputPath = Path.Combine(directory, "mismatch.json");
        var arguments = ValidArguments(outputPath);
        ReplaceValue(
            arguments,
            mismatch switch
            {
                "commit" => "--tested-commit",
                "tool-hash" => "--expected-tool-assembly-sha256",
                _ => "--expected-runtime-payload-manifest-sha256",
            },
            mismatch == "commit" ? new string('b', 40) : new string('d', 64));
        await AssertPreSecretExitTwo(arguments, outputPath);
    }

    [Fact]
    public async Task DependencyMutation_WithUnchangedToolDll_IsRejectedBeforeSecretProbe()
    {
        using var isolatedPayload = _payload.CreateIsolatedCopy();
        var toolHashBefore = isolatedPayload.ToolAssemblySha256;
        await File.AppendAllTextAsync(
            Path.Combine(isolatedPayload.PayloadRoot, "GoymenERP.BuildingBlocks.SqlServer.dll"),
            "dependency-drift");
        Assert.Equal(
            toolHashBefore,
            ComputeFileSha256(Path.Combine(
                isolatedPayload.PayloadRoot,
                "GoymenERP.SqlServerRcsiReadiness.dll")));
        var directory = CreateTemporaryDirectory();
        try
        {
            var arguments = ValidArguments(Path.Combine(directory, "drift.json"), isolatedPayload);
            await AssertPreSecretExitTwo(arguments, Path.Combine(directory, "drift.json"));
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Theory]
    [InlineData("extra")]
    [InlineData("missing")]
    public async Task RuntimePayloadInventoryDrift_IsRejectedBeforeSecretProbe(
        string mutation)
    {
        using var isolatedPayload = _payload.CreateIsolatedCopy();
        if (mutation == "extra")
        {
            await File.WriteAllTextAsync(
                Path.Combine(isolatedPayload.PayloadRoot, "unreviewed-runtime-file.bin"),
                "unreviewed");
        }
        else
        {
            File.Delete(Path.Combine(
                isolatedPayload.PayloadRoot,
                "GoymenERP.BuildingBlocks.SqlServer.dll"));
        }

        var directory = CreateTemporaryDirectory();
        var outputPath = Path.Combine(directory, "inventory-drift.json");
        try
        {
            await AssertPreSecretExitTwo(
                ValidArguments(outputPath, isolatedPayload),
                outputPath);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public async Task EvidencePathInsideRuntimePayload_IsRejectedBeforeSecretProbe()
    {
        using var isolatedPayload = _payload.CreateIsolatedCopy();
        var outputPath = Path.Combine(
            isolatedPayload.PayloadRoot,
            $"evidence-{Guid.NewGuid():N}.json");

        await AssertPreSecretExitTwo(
            ValidArguments(outputPath, isolatedPayload),
            outputPath);
    }

    public static TheoryData<string> MalformedManifestCases => new()
    {
        "contract-kind", "source-kind", "files-kind", "path-kind", "length-kind",
        "length-overflow", "sha-kind", "missing-property", "extra-property"
    };

    [Theory]
    [MemberData(nameof(MalformedManifestCases))]
    public async Task MalformedRuntimePayloadManifest_IsControlledExitTwo(
        string scenario)
    {
        using var isolatedPayload = _payload.CreateIsolatedCopy();
        isolatedPayload.ReplaceManifestWithMalformedDocument(scenario);
        var directory = CreateTemporaryDirectory();
        var outputPath = Path.Combine(directory, "malformed-manifest.json");
        try
        {
            await AssertPreSecretExitTwo(
                ValidArguments(outputPath, isolatedPayload),
                outputPath);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public async Task RuntimePayloadThroughReparseAncestor_IsRejectedBeforeSecretProbe()
    {
        using var isolatedPayload = _payload.CreateIsolatedCopy();
        var linkContainer = CreateTemporaryDirectory();
        var linkedPayloadRoot = Path.Combine(linkContainer, "linked-payload");
        try
        {
            Directory.CreateSymbolicLink(linkedPayloadRoot, isolatedPayload.PayloadRoot);
            var directory = CreateTemporaryDirectory();
            try
            {
                var outputPath = Path.Combine(directory, "reparse.json");
                var arguments = ValidArguments(outputPath, isolatedPayload);
                ReplaceValue(arguments, "--runtime-payload-root", linkedPayloadRoot);
                await AssertPreSecretExitTwo(arguments, outputPath);
            }
            finally
            {
                Directory.Delete(directory, recursive: true);
            }
        }
        finally
        {
            if (Directory.Exists(linkedPayloadRoot))
            {
                Directory.Delete(linkedPayloadRoot);
            }

            Directory.Delete(linkContainer, recursive: true);
        }
    }

    [Fact]
    public async Task RuntimeInjectionEnvironment_IsRejectedBeforeSecretProbe()
    {
        var directory = CreateTemporaryDirectory();
        var outputPath = Path.Combine(directory, "hook.json");
        var secretRead = false;
        try
        {
            await using var output = new MemoryStream();
            using var error = new StringWriter();
            var exitCode = await RunAsync(
                ValidArguments(outputPath),
                name => name switch
                {
                    "DOTNET_STARTUP_HOOKS" => "Injected.dll",
                    var value when value == ConnectionEnvironment => ReadSecret(),
                    _ => null,
                },
                output,
                error,
                ReadyProbe);

            Assert.Equal(2, exitCode);
            Assert.False(secretRead);
            Assert.False(File.Exists(outputPath));

            string ReadSecret()
            {
                secretRead = true;
                return "Server=secret-host;Password=SuperSecret;";
            }
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    private async Task AssertPreSecretExitTwo(string[] arguments, string outputPath)
    {
        var secretRead = false;
        var probeStarted = false;
        await using var output = new MemoryStream();
        using var error = new StringWriter();
        var exitCode = await RunAsync(
            arguments,
            name =>
            {
                if (name == ConnectionEnvironment)
                {
                    secretRead = true;
                }

                return null;
            },
            output,
            error,
            (_, _, _) =>
            {
                probeStarted = true;
                throw new InvalidOperationException();
            });
        Assert.Equal(2, exitCode);
        Assert.False(secretRead);
        Assert.False(probeStarted);
        Assert.False(File.Exists(outputPath));
        Assert.Empty(error.ToString());
    }

    private async Task<ConcurrentResult> InvokeConcurrent(
        string outputPath,
        string runId,
        Func<SqlConnection, SqlServerRcsiReadinessBinding, CancellationToken,
            Task<SqlServerRcsiReadinessEvidence>> probe)
    {
        await using var output = new MemoryStream();
        using var error = new StringWriter();
        var arguments = ValidArguments(outputPath);
        ReplaceValue(arguments, "--run-id", runId);
        var exitCode = await RunAsync(
            arguments,
            SafeEnvironment,
            output,
            error,
            probe);
        return new ConcurrentResult(exitCode, output.ToArray(), runId);
    }

    private string[] ValidArguments(
        string outputPath,
        RuntimePayloadFixture? payload = null,
        string timeoutSeconds = "30")
    {
        payload ??= _payload;
        return
        [
            "--connection-string-env", ConnectionEnvironment,
            "--expected-database", DatabaseName,
            "--target-label", "staging/blue",
            "--expected-logical-endpoint-sha256", LogicalEndpointBSha256,
            "--expected-logical-endpoint-sha256", LogicalEndpointASha256,
            "--run-id", "11111111-1111-1111-1111-111111111111",
            "--tested-commit", payload.SourceRevisionId,
            "--expected-tool-assembly-sha256", payload.ToolAssemblySha256,
            "--runtime-payload-root", payload.PayloadRoot,
            "--runtime-payload-manifest", payload.ManifestPath,
            "--expected-runtime-payload-manifest-sha256", payload.ManifestSha256,
            "--timeout-seconds", timeoutSeconds,
            "--output", outputPath,
        ];
    }

    private static Task<int> RunAsync(
        string[] arguments,
        Func<string, string?> getEnvironmentVariable,
        Stream output,
        TextWriter error,
        Func<SqlConnection, SqlServerRcsiReadinessBinding, CancellationToken,
            Task<SqlServerRcsiReadinessEvidence>> probe,
        CancellationToken cancellationToken = default,
        SqlServerRcsiReadinessRuntimePayloadVerifierDelegate? verifyRuntimePayload = null) =>
        global::SqlServerRcsiReadinessCli.RunAsync(
            arguments,
            getEnvironmentVariable,
            output,
            error,
            new FixedTimeProvider(ObservedAtUtc),
            probe,
            cancellationToken,
            verifyRuntimePayload);

    private static string? SafeEnvironment(string name) =>
        name == ConnectionEnvironment
            ? "Server=secret-host;Password=SuperSecret;"
            : null;

    private static Task<SqlServerRcsiReadinessEvidence> ReadyProbe(
        SqlConnection _,
        SqlServerRcsiReadinessBinding binding,
        CancellationToken __) => Task.FromResult(ReadyEvidence(binding));

    private static SqlServerRcsiReadinessEvidence ReadyEvidence(
        SqlServerRcsiReadinessBinding binding) =>
        SqlServerRcsiReadinessPolicy.Evaluate(
            binding,
            DatabaseName,
            DatabaseName,
            LogicalEndpointASha256,
            1);

    private static void MutateInvalidArguments(List<string> arguments, string scenario)
    {
        switch (scenario)
        {
            case "timeout-zero": ReplaceValue(arguments, "--timeout-seconds", "0"); break;
            case "timeout-negative": ReplaceValue(arguments, "--timeout-seconds", "-1"); break;
            case "timeout-overflow": ReplaceValue(arguments, "--timeout-seconds", long.MaxValue.ToString()); break;
            case "unsafe-env": ReplaceValue(arguments, "--connection-string-env", "BAD-NAME"); break;
            case "empty-run": ReplaceValue(arguments, "--run-id", string.Empty); break;
            case "invalid-run": ReplaceValue(arguments, "--run-id", "bad"); break;
            case "invalid-commit": ReplaceValue(arguments, "--tested-commit", "bad"); break;
            case "missing-endpoint": RemoveOptions(arguments, "--expected-logical-endpoint-sha256"); break;
            case "invalid-endpoint": ReplaceValue(arguments, "--expected-logical-endpoint-sha256", "bad"); break;
            case "invalid-tool-hash": ReplaceValue(arguments, "--expected-tool-assembly-sha256", "bad"); break;
            case "duplicate-scalar": arguments.AddRange(["--target-label", "duplicate"]); break;
            case "unknown-option": arguments[0] = "--unknown"; break;
            case "missing-option": RemoveOptions(arguments, "--output"); break;
            case "bad-output": ReplaceValue(arguments, "--output", "bad\0path"); break;
            default: throw new ArgumentOutOfRangeException(nameof(scenario));
        }
    }

    private static void ReplaceValue(IList<string> arguments, string option, string value)
    {
        var index = arguments.IndexOf(option);
        Assert.True(index >= 0);
        arguments[index + 1] = value;
    }

    private static void RemoveOptions(List<string> arguments, string option)
    {
        for (var index = arguments.Count - 2; index >= 0; index -= 2)
        {
            if (arguments[index] == option)
            {
                arguments.RemoveRange(index, 2);
            }
        }
    }

    private static string CreateTemporaryDirectory()
    {
        var path = Path.Combine(Path.GetTempPath(), $"goymen-rcsi-{Guid.NewGuid():N}");
        Directory.CreateDirectory(path);
        return path;
    }

    private static string ComputeFileSha256(string path)
    {
        using var stream = File.OpenRead(path);
        return Convert.ToHexString(SHA256.HashData(stream)).ToLowerInvariant();
    }

    private sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }

    private sealed record ConcurrentResult(int ExitCode, byte[] StandardOutput, string RunId);

    private sealed class RuntimePayloadFixture : IDisposable
    {
        private const string Contract = "GOYMEN-ERP-RCSI-RUNTIME-PAYLOAD-V1";
        private readonly string _manifestDirectory;
        private readonly bool _ownsPayloadRoot;

        private RuntimePayloadFixture(
            string payloadRoot,
            string sourceRevisionId,
            bool ownsPayloadRoot)
        {
            PayloadRoot = payloadRoot;
            SourceRevisionId = sourceRevisionId;
            _ownsPayloadRoot = ownsPayloadRoot;
            _manifestDirectory = CreateTemporaryDirectory();
            ManifestPath = Path.Combine(_manifestDirectory, "runtime-payload.json");
            var bytes = CreateManifestBytes(payloadRoot, sourceRevisionId);
            File.WriteAllBytes(ManifestPath, bytes);
            ManifestSha256 = Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
            ToolAssemblySha256 = ComputeFileSha256(Path.Combine(
                payloadRoot,
                "GoymenERP.SqlServerRcsiReadiness.dll"));
        }

        public string PayloadRoot { get; }
        public string ManifestPath { get; }
        public string ManifestSha256 { get; private set; }
        public string SourceRevisionId { get; }
        public string ToolAssemblySha256 { get; }

        public static RuntimePayloadFixture CreateBuiltTool()
        {
            Assert.True(SqlServerRcsiReadinessToolArtifactIdentity.TryReadExecutingAssembly(
                out var sourceRevisionId,
                out _,
                out _));
            var repositoryRoot = FindRepositoryRoot();
            var configuration = new DirectoryInfo(AppContext.BaseDirectory).Parent!.Name;
            var payloadRoot = Path.Combine(
                repositoryRoot,
                "tools",
                "GoymenERP.SqlServerRcsiReadiness",
                "bin",
                configuration,
                "net10.0");
            return new RuntimePayloadFixture(payloadRoot, sourceRevisionId, false);
        }

        public RuntimePayloadFixture CreateIsolatedCopy()
        {
            var copyRoot = CreateTemporaryDirectory();
            foreach (var directory in Directory.EnumerateDirectories(
                         PayloadRoot,
                         "*",
                         SearchOption.AllDirectories))
            {
                Directory.CreateDirectory(Path.Combine(
                    copyRoot,
                    Path.GetRelativePath(PayloadRoot, directory)));
            }

            foreach (var file in Directory.EnumerateFiles(
                         PayloadRoot,
                         "*",
                         SearchOption.AllDirectories))
            {
                File.Copy(
                    file,
                    Path.Combine(copyRoot, Path.GetRelativePath(PayloadRoot, file)));
            }

            return new RuntimePayloadFixture(copyRoot, SourceRevisionId, true);
        }

        public void ReplaceManifestWithMalformedDocument(string scenario)
        {
            var root = JsonNode.Parse(File.ReadAllBytes(ManifestPath))!.AsObject();
            var files = root["files"]!.AsArray();
            var firstFile = files[0]!.AsObject();
            switch (scenario)
            {
                case "contract-kind": root["contractVersion"] = 1; break;
                case "source-kind": root["sourceRevisionId"] = 1; break;
                case "files-kind": root["files"] = "not-an-array"; break;
                case "path-kind": firstFile["path"] = 1; break;
                case "length-kind": firstFile["length"] = "not-a-number"; break;
                case "length-overflow": firstFile["length"] = ulong.MaxValue; break;
                case "sha-kind": firstFile["sha256"] = 1; break;
                case "missing-property": root.Remove("contractVersion"); break;
                case "extra-property": root["extra"] = true; break;
                default: throw new ArgumentOutOfRangeException(nameof(scenario));
            }

            var bytes = JsonSerializer.SerializeToUtf8Bytes(root);
            File.WriteAllBytes(ManifestPath, bytes);
            ManifestSha256 = Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
        }

        public void Dispose()
        {
            if (Directory.Exists(_manifestDirectory))
            {
                Directory.Delete(_manifestDirectory, recursive: true);
            }

            if (_ownsPayloadRoot && Directory.Exists(PayloadRoot))
            {
                Directory.Delete(PayloadRoot, recursive: true);
            }
        }

        private static byte[] CreateManifestBytes(string payloadRoot, string sourceRevisionId)
        {
            var files = Directory.EnumerateFiles(payloadRoot, "*", SearchOption.AllDirectories)
                .Select(path => new
                {
                    Path = Path.GetRelativePath(payloadRoot, path).Replace('\\', '/'),
                    Length = new FileInfo(path).Length,
                    Sha256 = ComputeFileSha256(path),
                })
                .OrderBy(item => item.Path, StringComparer.Ordinal)
                .ToArray();
            using var stream = new MemoryStream();
            using (var writer = new Utf8JsonWriter(stream))
            {
                writer.WriteStartObject();
                writer.WriteString("contractVersion", Contract);
                writer.WriteString("sourceRevisionId", sourceRevisionId);
                writer.WriteStartArray("files");
                foreach (var file in files)
                {
                    writer.WriteStartObject();
                    writer.WriteString("path", file.Path);
                    writer.WriteNumber("length", file.Length);
                    writer.WriteString("sha256", file.Sha256);
                    writer.WriteEndObject();
                }

                writer.WriteEndArray();
                writer.WriteEndObject();
            }

            return stream.ToArray();
        }

        private static string FindRepositoryRoot()
        {
            var directory = new DirectoryInfo(AppContext.BaseDirectory);
            while (directory is not null)
            {
                if (File.Exists(Path.Combine(directory.FullName, "GoymenERP.slnx")))
                {
                    return directory.FullName;
                }

                directory = directory.Parent;
            }

            throw new DirectoryNotFoundException();
        }
    }
}
