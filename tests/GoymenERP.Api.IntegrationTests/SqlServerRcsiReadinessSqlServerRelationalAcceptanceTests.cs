using System.Net;
using System.Diagnostics;
using System.Globalization;
using System.Text.Json;
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
        await CreateDatabaseAsync(baseConnectionString, databaseName, rcsiEnabled: true);
        var factory = new SqlServerRelationalApiFactory(
            baseConnectionString,
            databaseName: databaseName);

        try
        {
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
            await factory.DisposeAsync();
            await DropDatabaseAsync(baseConnectionString, databaseName);
        }
    }

    [SqlServerFact]
    public async Task RcsiOff_ProbeFailsClosedAndDoesNotChangeTheDatabase()
    {
        var baseConnectionString = GetBaseConnectionString();
        var databaseName = NewDatabaseName();
        await CreateDatabaseAsync(baseConnectionString, databaseName, rcsiEnabled: false);

        try
        {
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
            await DropDatabaseAsync(baseConnectionString, databaseName);
        }
    }

    [SqlServerFact]
    public async Task ExpectedDatabaseMismatch_IsRejectedBeforeTheConnectionIsOpened()
    {
        var baseConnectionString = GetBaseConnectionString();
        var databaseName = NewDatabaseName();
        await CreateDatabaseAsync(baseConnectionString, databaseName, rcsiEnabled: true);

        try
        {
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
            await DropDatabaseAsync(baseConnectionString, databaseName);
        }
    }

    [SqlServerFact]
    public async Task UnapprovedLogicalEndpoint_IsRejectedWithoutChangingDatabase()
    {
        var baseConnectionString = GetBaseConnectionString();
        var databaseName = NewDatabaseName();
        await CreateDatabaseAsync(baseConnectionString, databaseName, rcsiEnabled: true);

        try
        {
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
            await DropDatabaseAsync(baseConnectionString, databaseName);
        }
    }

    [SqlServerFact]
    public async Task RcsiOffStartupFailsThenExplicitTestAdminEnableAllowsStartupAndProbe()
    {
        var baseConnectionString = GetBaseConnectionString();
        var databaseName = NewDatabaseName();
        await CreateDatabaseAsync(baseConnectionString, databaseName, rcsiEnabled: false);

        try
        {
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
            await DropDatabaseAsync(baseConnectionString, databaseName);
        }
    }

    [SqlServerFact]
    public async Task CliProcess_ProducesFreshAtomicEvidenceForReadyOffMismatchAndStalePaths()
    {
        const string testedCommit = "bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb";
        var baseConnectionString = GetBaseConnectionString();
        var readyDatabase = NewDatabaseName();
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
                testedCommit);
            AssertCliEvidence(
                readyResult,
                expectedExitCode: 0,
                expectedReasonCode: "RCSI_READY",
                logicalEndpoint,
                new SqlConnectionStringBuilder(readyConnectionString).Password,
                testedCommit);

            var offResult = await RunCliProcessAsync(
                offConnectionString,
                offDatabase,
                [logicalEndpointSha256],
                Path.Combine(evidenceDirectory, "off.json"),
                Guid.NewGuid().ToString("D"),
                testedCommit);
            AssertCliEvidence(
                offResult,
                expectedExitCode: 4,
                expectedReasonCode: "RCSI_DISABLED",
                logicalEndpoint,
                new SqlConnectionStringBuilder(offConnectionString).Password,
                testedCommit);
            Assert.Equal(0, await ReadRcsiSettingAsync(baseConnectionString, offDatabase));

            var wrongLogicalEndpointResult = await RunCliProcessAsync(
                readyConnectionString,
                readyDatabase,
                [SqlServerLogicalEndpointFingerprint.ComputeSha256(
                    "tcp:unapproved-logical-endpoint.invalid,1433")],
                Path.Combine(evidenceDirectory, "wrong-endpoint.json"),
                Guid.NewGuid().ToString("D"),
                testedCommit);
            AssertCliEvidence(
                wrongLogicalEndpointResult,
                expectedExitCode: 6,
                expectedReasonCode: "EXPECTED_LOGICAL_ENDPOINT_MISMATCH",
                logicalEndpoint,
                new SqlConnectionStringBuilder(readyConnectionString).Password,
                testedCommit);

            var staleOutputPath = Path.Combine(evidenceDirectory, "stale-ready.json");
            await File.WriteAllTextAsync(staleOutputPath, "pre-existing-ready-evidence");
            var staleResult = await RunCliProcessAsync(
                readyConnectionString,
                readyDatabase,
                [logicalEndpointSha256],
                staleOutputPath,
                Guid.NewGuid().ToString("D"),
                testedCommit);
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

    private static async Task<CliProcessResult> RunCliProcessAsync(
        string connectionString,
        string expectedDatabase,
        IReadOnlyList<string> approvedLogicalEndpointSha256,
        string outputPath,
        string runId,
        string testedCommit)
    {
        var repositoryRoot = FindRepositoryRoot();
        var configuration = new DirectoryInfo(AppContext.BaseDirectory)
            .Parent!
            .Name;
        var toolAssembly = Path.Combine(
            repositoryRoot,
            "tools",
            "GoymenERP.SqlServerRcsiReadiness",
            "bin",
            configuration,
            "net10.0",
            "GoymenERP.SqlServerRcsiReadiness.dll");
        Assert.True(File.Exists(toolAssembly), $"RCSI CLI assembly is missing: {toolAssembly}");

        var startInfo = new ProcessStartInfo("dotnet")
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };
        startInfo.ArgumentList.Add(toolAssembly);
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
        startInfo.ArgumentList.Add("--timeout-seconds");
        startInfo.ArgumentList.Add("30");
        startInfo.ArgumentList.Add("--output");
        startInfo.ArgumentList.Add(outputPath);
        startInfo.Environment["GOYMEN_ERP_RCSI_PROCESS_CONNECTION"] = connectionString;

        using var process = new Process { StartInfo = startInfo };
        var startedAtUtc = DateTimeOffset.UtcNow;
        Assert.True(process.Start());
        var standardOutputTask = process.StandardOutput.ReadToEndAsync();
        var standardErrorTask = process.StandardError.ReadToEndAsync();
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(60));
        await process.WaitForExitAsync(timeout.Token);
        var completedAtUtc = DateTimeOffset.UtcNow;
        var standardOutput = await standardOutputTask;
        var standardError = (await standardErrorTask).TrimEnd('\r', '\n');
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
        string testedCommit)
    {
        Assert.Equal(expectedExitCode, result.ExitCode);
        Assert.Empty(result.StandardError);
        Assert.True(File.Exists(result.OutputPath));
        var fileJson = File.ReadAllText(result.OutputPath);
        Assert.Equal(fileJson, result.StandardOutput);
        Assert.DoesNotContain(rawLogicalEndpoint, fileJson, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(secret, fileJson, StringComparison.Ordinal);

        using var document = JsonDocument.Parse(fileJson);
        var root = document.RootElement;
        Assert.Equal(result.RunId, root.GetProperty("runId").GetString());
        Assert.Equal(testedCommit, root.GetProperty("testedCommit").GetString());
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

    private sealed record CliProcessResult(
        int ExitCode,
        string StandardOutput,
        string StandardError,
        string OutputPath,
        string RunId,
        DateTimeOffset StartedAtUtc,
        DateTimeOffset CompletedAtUtc);

    private static string GetBaseConnectionString() =>
        Environment.GetEnvironmentVariable(
            SqlServerRelationalAcceptanceTests.ConnectionStringEnvironmentVariable)!;

    private static string NewDatabaseName() => $"goymen_erp_rcsi_{Guid.NewGuid():N}";

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
