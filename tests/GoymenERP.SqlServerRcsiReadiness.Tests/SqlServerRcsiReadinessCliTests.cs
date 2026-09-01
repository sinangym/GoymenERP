using System.Diagnostics;
using System.Text.Json;
using GoymenERP.BuildingBlocks.SqlServer;
using Microsoft.Data.SqlClient;

namespace GoymenERP.SqlServerRcsiReadiness.Tests;

public sealed class SqlServerRcsiReadinessCliTests
{
    private const string LogicalEndpointASha256 =
        "75a99409b81404222ad7347d4dad424dfab7b355844a736531e2105eb621c05f";
    private const string LogicalEndpointBSha256 =
        "f67561664930dd93b930601f71be9bcd6ce230064681b7b7f03695df0c745fe1";
    private static readonly DateTimeOffset ObservedAtUtc =
        new(2026, 9, 1, 10, 20, 30, TimeSpan.Zero);

    [Fact]
    public async Task ValidInvocation_WritesNewAtomicCanonicalEvidenceWithStdoutParity()
    {
        var directory = CreateTemporaryDirectory();
        var outputPath = Path.Combine(directory, "ready.json");
        try
        {
            using var standardOutput = new StringWriter();
            using var standardError = new StringWriter();

            var exitCode = await RunAsync(
                ValidArguments(outputPath),
                _ => "Server=secret-host;Password=SuperSecret;",
                standardOutput,
                standardError,
                static (_, binding, _) => Task.FromResult(
                    SqlServerRcsiReadinessPolicy.Evaluate(
                        binding,
                        "GoymenErp",
                        "GoymenErp",
                        LogicalEndpointASha256,
                        1)));

            Assert.Equal(0, exitCode);
            Assert.True(File.Exists(outputPath));
            var fileJson = await File.ReadAllTextAsync(outputPath);
            Assert.Equal(fileJson, standardOutput.ToString());
            Assert.Empty(standardError.ToString());

            using var document = JsonDocument.Parse(fileJson);
            var root = document.RootElement;
            Assert.Equal(
                "11111111-1111-1111-1111-111111111111",
                root.GetProperty("runId").GetString());
            Assert.Equal(
                new string('a', 40),
                root.GetProperty("testedCommit").GetString());
            Assert.Equal(
                "2026-09-01T10:20:30.0000000Z",
                root.GetProperty("observedAtUtc").GetString());
            Assert.Equal(
                [LogicalEndpointASha256, LogicalEndpointBSha256],
                root.GetProperty("approvedLogicalEndpointSha256")
                    .EnumerateArray()
                    .Select(item => item.GetString()!)
                    .ToArray());
            Assert.DoesNotContain("SuperSecret", fileJson, StringComparison.Ordinal);
            Assert.DoesNotContain("secret-host", fileJson, StringComparison.Ordinal);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public async Task StaleExistingEvidence_IsNeverOverwrittenAndProbeIsNotStarted()
    {
        var directory = CreateTemporaryDirectory();
        var outputPath = Path.Combine(directory, "ready.json");
        await File.WriteAllTextAsync(outputPath, "stale-ready-evidence");
        var environmentRead = false;
        var probeStarted = false;
        try
        {
            using var standardOutput = new StringWriter();
            using var standardError = new StringWriter();

            var exitCode = await RunAsync(
                ValidArguments(outputPath),
                _ =>
                {
                    environmentRead = true;
                    return "Server=secret-host;Password=SuperSecret;";
                },
                standardOutput,
                standardError,
                (_, _, _) =>
                {
                    probeStarted = true;
                    throw new InvalidOperationException("Probe must not start.");
                });

            Assert.Equal(5, exitCode);
            Assert.False(environmentRead);
            Assert.False(probeStarted);
            Assert.Equal("stale-ready-evidence", await File.ReadAllTextAsync(outputPath));
            Assert.Contains(
                "\"reasonCode\":\"EVIDENCE_WRITE_FAILED\"",
                standardOutput.ToString(),
                StringComparison.Ordinal);
            Assert.Empty(Directory.GetFiles(directory, "*.tmp"));
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public async Task RequiredTimeout_CancelsProbeAndProducesDeterministicFailure()
    {
        var directory = CreateTemporaryDirectory();
        var outputPath = Path.Combine(directory, "timeout.json");
        try
        {
            using var standardOutput = new StringWriter();
            using var standardError = new StringWriter();
            var stopwatch = Stopwatch.StartNew();

            var exitCode = await RunAsync(
                ValidArguments(outputPath, timeoutSeconds: "1"),
                _ => "Server=secret-host;Password=SuperSecret;",
                standardOutput,
                standardError,
                static async (_, _, cancellationToken) =>
                {
                    await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
                    throw new InvalidOperationException("Unreachable.");
                });

            stopwatch.Stop();
            Assert.Equal(5, exitCode);
            Assert.InRange(stopwatch.Elapsed, TimeSpan.FromMilliseconds(500), TimeSpan.FromSeconds(5));
            Assert.Contains(
                "\"reasonCode\":\"PROBE_FAILED\"",
                await File.ReadAllTextAsync(outputPath),
                StringComparison.Ordinal);
            Assert.Equal(
                await File.ReadAllTextAsync(outputPath),
                standardOutput.ToString());
            Assert.Empty(standardError.ToString());
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public async Task CallerCancellation_PropagatesAndDoesNotPublishEvidence()
    {
        var directory = CreateTemporaryDirectory();
        var outputPath = Path.Combine(directory, "cancelled.json");
        using var cancellationSource = new CancellationTokenSource();
        cancellationSource.Cancel();
        try
        {
            using var standardOutput = new StringWriter();
            using var standardError = new StringWriter();

            await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
                RunAsync(
                    ValidArguments(outputPath),
                    _ => "Server=secret-host;Password=SuperSecret;",
                    standardOutput,
                    standardError,
                    static async (_, _, cancellationToken) =>
                    {
                        await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
                        return SqlServerRcsiReadinessPolicy.InvalidBinding();
                    },
                    cancellationSource.Token));

            Assert.False(File.Exists(outputPath));
            Assert.Empty(standardOutput.ToString());
            Assert.Empty(standardError.ToString());
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public async Task InvalidBindingOrTimeout_ReturnsExitTwoWithoutReadingSecret()
    {
        var directory = CreateTemporaryDirectory();
        var outputPath = Path.Combine(directory, "invalid.json");
        var environmentRead = false;
        try
        {
            using var standardOutput = new StringWriter();
            using var standardError = new StringWriter();
            var arguments = ValidArguments(outputPath, timeoutSeconds: "0");
            arguments[5] = "staging blue-SuperSecret";

            var exitCode = await RunAsync(
                arguments,
                _ =>
                {
                    environmentRead = true;
                    return "Server=secret-host;Password=SuperSecret;";
                },
                standardOutput,
                standardError,
                static (_, _, _) => throw new InvalidOperationException());

            Assert.Equal(2, exitCode);
            Assert.False(environmentRead);
            Assert.False(File.Exists(outputPath));
            Assert.DoesNotContain("SuperSecret", standardOutput.ToString(), StringComparison.Ordinal);
            Assert.Empty(standardError.ToString());
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public async Task MalformedSecretConnectionString_IsNotEchoed()
    {
        var directory = CreateTemporaryDirectory();
        var outputPath = Path.Combine(directory, "invalid-connection.json");
        try
        {
            using var standardOutput = new StringWriter();
            using var standardError = new StringWriter();

            var exitCode = await RunAsync(
                ValidArguments(outputPath),
                _ => "Server=secret-host;Password=SuperSecret;UnknownKeyword=value;",
                standardOutput,
                standardError,
                static (_, _, _) => throw new InvalidOperationException());

            Assert.Equal(2, exitCode);
            var fileJson = await File.ReadAllTextAsync(outputPath);
            Assert.Equal(fileJson, standardOutput.ToString());
            Assert.DoesNotContain("SuperSecret", fileJson, StringComparison.Ordinal);
            Assert.DoesNotContain("secret-host", fileJson, StringComparison.Ordinal);
            Assert.Empty(standardError.ToString());
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    private static Task<int> RunAsync(
        string[] arguments,
        Func<string, string?> getEnvironmentVariable,
        TextWriter standardOutput,
        TextWriter standardError,
        Func<
            SqlConnection,
            SqlServerRcsiReadinessBinding,
            CancellationToken,
            Task<SqlServerRcsiReadinessEvidence>> probeAsync,
        CancellationToken cancellationToken = default) =>
        global::SqlServerRcsiReadinessCli.RunAsync(
            arguments,
            getEnvironmentVariable,
            standardOutput,
            standardError,
            new FixedTimeProvider(ObservedAtUtc),
            probeAsync,
            cancellationToken);

    private static string[] ValidArguments(
        string outputPath,
        string timeoutSeconds = "30") =>
    [
        "--connection-string-env",
        "GOYMEN_ERP_RCSI_PROBE_CONNECTION",
        "--expected-database",
        "GoymenErp",
        "--target-label",
        "staging/blue",
        "--expected-logical-endpoint-sha256",
        LogicalEndpointBSha256,
        "--expected-logical-endpoint-sha256",
        LogicalEndpointASha256,
        "--run-id",
        "11111111-1111-1111-1111-111111111111",
        "--tested-commit",
        new string('a', 40),
        "--timeout-seconds",
        timeoutSeconds,
        "--output",
        outputPath,
    ];

    private static string CreateTemporaryDirectory()
    {
        var path = Path.Combine(
            Path.GetTempPath(),
            $"goymen-erp-rcsi-cli-{Guid.NewGuid():N}");
        Directory.CreateDirectory(path);
        return path;
    }

    private sealed class FixedTimeProvider(DateTimeOffset utcNow) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => utcNow;
    }
}
