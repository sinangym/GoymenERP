using System.Net;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Data.SqlClient;

namespace GoymenERP.Api.IntegrationTests;

public sealed partial class AccountingApprovedSourceCatalogSqlServerRelationalAcceptanceTests
{
    [SqlServerFact]
    public async Task Provider_constraint_failure_is_unavailable_without_consuming_catalog_prepare_or_activation_keys()
    {
        await using var run = await Run.StartAsync();
        await run.GrantAsync(Preparer, CompanyA, PreparePermission, ActivatePermission);
        using var client = run.Client(Preparer, CompanyA);
        var package = Package(Guid.NewGuid(), "SENTETIK-provider-recovery");
        var prepareKey = Key("provider-prepare");
        var before = await run.CountsAsync(package.Id);
        await Execute("ALTER TABLE accounting.ApprovedSourceCatalogs ADD CONSTRAINT CK_SYNTHETIC_CatalogProviderFailure CHECK ([Status] <> 1)");
        try
        {
            using var failed = await PrepareAsync(client, package, prepareKey);
            Assert.Equal(HttpStatusCode.ServiceUnavailable, failed.StatusCode);
            Assert.Equal(before, await run.CountsAsync(package.Id));
            Assert.Equal(0, await Commands(prepareKey));
        }
        finally
        {
            await Execute("ALTER TABLE accounting.ApprovedSourceCatalogs DROP CONSTRAINT CK_SYNTHETIC_CatalogProviderFailure");
        }
        using (var recovered = await PrepareAsync(client, package, prepareKey)) Assert.Equal(HttpStatusCode.Created, recovered.StatusCode);
        using (var replay = await PrepareAsync(client, package, prepareKey))
        {
            Assert.Equal(HttpStatusCode.Created, replay.StatusCode);
            Assert.True(IsReplay(replay));
        }
        Assert.Equal(1, await Commands(prepareKey));

        var activationKey = Key("provider-activate");
        before = await run.CountsAsync(package.Id);
        await Execute("ALTER TABLE accounting.ApprovedSourceCatalogs ADD CONSTRAINT CK_SYNTHETIC_CatalogProviderFailure CHECK ([Status] <> 2)");
        try
        {
            using var failed = await ActivateAsync(client, package.Id, 1, 0, activationKey);
            Assert.Equal(HttpStatusCode.ServiceUnavailable, failed.StatusCode);
            Assert.Equal(before, await run.CountsAsync(package.Id));
            Assert.Equal(0, await Commands(activationKey));
            Assert.Equal(1, (await run.CatalogAsync(package.Id)).Status);
        }
        finally
        {
            await Execute("ALTER TABLE accounting.ApprovedSourceCatalogs DROP CONSTRAINT CK_SYNTHETIC_CatalogProviderFailure");
        }
        using (var recovered = await ActivateAsync(client, package.Id, 1, 0, activationKey)) Assert.Equal(HttpStatusCode.OK, recovered.StatusCode);
        using (var replay = await ActivateAsync(client, package.Id, 1, 0, activationKey))
        {
            Assert.Equal(HttpStatusCode.OK, replay.StatusCode);
            Assert.True(IsReplay(replay));
        }
        Assert.Equal(1, await Commands(activationKey));

        Task<int> Commands(string key) => run.ScalarAsync<int>(
            "SELECT COUNT(*) FROM accounting.Commands WHERE CompanyId = @company AND IdempotencyKeyHash = @key",
            ("@company", CompanyA), ("@key", Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(key)))));
        async Task Execute(string sql)
        {
            await using var connection = new SqlConnection(run.Factory.DatabaseConnectionString);
            await connection.OpenAsync();
            await using var command = new SqlCommand(sql, connection);
            await command.ExecuteNonQueryAsync();
        }
    }
}
