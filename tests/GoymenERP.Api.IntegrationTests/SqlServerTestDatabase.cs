using Microsoft.Data.SqlClient;

namespace GoymenERP.Api.IntegrationTests;

/// <summary>
/// A single test owns creation and cleanup; every host/probe subsequently connects by catalog.
/// Local file placement is chosen once, never by reattaching a file on each host restart.
/// </summary>
internal sealed class SqlServerTestDatabase
{
    private readonly string masterConnectionString;
    private readonly string? dataFile;
    private readonly string? logFile;
    private bool creationAttempted;
    private bool created;

    public SqlServerTestDatabase(string baseConnectionString, string name, string? localDataDirectory)
    {
        // The Unicode prefix is intentional coverage for the existing RCSI CLI/wrapper tests.
        if (!(name.StartsWith("goymen_", StringComparison.Ordinal)
                || name.StartsWith("göymen_", StringComparison.Ordinal))
            || name.Length is < 33 or > 124
            || name.Any(c => !char.IsAsciiLetterOrDigit(c) && c is not '_' and not 'ö')
            || !Guid.TryParseExact(name[^32..], "N", out _))
            throw new InvalidOperationException("Only unique synthetic test database names are allowed.");

        Name = name;
        var connection = new SqlConnectionStringBuilder(baseConnectionString)
        {
            InitialCatalog = name,
            AttachDBFilename = string.Empty,
        };
        ConnectionString = connection.ConnectionString;
        connection.InitialCatalog = "master";
        masterConnectionString = connection.ConnectionString;
        if (!string.IsNullOrWhiteSpace(localDataDirectory))
        {
            if (connection.DataSource is not ("localhost" or "." or "127.0.0.1" or "(local)")
                || !Path.IsPathFullyQualified(localDataDirectory) || !Directory.Exists(localDataDirectory))
                throw new InvalidOperationException("Local test storage requires a local server and an existing absolute directory.");
            dataFile = Path.Combine(Path.GetFullPath(localDataDirectory), name + ".mdf");
            logFile = Path.Combine(Path.GetFullPath(localDataDirectory), name + "_log.ldf");
            RequireNewFiles();
        }
    }

    public string Name { get; }
    public string ConnectionString { get; }

    internal string ConnectionForHost(string baseConnectionString, string name)
    {
        var expected = new SqlConnectionStringBuilder(baseConnectionString)
        {
            InitialCatalog = name,
            AttachDBFilename = string.Empty,
        };
        // ConnectionString is already serialized. Parse both sides before comparing:
        // parsing drops an explicitly empty AttachDBFilename, while EquivalentTo also
        // compares the number of stored keys. Compare every remaining setting unchanged.
        var requested = new SqlConnectionStringBuilder(expected.ConnectionString);
        if (name != Name || !requested.EquivalentTo(new SqlConnectionStringBuilder(ConnectionString)))
            throw new InvalidOperationException("A test database cannot be borrowed by a different host binding.");
        return ConnectionString;
    }

    internal string CreateSql => dataFile is null
        ? $"CREATE DATABASE [{Name}];"
        : $"CREATE DATABASE [{Name}] ON PRIMARY (NAME=N'{Name}', FILENAME={Literal(dataFile)}) "
            + $"LOG ON (NAME=N'{Name}_log', FILENAME={Literal(logFile!)});";

    public async Task CreateAsync(bool rcsiEnabled)
    {
        if (creationAttempted) throw new InvalidOperationException("A test database is created at most once.");
        creationAttempted = true;
        RequireNewFiles();
        await using var connection = new SqlConnection(masterConnectionString);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        // No IF NOT EXISTS: a collision must fail, and cleanup must not claim someone else's catalog.
        command.CommandText = CreateSql;
        await command.ExecuteNonQueryAsync();
        created = true;
        command.CommandText = $"ALTER DATABASE [{Name}] SET READ_COMMITTED_SNAPSHOT "
            + (rcsiEnabled ? "ON" : "OFF") + " WITH ROLLBACK IMMEDIATE;";
        await command.ExecuteNonQueryAsync();
    }

    public async Task DropAsync()
    {
        if (!created) return;
        await using var connection = new SqlConnection(masterConnectionString);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = $"IF DB_ID(@name) IS NOT NULL BEGIN ALTER DATABASE [{Name}] "
            + $"SET SINGLE_USER WITH ROLLBACK IMMEDIATE; DROP DATABASE [{Name}]; END;";
        command.Parameters.AddWithValue("@name", Name);
        await command.ExecuteNonQueryAsync();
        created = false;
        using var poolKey = new SqlConnection(ConnectionString);
        SqlConnection.ClearPool(poolKey);
    }

    private void RequireNewFiles()
    {
        if (dataFile is not null && (File.Exists(dataFile) || File.Exists(logFile)))
            throw new InvalidOperationException("Local SQL test files must not already exist.");
    }

    private static string Literal(string value) => "N'" + value.Replace("'", "''", StringComparison.Ordinal) + "'";
}
