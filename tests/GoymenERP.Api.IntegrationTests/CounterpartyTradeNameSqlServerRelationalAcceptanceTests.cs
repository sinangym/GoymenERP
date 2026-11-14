using System.Globalization;
using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using GoymenERP.BuildingBlocks.Security;
using GoymenERP.Modules.Identity.Infrastructure.Seeding;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.DependencyInjection;

namespace GoymenERP.Api.IntegrationTests;

/// <summary>
/// F-027 / C-02 — the Cari card trade name edit, against real SQL Server through the real API: the company-wide
/// <c>counterparty.parties.update.basic</c> permission, the Idempotency-Key replay, the PARTY version fence, one audit
/// per real change (none for a no-op), the active-company account as the existence fence and the legal name that never
/// changes on this path (CARI-DEC-004 keeps it approval-bound).
/// </summary>
public sealed class CounterpartyTradeNameSqlServerRelationalAcceptanceTests
{
    private const string CommandName = "counterparty.party.trade-name.update.v1";
    private const string EventName = "counterparty.party.trade-name.changed.v1";

    [SqlServerFact]
    public async Task A_trade_name_is_changed_with_replay_and_one_audit_and_the_legal_name_stays()
    {
        await using var test = new TradeNameTest();
        using var admin = test.Client();
        var party = await CreatePartyAsync(admin, "Örnek Ticaret Anonim Şirketi");
        var path = PathFor(party);

        var before = await ReadAsync(admin, path);
        Assert.Equal(party, before.GetProperty("partyId").GetGuid());
        Assert.Equal(TestCompanyIds.CompanyA, before.GetProperty("companyId").GetGuid());
        Assert.Equal(JsonValueKind.Null, before.GetProperty("tradeName").ValueKind);
        Assert.True(before.GetProperty("viewer").GetProperty("canUpdateTradeName").GetBoolean());
        var version = before.GetProperty("version").GetString()!;

        NewKey(admin);
        var body = Body(new { expectedVersion = version, tradeName = "  Örnek Market  " });
        using (var changed = await PutAsync(admin, path, body))
        {
            Assert.Equal(HttpStatusCode.NoContent, changed.StatusCode);
        }

        using (var replay = await PutAsync(admin, path, body))
        {
            Assert.Equal(HttpStatusCode.NoContent, replay.StatusCode);
            Assert.True(replay.Headers.Contains("Idempotency-Replayed"));
        }

        // The same key with another body is not a replay.
        using (var reused = await PutAsync(admin, path, Body(new { expectedVersion = version, tradeName = "Başka" })))
        {
            await AssertProblemAsync(reused, HttpStatusCode.Conflict, "idempotency_conflict");
        }

        var after = await ReadAsync(admin, path);
        Assert.Equal("Örnek Market", after.GetProperty("tradeName").GetString());
        Assert.Equal("Örnek Ticaret Anonim Şirketi", after.GetProperty("legalName").GetString());
        Assert.Equal((long.Parse(version, CultureInfo.InvariantCulture) + 1).ToString(CultureInfo.InvariantCulture), after.GetProperty("version").GetString());
        Assert.Equal(1, await test.ScalarAsync<int>(
            "SELECT COUNT(*) FROM [audit].[AuditEvents] WHERE [EventType]=@Event;", party));
        Assert.Equal("Örnek Market", await test.ScalarAsync<string>(
            "SELECT [TradeName] FROM [counterparty].[Parties] WHERE [Id]=@Party;", party));

        // Clearing is an explicit null and is a real change of its own.
        NewKey(admin);
        using (var cleared = await PutAsync(admin, path,
                   Body(new { expectedVersion = after.GetProperty("version").GetString(), tradeName = (string?)null })))
        {
            Assert.Equal(HttpStatusCode.NoContent, cleared.StatusCode);
        }

        Assert.Equal(JsonValueKind.Null, (await ReadAsync(admin, path)).GetProperty("tradeName").ValueKind);
        Assert.Equal(2, await test.ScalarAsync<int>(
            "SELECT COUNT(*) FROM [audit].[AuditEvents] WHERE [EventType]=@Event;", party));
    }

    [SqlServerFact]
    public async Task A_no_op_keeps_the_version_and_writes_no_audit_and_a_stale_version_is_a_replayable_409()
    {
        await using var test = new TradeNameTest();
        using var admin = test.Client();
        var party = await CreatePartyAsync(admin, "Örnek Taraf");
        var path = PathFor(party);
        var version = (await ReadAsync(admin, path)).GetProperty("version").GetString()!;

        NewKey(admin);
        using (var noOp = await PutAsync(admin, path, Body(new { expectedVersion = version, tradeName = "   " })))
        {
            Assert.Equal(HttpStatusCode.NoContent, noOp.StatusCode);
        }

        Assert.Equal(version, (await ReadAsync(admin, path)).GetProperty("version").GetString());
        Assert.Equal(0, await test.ScalarAsync<int>(
            "SELECT COUNT(*) FROM [audit].[AuditEvents] WHERE [EventType]=@Event;", party));
        Assert.Equal(1, await test.ScalarAsync<int>(
            "SELECT COUNT(*) FROM [idempotency].[CommandExecutions] WHERE [CommandName]=@Command;", party));

        NewKey(admin);
        using (var moved = await PutAsync(admin, path, Body(new { expectedVersion = version, tradeName = "Yeni" })))
        {
            Assert.Equal(HttpStatusCode.NoContent, moved.StatusCode);
        }

        NewKey(admin);
        var stale = Body(new { expectedVersion = version, tradeName = "Eski form" });
        using (var conflict = await PutAsync(admin, path, stale))
        {
            await AssertProblemAsync(conflict, HttpStatusCode.Conflict, "version_conflict");
        }

        using (var conflictReplay = await PutAsync(admin, path, stale))
        {
            await AssertProblemAsync(conflictReplay, HttpStatusCode.Conflict, "version_conflict");
            Assert.True(conflictReplay.Headers.Contains("Idempotency-Replayed"));
        }

        Assert.Equal("Yeni", (await ReadAsync(admin, path)).GetProperty("tradeName").GetString());
        Assert.Equal(1, await test.ScalarAsync<int>(
            "SELECT COUNT(*) FROM [audit].[AuditEvents] WHERE [EventType]=@Event;", party));
    }

    [SqlServerFact]
    public async Task Malformed_bodies_are_400_and_change_nothing()
    {
        await using var test = new TradeNameTest();
        using var admin = test.Client();
        var party = await CreatePartyAsync(admin, "Örnek Taraf");
        var path = PathFor(party);
        var version = (await ReadAsync(admin, path)).GetProperty("version").GetString()!;

        foreach (var invalid in new[]
                 {
                     Body(new { expectedVersion = version, tradeName = new string('x', 201) }),
                     Body(new { expectedVersion = version }),
                     Body(new { tradeName = "Eksik sürüm" }),
                     Body(new { expectedVersion = version, tradeName = "Ad", legalName = "Gizli değişiklik" }),
                     Body(new { expectedVersion = 1, tradeName = "Sayısal sürüm" }),
                 })
        {
            NewKey(admin);
            using var refused = await PutAsync(admin, path, invalid);
            Assert.Equal(HttpStatusCode.BadRequest, refused.StatusCode);
        }

        var after = await ReadAsync(admin, path);
        Assert.Equal(version, after.GetProperty("version").GetString());
        Assert.Equal("Örnek Taraf", after.GetProperty("legalName").GetString());
        Assert.Equal(JsonValueKind.Null, after.GetProperty("tradeName").ValueKind);
        Assert.Equal(0, await test.ScalarAsync<int>(
            "SELECT COUNT(*) FROM [audit].[AuditEvents] WHERE [EventType]=@Event;", party));
    }

    [SqlServerFact]
    public async Task Another_company_cannot_read_or_change_it_and_without_the_grant_nothing_is_written()
    {
        await using (var defaults = new TradeNameTest(grantBasic: false))
        {
            using var reader = defaults.Client();
            var own = await CreatePartyAsync(reader, "Yetkisiz Taraf");
            var view = await ReadAsync(reader, PathFor(own));
            Assert.False(view.GetProperty("viewer").GetProperty("canUpdateTradeName").GetBoolean());

            NewKey(reader);
            using var denied = await PutAsync(reader, PathFor(own),
                Body(new { expectedVersion = view.GetProperty("version").GetString(), tradeName = "Yetkisiz" }));
            Assert.Equal(HttpStatusCode.Forbidden, denied.StatusCode);
            Assert.Equal(0, await defaults.ScalarAsync<int>(
                "SELECT COUNT(*) FROM [counterparty].[Parties] WHERE [Id]=@Party AND [TradeName] IS NOT NULL;", own));
        }

        await using var test = new TradeNameTest();
        using var adminA = test.Client();
        using var adminB = test.Client(TestCompanyIds.CompanyB);
        var party = await CreatePartyAsync(adminA, "A Müşterisi");
        var path = PathFor(party);
        var version = (await ReadAsync(adminA, path)).GetProperty("version").GetString();

        // Company B has no account for A's party: the party exists, but not for B.
        using (var missing = await adminB.GetAsync(path))
        {
            Assert.Equal(HttpStatusCode.NotFound, missing.StatusCode);
        }

        NewKey(adminB);
        using (var missingWrite = await PutAsync(adminB, path, Body(new { expectedVersion = version, tradeName = "Ele geçirildi" })))
        {
            await AssertProblemAsync(missingWrite, HttpStatusCode.NotFound, "not_found");
        }

        using var narrow = test.Client(TestCompanyIds.CompanyA, TestUsers.CompanyAViewer);
        NewKey(narrow);
        using (var deniedWrite = await PutAsync(narrow, path, Body(new { expectedVersion = version, tradeName = "Dar yetki" })))
        {
            Assert.Equal(HttpStatusCode.Forbidden, deniedWrite.StatusCode);
        }

        Assert.Equal(0, await test.ScalarAsync<int>(
            "SELECT COUNT(*) FROM [counterparty].[Parties] WHERE [Id]=@Party AND [TradeName] IS NOT NULL;", party));
        Assert.Equal(0, await test.ScalarAsync<int>(
            "SELECT COUNT(*) FROM [audit].[AuditEvents] WHERE [EventType]=@Event;", party));
    }

    private static string PathFor(Guid party) => $"/api/v1/counterparties/{party}/trade-name";

    private static string Body(object value) => JsonSerializer.Serialize(value);

    private static async Task<Guid> CreatePartyAsync(HttpClient client, string legalName)
    {
        NewKey(client);
        using var response = await client.PostAsJsonAsync("/api/v1/counterparties", new { kind = 1, legalName, roles = 1 });
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return (await JsonAsync(response)).GetProperty("id").GetGuid();
    }

    private static async Task AssertProblemAsync(HttpResponseMessage response, HttpStatusCode status, string code)
    {
        Assert.Equal(status, response.StatusCode);
        Assert.Equal(code, (await JsonAsync(response)).GetProperty("code").GetString());
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

    private static void NewKey(HttpClient client)
    {
        client.DefaultRequestHeaders.Remove("Idempotency-Key");
        client.DefaultRequestHeaders.Add("Idempotency-Key", Guid.NewGuid().ToString("N"));
        client.DefaultRequestHeaders.Remove("X-Correlation-Id");
        client.DefaultRequestHeaders.Add("X-Correlation-Id", Guid.NewGuid().ToString("N"));
    }

    private sealed class TradeNameTest : IAsyncDisposable
    {
        private readonly string _baseConnection = Environment.GetEnvironmentVariable(
            SqlServerRelationalAcceptanceTests.ConnectionStringEnvironmentVariable)!;

        public TradeNameTest(bool grantBasic = true)
        {
            Factory = new SqlServerRelationalApiFactory(_baseConnection, services =>
            {
                if (!grantBasic)
                {
                    return;
                }

                // Test-only grant on the disposable database; no shipped seed role is changed.
                services.PostConfigure<IdentitySeedOptions>(options =>
                    options.Roles.Single(role => role.Code == "SYSTEM_ADMINISTRATOR")
                        .Permissions.Add(ErpPermissions.CounterpartyPartiesUpdateBasic));
            });
        }

        public SqlServerRelationalApiFactory Factory { get; }

        public HttpClient Client(Guid? company = null, string user = TestUsers.GroupAdmin) =>
            Factory.CreateClientFor(user, company ?? TestCompanyIds.CompanyA);

        public async Task<T> ScalarAsync<T>(string sql, Guid party)
        {
            await using var connection = new SqlConnection(Factory.DatabaseConnectionString);
            await connection.OpenAsync();
            await using var command = connection.CreateCommand();
            command.CommandText = sql;
            if (sql.Contains("@Party", StringComparison.Ordinal)) command.Parameters.AddWithValue("@Party", party);
            if (sql.Contains("@Event", StringComparison.Ordinal)) command.Parameters.AddWithValue("@Event", EventName);
            if (sql.Contains("@Command", StringComparison.Ordinal)) command.Parameters.AddWithValue("@Command", CommandName);
            return Assert.IsType<T>(await command.ExecuteScalarAsync());
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
