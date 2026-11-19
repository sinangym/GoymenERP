using Microsoft.Data.SqlClient;

namespace GoymenERP.Api.IntegrationTests;

public sealed class LocalSqlServerTestStorageTests
{
    [Fact]
    public void Manual_creation_keeps_catalog_connections_and_escapes_file_paths_without_adopting_existing_files()
    {
        var directory = Directory.CreateTempSubdirectory("goymen-'storage-");
        try
        {
            const string connection = "Server=localhost;Database=master;Integrated Security=True;Encrypt=True";
            var name = "goymen_storage_" + Guid.NewGuid().ToString("N");
            var owned = new SqlServerTestDatabase(connection, name, directory.FullName);
            var reopened = new SqlConnectionStringBuilder(owned.ConnectionForHost(connection, name));
            Assert.Equal(name, reopened.InitialCatalog);
            Assert.Empty(reopened.AttachDBFilename);
            Assert.Equal(SqlConnectionEncryptOption.Mandatory, reopened.Encrypt);
            Assert.False(reopened.TrustServerCertificate);
            Assert.Contains("goymen-''storage-", owned.CreateSql, StringComparison.Ordinal);
            Assert.Throws<InvalidOperationException>(() => owned.ConnectionForHost(connection, "goymen_storage_" + Guid.NewGuid().ToString("N")));
            Assert.Throws<InvalidOperationException>(() => owned.ConnectionForHost("Server=example.invalid;Integrated Security=True", name));
            File.WriteAllText(Path.Combine(directory.FullName, name + ".mdf"), "SENTETIK existing file");
            Assert.Throws<InvalidOperationException>(() => new SqlServerTestDatabase(connection, name, directory.FullName));
            // The original owner does not request attach/create again when it opens a second host.
            Assert.Equal(owned.ConnectionString, owned.ConnectionForHost(connection, name));
            Assert.Throws<InvalidOperationException>(() => new SqlServerTestDatabase("Server=example.invalid", name, directory.FullName));
            Assert.Throws<InvalidOperationException>(() => new SqlServerTestDatabase(connection, name, "relative"));
            Assert.Throws<InvalidOperationException>(() => new SqlServerTestDatabase(connection, "master", null));
            var ci = new SqlServerTestDatabase("Server=example.invalid;Encrypt=True", name, null);
            Assert.Equal($"CREATE DATABASE [{name}];", ci.CreateSql);
        }
        finally { directory.Delete(recursive: true); }
    }

    [Fact]
    public void Unconfigured_local_storage_preserves_the_existing_SQL_connection_contract()
    {
        var result = new SqlConnectionStringBuilder(SqlServerRelationalApiFactory.BuildDatabaseConnectionString(
            "Server=localhost;Database=master;Integrated Security=True;Encrypt=True", "goymen_erp_ci_" + Guid.NewGuid().ToString("N"), null));
        Assert.Equal(string.Empty, result.AttachDBFilename);
        Assert.True(result.IntegratedSecurity);
        Assert.Equal(SqlConnectionEncryptOption.Mandatory, result.Encrypt);
        Assert.False(result.TrustServerCertificate);
    }

    [Fact]
    public void Local_storage_selects_a_unique_file_without_creating_a_database_or_changing_TLS()
    {
        var name = "goymen_erp_ci_" + Guid.NewGuid().ToString("N");
        var root = Path.GetFullPath(Path.GetTempPath());
        var result = new SqlConnectionStringBuilder(SqlServerRelationalApiFactory.BuildDatabaseConnectionString(
            "Server=localhost;Database=master;Integrated Security=True;Encrypt=True", name, root));
        Assert.Equal(name, result.InitialCatalog);
        Assert.Equal(Path.Combine(root, name + ".mdf"), result.AttachDBFilename);
        Assert.False(File.Exists(result.AttachDBFilename));
        Assert.Equal(SqlConnectionEncryptOption.Mandatory, result.Encrypt);
        Assert.False(result.TrustServerCertificate);
        var legacyName = "GoymenERP_Identity_UserDirectory_" + Guid.NewGuid().ToString("N");
        var legacy = new SqlConnectionStringBuilder(SqlServerRelationalApiFactory.BuildDatabaseConnectionString(
            "Server=localhost;Database=master;Integrated Security=True;Encrypt=True", legacyName, root));
        Assert.Equal(legacyName, legacy.InitialCatalog);
        Assert.Equal(Path.Combine(root, legacyName + ".mdf"), legacy.AttachDBFilename);
        Assert.False(File.Exists(legacy.AttachDBFilename));
    }

    [Fact]
    public void Local_storage_refuses_remote_servers_relative_directories_and_non_synthetic_database_names()
    {
        var name = "goymen_erp_ci_" + Guid.NewGuid().ToString("N");
        var root = Path.GetFullPath(Path.GetTempPath());
        Assert.Throws<InvalidOperationException>(() => SqlServerRelationalApiFactory.BuildDatabaseConnectionString(
            "Server=example.invalid;Integrated Security=True", name, root));
        Assert.Throws<InvalidOperationException>(() => SqlServerRelationalApiFactory.BuildDatabaseConnectionString(
            "Server=localhost;Integrated Security=True", name, "relative-tests"));
        Assert.Throws<InvalidOperationException>(() => SqlServerRelationalApiFactory.BuildDatabaseConnectionString(
            "Server=localhost;Integrated Security=True", "master", root));
        Assert.Throws<InvalidOperationException>(() => SqlServerRelationalApiFactory.BuildDatabaseConnectionString(
            "Server=localhost;Integrated Security=True", "goymen_../" + Guid.NewGuid().ToString("N"), root));
    }
}
