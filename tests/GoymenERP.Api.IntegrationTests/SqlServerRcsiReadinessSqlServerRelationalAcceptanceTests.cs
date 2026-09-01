using System.Net;
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
            var binding = SqlServerRcsiReadinessBinding.CreateFromDataSource(
                "relational/mismatch",
                $"{databaseName}_other",
                new SqlConnectionStringBuilder(connection.ConnectionString).DataSource);

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
    public async Task ExpectedDataSourceFingerprintMismatch_IsRejectedWithoutChangingDatabase()
    {
        var baseConnectionString = GetBaseConnectionString();
        var databaseName = NewDatabaseName();
        await CreateDatabaseAsync(baseConnectionString, databaseName, rcsiEnabled: true);

        try
        {
            await using var connection = new SqlConnection(
                BuildDatabaseConnectionString(baseConnectionString, databaseName));
            var binding = SqlServerRcsiReadinessBinding.CreateFromDataSource(
                "relational/wrong-target",
                databaseName,
                "tcp:deliberately-wrong-target.invalid,1433");

            var evidence = await new SqlServerRcsiReadinessProbe()
                .ProbeAsync(connection, binding);

            Assert.Equal(System.Data.ConnectionState.Closed, connection.State);
            Assert.False(evidence.IsReady);
            Assert.Equal(
                SqlServerRcsiReadinessExitCode.ExpectedTargetMismatch,
                evidence.ExitCode);
            Assert.Equal("EXPECTED_TARGET_MISMATCH", evidence.ReasonCode);
            Assert.False(evidence.DataSourceMatches);
            Assert.NotEqual(
                evidence.ExpectedDataSourceSha256,
                evidence.ObservedDataSourceSha256);
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
            Assert.Equal(0, await ReadRcsiSettingAsync(baseConnectionString, databaseName));
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
            Assert.Equal(1, await ReadRcsiSettingAsync(baseConnectionString, databaseName));

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
            await DropDatabaseAsync(baseConnectionString, databaseName);
        }
    }

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
        var dataSource = new SqlConnectionStringBuilder(connectionString).DataSource;
        return await new SqlServerRcsiReadinessProbe().ProbeAsync(
            connection,
            SqlServerRcsiReadinessBinding.CreateFromDataSource(
                "relational/acceptance",
                expectedDatabase,
                dataSource));
    }

    private static void AssertReady(
        SqlServerRcsiReadinessEvidence evidence,
        string databaseName)
    {
        Assert.True(evidence.IsReady);
        Assert.Equal(SqlServerRcsiReadinessExitCode.Ready, evidence.ExitCode);
        Assert.Equal("RCSI_READY", evidence.ReasonCode);
        Assert.True(evidence.ReadCommittedSnapshotEnabled);
        Assert.True(evidence.DataSourceMatches);
        Assert.Equal(
            evidence.ExpectedDataSourceSha256,
            evidence.ObservedDataSourceSha256);
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
