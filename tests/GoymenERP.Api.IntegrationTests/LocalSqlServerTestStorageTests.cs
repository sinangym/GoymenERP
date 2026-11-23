using Microsoft.Data.SqlClient;

namespace GoymenERP.Api.IntegrationTests;

public sealed class LocalSqlServerTestStorageTests
{
    [Theory]
    [InlineData("True", "Mandatory")]
    [InlineData("False", "Optional")]
    [InlineData("Strict", "Strict")]
    public void Host_binding_accepts_round_tripped_empty_fields_and_preserves_the_encryption_mode(
        string encrypt, string expectedEncrypt)
    {
        var name = "goymen_storage_" + Guid.NewGuid().ToString("N");
        var original = $"Server=example.invalid;Database=master;Integrated Security=True;Encrypt={encrypt};"
            + "TrustServerCertificate=False;Application Name=SENTETIK;AttachDBFilename=;Workstation ID=";
        var owned = new SqlServerTestDatabase(original, name, localDataDirectory: null);
        var serialized = new SqlConnectionStringBuilder(original).ConnectionString;
        var reordered = $"App=SENTETIK;TrustServerCertificate=False;Encrypt={expectedEncrypt};"
            + "Trusted_Connection=True;Initial Catalog=master;Data Source=example.invalid";

        foreach (var binding in new[] { original, serialized, reordered, owned.ConnectionString })
        {
            var connectionString = owned.ConnectionForHost(binding, name);
            Assert.Equal(owned.ConnectionString, connectionString);
            var reopened = new SqlConnectionStringBuilder(connectionString);
            Assert.Equal(name, reopened.InitialCatalog);
            Assert.Empty(reopened.AttachDBFilename);
            Assert.Equal(SqlConnectionEncryptOption.Parse(expectedEncrypt), reopened.Encrypt);
            Assert.True(reopened.IntegratedSecurity);
            Assert.False(reopened.TrustServerCertificate);
        }
    }

    [Fact]
    public void Host_binding_rejects_changed_endpoint_identity_TLS_and_connection_behavior()
    {
        const string original = "Server=example.invalid;Database=master;Integrated Security=False;"
            + "User ID=SENTETIK_USER;Password=SENTETIK_PASSWORD;Encrypt=Mandatory;TrustServerCertificate=False";
        var name = "goymen_storage_" + Guid.NewGuid().ToString("N");
        var owned = new SqlServerTestDatabase(original, name, localDataDirectory: null);
        Assert.Equal(owned.ConnectionString, owned.ConnectionForHost(original, name));
        var changes = new Dictionary<string, object>
        {
            ["Data Source"] = "other.invalid",
            ["Integrated Security"] = true,
            ["User ID"] = "OTHER_SENTETIK_USER",
            ["Password"] = "OTHER_SENTETIK_PASSWORD",
            ["Encrypt"] = "Optional",
            ["TrustServerCertificate"] = true,
            ["Host Name In Certificate"] = "other.invalid",
            ["Application Intent"] = "ReadOnly",
            ["Pooling"] = false,
        };
        foreach (var (key, value) in changes)
        {
            var changed = new SqlConnectionStringBuilder(original) { [key] = value };
            Assert.Throws<InvalidOperationException>(() => owned.ConnectionForHost(changed.ConnectionString, name));
        }
        Assert.Throws<InvalidOperationException>(() => owned.ConnectionForHost(original,
            "goymen_storage_" + Guid.NewGuid().ToString("N")));
        Assert.Equal(owned.ConnectionString, owned.ConnectionForHost(original, name));
    }

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
