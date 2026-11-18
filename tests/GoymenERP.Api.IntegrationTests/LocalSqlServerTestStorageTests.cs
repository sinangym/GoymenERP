using Microsoft.Data.SqlClient;

namespace GoymenERP.Api.IntegrationTests;

public sealed class LocalSqlServerTestStorageTests
{
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
