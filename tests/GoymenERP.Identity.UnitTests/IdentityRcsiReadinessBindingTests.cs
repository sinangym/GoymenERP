using GoymenERP.BuildingBlocks.SqlServer;
using GoymenERP.Modules.Identity.Infrastructure;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace GoymenERP.Identity.UnitTests;

public sealed class IdentityRcsiReadinessBindingTests
{
    [Fact]
    public void StartupBinding_IncludesConfiguredPrimaryAndFailoverLogicalEndpoints()
    {
        const string primary = "tcp:primary-sql.example,1433";
        const string failover = "tcp:failover-sql.example,1433";
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["DB_PROVIDER"] = "SqlServer",
                ["DB_CONNECTION_STRING"] =
                    $"Server={primary};Failover Partner={failover};"
                    + "Initial Catalog=GoymenErp;User Id=probe;Password=not-used;"
                    + "Encrypt=True;TrustServerCertificate=False",
            })
            .Build();
        var services = new ServiceCollection();

        services.AddIdentityModule(configuration);

        using var provider = services.BuildServiceProvider();
        var binding = provider.GetRequiredService<SqlServerRcsiReadinessBinding>();
        Assert.Equal(
            new[]
            {
                SqlServerLogicalEndpointFingerprint.ComputeSha256(failover),
                SqlServerLogicalEndpointFingerprint.ComputeSha256(primary),
            }.Order(StringComparer.Ordinal),
            binding.ApprovedLogicalEndpointSha256);
    }
}
