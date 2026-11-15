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
/// ALS-D95 (confirmed by SINAN-CEVAP 19.22) — the Cari official legal name change, against real SQL Server through the
/// real API: one step and no approval step under the separate company-wide
/// <c>counterparty.parties.legal-name.update</c> permission (granted here only on the disposable database; no shipped
/// role carries it), the Idempotency-Key replay, the PARTY version fence, one audit per real change carrying the old and
/// the new legal name (none for a no-op), the active-company account as the existence fence, and the trade name that
/// never changes on this path. The basic Cari edit permission does not reach the legal name, and the legal-name
/// permission does not reach the trade name.
/// </summary>
public sealed class CounterpartyLegalNameSqlServerRelationalAcceptanceTests
{
    private const string CommandName = "counterparty.party.legal-name.update.v1";
    private const string EventName = "counterparty.party.legal-name.changed.v1";

    [SqlServerFact]
    public async Task A_legal_name_is_changed_in_one_step_with_replay_and_one_audit_holding_old_and_new()
    {
        await using var test = new LegalNameTest(ErpPermissions.CounterpartyPartiesLegalNameUpdate);
        using var admin = test.Client();
        var party = await CreatePartyAsync(admin, "Eski Unvan Ticaret Anonim Şirketi");
        var path = PathFor(party);

        var before = await ReadAsync(admin, path);
        Assert.Equal(party, before.GetProperty("partyId").GetGuid());
        Assert.Equal(TestCompanyIds.CompanyA, before.GetProperty("companyId").GetGuid());
        Assert.Equal("Eski Unvan Ticaret Anonim Şirketi", before.GetProperty("legalName").GetString());
        Assert.True(before.GetProperty("viewer").GetProperty("canUpdateLegalName").GetBoolean());
        var version = before.GetProperty("version").GetString()!;

        NewKey(admin);
        var body = Body(new { expectedVersion = version, legalName = "  Yeni Unvan Sanayi Anonim Şirketi  " });
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
        using (var reused = await PutAsync(admin, path, Body(new { expectedVersion = version, legalName = "Başka Unvan" })))
        {
            await AssertProblemAsync(reused, HttpStatusCode.Conflict, "idempotency_conflict");
        }

        var after = await ReadAsync(admin, path);
        Assert.Equal("Yeni Unvan Sanayi Anonim Şirketi", after.GetProperty("legalName").GetString());
        Assert.Equal(
            (long.Parse(version, CultureInfo.InvariantCulture) + 1).ToString(CultureInfo.InvariantCulture),
            after.GetProperty("version").GetString());
        Assert.Equal("Yeni Unvan Sanayi Anonim Şirketi", await test.ScalarAsync<string>(
            "SELECT [LegalName] FROM [counterparty].[Parties] WHERE [Id]=@Party;", party));
        Assert.Equal(1, await test.ScalarAsync<int>(
            "SELECT COUNT(*) FROM [counterparty].[Parties] WHERE [Id]=@Party AND [TradeName] IS NULL;", party));

        // One audit event, old and new legal name in it, under the separate permission, with no approval context.
        Assert.Equal(1, await test.ScalarAsync<int>(
            "SELECT COUNT(*) FROM [audit].[AuditEvents] WHERE [EventType]=@Event;", party));
        Assert.Equal("Eski Unvan Ticaret Anonim Şirketi", await test.ScalarAsync<string>(
            "SELECT JSON_VALUE([BeforeJson], '$.LegalName') FROM [audit].[AuditEvents] WHERE [EventType]=@Event;", party));
        Assert.Equal("Yeni Unvan Sanayi Anonim Şirketi", await test.ScalarAsync<string>(
            "SELECT JSON_VALUE([AfterJson], '$.LegalName') FROM [audit].[AuditEvents] WHERE [EventType]=@Event;", party));
        Assert.Equal(1, await test.ScalarAsync<int>(
            "SELECT COUNT(*) FROM [audit].[AuditEvents] WHERE [EventType]=@Event AND [EntityId]=LOWER(CONVERT(nvarchar(36), @Party)) "
            + "AND [PermissionCode]=@Permission AND [ApprovalContextJson] IS NULL;", party));
        Assert.Equal(1, await test.ScalarAsync<int>(
            "SELECT COUNT(*) FROM [idempotency].[CommandExecutions] WHERE [CommandName]=@Command;", party));
    }

    [SqlServerFact]
    public async Task A_no_op_keeps_the_version_and_writes_no_audit_and_a_stale_version_is_a_replayable_409()
    {
        await using var test = new LegalNameTest(ErpPermissions.CounterpartyPartiesLegalNameUpdate);
        using var admin = test.Client();
        var party = await CreatePartyAsync(admin, "Örnek Unvan");
        var path = PathFor(party);
        var version = (await ReadAsync(admin, path)).GetProperty("version").GetString()!;

        NewKey(admin);
        using (var noOp = await PutAsync(admin, path, Body(new { expectedVersion = version, legalName = " Örnek Unvan " })))
        {
            Assert.Equal(HttpStatusCode.NoContent, noOp.StatusCode);
        }

        Assert.Equal(version, (await ReadAsync(admin, path)).GetProperty("version").GetString());
        Assert.Equal(0, await test.ScalarAsync<int>(
            "SELECT COUNT(*) FROM [audit].[AuditEvents] WHERE [EventType]=@Event;", party));
        Assert.Equal(1, await test.ScalarAsync<int>(
            "SELECT COUNT(*) FROM [idempotency].[CommandExecutions] WHERE [CommandName]=@Command;", party));

        NewKey(admin);
        using (var moved = await PutAsync(admin, path, Body(new { expectedVersion = version, legalName = "Yeni Unvan" })))
        {
            Assert.Equal(HttpStatusCode.NoContent, moved.StatusCode);
        }

        NewKey(admin);
        var stale = Body(new { expectedVersion = version, legalName = "Eski form unvanı" });
        using (var conflict = await PutAsync(admin, path, stale))
        {
            await AssertProblemAsync(conflict, HttpStatusCode.Conflict, "version_conflict");
        }

        using (var conflictReplay = await PutAsync(admin, path, stale))
        {
            await AssertProblemAsync(conflictReplay, HttpStatusCode.Conflict, "version_conflict");
            Assert.True(conflictReplay.Headers.Contains("Idempotency-Replayed"));
        }

        Assert.Equal("Yeni Unvan", (await ReadAsync(admin, path)).GetProperty("legalName").GetString());
        Assert.Equal(1, await test.ScalarAsync<int>(
            "SELECT COUNT(*) FROM [audit].[AuditEvents] WHERE [EventType]=@Event;", party));
    }

    [SqlServerFact]
    public async Task Blank_too_long_or_malformed_bodies_are_400_and_change_nothing()
    {
        await using var test = new LegalNameTest(ErpPermissions.CounterpartyPartiesLegalNameUpdate);
        using var admin = test.Client();
        var party = await CreatePartyAsync(admin, "Örnek Unvan");
        var path = PathFor(party);
        var version = (await ReadAsync(admin, path)).GetProperty("version").GetString()!;

        foreach (var invalid in new[]
                 {
                     Body(new { expectedVersion = version, legalName = "   " }),
                     Body(new { expectedVersion = version, legalName = "" }),
                     Body(new { expectedVersion = version, legalName = (string?)null }),
                     Body(new { expectedVersion = version, legalName = new string('x', 251) }),
                     Body(new { expectedVersion = version }),
                     Body(new { legalName = "Eksik sürüm" }),
                     Body(new { expectedVersion = version, legalName = "Unvan", tradeName = "Gizli değişiklik" }),
                     Body(new { expectedVersion = 1, legalName = "Sayısal sürüm" }),
                 })
        {
            NewKey(admin);
            using var refused = await PutAsync(admin, path, invalid);
            Assert.Equal(HttpStatusCode.BadRequest, refused.StatusCode);
        }

        var after = await ReadAsync(admin, path);
        Assert.Equal(version, after.GetProperty("version").GetString());
        Assert.Equal("Örnek Unvan", after.GetProperty("legalName").GetString());
        Assert.Equal(0, await test.ScalarAsync<int>(
            "SELECT COUNT(*) FROM [audit].[AuditEvents] WHERE [EventType]=@Event;", party));
        Assert.Equal(1, await test.ScalarAsync<int>(
            "SELECT COUNT(*) FROM [counterparty].[Parties] WHERE [Id]=@Party AND [TradeName] IS NULL;", party));
    }

    [SqlServerFact]
    public async Task Without_the_separate_grant_nothing_is_written_and_another_company_cannot_read_or_change_it()
    {
        // The basic Cari edit permission does not reach the legal name (19.22: a separate permission).
        await using (var basicOnly = new LegalNameTest(ErpPermissions.CounterpartyPartiesUpdateBasic))
        {
            using var editor = basicOnly.Client();
            var own = await CreatePartyAsync(editor, "Temel Yetkili Unvan");
            var view = await ReadAsync(editor, PathFor(own));
            Assert.False(view.GetProperty("viewer").GetProperty("canUpdateLegalName").GetBoolean());

            NewKey(editor);
            using var denied = await PutAsync(editor, PathFor(own),
                Body(new { expectedVersion = view.GetProperty("version").GetString(), legalName = "Yetkisiz Unvan" }));
            Assert.Equal(HttpStatusCode.Forbidden, denied.StatusCode);
            Assert.Equal("Temel Yetkili Unvan", await basicOnly.ScalarAsync<string>(
                "SELECT [LegalName] FROM [counterparty].[Parties] WHERE [Id]=@Party;", own));
            Assert.Equal(0, await basicOnly.ScalarAsync<int>(
                "SELECT COUNT(*) FROM [audit].[AuditEvents] WHERE [EventType]=@Event;", own));
        }

        await using var test = new LegalNameTest(ErpPermissions.CounterpartyPartiesLegalNameUpdate);
        using var adminA = test.Client();
        using var adminB = test.Client(TestCompanyIds.CompanyB);
        var party = await CreatePartyAsync(adminA, "A Müşterisi Unvanı");
        var path = PathFor(party);
        var version = (await ReadAsync(adminA, path)).GetProperty("version").GetString();

        // The legal-name permission does not reach the trade name either.
        NewKey(adminA);
        using (var tradeNameDenied = await PutAsync(adminA, $"/api/v1/counterparties/{party}/trade-name",
                   Body(new { expectedVersion = version, tradeName = "Ticari Ad" })))
        {
            Assert.Equal(HttpStatusCode.Forbidden, tradeNameDenied.StatusCode);
        }

        // Company B has no account for A's party: the party exists, but not for B.
        using (var missing = await adminB.GetAsync(path))
        {
            Assert.Equal(HttpStatusCode.NotFound, missing.StatusCode);
        }

        NewKey(adminB);
        using (var missingWrite = await PutAsync(adminB, path, Body(new { expectedVersion = version, legalName = "Ele Geçirildi" })))
        {
            await AssertProblemAsync(missingWrite, HttpStatusCode.NotFound, "not_found");
        }

        using var narrow = test.Client(TestCompanyIds.CompanyA, TestUsers.CompanyAViewer);
        NewKey(narrow);
        using (var deniedWrite = await PutAsync(narrow, path, Body(new { expectedVersion = version, legalName = "Dar Yetki" })))
        {
            Assert.Equal(HttpStatusCode.Forbidden, deniedWrite.StatusCode);
        }

        Assert.Equal("A Müşterisi Unvanı", await test.ScalarAsync<string>(
            "SELECT [LegalName] FROM [counterparty].[Parties] WHERE [Id]=@Party;", party));
        Assert.Equal(1, await test.ScalarAsync<int>(
            "SELECT COUNT(*) FROM [counterparty].[Parties] WHERE [Id]=@Party AND [TradeName] IS NULL;", party));
        Assert.Equal(0, await test.ScalarAsync<int>(
            "SELECT COUNT(*) FROM [audit].[AuditEvents] WHERE [EventType]=@Event;", party));
    }

    private static string PathFor(Guid party) => $"/api/v1/counterparties/{party}/legal-name";

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

    private sealed class LegalNameTest : IAsyncDisposable
    {
        private readonly string _baseConnection = Environment.GetEnvironmentVariable(
            SqlServerRelationalAcceptanceTests.ConnectionStringEnvironmentVariable)!;

        public LegalNameTest(string grantedPermission)
        {
            Factory = new SqlServerRelationalApiFactory(_baseConnection, services =>
            {
                // Test-only grant on the disposable database; no shipped seed role is changed.
                services.PostConfigure<IdentitySeedOptions>(options =>
                    options.Roles.Single(role => role.Code == "SYSTEM_ADMINISTRATOR")
                        .Permissions.Add(grantedPermission));
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
            if (sql.Contains("@Permission", StringComparison.Ordinal))
            {
                command.Parameters.AddWithValue("@Permission", ErpPermissions.CounterpartyPartiesLegalNameUpdate);
            }

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
