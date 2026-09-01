using System.Net;
using System.Net.Sockets;
using System.Diagnostics;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Runtime.InteropServices;
using GoymenERP.BuildingBlocks.SqlServer;
using Microsoft.Data.SqlClient;

namespace GoymenERP.Api.IntegrationTests;

public sealed class SqlServerRcsiReadinessSqlServerRelationalAcceptanceTests
{
    [SqlServerFact]
    public async Task RcsiOn_ProbeAndIdentityStartupUseTheSameReadyPolicy()
    {
        var baseConnectionString = GetBaseConnectionString();
        var databaseName = NewDatabaseName();
        SqlServerRelationalApiFactory? factory = null;
        var databaseCreated = false;

        try
        {
            await CreateDatabaseAsync(baseConnectionString, databaseName, rcsiEnabled: true);
            databaseCreated = true;
            factory = new SqlServerRelationalApiFactory(
                baseConnectionString,
                databaseName: databaseName);
            var evidence = await ProbeAsync(factory.DatabaseConnectionString, databaseName);
            AssertReady(evidence, databaseName);

            using var client = factory.CreateClient();
            using var response = await client.GetAsync("/health/live");
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);

            var afterStartup = await ProbeAsync(
                factory.DatabaseConnectionString,
                databaseName);
            AssertReady(afterStartup, databaseName);
        }
        finally
        {
            try
            {
                if (factory is not null)
                {
                    await factory.DisposeAsync();
                }
            }
            finally
            {
                if (databaseCreated)
                {
                    await DropDatabaseAsync(baseConnectionString, databaseName);
                }
            }
        }
    }

    [SqlServerFact]
    public async Task RcsiOff_ProbeFailsClosedAndDoesNotChangeTheDatabase()
    {
        var baseConnectionString = GetBaseConnectionString();
        var databaseName = NewDatabaseName();
        var databaseCreated = false;

        try
        {
            await CreateDatabaseAsync(baseConnectionString, databaseName, rcsiEnabled: false);
            databaseCreated = true;
            Assert.Equal(0, await ReadRcsiSettingAsync(baseConnectionString, databaseName));

            var evidence = await ProbeAsync(
                BuildDatabaseConnectionString(baseConnectionString, databaseName),
                databaseName);

            Assert.False(evidence.IsReady);
            Assert.Equal(SqlServerRcsiReadinessExitCode.RcsiDisabled, evidence.ExitCode);
            Assert.Equal("RCSI_DISABLED", evidence.ReasonCode);
            Assert.False(evidence.ReadCommittedSnapshotEnabled);
            Assert.Equal(0, await ReadRcsiSettingAsync(baseConnectionString, databaseName));
        }
        finally
        {
            if (databaseCreated)
            {
                await DropDatabaseAsync(baseConnectionString, databaseName);
            }
        }
    }

    [SqlServerFact]
    public async Task ExpectedDatabaseMismatch_IsRejectedBeforeTheConnectionIsOpened()
    {
        var baseConnectionString = GetBaseConnectionString();
        var databaseName = NewDatabaseName();
        var databaseCreated = false;

        try
        {
            await CreateDatabaseAsync(baseConnectionString, databaseName, rcsiEnabled: true);
            databaseCreated = true;
            await using var connection = new SqlConnection(
                BuildDatabaseConnectionString(baseConnectionString, databaseName));
            var binding = SqlServerRcsiReadinessBinding.CreateFromLogicalEndpoints(
                "relational/mismatch",
                $"{databaseName}_other",
                [new SqlConnectionStringBuilder(connection.ConnectionString).DataSource]);

            var evidence = await new SqlServerRcsiReadinessProbe()
                .ProbeAsync(connection, binding);

            Assert.Equal(System.Data.ConnectionState.Closed, connection.State);
            Assert.False(evidence.IsReady);
            Assert.Equal(
                SqlServerRcsiReadinessExitCode.ExpectedDatabaseMismatch,
                evidence.ExitCode);
            Assert.Equal("EXPECTED_DATABASE_MISMATCH", evidence.ReasonCode);
            Assert.Equal(databaseName, evidence.ConfiguredDatabase);
            Assert.Null(evidence.ObservedDatabase);
            Assert.Equal(1, await ReadRcsiSettingAsync(baseConnectionString, databaseName));
        }
        finally
        {
            if (databaseCreated)
            {
                await DropDatabaseAsync(baseConnectionString, databaseName);
            }
        }
    }

    [SqlServerFact]
    public async Task UnapprovedLogicalEndpoint_IsRejectedWithoutChangingDatabase()
    {
        var baseConnectionString = GetBaseConnectionString();
        var databaseName = NewDatabaseName();
        var databaseCreated = false;

        try
        {
            await CreateDatabaseAsync(baseConnectionString, databaseName, rcsiEnabled: true);
            databaseCreated = true;
            await using var connection = new SqlConnection(
                BuildDatabaseConnectionString(baseConnectionString, databaseName));
            var binding = SqlServerRcsiReadinessBinding.CreateFromLogicalEndpoints(
                "relational/wrong-target",
                databaseName,
                ["tcp:deliberately-wrong-target.invalid,1433"]);

            var evidence = await new SqlServerRcsiReadinessProbe()
                .ProbeAsync(connection, binding);

            Assert.Equal(System.Data.ConnectionState.Closed, connection.State);
            Assert.False(evidence.IsReady);
            Assert.Equal(
                SqlServerRcsiReadinessExitCode.ExpectedLogicalEndpointMismatch,
                evidence.ExitCode);
            Assert.Equal("EXPECTED_LOGICAL_ENDPOINT_MISMATCH", evidence.ReasonCode);
            Assert.False(evidence.LogicalEndpointMatches);
            Assert.DoesNotContain(
                evidence.ObservedLogicalEndpointSha256,
                evidence.ApprovedLogicalEndpointSha256!);
            Assert.Null(evidence.ReadCommittedSnapshotEnabled);
            Assert.Equal(1, await ReadRcsiSettingAsync(baseConnectionString, databaseName));
        }
        finally
        {
            if (databaseCreated)
            {
                await DropDatabaseAsync(baseConnectionString, databaseName);
            }
        }
    }

    [SqlServerFact]
    public async Task RcsiOffStartupFailsThenExplicitTestAdminEnableAllowsStartupAndProbe()
    {
        var baseConnectionString = GetBaseConnectionString();
        var databaseName = NewDatabaseName();
        var databaseCreated = false;

        try
        {
            await CreateDatabaseAsync(baseConnectionString, databaseName, rcsiEnabled: false);
            databaseCreated = true;
            var disabledFactory = new SqlServerRelationalApiFactory(
                baseConnectionString,
                databaseName: databaseName);
            try
            {
                var disabledEvidence = await ProbeAsync(
                    disabledFactory.DatabaseConnectionString,
                    databaseName);
                Assert.Equal(
                    SqlServerRcsiReadinessExitCode.RcsiDisabled,
                    disabledEvidence.ExitCode);

                var startupException = await Record.ExceptionAsync(async () =>
                {
                    using var client = disabledFactory.CreateClient();
                    using var response = await client.GetAsync("/health/live");
                });
                Assert.NotNull(startupException);
                Assert.Contains(
                    SqlServerRcsiReadinessPolicy.StartupRcsiDisabledMessage,
                    startupException.ToString(),
                    StringComparison.Ordinal);
                Assert.Equal(
                    0,
                    await ReadRcsiSettingAsync(baseConnectionString, databaseName));
            }
            finally
            {
                await disabledFactory.DisposeAsync();
            }

            await SetRcsiForTestAsync(baseConnectionString, databaseName, enabled: true);
            var enabledFactory = new SqlServerRelationalApiFactory(
                baseConnectionString,
                databaseName: databaseName);
            try
            {
                Assert.Equal(
                    1,
                    await ReadRcsiSettingAsync(baseConnectionString, databaseName));

                using var client = enabledFactory.CreateClient();
                using var response = await client.GetAsync("/health/live");
                Assert.Equal(HttpStatusCode.OK, response.StatusCode);

                var enabledEvidence = await ProbeAsync(
                    enabledFactory.DatabaseConnectionString,
                    databaseName);
                AssertReady(enabledEvidence, databaseName);
            }
            finally
            {
                await enabledFactory.DisposeAsync();
            }
        }
        finally
        {
            if (databaseCreated)
            {
                await DropDatabaseAsync(baseConnectionString, databaseName);
            }
        }
    }

    [SqlServerFact]
    public async Task CliProcess_ProducesFreshAtomicEvidenceForReadyOffMismatchAndStalePaths()
    {
        var baseConnectionString = GetBaseConnectionString();
        var readyDatabase = NewDatabaseName("göymen_erp_rcsi");
        var offDatabase = NewDatabaseName();
        var evidenceDirectory = Path.Combine(
            Path.GetTempPath(),
            $"goymen-erp-rcsi-process-{Guid.NewGuid():N}");
        Directory.CreateDirectory(evidenceDirectory);
        var readyDatabaseCreated = false;
        var offDatabaseCreated = false;

        try
        {
            await CreateDatabaseAsync(baseConnectionString, readyDatabase, rcsiEnabled: true);
            readyDatabaseCreated = true;
            await CreateDatabaseAsync(baseConnectionString, offDatabase, rcsiEnabled: false);
            offDatabaseCreated = true;
            var runtimePayload = CreateRuntimePayloadManifest(evidenceDirectory);
            var testedCommit = runtimePayload.SourceRevisionId;

            var readyConnectionString = BuildDatabaseConnectionString(
                baseConnectionString,
                readyDatabase);
            var offConnectionString = BuildDatabaseConnectionString(
                baseConnectionString,
                offDatabase);
            var logicalEndpoint = new SqlConnectionStringBuilder(
                readyConnectionString).DataSource;
            var logicalEndpointSha256 =
                SqlServerLogicalEndpointFingerprint.ComputeSha256(logicalEndpoint);

            var readyResult = await RunCliProcessAsync(
                readyConnectionString,
                readyDatabase,
                [logicalEndpointSha256],
                Path.Combine(evidenceDirectory, "ready.json"),
                Guid.NewGuid().ToString("D"),
                testedCommit,
                runtimePayload);
            AssertCliEvidence(
                readyResult,
                expectedExitCode: 0,
                expectedReasonCode: "RCSI_READY",
                logicalEndpoint,
                new SqlConnectionStringBuilder(readyConnectionString).Password,
                readyDatabase,
                runtimePayload);

            var offResult = await RunCliProcessAsync(
                offConnectionString,
                offDatabase,
                [logicalEndpointSha256],
                Path.Combine(evidenceDirectory, "off.json"),
                Guid.NewGuid().ToString("D"),
                testedCommit,
                runtimePayload);
            AssertCliEvidence(
                offResult,
                expectedExitCode: 4,
                expectedReasonCode: "RCSI_DISABLED",
                logicalEndpoint,
                new SqlConnectionStringBuilder(offConnectionString).Password,
                offDatabase,
                runtimePayload);
            Assert.Equal(0, await ReadRcsiSettingAsync(baseConnectionString, offDatabase));

            var wrongLogicalEndpointResult = await RunCliProcessAsync(
                readyConnectionString,
                readyDatabase,
                [SqlServerLogicalEndpointFingerprint.ComputeSha256(
                    "tcp:unapproved-logical-endpoint.invalid,1433")],
                Path.Combine(evidenceDirectory, "wrong-endpoint.json"),
                Guid.NewGuid().ToString("D"),
                testedCommit,
                runtimePayload);
            AssertCliEvidence(
                wrongLogicalEndpointResult,
                expectedExitCode: 6,
                expectedReasonCode: "EXPECTED_LOGICAL_ENDPOINT_MISMATCH",
                logicalEndpoint,
                new SqlConnectionStringBuilder(readyConnectionString).Password,
                readyDatabase,
                runtimePayload);

            var staleOutputPath = Path.Combine(evidenceDirectory, "stale-ready.json");
            await File.WriteAllTextAsync(staleOutputPath, "pre-existing-ready-evidence");
            var staleResult = await RunCliProcessAsync(
                readyConnectionString,
                readyDatabase,
                [logicalEndpointSha256],
                staleOutputPath,
                Guid.NewGuid().ToString("D"),
                testedCommit,
                runtimePayload);
            Assert.Equal(5, staleResult.ExitCode);
            Assert.Equal(
                "pre-existing-ready-evidence",
                await File.ReadAllTextAsync(staleOutputPath));
            using var staleDocument = JsonDocument.Parse(staleResult.StandardOutput);
            Assert.Equal(
                "EVIDENCE_WRITE_FAILED",
                staleDocument.RootElement.GetProperty("reasonCode").GetString());
            Assert.Empty(Directory.GetFiles(evidenceDirectory, "*.tmp"));
        }
        finally
        {
            try
            {
                if (readyDatabaseCreated)
                {
                    await DropDatabaseAsync(baseConnectionString, readyDatabase);
                }
            }
            finally
            {
                try
                {
                    if (offDatabaseCreated)
                    {
                        await DropDatabaseAsync(baseConnectionString, offDatabase);
                    }
                }
                finally
                {
                    Directory.Delete(evidenceDirectory, recursive: true);
                }
            }
        }
    }

    [SqlServerFact]
    public async Task CliProcess_OverallTimeoutKillsAndAwaitsTheHungChildWithoutEvidence()
    {
        var evidenceDirectory = Path.Combine(
            Path.GetTempPath(),
            $"goymen-erp-rcsi-timeout-{Guid.NewGuid():N}");
        Directory.CreateDirectory(evidenceDirectory);
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        try
        {
            var runtimePayload = CreateRuntimePayloadManifest(evidenceDirectory);
            var endpoint = (IPEndPoint)listener.LocalEndpoint;
            var connectionString = new SqlConnectionStringBuilder
            {
                DataSource = $"127.0.0.1,{endpoint.Port}",
                InitialCatalog = "goymen_erp_rcsi_timeout",
                UserID = "probe",
                Password = "timeout-secret",
                Encrypt = false,
                ConnectTimeout = 30,
            }.ConnectionString;
            var outputPath = Path.Combine(evidenceDirectory, "must-not-exist.json");
            var acceptTask = listener.AcceptTcpClientAsync();
            var invocation = RunCliProcessAsync(
                connectionString,
                "goymen_erp_rcsi_timeout",
                [SqlServerLogicalEndpointFingerprint.ComputeSha256(
                    new SqlConnectionStringBuilder(connectionString).DataSource)],
                outputPath,
                Guid.NewGuid().ToString("D"),
                runtimePayload.SourceRevisionId,
                runtimePayload,
                TimeSpan.FromSeconds(10));

            var acceptedTask = acceptTask;
            var firstCompleted = await Task.WhenAny(acceptedTask, invocation)
                .WaitAsync(TimeSpan.FromSeconds(60));
            Assert.Same(acceptedTask, firstCompleted);
            using var acceptedClient = await acceptedTask;
            await Assert.ThrowsAsync<TimeoutException>(() => invocation);
            Assert.False(File.Exists(outputPath));
            Assert.Empty(Directory.GetFiles(evidenceDirectory, "*.tmp"));
        }
        finally
        {
            listener.Stop();
            Directory.Delete(evidenceDirectory, recursive: true);
        }
    }

    [SqlServerFact]
    public async Task PowerShellWrapper_MultipleEndpointsReadyAndOffUseBoundApphostAndPropagateExit()
    {
        // Test-only host budget: this protects the wrapper contract from cold-host and
        // full-matrix scheduling variance. Production operators still own T via the runbook.
        const int wrapperOverallTimeoutSeconds = 60;
        var wrapperTestProcessTimeout = TimeSpan.FromSeconds(90);
        var baseConnectionString = GetBaseConnectionString();
        var readyDatabase = NewDatabaseName("göymen_erp_rcsi_wrapper");
        var offDatabase = NewDatabaseName("goymen_erp_rcsi_wrapper");
        var root = Path.Combine(
            Path.GetTempPath(),
            $"goymen-erp-rcsi-wrapper-{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);
        var readyCreated = false;
        var offCreated = false;
        try
        {
            await CreateDatabaseAsync(baseConnectionString, readyDatabase, rcsiEnabled: true);
            readyCreated = true;
            await CreateDatabaseAsync(baseConnectionString, offDatabase, rcsiEnabled: false);
            offCreated = true;
            var payload = CreateRuntimePayloadManifest(root);
            var readyConnection = BuildDatabaseConnectionString(baseConnectionString, readyDatabase);
            var endpoint = new SqlConnectionStringBuilder(readyConnection).DataSource;
            var endpointSha256 = SqlServerLogicalEndpointFingerprint.ComputeSha256(endpoint);
            var alternateEndpointSha256 = SqlServerLogicalEndpointFingerprint.ComputeSha256(
                "tcp:approved-but-not-observed.invalid,1433");
            var approvedEndpointInventory = new[]
            {
                alternateEndpointSha256,
                endpointSha256,
            }.OrderBy(value => value, StringComparer.Ordinal).ToArray();

            var readyOutput = Path.Combine(root, "ready-run", "readiness.json");
            var ready = await RunPowerShellWrapperAsync(
                readyConnection,
                readyDatabase,
                approvedEndpointInventory,
                readyOutput,
                payload,
                overallTimeoutSeconds: wrapperOverallTimeoutSeconds,
                testProcessTimeout: wrapperTestProcessTimeout);
            Assert.True(
                ready.ExitCode == 0,
                $"Wrapper READY exit was {ready.ExitCode}; stderr: {ready.StandardError}");
            Assert.Empty(ready.StandardError);
            Assert.True(
                File.ReadAllBytes(readyOutput).SequenceEqual(ready.StandardOutput),
                $"Wrapper stdout differed from evidence: {Encoding.UTF8.GetString(ready.StandardOutput)}");
            Assert.DoesNotContain(
                new SqlConnectionStringBuilder(readyConnection).Password,
                Encoding.UTF8.GetString(ready.StandardOutput),
                StringComparison.Ordinal);
            using (var document = JsonDocument.Parse(ready.StandardOutput))
            {
                var hashes = document.RootElement
                    .GetProperty("approvedLogicalEndpointSha256")
                    .EnumerateArray()
                    .Select(item => item.GetString()!)
                    .ToArray();
                Assert.Equal(approvedEndpointInventory, hashes);
            }

            var offConnection = BuildDatabaseConnectionString(baseConnectionString, offDatabase);
            var offOutput = Path.Combine(root, "off-run", "readiness.json");
            var off = await RunPowerShellWrapperAsync(
                offConnection,
                offDatabase,
                [endpointSha256],
                offOutput,
                payload,
                overallTimeoutSeconds: wrapperOverallTimeoutSeconds,
                testProcessTimeout: wrapperTestProcessTimeout);
            Assert.Equal(4, off.ExitCode);
            Assert.Contains("native exit 4", off.StandardError, StringComparison.Ordinal);
            Assert.DoesNotContain(
                new SqlConnectionStringBuilder(offConnection).Password,
                off.StandardError,
                StringComparison.Ordinal);
            Assert.True(File.Exists(offOutput));
            Assert.Equal(File.ReadAllBytes(offOutput), off.StandardOutput);
            using (var document = JsonDocument.Parse(off.StandardOutput))
            {
                Assert.Equal(4, document.RootElement.GetProperty("exitCode").GetInt32());
                Assert.Equal(
                    "RCSI_DISABLED",
                    document.RootElement.GetProperty("reasonCode").GetString());
            }
            Assert.Equal(0, await ReadRcsiSettingAsync(baseConnectionString, offDatabase));

            var invalidBindingOutput = Path.Combine(
                root,
                "invalid-binding-run",
                "readiness.json");
            var invalidBinding = await RunPowerShellWrapperAsync(
                "not-a-connection-string",
                readyDatabase,
                [endpointSha256],
                invalidBindingOutput,
                payload,
                overallTimeoutSeconds: wrapperOverallTimeoutSeconds,
                testProcessTimeout: wrapperTestProcessTimeout);
            Assert.Equal(2, invalidBinding.ExitCode);
            Assert.Contains("native exit 2", invalidBinding.StandardError, StringComparison.Ordinal);
            Assert.True(File.Exists(invalidBindingOutput));
            Assert.Equal(File.ReadAllBytes(invalidBindingOutput), invalidBinding.StandardOutput);
            using (var document = JsonDocument.Parse(invalidBinding.StandardOutput))
            {
                Assert.Equal(2, document.RootElement.GetProperty("exitCode").GetInt32());
                Assert.Equal(
                    "BINDING_INVALID",
                    document.RootElement.GetProperty("reasonCode").GetString());
                Assert.Equal(
                    JsonValueKind.Null,
                    document.RootElement.GetProperty("targetLabel").ValueKind);
                Assert.Equal(
                    JsonValueKind.Null,
                    document.RootElement.GetProperty("approvedLogicalEndpointSha256").ValueKind);
            }
            Assert.DoesNotContain(
                "not-a-connection-string",
                invalidBinding.StandardError,
                StringComparison.Ordinal);
        }
        finally
        {
            try
            {
                if (readyCreated)
                {
                    await DropDatabaseAsync(baseConnectionString, readyDatabase);
                }
            }
            finally
            {
                try
                {
                    if (offCreated)
                    {
                        await DropDatabaseAsync(baseConnectionString, offDatabase);
                    }
                }
                finally
                {
                    Directory.Delete(root, recursive: true);
                }
            }
        }
    }

    [SqlServerFact]
    public async Task PowerShellWrapper_OverallTimeoutKillsChildWithoutEvidenceOrSecretEcho()
    {
        var root = Path.Combine(
            Path.GetTempPath(),
            $"goymen-erp-rcsi-wrapper-timeout-{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        try
        {
            var payload = CreateRuntimePayloadManifest(root);
            var endpoint = (IPEndPoint)listener.LocalEndpoint;
            const string timeoutSecret = "wrapper-timeout-secret";
            var connectionString = new SqlConnectionStringBuilder
            {
                DataSource = $"127.0.0.1,{endpoint.Port}",
                InitialCatalog = "goymen_erp_rcsi_wrapper_timeout",
                UserID = "probe",
                Password = timeoutSecret,
                Encrypt = false,
                ConnectTimeout = 30,
            }.ConnectionString;
            var outputPath = Path.Combine(root, "timeout-run", "readiness.json");
            var acceptTask = listener.AcceptTcpClientAsync();
            var invocation = RunPowerShellWrapperAsync(
                connectionString,
                "goymen_erp_rcsi_wrapper_timeout",
                [SqlServerLogicalEndpointFingerprint.ComputeSha256(
                    new SqlConnectionStringBuilder(connectionString).DataSource)],
                outputPath,
                payload,
                overallTimeoutSeconds: 20,
                testProcessTimeout: TimeSpan.FromSeconds(90));

            var acceptedTask = acceptTask;
            var firstCompleted = await Task.WhenAny(acceptedTask, invocation)
                // Keep the behavioral timeout at 20 seconds, but allow a cold or
                // fully-loaded host the same 60-second child-start budget used by
                // the direct CLI containment test above.
                .WaitAsync(TimeSpan.FromSeconds(60));
            if (firstCompleted == invocation)
            {
                var earlyResult = await invocation;
                Assert.Fail(
                    $"Wrapper exited before opening the test socket. Exit: {earlyResult.ExitCode}; " +
                    $"stderr: {earlyResult.StandardError}");
            }

            using var accepted = await acceptedTask;
            var result = await invocation;
            Assert.Equal(5, result.ExitCode);
            Assert.Contains("timeout expired", result.StandardError, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain(timeoutSecret, result.StandardError, StringComparison.Ordinal);
            Assert.DoesNotContain(timeoutSecret, Encoding.UTF8.GetString(result.StandardOutput), StringComparison.Ordinal);
            Assert.False(File.Exists(outputPath));
            Assert.Empty(Directory.GetFiles(root, "*.tmp", SearchOption.AllDirectories));
            var eofBuffer = new byte[256];
            using var drainTimeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            try
            {
                while (await accepted.GetStream()
                           .ReadAsync(eofBuffer.AsMemory(), drainTimeout.Token) != 0)
                {
                    // Drain buffered SQL pre-login bytes until containment closes the socket.
                }
            }
            catch (IOException exception) when (
                exception.InnerException is SocketException
                {
                    SocketErrorCode: SocketError.ConnectionReset
                        or SocketError.ConnectionAborted,
                })
            {
                // Kill-tree can close the accepted SQL pre-login socket by EOF or RST.
            }
        }
        finally
        {
            listener.Stop();
            Directory.Delete(root, recursive: true);
        }
    }

    [SqlServerFact]
    public async Task PowerShellWrapper_InFlightScriptDriftFailsClosedAndDiscardsEvidence()
    {
        var root = Path.Combine(
            Path.GetTempPath(),
            $"goymen-erp-rcsi-wrapper-drift-{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        try
        {
            var payload = CreateRuntimePayloadManifest(root);
            var wrapperPath = Path.Combine(root, "approved-wrapper.ps1");
            File.Copy(payload.WrapperScriptPath, wrapperPath);
            var approvedWrapperSha256 = ComputeFileSha256(wrapperPath);
            var endpoint = (IPEndPoint)listener.LocalEndpoint;
            const string secret = "in-flight-wrapper-drift-secret";
            const string expectedDatabase = "goymen_erp_rcsi_wrapper_drift";
            var connectionString = new SqlConnectionStringBuilder
            {
                DataSource = $"127.0.0.1,{endpoint.Port}",
                InitialCatalog = expectedDatabase,
                UserID = "probe",
                Password = secret,
                Encrypt = false,
                ConnectTimeout = 30,
            }.ConnectionString;
            var outputPath = Path.Combine(root, "drift-run", "readiness.json");
            var acceptTask = listener.AcceptTcpClientAsync();
            var invocation = RunPowerShellWrapperAsync(
                connectionString,
                expectedDatabase,
                [SqlServerLogicalEndpointFingerprint.ComputeSha256(
                    new SqlConnectionStringBuilder(connectionString).DataSource)],
                outputPath,
                payload,
                overallTimeoutSeconds: 6,
                testProcessTimeout: TimeSpan.FromSeconds(60),
                wrapperPathOverride: wrapperPath,
                expectedWrapperScriptSha256Override: approvedWrapperSha256);

            var firstCompleted = await Task.WhenAny(acceptTask, invocation);
            if (firstCompleted == invocation)
            {
                var early = await invocation;
                Assert.Fail(
                    $"Wrapper exited before opening the drift socket. " +
                    $"Exit: {early.ExitCode}; stderr: {early.StandardError}");
            }

            using var accepted = await acceptTask;
            await File.AppendAllTextAsync(wrapperPath, "# post-start-drift");
            var result = await invocation;

            Assert.Equal(2, result.ExitCode);
            Assert.Empty(result.StandardOutput);
            Assert.Contains(
                "Final wrapper script verification failed closed.",
                result.StandardError,
                StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain(secret, result.StandardError, StringComparison.Ordinal);
            Assert.False(File.Exists(outputPath));
            Assert.Empty(Directory.GetFiles(root, "*.tmp", SearchOption.AllDirectories));
        }
        finally
        {
            listener.Stop();
            Directory.Delete(root, recursive: true);
        }
    }

    internal static async Task<WrapperProcessResult> RunPowerShellWrapperAsync(
        string connectionString,
        string expectedDatabase,
        IReadOnlyList<string> approvedLogicalEndpointSha256,
        string outputPath,
        RuntimePayloadManifest payload,
        int overallTimeoutSeconds,
        TimeSpan testProcessTimeout,
        string? wrapperPathOverride = null,
        string? expectedWrapperScriptSha256Override = null)
    {
        var wrapperPath = wrapperPathOverride ?? payload.WrapperScriptPath;
        var expectedWrapperScriptSha256 =
            expectedWrapperScriptSha256Override ?? payload.WrapperScriptSha256;
        Assert.True(File.Exists(wrapperPath));
        const string secretEnvironment = "GOYMEN_ERP_RCSI_PROBE_CONNECTION";
        var startInfo = new ProcessStartInfo(GetPowerShellPath())
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };
        foreach (var argument in new[]
                 {
                     "-NoLogo", "-NoProfile", "-NonInteractive", "-File", wrapperPath,
                     "-PayloadRoot", payload.PayloadRoot,
                     "-PayloadManifest", payload.ManifestPath,
                     "-ExpectedPayloadManifestSha256", payload.ManifestSha256,
                     "-ExpectedToolAssemblySha256", payload.ToolAssemblySha256,
                     "-ExpectedWrapperScriptSha256", expectedWrapperScriptSha256,
                     "-TestedCommit", payload.SourceRevisionId,
                     "-ApprovedLogicalEndpointSha256Json",
                     JsonSerializer.Serialize(approvedLogicalEndpointSha256),
                     "-ExpectedDatabase", expectedDatabase,
                     "-TargetLabel", "relational/wrapper-entrypoint",
                     "-ProbeTimeoutSeconds", "30",
                     "-OverallProcessTimeoutSeconds",
                     overallTimeoutSeconds.ToString(CultureInfo.InvariantCulture),
                     "-OutputPath", outputPath,
                 })
        {
            startInfo.ArgumentList.Add(argument);
        }

        startInfo.Environment.Clear();
        AddRequiredOperatingSystemEnvironment(startInfo);
        startInfo.Environment[secretEnvironment] = connectionString;
        using var process = new Process { StartInfo = startInfo };
        Assert.True(process.Start());
        var stdoutTask = ReadAllBytesAsync(process.StandardOutput.BaseStream);
        var stderrTask = process.StandardError.ReadToEndAsync();
        using var timeout = new CancellationTokenSource(testProcessTimeout);
        try
        {
            await process.WaitForExitAsync(timeout.Token);
        }
        catch (OperationCanceledException)
        {
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
            }

            await process.WaitForExitAsync(CancellationToken.None);
            throw new TimeoutException("The PowerShell wrapper test process did not exit.");
        }

        return new WrapperProcessResult(
            process.ExitCode,
            await stdoutTask,
            (await stderrTask).TrimEnd('\r', '\n'));
    }

    private static string GetPowerShellPath()
    {
        var executableName = OperatingSystem.IsWindows() ? "pwsh.exe" : "pwsh";
        var candidates = (Environment.GetEnvironmentVariable("PATH") ?? string.Empty)
            .Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries)
            .Select(path => path.Trim().Trim('"'))
            .Where(path => path.Length > 0)
            .Select(path => Path.Combine(path, executableName))
            .Where(File.Exists)
            .Select(Path.GetFullPath)
            .Distinct(OperatingSystem.IsWindows()
                ? StringComparer.OrdinalIgnoreCase
                : StringComparer.Ordinal)
            .ToArray();
        Assert.NotEmpty(candidates);
        return candidates[0];
    }

    private static async Task<CliProcessResult> RunCliProcessAsync(
        string connectionString,
        string expectedDatabase,
        IReadOnlyList<string> approvedLogicalEndpointSha256,
        string outputPath,
        string runId,
        string testedCommit,
        RuntimePayloadManifest runtimePayload,
        TimeSpan? overallTimeout = null)
    {
        var entryExecutable = runtimePayload.EntryExecutablePath;
        Assert.True(
            File.Exists(entryExecutable),
            $"Self-contained RCSI CLI apphost is missing: {entryExecutable}");

        var startInfo = new ProcessStartInfo(entryExecutable)
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };
        startInfo.ArgumentList.Add("--connection-string-env");
        startInfo.ArgumentList.Add("GOYMEN_ERP_RCSI_PROCESS_CONNECTION");
        startInfo.ArgumentList.Add("--expected-database");
        startInfo.ArgumentList.Add(expectedDatabase);
        startInfo.ArgumentList.Add("--target-label");
        startInfo.ArgumentList.Add("relational/process-entrypoint");
        foreach (var fingerprint in approvedLogicalEndpointSha256)
        {
            startInfo.ArgumentList.Add("--expected-logical-endpoint-sha256");
            startInfo.ArgumentList.Add(fingerprint);
        }

        startInfo.ArgumentList.Add("--run-id");
        startInfo.ArgumentList.Add(runId);
        startInfo.ArgumentList.Add("--tested-commit");
        startInfo.ArgumentList.Add(testedCommit);
        startInfo.ArgumentList.Add("--expected-wrapper-script-sha256");
        startInfo.ArgumentList.Add(runtimePayload.WrapperScriptSha256);
        startInfo.ArgumentList.Add("--observed-wrapper-script-sha256");
        startInfo.ArgumentList.Add(runtimePayload.WrapperScriptSha256);
        startInfo.ArgumentList.Add("--expected-tool-assembly-sha256");
        startInfo.ArgumentList.Add(runtimePayload.ToolAssemblySha256);
        startInfo.ArgumentList.Add("--runtime-payload-root");
        startInfo.ArgumentList.Add(runtimePayload.PayloadRoot);
        startInfo.ArgumentList.Add("--runtime-payload-manifest");
        startInfo.ArgumentList.Add(runtimePayload.ManifestPath);
        startInfo.ArgumentList.Add("--expected-runtime-payload-manifest-sha256");
        startInfo.ArgumentList.Add(runtimePayload.ManifestSha256);
        startInfo.ArgumentList.Add("--timeout-seconds");
        startInfo.ArgumentList.Add("30");
        startInfo.ArgumentList.Add("--output");
        startInfo.ArgumentList.Add(outputPath);
        startInfo.Environment.Clear();
        AddRequiredOperatingSystemEnvironment(startInfo);
        startInfo.Environment["GOYMEN_ERP_RCSI_PROCESS_CONNECTION"] = connectionString;

        using var process = new Process { StartInfo = startInfo };
        var startedAtUtc = DateTimeOffset.UtcNow;
        Assert.True(process.Start());
        var standardOutputTask = ReadAllBytesAsync(process.StandardOutput.BaseStream);
        var standardErrorTask = process.StandardError.ReadToEndAsync();
        using var timeout = new CancellationTokenSource(
            overallTimeout ?? TimeSpan.FromSeconds(60));
        var processTimedOut = false;
        try
        {
            await process.WaitForExitAsync(timeout.Token);
        }
        catch (OperationCanceledException)
        {
            process.Kill(entireProcessTree: true);
            await process.WaitForExitAsync(CancellationToken.None);
            processTimedOut = true;
        }

        var standardOutput = await standardOutputTask;
        var standardError = (await standardErrorTask).TrimEnd('\r', '\n');
        if (processTimedOut)
        {
            throw new TimeoutException(
                "RCSI readiness process did not exit before the test-owned timeout.");
        }

        var completedAtUtc = DateTimeOffset.UtcNow;
        return new CliProcessResult(
            process.ExitCode,
            standardOutput,
            standardError,
            outputPath,
            runId,
            startedAtUtc,
            completedAtUtc);
    }

    private static void AssertCliEvidence(
        CliProcessResult result,
        int expectedExitCode,
        string expectedReasonCode,
        string rawLogicalEndpoint,
        string secret,
        string expectedDatabase,
        RuntimePayloadManifest runtimePayload)
    {
        Assert.True(
            result.ExitCode == expectedExitCode,
            $"Expected CLI exit {expectedExitCode}, observed {result.ExitCode}; " +
            $"stderr: {result.StandardError}; stdout: {Encoding.UTF8.GetString(result.StandardOutput)}");
        Assert.Empty(result.StandardError);
        Assert.True(File.Exists(result.OutputPath));
        var fileBytes = File.ReadAllBytes(result.OutputPath);
        Assert.Equal(fileBytes, result.StandardOutput);
        Assert.False(fileBytes.AsSpan().StartsWith(new byte[] { 0xEF, 0xBB, 0xBF }));
        var fileJson = Encoding.UTF8.GetString(fileBytes);
        Assert.DoesNotContain(rawLogicalEndpoint, fileJson, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(secret, fileJson, StringComparison.Ordinal);

        using var document = JsonDocument.Parse(fileBytes);
        var root = document.RootElement;
        Assert.Equal(
            "GOYMEN-ERP-SQL-RCSI-READINESS-V1",
            root.GetProperty("contractVersion").GetString());
        Assert.Equal(
            expectedExitCode == 0 ? "READY" : "NOT_READY",
            root.GetProperty("status").GetString());
        Assert.Equal(
            "READ_ONLY_SYS_DATABASES_CURRENT_V1",
            root.GetProperty("queryMode").GetString());
        Assert.Equal(
            "relational/process-entrypoint",
            root.GetProperty("targetLabel").GetString());
        Assert.Equal(result.RunId, root.GetProperty("runId").GetString());
        Assert.Equal(
            runtimePayload.SourceRevisionId,
            root.GetProperty("testedCommit").GetString());
        Assert.Equal(
            runtimePayload.WrapperScriptSha256,
            root.GetProperty("expectedWrapperScriptSha256").GetString());
        Assert.Equal(
            runtimePayload.WrapperScriptSha256,
            root.GetProperty("observedWrapperScriptSha256").GetString());
        Assert.Equal(
            runtimePayload.ToolAssemblySha256,
            root.GetProperty("expectedToolAssemblySha256").GetString());
        Assert.Equal(
            runtimePayload.ToolAssemblySha256,
            root.GetProperty("observedToolAssemblySha256").GetString());
        Assert.Equal(
            runtimePayload.ManifestSha256,
            root.GetProperty("runtimePayloadManifestSha256").GetString());
        Assert.Equal(
            runtimePayload.EntryExecutableSha256,
            root.GetProperty("runtimePayloadEntryExecutableSha256").GetString());
        Assert.Equal(expectedDatabase, root.GetProperty("expectedDatabase").GetString());
        Assert.Equal(expectedDatabase, root.GetProperty("configuredDatabase").GetString());
        Assert.Equal(
            SqlServerLogicalEndpointFingerprint.ComputeSha256(rawLogicalEndpoint),
            root.GetProperty("observedLogicalEndpointSha256").GetString());
        Assert.Equal(
            expectedExitCode == 6 ? false : true,
            root.GetProperty("logicalEndpointMatches").GetBoolean());
        if (expectedExitCode == 6)
        {
            Assert.Equal(JsonValueKind.Null, root.GetProperty("observedDatabase").ValueKind);
            Assert.Equal(
                JsonValueKind.Null,
                root.GetProperty("readCommittedSnapshotEnabled").ValueKind);
        }
        else
        {
            Assert.Equal(expectedDatabase, root.GetProperty("observedDatabase").GetString());
            Assert.Equal(
                expectedExitCode == 0,
                root.GetProperty("readCommittedSnapshotEnabled").GetBoolean());
        }

        Assert.Equal(expectedExitCode, root.GetProperty("exitCode").GetInt32());
        Assert.Equal(expectedReasonCode, root.GetProperty("reasonCode").GetString());
        Assert.True(DateTimeOffset.TryParseExact(
            root.GetProperty("observedAtUtc").GetString(),
            "yyyy-MM-dd'T'HH:mm:ss.fffffff'Z'",
            CultureInfo.InvariantCulture,
            DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal,
            out var observedAtUtc));
        Assert.InRange(observedAtUtc, result.StartedAtUtc, result.CompletedAtUtc);
    }

    internal static RuntimePayloadManifest CreateRuntimePayloadManifest(
        string manifestDirectory)
    {
        var repositoryRoot = FindRepositoryRoot();
        var configuration = new DirectoryInfo(AppContext.BaseDirectory).Parent!.Name;
        var payloadRoot = Path.Combine(
            repositoryRoot,
            "tests",
            "GoymenERP.Api.IntegrationTests",
            "bin",
            configuration,
            "rcsi-tool-self-contained",
            RuntimeInformation.RuntimeIdentifier);
        var entryExecutableFileName = OperatingSystem.IsWindows()
            ? "GoymenERP.SqlServerRcsiReadiness.exe"
            : "GoymenERP.SqlServerRcsiReadiness";
        var entryExecutablePath = Path.Combine(payloadRoot, entryExecutableFileName);
        var toolAssemblyPath = Path.Combine(
            payloadRoot,
            "GoymenERP.SqlServerRcsiReadiness.dll");
        var wrapperScriptPath = Path.Combine(
            repositoryRoot,
            "tools",
            "GoymenERP.SqlServerRcsiReadiness",
            "Invoke-GoymenERP.SqlServerRcsiReadiness.ps1");
        var productVersion = FileVersionInfo.GetVersionInfo(toolAssemblyPath).ProductVersion;
        var separatorIndex = productVersion?.LastIndexOf('+') ?? -1;
        Assert.True(separatorIndex >= 0);
        var sourceRevisionId = productVersion![(separatorIndex + 1)..].ToLowerInvariant();
        Assert.Equal(40, sourceRevisionId.Length);
        Assert.All(sourceRevisionId, character => Assert.True(Uri.IsHexDigit(character)));
        Assert.Equal(ReadRepositoryHead(repositoryRoot), sourceRevisionId);

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
            writer.WriteString(
                "contractVersion",
                "GOYMEN-ERP-RCSI-RUNTIME-PAYLOAD-V2");
            writer.WriteString("sourceRevisionId", sourceRevisionId);
            writer.WriteString("entryExecutablePath", entryExecutableFileName);
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

        var manifestBytes = stream.ToArray();
        var manifestPath = Path.Combine(manifestDirectory, "runtime-payload.json");
        File.WriteAllBytes(manifestPath, manifestBytes);
        return new RuntimePayloadManifest(
            payloadRoot,
            manifestPath,
            ComputeSha256(manifestBytes),
            toolAssemblyPath,
            ComputeFileSha256(toolAssemblyPath),
            entryExecutablePath,
            ComputeFileSha256(entryExecutablePath),
            wrapperScriptPath,
            ComputeFileSha256(wrapperScriptPath),
            sourceRevisionId);
    }

    private static void AddRequiredOperatingSystemEnvironment(ProcessStartInfo startInfo)
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        var windowsDirectory = Directory.GetParent(Environment.SystemDirectory)?.FullName;
        Assert.False(string.IsNullOrWhiteSpace(windowsDirectory));
        startInfo.Environment["SystemRoot"] = windowsDirectory;
        startInfo.Environment["WINDIR"] = windowsDirectory;
    }

    private static async Task<byte[]> ReadAllBytesAsync(Stream stream)
    {
        using var buffer = new MemoryStream();
        await stream.CopyToAsync(buffer);
        return buffer.ToArray();
    }

    private static string ComputeFileSha256(string path)
    {
        using var stream = File.OpenRead(path);
        return Convert.ToHexString(SHA256.HashData(stream)).ToLowerInvariant();
    }

    private static string ComputeSha256(byte[] bytes) =>
        Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();

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

        throw new DirectoryNotFoundException("Göymen ERP repository root was not found.");
    }

    private static string ReadRepositoryHead(string repositoryRoot)
    {
        var marker = Path.Combine(repositoryRoot, ".git");
        var gitDirectory = Directory.Exists(marker)
            ? marker
            : Path.GetFullPath(
                File.ReadAllText(marker).Trim()["gitdir:".Length..].Trim(),
                repositoryRoot);
        var head = File.ReadAllText(Path.Combine(gitDirectory, "HEAD")).Trim();
        if (!head.StartsWith("ref: ", StringComparison.Ordinal))
        {
            return head.ToLowerInvariant();
        }

        var commonDirectoryPath = Path.Combine(gitDirectory, "commondir");
        var commonDirectory = File.Exists(commonDirectoryPath)
            ? Path.GetFullPath(File.ReadAllText(commonDirectoryPath).Trim(), gitDirectory)
            : gitDirectory;
        var reference = head["ref: ".Length..];
        var looseReferencePath = Path.Combine(
            commonDirectory,
            reference.Replace('/', Path.DirectorySeparatorChar));
        if (File.Exists(looseReferencePath))
        {
            return File.ReadAllText(looseReferencePath).Trim().ToLowerInvariant();
        }

        var packedLine = File.ReadLines(Path.Combine(commonDirectory, "packed-refs"))
            .Single(line => line.EndsWith(" " + reference, StringComparison.Ordinal));
        return packedLine[..40].ToLowerInvariant();
    }

    private sealed record CliProcessResult(
        int ExitCode,
        byte[] StandardOutput,
        string StandardError,
        string OutputPath,
        string RunId,
        DateTimeOffset StartedAtUtc,
        DateTimeOffset CompletedAtUtc);

    internal sealed record WrapperProcessResult(
        int ExitCode,
        byte[] StandardOutput,
        string StandardError);

    internal sealed record RuntimePayloadManifest(
        string PayloadRoot,
        string ManifestPath,
        string ManifestSha256,
        string ToolAssemblyPath,
        string ToolAssemblySha256,
        string EntryExecutablePath,
        string EntryExecutableSha256,
        string WrapperScriptPath,
        string WrapperScriptSha256,
        string SourceRevisionId);

    private static string GetBaseConnectionString() =>
        Environment.GetEnvironmentVariable(
            SqlServerRelationalAcceptanceTests.ConnectionStringEnvironmentVariable)!;

    private static string NewDatabaseName(string prefix = "goymen_erp_rcsi") =>
        $"{prefix}_{Guid.NewGuid():N}";

    private static string BuildDatabaseConnectionString(
        string baseConnectionString,
        string databaseName)
    {
        var builder = new SqlConnectionStringBuilder(baseConnectionString)
        {
            InitialCatalog = databaseName,
        };
        return builder.ConnectionString;
    }

    private static async Task<SqlServerRcsiReadinessEvidence> ProbeAsync(
        string connectionString,
        string expectedDatabase)
    {
        await using var connection = new SqlConnection(connectionString);
        var logicalEndpoint = new SqlConnectionStringBuilder(connectionString).DataSource;
        return await new SqlServerRcsiReadinessProbe().ProbeAsync(
            connection,
            SqlServerRcsiReadinessBinding.CreateFromLogicalEndpoints(
                "relational/acceptance",
                expectedDatabase,
                [logicalEndpoint]));
    }

    private static void AssertReady(
        SqlServerRcsiReadinessEvidence evidence,
        string databaseName)
    {
        Assert.True(evidence.IsReady);
        Assert.Equal(SqlServerRcsiReadinessExitCode.Ready, evidence.ExitCode);
        Assert.Equal("RCSI_READY", evidence.ReasonCode);
        Assert.True(evidence.ReadCommittedSnapshotEnabled);
        Assert.True(evidence.LogicalEndpointMatches);
        Assert.Contains(
            evidence.ObservedLogicalEndpointSha256,
            evidence.ApprovedLogicalEndpointSha256!);
        Assert.Equal(databaseName, evidence.ConfiguredDatabase);
        Assert.Equal(databaseName, evidence.ObservedDatabase);
    }

    private static async Task CreateDatabaseAsync(
        string baseConnectionString,
        string databaseName,
        bool rcsiEnabled)
    {
        try
        {
            await using var connection = await OpenMasterConnectionAsync(baseConnectionString);
            await using var command = connection.CreateCommand();
            command.CommandText = $"""
                CREATE DATABASE [{databaseName}];
                ALTER DATABASE [{databaseName}]
                    SET READ_COMMITTED_SNAPSHOT {(rcsiEnabled ? "ON" : "OFF")}
                    WITH ROLLBACK IMMEDIATE;
                """;
            await command.ExecuteNonQueryAsync();
        }
        catch
        {
            try
            {
                await DropDatabaseAsync(baseConnectionString, databaseName);
            }
            catch
            {
                // Preserve the creation/ALTER failure while still attempting cleanup.
            }

            throw;
        }
    }

    private static async Task SetRcsiForTestAsync(
        string baseConnectionString,
        string databaseName,
        bool enabled)
    {
        await using var connection = await OpenMasterConnectionAsync(baseConnectionString);
        await using var command = connection.CreateCommand();
        command.CommandText = $"""
            ALTER DATABASE [{databaseName}]
                SET READ_COMMITTED_SNAPSHOT {(enabled ? "ON" : "OFF")}
                WITH ROLLBACK IMMEDIATE;
            """;
        await command.ExecuteNonQueryAsync();
    }

    private static async Task<int> ReadRcsiSettingAsync(
        string baseConnectionString,
        string databaseName)
    {
        await using var connection = await OpenMasterConnectionAsync(baseConnectionString);
        await using var command = connection.CreateCommand();
        command.CommandText =
            "SELECT CONVERT(int, [is_read_committed_snapshot_on]) "
            + "FROM [sys].[databases] WHERE [name] = @DatabaseName;";
        command.Parameters.Add(new SqlParameter("@DatabaseName", databaseName));
        return Assert.IsType<int>(await command.ExecuteScalarAsync());
    }

    private static async Task DropDatabaseAsync(
        string baseConnectionString,
        string databaseName)
    {
        await using var connection = await OpenMasterConnectionAsync(baseConnectionString);
        await using var command = connection.CreateCommand();
        command.CommandText = $"""
            IF DB_ID(N'{databaseName}') IS NOT NULL
            BEGIN
                ALTER DATABASE [{databaseName}] SET SINGLE_USER WITH ROLLBACK IMMEDIATE;
                DROP DATABASE [{databaseName}];
            END;
            """;
        await command.ExecuteNonQueryAsync();
    }

    private static async Task<SqlConnection> OpenMasterConnectionAsync(
        string baseConnectionString)
    {
        var builder = new SqlConnectionStringBuilder(baseConnectionString)
        {
            InitialCatalog = "master",
        };
        var connection = new SqlConnection(builder.ConnectionString);
        await connection.OpenAsync();
        return connection;
    }
}

public sealed class SqlServerRcsiReadinessProcessAcceptanceTests
{
    [Fact]
    public async Task Wrapper_MalformedSecretPropagatesCanonicalExitTwoWithoutSqlServer()
    {
        var root = CreateTemporaryDirectory("invalid-binding");
        try
        {
            var payload = SqlServerRcsiReadinessSqlServerRelationalAcceptanceTests
                .CreateRuntimePayloadManifest(root);
            var outputPath = Path.Combine(root, "evidence", "readiness.json");
            var endpointSha256 = SqlServerLogicalEndpointFingerprint.ComputeSha256(
                "tcp:127.0.0.1,1");
            var result = await SqlServerRcsiReadinessSqlServerRelationalAcceptanceTests
                .RunPowerShellWrapperAsync(
                    "not-a-connection-string",
                    "goymen_erp_rcsi_process_invalid",
                    [endpointSha256],
                    outputPath,
                    payload,
                    overallTimeoutSeconds: 30,
                    testProcessTimeout: TimeSpan.FromSeconds(90));

            Assert.Equal(2, result.ExitCode);
            Assert.Equal(File.ReadAllBytes(outputPath), result.StandardOutput);
            Assert.Contains("native exit 2", result.StandardError, StringComparison.Ordinal);
            Assert.DoesNotContain(
                "not-a-connection-string",
                result.StandardError,
                StringComparison.Ordinal);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task Wrapper_PreOpenDatabaseAndLogicalEndpointMismatchesAreCanonical()
    {
        var root = CreateTemporaryDirectory("pre-open-mismatch");
        try
        {
            var payload = SqlServerRcsiReadinessSqlServerRelationalAcceptanceTests
                .CreateRuntimePayloadManifest(root);
            const string expectedDatabase = "goymen_erp_rcsi_process_expected";
            const string logicalEndpoint = "127.0.0.1,1";
            var actualEndpointSha256 = SqlServerLogicalEndpointFingerprint.ComputeSha256(
                logicalEndpoint);
            var databaseMismatchConnection = new SqlConnectionStringBuilder
            {
                DataSource = logicalEndpoint,
                InitialCatalog = "goymen_erp_rcsi_process_other",
                UserID = "probe",
                Password = "pre-open-database-secret",
                Encrypt = false,
                ConnectTimeout = 1,
            }.ConnectionString;
            var databaseOutput = Path.Combine(root, "database-mismatch", "readiness.json");
            var databaseMismatch = await SqlServerRcsiReadinessSqlServerRelationalAcceptanceTests
                .RunPowerShellWrapperAsync(
                    databaseMismatchConnection,
                    expectedDatabase,
                    [actualEndpointSha256],
                    databaseOutput,
                    payload,
                    overallTimeoutSeconds: 30,
                    testProcessTimeout: TimeSpan.FromSeconds(90));
            Assert.Equal(3, databaseMismatch.ExitCode);
            Assert.Equal(File.ReadAllBytes(databaseOutput), databaseMismatch.StandardOutput);

            var endpointMismatchConnection = new SqlConnectionStringBuilder(
                databaseMismatchConnection)
            {
                InitialCatalog = expectedDatabase,
            }.ConnectionString;
            var endpointOutput = Path.Combine(root, "endpoint-mismatch", "readiness.json");
            var endpointMismatch = await SqlServerRcsiReadinessSqlServerRelationalAcceptanceTests
                .RunPowerShellWrapperAsync(
                    endpointMismatchConnection,
                    expectedDatabase,
                    [SqlServerLogicalEndpointFingerprint.ComputeSha256(
                        "tcp:approved-but-not-observed.invalid,1433")],
                    endpointOutput,
                    payload,
                    overallTimeoutSeconds: 30,
                    testProcessTimeout: TimeSpan.FromSeconds(90));
            Assert.Equal(6, endpointMismatch.ExitCode);
            Assert.Equal(File.ReadAllBytes(endpointOutput), endpointMismatch.StandardOutput);
            Assert.DoesNotContain(
                "pre-open-database-secret",
                databaseMismatch.StandardError + endpointMismatch.StandardError,
                StringComparison.Ordinal);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task Wrapper_OneDeadlineKillsHungPreLoginSocketWithoutResidue()
    {
        var root = CreateTemporaryDirectory("deadline");
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        try
        {
            var payload = SqlServerRcsiReadinessSqlServerRelationalAcceptanceTests
                .CreateRuntimePayloadManifest(root);
            var endpoint = (IPEndPoint)listener.LocalEndpoint;
            const string expectedDatabase = "goymen_erp_rcsi_process_timeout";
            const string timeoutSecret = "process-timeout-secret";
            var connectionString = new SqlConnectionStringBuilder
            {
                DataSource = $"127.0.0.1,{endpoint.Port}",
                InitialCatalog = expectedDatabase,
                UserID = "probe",
                Password = timeoutSecret,
                Encrypt = false,
                ConnectTimeout = 30,
            }.ConnectionString;
            var outputPath = Path.Combine(root, "timeout", "readiness.json");
            var acceptTask = listener.AcceptTcpClientAsync();
            var invocation = SqlServerRcsiReadinessSqlServerRelationalAcceptanceTests
                .RunPowerShellWrapperAsync(
                    connectionString,
                    expectedDatabase,
                    [SqlServerLogicalEndpointFingerprint.ComputeSha256(
                        new SqlConnectionStringBuilder(connectionString).DataSource)],
                    outputPath,
                    payload,
                    overallTimeoutSeconds: 20,
                    testProcessTimeout: TimeSpan.FromSeconds(90));

            var firstCompleted = await Task.WhenAny(acceptTask, invocation)
                .WaitAsync(TimeSpan.FromSeconds(60));
            Assert.Same(acceptTask, firstCompleted);
            using var accepted = await acceptTask;
            var containedChildStopwatch = Stopwatch.StartNew();
            var eofBuffer = new byte[256];
            try
            {
                while (await accepted.GetStream()
                           .ReadAsync(eofBuffer.AsMemory())
                           .AsTask()
                           .WaitAsync(TimeSpan.FromSeconds(22)) != 0)
                {
                    // Drain SQL pre-login bytes until containment closes the socket.
                }
            }
            catch (IOException exception) when (
                exception.InnerException is SocketException
                {
                    SocketErrorCode: SocketError.ConnectionReset
                        or SocketError.ConnectionAborted,
                })
            {
                // Containment can close the pre-login socket by EOF or RST.
            }
            containedChildStopwatch.Stop();
            Assert.InRange(
                containedChildStopwatch.Elapsed,
                TimeSpan.Zero,
                TimeSpan.FromSeconds(22));

            var result = await invocation;
            Assert.Equal(5, result.ExitCode);
            Assert.False(File.Exists(outputPath));
            Assert.Empty(Directory.GetFiles(root, "*.tmp", SearchOption.AllDirectories));
            Assert.DoesNotContain(timeoutSecret, result.StandardError, StringComparison.Ordinal);
            Assert.DoesNotContain(
                timeoutSecret,
                Encoding.UTF8.GetString(result.StandardOutput),
                StringComparison.Ordinal);
        }
        finally
        {
            listener.Stop();
            Directory.Delete(root, recursive: true);
        }
    }

    private static string CreateTemporaryDirectory(string scenario)
    {
        var path = Path.Combine(
            Path.GetTempPath(),
            $"goymen-erp-rcsi-process-{scenario}-{Guid.NewGuid():N}");
        Directory.CreateDirectory(path);
        return path;
    }
}
