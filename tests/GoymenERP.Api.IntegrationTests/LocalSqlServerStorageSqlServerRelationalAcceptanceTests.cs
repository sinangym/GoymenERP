using Microsoft.Data.SqlClient;

namespace GoymenERP.Api.IntegrationTests;

public sealed class LocalSqlServerStorageSqlServerRelationalAcceptanceTests
{
    [SqlServerFact]
    public async Task One_owned_catalog_reopens_without_attach_and_a_failed_creator_cannot_drop_it()
    {
        var baseConnection = Environment.GetEnvironmentVariable(SqlServerRelationalAcceptanceTests.ConnectionStringEnvironmentVariable)!;
        var directory = Environment.GetEnvironmentVariable("GOYMEN_ERP_TEST_SQLSERVER_DATA_DIRECTORY");
        var database = new SqlServerTestDatabase(baseConnection, $"goymen_storage_{Guid.NewGuid():N}", directory);
        // Prepare the competing descriptor before creation: its failed CREATE must never acquire cleanup ownership.
        var collision = new SqlServerTestDatabase(baseConnection, database.Name, localDataDirectory: null);
        try
        {
            await database.CreateAsync(rcsiEnabled: true);
            await Assert.ThrowsAsync<InvalidOperationException>(() => database.CreateAsync(rcsiEnabled: true));
            var error = await Assert.ThrowsAsync<SqlException>(() => collision.CreateAsync(rcsiEnabled: true));
            Assert.Equal(1801, error.Number);
            await collision.DropAsync();
            await using (var connection = new SqlConnection(database.ConnectionString))
            {
                await connection.OpenAsync();
                await using var command = connection.CreateCommand();
                command.CommandText = "CREATE TABLE [dbo].[SENTETIK_StorageProbe] ([Value] int NOT NULL); INSERT INTO [dbo].[SENTETIK_StorageProbe] VALUES (17);";
                await command.ExecuteNonQueryAsync();
                command.CommandText = "SELECT CONVERT(int,is_read_committed_snapshot_on) FROM sys.databases WHERE name=DB_NAME();";
                Assert.Equal(1, (int)(await command.ExecuteScalarAsync())!);
                if (!string.IsNullOrWhiteSpace(directory))
                {
                    command.CommandText = "SELECT physical_name FROM sys.database_files ORDER BY file_id;";
                    await using var reader = await command.ExecuteReaderAsync();
                    Assert.True(await reader.ReadAsync());
                    Assert.Equal(Path.GetFullPath(Path.Combine(directory, database.Name + ".mdf")), Path.GetFullPath(reader.GetString(0)), ignoreCase: true);
                    Assert.True(await reader.ReadAsync());
                    Assert.Equal(Path.GetFullPath(Path.Combine(directory, database.Name + "_log.ldf")), Path.GetFullPath(reader.GetString(0)), ignoreCase: true);
                    Assert.False(await reader.ReadAsync());
                }
            }
            for (var restart = 0; restart < 2; restart++)
            {
                await using var host = new SqlServerRelationalApiFactory(baseConnection, testDatabase: database);
                Assert.Equal(string.Empty, new SqlConnectionStringBuilder(host.DatabaseConnectionString).AttachDBFilename);
                await using var connection = new SqlConnection(host.DatabaseConnectionString);
                await connection.OpenAsync();
                await using var command = new SqlCommand("SELECT [Value] FROM [dbo].[SENTETIK_StorageProbe];", connection);
                Assert.Equal(17, (int)(await command.ExecuteScalarAsync())!);
            }
        }
        finally { await database.DropAsync(); }

        var master = new SqlConnectionStringBuilder(baseConnection) { InitialCatalog = "master", AttachDBFilename = string.Empty };
        await using var after = new SqlConnection(master.ConnectionString);
        await after.OpenAsync();
        await using var absent = new SqlCommand("SELECT DB_ID(@name);", after);
        absent.Parameters.AddWithValue("@name", database.Name);
        Assert.Equal(DBNull.Value, await absent.ExecuteScalarAsync());
        if (!string.IsNullOrWhiteSpace(directory))
        {
            Assert.False(File.Exists(Path.Combine(directory, database.Name + ".mdf")));
            Assert.False(File.Exists(Path.Combine(directory, database.Name + "_log.ldf")));
        }
    }
}
