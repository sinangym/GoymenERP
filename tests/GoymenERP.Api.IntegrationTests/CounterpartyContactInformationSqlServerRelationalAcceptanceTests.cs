using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using GoymenERP.BuildingBlocks.Security;
using GoymenERP.Modules.Counterparty.Application.Contracts;
using GoymenERP.Modules.Counterparty.Infrastructure.Persistence;
using GoymenERP.Modules.Identity.Infrastructure.Seeding;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.DependencyInjection;

namespace GoymenERP.Api.IntegrationTests;

public sealed class CounterpartyContactInformationSqlServerRelationalAcceptanceTests
{
    private const string CommandName = "counterparty.company-account.contact-information.update.v1";
    private const string EventName = "counterparty.company-account.contact-information.changed.v1";

    [SqlServerFact]
    public async Task AccountContactCard_RoundTripsExplicitChildrenWithoutChangingPartyOrOtherCompany()
    {
        await using var test = new ContactTest();
        using var a = test.Client();
        var party = await CreateAsync(a);
        using var b = test.Client(TestCompanyIds.CompanyB);
        using var link = await b.PostAsJsonAsync($"/api/v1/counterparties/{party}/company-account", new { roles = 0 });
        Assert.Equal(HttpStatusCode.Created, link.StatusCode);
        var path = PathFor(party);
        var beforeA = await ReadAsync(a, path);
        var beforeB = await ReadAsync(b, path);
        var beforeParty = await test.ScalarAsync<string>(
            "SELECT (SELECT * FROM [counterparty].[Parties] WHERE [Id]=@Party FOR JSON PATH);", party);
        Assert.Equal("1", beforeA.GetProperty("version").GetString());
        Assert.Empty(beforeA.GetProperty("information").GetProperty("addresses").EnumerateArray());
        var firstAddress = Guid.NewGuid();
        var secondAddress = Guid.NewGuid();
        var contact = Guid.NewGuid();
        var phone = Guid.NewGuid();
        var email = Guid.NewGuid();
        var body = $$$"""
            {"expectedVersion":"1","changes":{"phone":"+90 0328 000 00 00",
             "addresses":[
               {"id":"{{{firstAddress}}}","operation":"upsert","purpose":"delivery","countryCode":"tr","line1":"First"},
               {"id":"{{{secondAddress}}}","operation":"upsert","purpose":"operational","countryCode":"de","line1":"Second"}],
             "contacts":[{"id":"{{{contact}}}","operation":"upsert","displayName":"Contact","purpose":"Operations",
               "channels":[{"id":"{{{phone}}}","operation":"upsert","kind":"phone","value":"00123","extension":"001"},
                           {"id":"{{{email}}}","operation":"upsert","kind":"email","value":"contact@example.test"}]}]}}
            """;
        NewKey(a);
        using var saved = await PutAsync(a, path, body);
        Assert.Equal(HttpStatusCode.NoContent, saved.StatusCode);
        using var replay = await PutAsync(a, path, body);
        Assert.Equal(HttpStatusCode.NoContent, replay.StatusCode);
        Assert.True(replay.Headers.Contains("Idempotency-Replayed"));
        var fresh = await ReadAsync(a, path);
        Assert.Equal("2", fresh.GetProperty("version").GetString());
        Assert.Equal(2, fresh.GetProperty("information").GetProperty("addresses").GetArrayLength());
        var channels = fresh.GetProperty("information").GetProperty("contacts")[0].GetProperty("channels");
        Assert.Equal(2, channels.GetArrayLength());
        Assert.Contains(channels.EnumerateArray(), channel => channel.GetProperty("value").GetString() == "00123");
        Assert.Equal(beforeB.GetRawText(), (await ReadAsync(b, path)).GetRawText());
        Assert.Equal(beforeParty, await test.ScalarAsync<string>(
            "SELECT (SELECT * FROM [counterparty].[Parties] WHERE [Id]=@Party FOR JSON PATH);", party));
        Assert.Equal(1, await test.ScalarAsync<int>(
            "SELECT COUNT(*) FROM [audit].[AuditEvents] WHERE [EventType]=@Event;", party));
        Assert.Equal(1, await test.ScalarAsync<int>(
            "SELECT COUNT(*) FROM [idempotency].[CommandExecutions] WHERE [CommandName]=@Command;", party));
        NewKey(a);
        using var update = await PutAsync(a, path, $$$"""
            {"expectedVersion":"2","changes":{"phone":null,
             "addresses":[{"id":"{{{firstAddress}}}","operation":"deactivate"}]}}
            """);
        Assert.Equal(HttpStatusCode.NoContent, update.StatusCode);
        using var reopened = test.Client();
        var after = await ReadAsync(reopened, path);
        Assert.Equal("3", after.GetProperty("version").GetString());
        Assert.Equal(JsonValueKind.Null, after.GetProperty("information").GetProperty("phone").ValueKind);
        Assert.Equal(2, after.GetProperty("information").GetProperty("addresses").GetArrayLength());
        Assert.Single(after.GetProperty("information").GetProperty("addresses").EnumerateArray(),
            address => !address.GetProperty("isActive").GetBoolean());
        Assert.Single(after.GetProperty("information").GetProperty("contacts").EnumerateArray());
        var detail = await ReadAsync(a, $"/api/v1/counterparties/{party}");
        Assert.Contains(detail.GetProperty("companyAccounts").EnumerateArray(),
            account => account.GetProperty("companyId").GetGuid() == TestCompanyIds.CompanyA
                && account.GetProperty("version").GetString() == "3"
                && account.GetProperty("roles").GetInt32() == 0);
        var list = await ReadAsync(a, "/api/v1/counterparties");
        Assert.Contains(list.GetProperty("items").EnumerateArray(),
            item => item.GetProperty("id").GetGuid() == party);
        var history = await ReadAsync(a,
            $"/api/v1/audit/events?entityType=Counterparty.CompanyPartyAccount&entityId={after.GetProperty("accountId").GetGuid():D}");
        Assert.Equal(2, history.GetProperty("items").EnumerateArray()
            .Count(item => item.GetProperty("eventType").GetString() == EventName));
    }

    [SqlServerFact]
    public async Task ContactCard_AuthorizationNoDefaultGrantAndMissingRelationAreNonDisclosing()
    {
        await using (var defaults = new ContactTest(grantBasic: false))
        {
            using var reader = defaults.Client();
            var id = await CreateAsync(reader);
            using var denied = await PutAsync(reader, PathFor(id), """{"expectedVersion":"1","changes":{"phone":"001"}}""");
            Assert.Equal(HttpStatusCode.Forbidden, denied.StatusCode);
            Assert.Equal("1", (await ReadAsync(reader, PathFor(id))).GetProperty("version").GetString());
        }

        await using var test = new ContactTest();
        using var a = test.Client();
        var party = await CreateAsync(a);
        var path = PathFor(party);
        using var b = test.Client(TestCompanyIds.CompanyB);
        using var missing = await b.GetAsync(path);
        Assert.Equal(HttpStatusCode.NotFound, missing.StatusCode);
        using var missingWrite = await PutAsync(b, path, """{"expectedVersion":"1","changes":{"phone":"001"}}""");
        Assert.Equal(HttpStatusCode.NotFound, missingWrite.StatusCode);
        using var narrow = test.Client(TestCompanyIds.CompanyA, TestUsers.CompanyAViewer);
        using var deniedRead = await narrow.GetAsync(path);
        using var deniedWrite = await PutAsync(narrow, path, """{"expectedVersion":"1","changes":{}}""");
        Assert.Equal(HttpStatusCode.Forbidden, deniedRead.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, deniedWrite.StatusCode);
        using var foreign = test.Client(Guid.Parse("6f000000-0000-0000-0000-000000000099"));
        using var foreignRead = await foreign.GetAsync(path);
        Assert.Equal(HttpStatusCode.Forbidden, foreignRead.StatusCode);
        a.DefaultRequestHeaders.Remove("X-ERP-Counterparty-Contract");
        using var oldClient = await PutAsync(a, path, """{"expectedVersion":"1","changes":{"phone":"001"}}""");
        Assert.Equal(HttpStatusCode.Conflict, oldClient.StatusCode);
        Assert.Equal("client_contract_required", (await JsonAsync(oldClient)).GetProperty("code").GetString());
        Assert.Equal(1, await test.ScalarAsync<int>(
            "SELECT COUNT(*) FROM [counterparty].[CompanyPartyAccounts] WHERE [PartyId]=@Party;", party));
        Assert.Equal(0, await test.ScalarAsync<int>(
            "SELECT COUNT(*) FROM [audit].[AuditEvents] WHERE [EventType]=@Event;", party));
    }

    [SqlServerFact]
    public async Task ContactCard_RejectsMalformedChangesAndPreservesExactInt64Versions()
    {
        await using var test = new ContactTest();
        using var client = test.Client();
        var party = await CreateAsync(client);
        var path = PathFor(party);
        var bodies = new[]
        {
            """{"changes":{}}""",
            """{"expectedVersion":1,"changes":{}}""",
            """{"expectedVersion":null,"changes":{}}""",
            """{"expectedVersion":"0","changes":{}}""",
            """{"expectedVersion":"01","changes":{}}""",
            """{"expectedVersion":"9223372036854775808","changes":{}}""",
            """{"expectedVersion":"1","changes":null}""",
            """{"expectedVersion":"1","changes":{"email":"not-an-email"}}""",
            """{"expectedVersion":"1","changes":{"addresses":null}}""",
            """{"expectedVersion":"1","changes":{"legalName":"forbidden"}}""",
            """{"expectedVersion":"1","changes":{"phone":123}}""",
            """{"expectedVersion":"1","changes":{"contacts":[null]}}""",
            $$$"""{"expectedVersion":"1","companyId":"{{{TestCompanyIds.CompanyB}}}","changes":{}}""",
        };
        foreach (var body in bodies)
        {
            NewKey(client);
            using var response = await PutAsync(client, path, body);
            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        }

        Assert.Equal(0, await test.ScalarAsync<int>(
            "SELECT COUNT(*) FROM [idempotency].[CommandExecutions] WHERE [CommandName]=@Command;", party));
        Assert.Equal(0, await test.ScalarAsync<int>(
            "SELECT COUNT(*) FROM [audit].[AuditEvents] WHERE [EventType]=@Event;", party));
        await test.ExecuteAsync(
            "UPDATE [counterparty].[CompanyPartyAccounts] SET [Version]=9007199254740993 WHERE [PartyId]=@Party;", party);
        Assert.Equal("9007199254740993", (await ReadAsync(client, path)).GetProperty("version").GetString());
        NewKey(client);
        using var exact = await PutAsync(client, path,
            """{"expectedVersion":"9007199254740993","changes":{"phone":"001"}}""");
        Assert.Equal(HttpStatusCode.NoContent, exact.StatusCode);
        Assert.Equal("9007199254740994", (await ReadAsync(client, path)).GetProperty("version").GetString());
        NewKey(client);
        using var stale = await PutAsync(client, path,
            """{"expectedVersion":"9007199254740993","changes":{"phone":"002"}}""");
        Assert.Equal(HttpStatusCode.Conflict, stale.StatusCode);
        var problem = await JsonAsync(stale);
        Assert.Equal("version_conflict", problem.GetProperty("code").GetString());
        Assert.False(problem.TryGetProperty("conflict", out _));
    }

    [SqlServerFact]
    public async Task ContactCard_ConcurrentCommandsHaveOneEffectAndDurableTerminalReplay()
    {
        await using var test = new ContactTest();
        using var a = test.Client();
        using var b = test.Client();
        var party = await CreateAsync(a);
        var path = PathFor(party);
        var body = """{"expectedVersion":"1","changes":{"phone":"001"}}""";
        NewKey(a);
        var responses = await Task.WhenAll(PutAsync(a, path, body), PutAsync(b, path, body));
        try
        {
            Assert.Single(responses, response => response.StatusCode == HttpStatusCode.NoContent);
            Assert.Single(responses, response => response.StatusCode == HttpStatusCode.Conflict);
            using var replayA = await PutAsync(a, path, body);
            using var replayB = await PutAsync(b, path, body);
            Assert.Equal(responses[0].StatusCode, replayA.StatusCode);
            Assert.Equal(responses[1].StatusCode, replayB.StatusCode);
            Assert.True(replayA.Headers.Contains("Idempotency-Replayed"));
            Assert.True(replayB.Headers.Contains("Idempotency-Replayed"));
        }
        finally
        {
            foreach (var response in responses) response.Dispose();
        }

        var key = Guid.NewGuid().ToString("N");
        NewKey(a, key);
        NewKey(b, key);
        var secondBody = """{"expectedVersion":"2","changes":{"email":"a@example.test"}}""";
        var sameKey = await Task.WhenAll(PutAsync(a, path, secondBody), PutAsync(b, path, secondBody));
        try
        {
            Assert.All(sameKey, response => Assert.Equal(HttpStatusCode.NoContent, response.StatusCode));
            Assert.Single(sameKey, response => response.Headers.Contains("Idempotency-Replayed"));
        }
        finally
        {
            foreach (var response in sameKey) response.Dispose();
        }

        using var mismatch = await PutAsync(a, path, """{"expectedVersion":"2","changes":{"email":"b@example.test"}}""");
        Assert.Equal(HttpStatusCode.Conflict, mismatch.StatusCode);
        Assert.Equal("idempotency_conflict", (await JsonAsync(mismatch)).GetProperty("code").GetString());
        Assert.Equal("3", (await ReadAsync(a, path)).GetProperty("version").GetString());
        Assert.Equal(2, await test.ScalarAsync<int>(
            "SELECT COUNT(*) FROM [audit].[AuditEvents] WHERE [EventType]=@Event;", party));
        Assert.Equal(3, await test.ScalarAsync<int>(
            "SELECT COUNT(*) FROM [idempotency].[CommandExecutions] WHERE [CommandName]=@Command;", party));
    }

    [SqlServerFact]
    public async Task ContactCard_AuditAndIdempotencyFailuresRollBackAndAllowSameKeyRetry()
    {
        await using var test = new ContactTest();
        using var client = test.Client();
        var party = await CreateAsync(client);
        var version = 1;
        foreach (var (schema, table) in new[] { ("audit", "AuditEvents"), ("idempotency", "CommandExecutions") })
        {
            var body = $$$"""{"expectedVersion":"{{{version}}}","changes":{"phone":"00{{{version}}}"}}""";
            NewKey(client);
            var before = (await ReadAsync(client, PathFor(party))).GetRawText();
            await test.ExecuteAsync($"""
                CREATE TRIGGER [{schema}].[TR_ContactCard_RejectInsertProbe] ON [{schema}].[{table}]
                AFTER INSERT AS BEGIN SET NOCOUNT ON; THROW 51029, 'Contact card atomicity probe.', 1; END;
                """, party);
            try
            {
                using var rejected = await PutAsync(client, PathFor(party), body);
                Assert.Equal(HttpStatusCode.InternalServerError, rejected.StatusCode);
            }
            finally
            {
                await test.ExecuteAsync($"DROP TRIGGER [{schema}].[TR_ContactCard_RejectInsertProbe];", party);
            }

            Assert.Equal(before, (await ReadAsync(client, PathFor(party))).GetRawText());
            Assert.Equal(version - 1, await test.ScalarAsync<int>(
                "SELECT COUNT(*) FROM [audit].[AuditEvents] WHERE [EventType]=@Event;", party));
            Assert.Equal(version - 1, await test.ScalarAsync<int>(
                "SELECT COUNT(*) FROM [idempotency].[CommandExecutions] WHERE [CommandName]=@Command;", party));
            using var retry = await PutAsync(client, PathFor(party), body);
            Assert.Equal(HttpStatusCode.NoContent, retry.StatusCode);
            Assert.False(retry.Headers.Contains("Idempotency-Replayed"));
            version++;
        }

        Assert.Equal("3", (await ReadAsync(client, PathFor(party))).GetProperty("version").GetString());
    }

    [SqlServerFact]
    public async Task ContactCard_NoOpKeepsLogicalVersionAndAuditWhilePersistingOneReplayResult()
    {
        await using var test = new ContactTest();
        using var client = test.Client();
        var party = await CreateAsync(client);
        var path = PathFor(party);
        NewKey(client);
        using var initial = await PutAsync(client, path, """{"expectedVersion":"1","changes":{}}""");
        Assert.Equal(HttpStatusCode.NoContent, initial.StatusCode);
        using var replay = await PutAsync(client, path, """{"expectedVersion":"1","changes":{}}""");
        Assert.Equal(HttpStatusCode.NoContent, replay.StatusCode);
        Assert.True(replay.Headers.Contains("Idempotency-Replayed"));
        Assert.Equal("1", (await ReadAsync(client, path)).GetProperty("version").GetString());
        Assert.Equal(0, await test.ScalarAsync<int>(
            "SELECT COUNT(*) FROM [audit].[AuditEvents] WHERE [EventType]=@Event;", party));
        Assert.Equal(1, await test.ScalarAsync<int>(
            "SELECT COUNT(*) FROM [idempotency].[CommandExecutions] WHERE [CommandName]=@Command;", party));
        Assert.Equal(0, await test.ScalarAsync<int>(
            "SELECT COUNT(*) FROM [counterparty].[CompanyPartyAccounts] WHERE [PartyId]=@Party AND [ContactInformationJson] IS NOT NULL;", party));
    }

    [SqlServerFact]
    public async Task ContactCard_MigrationChecksJsonAndBlocksLossyDownWithoutChangingExistingIdentity()
    {
        await using var test = new ContactTest();
        using var client = test.Client();
        var party = await CreateAsync(client);
        await using var scope = test.Factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<CounterpartyDbContext>();
        var migrator = db.GetService<IMigrator>();
        await migrator.MigrateAsync("20260910120000_AllowCounterpartyAccountNoRole");
        Assert.Equal(1, await test.ScalarAsync<int>(
            "SELECT COUNT(*) FROM [counterparty].[CompanyPartyAccounts] WHERE [PartyId]=@Party AND [Roles]=0;", party));
        await migrator.MigrateAsync();
        Assert.False(db.Database.HasPendingModelChanges());
        var invalid = await Assert.ThrowsAsync<SqlException>(() => test.ExecuteAsync(
            "UPDATE [counterparty].[CompanyPartyAccounts] SET [ContactInformationJson]=N'not-json' WHERE [PartyId]=@Party;", party));
        Assert.Equal(547, invalid.Number);
        NewKey(client);
        using var update = await PutAsync(client, PathFor(party), """{"expectedVersion":"1","changes":{"phone":"001"}}""");
        Assert.Equal(HttpStatusCode.NoContent, update.StatusCode);
        var before = (await ReadAsync(client, PathFor(party))).GetRawText();
        var down = await Assert.ThrowsAsync<SqlException>(() =>
            migrator.MigrateAsync("20260910120000_AllowCounterpartyAccountNoRole"));
        Assert.Equal(51000, down.Number);
        Assert.Equal(before, (await ReadAsync(client, PathFor(party))).GetRawText());
        await migrator.MigrateAsync();
        Assert.Equal(before, (await ReadAsync(client, PathFor(party))).GetRawText());
    }

    private static string PathFor(Guid party) => $"/api/v1/counterparties/{party}/contact-information";

    private static async Task<Guid> CreateAsync(HttpClient client)
    {
        using var response = await client.PostAsJsonAsync("/api/v1/counterparties",
            new { kind = 1, legalName = $"Contact SQL {Guid.NewGuid():N}", roles = 0 });
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return (await JsonAsync(response)).GetProperty("id").GetGuid();
    }

    private static async Task<JsonElement> ReadAsync(HttpClient client, string path)
    {
        using var response = await client.GetAsync(path);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return await JsonAsync(response);
    }

    private static async Task<JsonElement> JsonAsync(HttpResponseMessage response)
    {
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return document.RootElement.Clone();
    }

    private static Task<HttpResponseMessage> PutAsync(HttpClient client, string path, string body) =>
        client.PutAsync(path, new StringContent(body, Encoding.UTF8, "application/json"));

    private static void NewKey(HttpClient client, string? key = null)
    {
        client.DefaultRequestHeaders.Remove("Idempotency-Key");
        client.DefaultRequestHeaders.Add("Idempotency-Key", key ?? Guid.NewGuid().ToString("N"));
        client.DefaultRequestHeaders.Remove("X-Correlation-Id");
        client.DefaultRequestHeaders.Add("X-Correlation-Id", Guid.NewGuid().ToString("N"));
    }

    // Uses the existing isolated SQL factory. No production seed or provider changes.
    private sealed class ContactTest : IAsyncDisposable
    {
        private readonly string _baseConnection = Environment.GetEnvironmentVariable(
            SqlServerRelationalAcceptanceTests.ConnectionStringEnvironmentVariable)!;

        public ContactTest(bool grantBasic = true)
        {
            Factory = new SqlServerRelationalApiFactory(_baseConnection, services =>
            {
                if (grantBasic)
                {
                    services.PostConfigure<IdentitySeedOptions>(options =>
                        options.Roles.Single(role => role.Code == "SYSTEM_ADMINISTRATOR")
                            .Permissions.Add(ErpPermissions.CounterpartyPartiesUpdateBasic));
                }
            });
        }

        public SqlServerRelationalApiFactory Factory { get; }

        public HttpClient Client(Guid? company = null, string user = TestUsers.GroupAdmin) =>
            Factory.CreateClientFor(user, company ?? TestCompanyIds.CompanyA);

        public async Task<T> ScalarAsync<T>(string sql, Guid party)
        {
            await using var connection = new SqlConnection(Factory.DatabaseConnectionString);
            await connection.OpenAsync();
            await using var command = CreateCommand(connection, sql, party);
            return Assert.IsType<T>(await command.ExecuteScalarAsync());
        }

        public async Task ExecuteAsync(string sql, Guid party)
        {
            await using var connection = new SqlConnection(Factory.DatabaseConnectionString);
            await connection.OpenAsync();
            await using var command = CreateCommand(connection, sql, party);
            await command.ExecuteNonQueryAsync();
        }

        private static SqlCommand CreateCommand(SqlConnection connection, string sql, Guid party)
        {
            var command = connection.CreateCommand();
            command.CommandText = sql;
            if (sql.Contains("@Party", StringComparison.Ordinal)) command.Parameters.AddWithValue("@Party", party);
            if (sql.Contains("@Event", StringComparison.Ordinal)) command.Parameters.AddWithValue("@Event", EventName);
            if (sql.Contains("@Command", StringComparison.Ordinal)) command.Parameters.AddWithValue("@Command", CommandName);
            return command;
        }

        public async ValueTask DisposeAsync()
        {
            await Factory.DisposeAsync();
            Assert.Matches("^goymen_erp_ci_[a-f0-9]{32}$", Factory.DatabaseName);
            var builder = new SqlConnectionStringBuilder(_baseConnection) { InitialCatalog = "master" };
            await using var connection = new SqlConnection(builder.ConnectionString);
            await connection.OpenAsync();
            await using var command = connection.CreateCommand();
            command.CommandText = $"""
                IF DB_ID(N'{Factory.DatabaseName}') IS NOT NULL
                BEGIN
                    ALTER DATABASE [{Factory.DatabaseName}] SET SINGLE_USER WITH ROLLBACK IMMEDIATE;
                    DROP DATABASE [{Factory.DatabaseName}];
                END;
                """;
            await command.ExecuteNonQueryAsync();
        }
    }
}
