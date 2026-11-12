using System.Net;
using GoymenERP.Api.ProductRuntime;
using GoymenERP.Modules.Identity.Infrastructure.Services;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace GoymenERP.Api.IntegrationTests;

/// <summary>
/// F-022: ValidateOnly readiness must refuse a drifted migration history of every context the host migrates,
/// without repairing it. Creates only disposable databases; no fixture, permission or grant.
/// </summary>
public sealed class ReadinessMigrationHistorySqlServerRelationalAcceptanceTests
{
    // F-022: the Settings and Documents contexts are migrated by the host, so a drifted history must keep the host not ready.
    [SqlServerFact]
    public async Task ValidateOnly_SettingsAndDocumentsHistoryDriftFailsClosedWithoutRepair()
    {
        var baseConnectionString = GetBaseConnectionString();
        var databaseName = NewDatabaseName("goymen_erp_validate_only_module_drift");
        const string futureMigration = "99999999999999_ValidateOnlyFuture";
        (string Schema, string HistoryTable)[] histories =
        [
            ("settings", "__EFMigrationsHistory"),
            ("documents", "__EFMigrationsHistory"),
        ];

        try
        {
            await CreateDatabaseAsync(baseConnectionString, databaseName);
            await InitializeDatabaseAsync(baseConnectionString, databaseName);
            var connection = BuildDatabaseConnectionString(baseConnectionString, databaseName);
            Assert.Equal(HttpStatusCode.OK, await ReadValidateOnlyReadinessAsync(baseConnectionString, databaseName));

            foreach (var (schema, historyTable) in histories)
            {
                var removed = await RemoveLatestMigrationAsync(connection, schema, historyTable);
                Assert.Equal(
                    HttpStatusCode.ServiceUnavailable,
                    await ReadValidateOnlyReadinessAsync(baseConnectionString, databaseName));
                Assert.False(await MigrationExistsAsync(connection, schema, historyTable, removed.MigrationId));
                await InsertMigrationAsync(connection, schema, historyTable, removed.MigrationId, removed.ProductVersion);

                await InsertMigrationAsync(connection, schema, historyTable, futureMigration, "10.0.0");
                Assert.Equal(
                    HttpStatusCode.ServiceUnavailable,
                    await ReadValidateOnlyReadinessAsync(baseConnectionString, databaseName));
                Assert.True(await MigrationExistsAsync(connection, schema, historyTable, futureMigration));
                await DeleteMigrationAsync(connection, schema, historyTable, futureMigration);
            }

            Assert.Equal(HttpStatusCode.OK, await ReadValidateOnlyReadinessAsync(baseConnectionString, databaseName));
        }
        finally
        {
            await DropDatabaseAsync(baseConnectionString, databaseName);
        }
    }

    // F-022: the price-list context belongs to the Product runtime gate (B-06); it is checked only while that runtime is on.
    [SqlServerFact]
    public async Task ValidateOnly_PriceListHistoryDriftFailsClosedOnlyWhileProductRuntimeIsEnabled()
    {
        var baseConnectionString = GetBaseConnectionString();
        var databaseName = NewDatabaseName("goymen_erp_validate_only_price_list");
        var databaseConnectionString = BuildDatabaseConnectionString(baseConnectionString, databaseName);
        const string schema = "product";
        const string historyTable = "__EFMigrationsHistory_PriceLists";

        try
        {
            await CreateDatabaseAsync(baseConnectionString, databaseName);
            await using (var initializer = CreateProductRuntimeFactory(
                             baseConnectionString,
                             databaseName,
                             databaseConnectionString,
                             validateOnly: false))
            using (var initializerClient = initializer.CreateClient())
            using (var live = await initializerClient.GetAsync("/health/live"))
            {
                Assert.Equal(HttpStatusCode.OK, live.StatusCode);
            }

            Assert.Equal(
                HttpStatusCode.OK,
                await ReadProductRuntimeValidateOnlyReadinessAsync(
                    baseConnectionString,
                    databaseName,
                    databaseConnectionString));

            var removed = await RemoveLatestMigrationAsync(databaseConnectionString, schema, historyTable);
            Assert.Equal(
                HttpStatusCode.ServiceUnavailable,
                await ReadProductRuntimeValidateOnlyReadinessAsync(
                    baseConnectionString,
                    databaseName,
                    databaseConnectionString));
            Assert.False(await MigrationExistsAsync(databaseConnectionString, schema, historyTable, removed.MigrationId));

            // Product runtime off: the price-list context is skipped like the other Product contexts.
            Assert.Equal(HttpStatusCode.OK, await ReadValidateOnlyReadinessAsync(baseConnectionString, databaseName));
            Assert.False(await MigrationExistsAsync(databaseConnectionString, schema, historyTable, removed.MigrationId));
        }
        finally
        {
            await DropDatabaseAsync(baseConnectionString, databaseName);
        }
    }

    private static async Task<HttpStatusCode> ReadValidateOnlyReadinessAsync(
        string baseConnectionString,
        string databaseName)
    {
        await using var factory = CreateValidateOnlyFactory(baseConnectionString, databaseName);
        using var client = factory.CreateClient();
        using var ready = await client.GetAsync("/health/ready");
        return ready.StatusCode;
    }

    private static async Task<HttpStatusCode> ReadProductRuntimeValidateOnlyReadinessAsync(
        string baseConnectionString,
        string databaseName,
        string databaseConnectionString)
    {
        await using var factory = CreateProductRuntimeFactory(
            baseConnectionString,
            databaseName,
            databaseConnectionString,
            validateOnly: true);
        using var client = factory.CreateClient();
        using var ready = await client.GetAsync("/health/ready");
        return ready.StatusCode;
    }

    private static SqlServerRelationalApiFactory CreateProductRuntimeFactory(
        string baseConnectionString,
        string databaseName,
        string databaseConnectionString,
        bool validateOnly) =>
        new(
            baseConnectionString,
            services =>
            {
                services.RemoveAll<ProductRuntimeOptions>();
                services.AddSingleton(new ProductRuntimeOptions(
                    databaseConnectionString,
                    new ProductPilotIssuerBinding(Guid.NewGuid(), Guid.NewGuid()),
                    new byte[32]));
            },
            databaseName: databaseName,
            configurationOverrides: validateOnly
                ? new Dictionary<string, string?> { ["DatabaseStartup:Mode"] = "ValidateOnly" }
                : null);

    private static async Task<MigrationHistoryRow> RemoveLatestMigrationAsync(
        string connectionString,
        string schema,
        string historyTable)
    {
        var table = $"{QuoteIdentifier(schema)}.{QuoteIdentifier(historyTable)}";
        await using var connection = new SqlConnection(connectionString);
        await connection.OpenAsync();
        await using var selectCommand = connection.CreateCommand();
        selectCommand.CommandText = $"SELECT TOP (1) [MigrationId], [ProductVersion] FROM {table} ORDER BY [MigrationId] DESC;";
        await using var reader = await selectCommand.ExecuteReaderAsync();
        Assert.True(await reader.ReadAsync());
        var row = new MigrationHistoryRow(reader.GetString(0), reader.GetString(1));
        await reader.CloseAsync();

        await DeleteMigrationAsync(connectionString, schema, historyTable, row.MigrationId);
        return row;
    }

    private static async Task InsertMigrationAsync(
        string connectionString,
        string schema,
        string historyTable,
        string migrationId,
        string productVersion)
    {
        await using var connection = new SqlConnection(connectionString);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = $"INSERT INTO {QuoteIdentifier(schema)}.{QuoteIdentifier(historyTable)} "
            + "([MigrationId], [ProductVersion]) VALUES (@MigrationId, @ProductVersion);";
        command.Parameters.Add(new SqlParameter("@MigrationId", migrationId));
        command.Parameters.Add(new SqlParameter("@ProductVersion", productVersion));
        Assert.Equal(1, await command.ExecuteNonQueryAsync());
    }

    private static async Task DeleteMigrationAsync(
        string connectionString,
        string schema,
        string historyTable,
        string migrationId)
    {
        await using var connection = new SqlConnection(connectionString);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = $"DELETE FROM {QuoteIdentifier(schema)}.{QuoteIdentifier(historyTable)} "
            + "WHERE [MigrationId] = @MigrationId;";
        command.Parameters.Add(new SqlParameter("@MigrationId", migrationId));
        Assert.Equal(1, await command.ExecuteNonQueryAsync());
    }

    private static async Task<bool> MigrationExistsAsync(
        string connectionString,
        string schema,
        string historyTable,
        string migrationId)
    {
        await using var connection = new SqlConnection(connectionString);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = $"SELECT COUNT_BIG(*) FROM {QuoteIdentifier(schema)}.{QuoteIdentifier(historyTable)} "
            + "WHERE [MigrationId] = @MigrationId;";
        command.Parameters.Add(new SqlParameter("@MigrationId", migrationId));
        return Assert.IsType<long>(await command.ExecuteScalarAsync()) == 1L;
    }

    private sealed record MigrationHistoryRow(string MigrationId, string ProductVersion);

    private static string GetBaseConnectionString() =>
        Environment.GetEnvironmentVariable(
            SqlServerRelationalAcceptanceTests.ConnectionStringEnvironmentVariable)!;

    private static string NewDatabaseName(string prefix) => $"{prefix}_{Guid.NewGuid():N}";

    private static string QuoteIdentifier(string identifier) =>
        $"[{identifier.Replace("]", "]]", StringComparison.Ordinal)}]";

    private static string BuildDatabaseConnectionString(string baseConnectionString, string databaseName) =>
        new SqlConnectionStringBuilder(baseConnectionString) { InitialCatalog = databaseName }.ConnectionString;

    private static SqlServerRelationalApiFactory CreateValidateOnlyFactory(
        string baseConnectionString,
        string databaseName) =>
        new(
            baseConnectionString,
            databaseName: databaseName,
            configurationOverrides: new Dictionary<string, string?> { ["DatabaseStartup:Mode"] = "ValidateOnly" });

    private static async Task InitializeDatabaseAsync(string baseConnectionString, string databaseName)
    {
        await using var factory = new SqlServerRelationalApiFactory(baseConnectionString, databaseName: databaseName);
        using var client = factory.CreateClient();
        using var live = await client.GetAsync("/health/live");
        Assert.Equal(HttpStatusCode.OK, live.StatusCode);
    }

    private static async Task CreateDatabaseAsync(string baseConnectionString, string databaseName)
    {
        try
        {
            await using var connection = await OpenMasterConnectionAsync(baseConnectionString);
            await using var command = connection.CreateCommand();
            command.CommandText = $"""
                CREATE DATABASE {QuoteIdentifier(databaseName)};
                ALTER DATABASE {QuoteIdentifier(databaseName)} SET READ_COMMITTED_SNAPSHOT ON WITH ROLLBACK IMMEDIATE;
                """;
            await command.ExecuteNonQueryAsync();
            ClearDatabasePool(baseConnectionString, databaseName);
        }
        catch
        {
            try
            {
                await DropDatabaseAsync(baseConnectionString, databaseName);
            }
            catch
            {
                // Keep the creation failure while still attempting cleanup.
            }

            throw;
        }
    }

    private static async Task DropDatabaseAsync(string baseConnectionString, string databaseName)
    {
        await using var connection = await OpenMasterConnectionAsync(baseConnectionString);
        await using var command = connection.CreateCommand();
        command.CommandText = $"""
            IF DB_ID(@DatabaseName) IS NOT NULL
            BEGIN
                ALTER DATABASE {QuoteIdentifier(databaseName)} SET SINGLE_USER WITH ROLLBACK IMMEDIATE;
                DROP DATABASE {QuoteIdentifier(databaseName)};
            END;
            """;
        command.Parameters.Add(new SqlParameter("@DatabaseName", databaseName));
        await command.ExecuteNonQueryAsync();
        ClearDatabasePool(baseConnectionString, databaseName);
    }

    private static void ClearDatabasePool(string baseConnectionString, string databaseName)
    {
        // The drop disconnects this database's sessions; discard only its pooled connections.
        using var poolKey = new SqlConnection(BuildDatabaseConnectionString(baseConnectionString, databaseName));
        SqlConnection.ClearPool(poolKey);
    }

    private static async Task<SqlConnection> OpenMasterConnectionAsync(string baseConnectionString)
    {
        var connection = new SqlConnection(
            new SqlConnectionStringBuilder(baseConnectionString) { InitialCatalog = "master" }.ConnectionString);
        await connection.OpenAsync();
        return connection;
    }
}
