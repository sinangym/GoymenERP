using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using GoymenERP.BuildingBlocks.Security;
using GoymenERP.Modules.Counterparty.Application.Contracts;
using GoymenERP.Modules.Counterparty.Infrastructure.Persistence.Migrations;
using GoymenERP.Modules.Identity.Infrastructure.Seeding;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore.Migrations.Operations;
using Microsoft.Extensions.DependencyInjection;

namespace GoymenERP.Api.IntegrationTests;

/// <summary>
/// WP14 (YOL-D31) real-SQL evidence for the party card's group-company link through the real API: the permission fence, the
/// PARTY version, Idempotency-Key replay, an unknown company refused, one audit event per real change, the owner read port and
/// the forward-fix migration guard (52271). The link permission is granted only on the disposable database (SENTETIK).
/// </summary>
public sealed class CounterpartyGroupCompanyLinkSqlServerRelationalAcceptanceTests
{
    [SqlServerFact]
    public async Task Group_company_link_is_versioned_replayed_audited_listed_and_guards_its_Down_on_real_sql()
    {
        var baseConnection = Environment.GetEnvironmentVariable(SqlServerRelationalAcceptanceTests.ConnectionStringEnvironmentVariable)!;
        await using var factory = new SqlServerRelationalApiFactory(baseConnection, services =>
            services.PostConfigure<IdentitySeedOptions>(options =>
            {
                var administrator = options.Roles.Single(role => role.Code == "SYSTEM_ADMINISTRATOR");
                administrator.Permissions.Add(ErpPermissions.CounterpartyPartiesGroupCompanyLinkUpdate);
            }));
        try
        {
            using var client = factory.CreateClientFor(TestUsers.GroupAdmin, TestCompanyIds.CompanyA);
            NewKey(client);
            using (var created = await client.PostAsJsonAsync("/api/v1/counterparties", new { kind = 1, legalName = "SENTETIK Grup Şirketi", roles = 1 }))
            {
                Assert.Equal(HttpStatusCode.Created, created.StatusCode);
            }

            var party = (await ListPartyIdAsync(factory)) ?? throw new Xunit.Sdk.XunitException("party not stored");
            var path = $"/api/v1/counterparties/{party}/group-company-link";
            var before = await ReadAsync(client, path);
            Assert.Equal(JsonValueKind.Null, before.GetProperty("groupCompanyId").ValueKind);
            Assert.True(before.GetProperty("viewer").GetProperty("canUpdateGroupCompanyLink").GetBoolean());
            var version = before.GetProperty("version").GetString()!;

            NewKey(client);
            using (var unknown = await PutAsync(client, path, new { expectedVersion = version, groupCompanyId = Guid.NewGuid() }))
            {
                Assert.Equal(HttpStatusCode.BadRequest, unknown.StatusCode);
            }

            NewKey(client);
            var body = new { expectedVersion = version, groupCompanyId = TestCompanyIds.CompanyB };
            using (var linked = await PutAsync(client, path, body))
            {
                Assert.Equal(HttpStatusCode.NoContent, linked.StatusCode);
            }

            using (var replay = await PutAsync(client, path, body))
            {
                Assert.Equal(HttpStatusCode.NoContent, replay.StatusCode);
                Assert.True(replay.Headers.Contains("Idempotency-Replayed"));
            }

            NewKey(client);
            using (var stale = await PutAsync(client, path, body))
            {
                Assert.Equal(HttpStatusCode.Conflict, stale.StatusCode);
            }

            var after = await ReadAsync(client, path);
            Assert.Equal(TestCompanyIds.CompanyB, after.GetProperty("groupCompanyId").GetGuid());
            Assert.Equal(1, await ScalarAsync<int>(factory,
                "SELECT COUNT(*) FROM [audit].[AuditEvents] WHERE [EventType] = N'counterparty.party.group-company-link.changed.v1' AND [EntityId] = @party "
                + "AND [PermissionCode] = N'counterparty.parties.group-company-link.update'", party.ToString("D")));
            await using (var scope = factory.Services.CreateAsyncScope())
            {
                scope.ServiceProvider.GetRequiredService<GoymenERP.BuildingBlocks.Multitenancy.ICompanyContext>().Set(TestCompanyIds.CompanyA);
                var links = await scope.ServiceProvider.GetRequiredService<ICounterpartyGroupCompanyLinkReadPortV1>().ListLinksAsync();
                Assert.Contains(links, link => link.PartyId == party && link.GroupCompanyId == TestCompanyIds.CompanyB);
            }

            var guard = Assert.IsType<SqlOperation>(new AddPartyGroupCompanyLink().DownOperations[0]).Sql;
            var refusal = await Assert.ThrowsAsync<SqlException>(() => ExecAsync(factory, guard));
            Assert.Equal(52271, refusal.Number);

            NewKey(client);
            using (var cleared = await PutAsync(client, path, new { expectedVersion = after.GetProperty("version").GetString(), groupCompanyId = (Guid?)null }))
            {
                Assert.Equal(HttpStatusCode.NoContent, cleared.StatusCode);
            }

            await ExecAsync(factory, guard);
        }
        finally
        {
            await using var connection = new SqlConnection(new SqlConnectionStringBuilder(baseConnection) { InitialCatalog = "master" }.ConnectionString);
            await connection.OpenAsync();
            await using var command = connection.CreateCommand();
            command.CommandText = $"IF DB_ID(N'{factory.DatabaseName}') IS NOT NULL BEGIN ALTER DATABASE [{factory.DatabaseName}] SET SINGLE_USER WITH ROLLBACK IMMEDIATE; DROP DATABASE [{factory.DatabaseName}]; END;";
            await command.ExecuteNonQueryAsync();
        }
    }

    private static async Task<Guid?> ListPartyIdAsync(SqlServerRelationalApiFactory factory)
    {
        await using var connection = new SqlConnection(factory.DatabaseConnectionString);
        await connection.OpenAsync();
        await using var command = new SqlCommand("SELECT TOP 1 [Id] FROM [counterparty].[Parties] WHERE [LegalName] = N'SENTETIK Grup Şirketi'", connection);
        return await command.ExecuteScalarAsync() as Guid?;
    }

    private static async Task<T> ScalarAsync<T>(SqlServerRelationalApiFactory factory, string sql, string party)
    {
        await using var connection = new SqlConnection(factory.DatabaseConnectionString);
        await connection.OpenAsync();
        await using var command = new SqlCommand(sql, connection);
        command.Parameters.AddWithValue("@party", party);
        return Assert.IsType<T>(await command.ExecuteScalarAsync());
    }

    private static async Task ExecAsync(SqlServerRelationalApiFactory factory, string sql)
    {
        await using var connection = new SqlConnection(factory.DatabaseConnectionString);
        await connection.OpenAsync();
        await using var command = new SqlCommand(sql, connection);
        await command.ExecuteNonQueryAsync();
    }

    private static async Task<JsonElement> ReadAsync(HttpClient client, string path)
    {
        using var response = await client.GetAsync(path);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return document.RootElement.Clone();
    }

    private static Task<HttpResponseMessage> PutAsync(HttpClient client, string path, object body) =>
        client.PutAsync(path, new StringContent(JsonSerializer.Serialize(body, new JsonSerializerOptions(JsonSerializerDefaults.Web)), Encoding.UTF8,
            "application/json"));

    private static void NewKey(HttpClient client)
    {
        client.DefaultRequestHeaders.Remove("Idempotency-Key");
        client.DefaultRequestHeaders.Add("Idempotency-Key", Guid.NewGuid().ToString("N"));
        client.DefaultRequestHeaders.Remove("X-Correlation-Id");
        client.DefaultRequestHeaders.Add("X-Correlation-Id", Guid.NewGuid().ToString("N"));
    }
}
