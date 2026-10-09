using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using GoymenERP.BuildingBlocks.Security;
using GoymenERP.Modules.Counterparty.Application.Contracts;
using GoymenERP.Modules.Counterparty.Domain.Accounts;
using GoymenERP.Modules.Counterparty.Infrastructure.Persistence;
using GoymenERP.Modules.Identity.Infrastructure.Seeding;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace GoymenERP.Api.IntegrationTests;

public sealed class CounterpartyContactInformationSqlServerRelationalAcceptanceTests(Xunit.Abstractions.ITestOutputHelper output)
{
    private const string CommandName = "counterparty.company-account.contact-information.update.v1";
    private const string EventName = "counterparty.company-account.contact-information.changed.v1";

    [SqlServerFact]
    public async Task AccountContactCard_RoundTripsExplicitChildrenWithoutChangingPartyOrOtherCompany()
    {
        await using var test = new ContactTest();
        using var a = test.Client();
        // C006 initial snapshot regression: exercise the same query before any Party exists.
        // Keep the real SQL scalar type and the old cast's stack as failure provenance.
        var emptyScalar = await test.ScalarAsync<DBNull>(
            "SELECT (SELECT * FROM [counterparty].[Parties] ORDER BY [Id] FOR JSON PATH, INCLUDE_NULL_VALUES);",
            Guid.Empty);
        var legacyCast = Assert.Throws<InvalidCastException>(() => (string)(object)emptyScalar);
        output.WriteLine("C006 empty Party scalar: " + emptyScalar.GetType().FullName);
        output.WriteLine(legacyCast.ToString());
        Assert.Equal(0, await test.ScalarAsync<int>("SELECT COUNT(*) FROM [counterparty].[Parties];", Guid.Empty));
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

        // Editing an inactive child must not silently reactivate it. The same UUID
        // survives an explicit reactivation, with one history event per real change.
        NewKey(a);
        using var inactiveEdit = await PutAsync(a, path, $$$"""
            {"expectedVersion":"3","changes":{"addresses":[
              {"id":"{{{firstAddress}}}","operation":"upsert","line1":"Edited while inactive"}]}}
            """);
        Assert.Equal(HttpStatusCode.NoContent, inactiveEdit.StatusCode);
        var inactive = await ReadAsync(a, path);
        Assert.Equal("4", inactive.GetProperty("version").GetString());
        var inactiveAddress = inactive.GetProperty("information").GetProperty("addresses").EnumerateArray()
            .Single(address => address.GetProperty("id").GetGuid() == firstAddress);
        Assert.False(inactiveAddress.GetProperty("isActive").GetBoolean());
        Assert.Equal("3", inactiveAddress.GetProperty("version").GetString());
        Assert.Equal("Edited while inactive", inactiveAddress.GetProperty("line1").GetString());
        NewKey(a);
        using var reactivate = await PutAsync(a, path, $$$"""
            {"expectedVersion":"4","changes":{"addresses":[
              {"id":"{{{firstAddress}}}","operation":"reactivate"}]}}
            """);
        Assert.Equal(HttpStatusCode.NoContent, reactivate.StatusCode);
        var reactivated = await ReadAsync(a, path);
        Assert.Equal("5", reactivated.GetProperty("version").GetString());
        var reactivatedAddresses = reactivated.GetProperty("information").GetProperty("addresses");
        Assert.Equal(2, reactivatedAddresses.GetArrayLength());
        var sameAddress = reactivatedAddresses.EnumerateArray()
            .Single(address => address.GetProperty("id").GetGuid() == firstAddress);
        Assert.True(sameAddress.GetProperty("isActive").GetBoolean());
        Assert.Equal("4", sameAddress.GetProperty("version").GetString());
        Assert.Equal("Edited while inactive", sameAddress.GetProperty("line1").GetString());
        Assert.Equal(after.GetProperty("information").GetProperty("contacts").GetRawText(),
            reactivated.GetProperty("information").GetProperty("contacts").GetRawText());
        Assert.Equal(beforeB.GetRawText(), (await ReadAsync(b, path)).GetRawText());
        Assert.Equal(beforeParty, await test.ScalarAsync<string>(
            "SELECT (SELECT * FROM [counterparty].[Parties] WHERE [Id]=@Party FOR JSON PATH);", party));
        var extendedHistory = await ReadAsync(a,
            $"/api/v1/audit/events?entityType=Counterparty.CompanyPartyAccount&entityId={after.GetProperty("accountId").GetGuid():D}");
        Assert.Equal(4, extendedHistory.GetProperty("items").EnumerateArray()
            .Count(item => item.GetProperty("eventType").GetString() == EventName));
        Assert.Equal(4, await test.ScalarAsync<int>(
            "SELECT COUNT(*) FROM [idempotency].[CommandExecutions] WHERE [CommandName]=@Command;", party));
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

        a.DefaultRequestHeaders.Add("X-ERP-Counterparty-Contract", "optional-trade-roles-v1");
        var addressId = Guid.NewGuid();
        var firstContact = Guid.NewGuid();
        var secondContact = Guid.NewGuid();
        var channelId = Guid.NewGuid();
        NewKey(a);
        using var initializeA = await PutAsync(a, path, $$$"""
            {"expectedVersion":"1","changes":{
             "addresses":[{"id":"{{{addressId}}}","operation":"upsert","purpose":"delivery","countryCode":"TR","line1":"Company A only"}],
             "contacts":[
              {"id":"{{{firstContact}}}","operation":"upsert","displayName":"First","purpose":"Operations",
               "channels":[{"id":"{{{channelId}}}","operation":"upsert","kind":"phone","value":"00123"}]},
              {"id":"{{{secondContact}}}","operation":"upsert","displayName":"Second","purpose":"Operations"}]}}
            """);
        Assert.Equal(HttpStatusCode.NoContent, initializeA.StatusCode);
        var preservedA = (await ReadAsync(a, path)).GetRawText();
        NewKey(b);
        using var linkB = await b.PostAsJsonAsync($"/api/v1/counterparties/{party}/company-account", new { roles = 0 });
        Assert.Equal(HttpStatusCode.Created, linkB.StatusCode);
        var untouchedB = (await ReadAsync(b, path)).GetRawText();
        foreach (var foreignChild in new[]
        {
            $$$"""{"expectedVersion":"1","changes":{"addresses":[{"id":"{{{addressId}}}","operation":"deactivate"}]}}""",
            $$$"""{"expectedVersion":"1","changes":{"addresses":[{"id":"{{{addressId}}}","operation":"upsert","line1":"Cannot resolve A"}]}}""",
        })
        {
            NewKey(b);
            using var foreignChildResponse = await PutAsync(b, path, foreignChild);
            Assert.Equal(HttpStatusCode.BadRequest, foreignChildResponse.StatusCode);
            Assert.Equal(untouchedB, (await ReadAsync(b, path)).GetRawText());
            Assert.Equal(preservedA, (await ReadAsync(a, path)).GetRawText());
        }
        // A UUID is scoped to its owning account, not a global address lookup.
        NewKey(b);
        using var ownAddressB = await PutAsync(b, path, $$$"""
            {"expectedVersion":"1","changes":{"addresses":[
             {"id":"{{{addressId}}}","operation":"upsert","purpose":"operational","countryCode":"DE","line1":"Company B only"}]}}
            """);
        Assert.Equal(HttpStatusCode.NoContent, ownAddressB.StatusCode);
        Assert.Equal(preservedA, (await ReadAsync(a, path)).GetRawText());
        var ownB = await ReadAsync(b, path);
        Assert.Equal("2", ownB.GetProperty("version").GetString());
        var ownBAddress = Assert.Single(ownB.GetProperty("information").GetProperty("addresses").EnumerateArray());
        Assert.Equal(addressId, ownBAddress.GetProperty("id").GetGuid());
        Assert.Equal("Company B only", ownBAddress.GetProperty("line1").GetString());
        // A channel under the first contact cannot be addressed through the second.
        NewKey(a);
        using var foreignParent = await PutAsync(a, path, $$$"""
            {"expectedVersion":"2","changes":{"contacts":[
             {"id":"{{{secondContact}}}","operation":"upsert","channels":[{"id":"{{{channelId}}}","operation":"deactivate"}]}]}}
            """);
        Assert.Equal(HttpStatusCode.BadRequest, foreignParent.StatusCode);
        Assert.Equal(preservedA, (await ReadAsync(a, path)).GetRawText());
        Assert.Equal(ownB.GetRawText(), (await ReadAsync(b, path)).GetRawText());
        Assert.Equal(2, await test.ScalarAsync<int>(
            "SELECT COUNT(*) FROM [audit].[AuditEvents] WHERE [EventType]=@Event;", party));
    }

    [SqlServerFact]
    public async Task ContactCard_FreshAuthorityRevocationAfterOwnerReadDoesNotDiscloseOrPersist()
    {
        var authority = new SwitchableFreshAuthority(revoked: true);
        await using var test = new ContactTest(freshAuthority: authority);
        using var client = test.Client();
        var party = await CreateAsync(client);
        var path = PathFor(party);

        using var deniedRead = await client.GetAsync(path);
        Assert.Equal(HttpStatusCode.Forbidden, deniedRead.StatusCode);
        Assert.DoesNotContain(
            "information",
            await deniedRead.Content.ReadAsStringAsync(),
            StringComparison.OrdinalIgnoreCase);

        NewKey(client);
        using var deniedWrite = await PutAsync(
            client,
            path,
            """{"expectedVersion":"1","changes":{"phone":"must-not-persist"}}""");
        Assert.Equal(HttpStatusCode.Forbidden, deniedWrite.StatusCode);
        Assert.Equal(1, await test.ScalarAsync<long>(
            "SELECT [Version] FROM [counterparty].[CompanyPartyAccounts] WHERE [PartyId]=@Party;", party));
        Assert.Equal(0, await test.ScalarAsync<int>(
            "SELECT COUNT(*) FROM [audit].[AuditEvents] WHERE [EventType]=@Event;", party));
        Assert.Equal(0, await test.ScalarAsync<int>(
            "SELECT COUNT(*) FROM [idempotency].[CommandExecutions] WHERE [CommandName]=@Command;", party));
        Assert.True(authority.Measurements >= 2);
    }

    [SqlServerFact]
    public async Task ContactCard_PersistedReplayRequiresFreshAuthorityAndKeepsSingleEffect()
    {
        var authority = new SwitchableFreshAuthority();
        await using var test = new ContactTest(freshAuthority: authority);
        using var client = test.Client();
        var party = await CreateAsync(client);
        var path = PathFor(party);
        const string body = """{"expectedVersion":"1","changes":{"phone":"001"}}""";
        NewKey(client);

        using var saved = await PutAsync(client, path, body);
        Assert.Equal(HttpStatusCode.NoContent, saved.StatusCode);
        authority.Revoke();
        using var deniedReplay = await PutAsync(client, path, body);

        Assert.Equal(HttpStatusCode.Forbidden, deniedReplay.StatusCode);
        Assert.False(deniedReplay.Headers.Contains("Idempotency-Replayed"));
        Assert.Equal(2, await test.ScalarAsync<long>(
            "SELECT [Version] FROM [counterparty].[CompanyPartyAccounts] WHERE [PartyId]=@Party;", party));
        Assert.Equal(1, await test.ScalarAsync<int>(
            "SELECT COUNT(*) FROM [audit].[AuditEvents] WHERE [EventType]=@Event;", party));
        Assert.Equal(1, await test.ScalarAsync<int>(
            "SELECT COUNT(*) FROM [idempotency].[CommandExecutions] WHERE [CommandName]=@Command;", party));
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
        var beforeControl = (await ReadAsync(client, path)).GetRawText();
        foreach (var raw in new[] { "\r001", "001\n", "\r\n", "\t", " \n " })
        {
            NewKey(client);
            using var control = await PutAsync(client, path, JsonSerializer.Serialize(new
            {
                expectedVersion = "9007199254740994", changes = new { phone = raw },
            }));
            Assert.Equal(HttpStatusCode.BadRequest, control.StatusCode);
            Assert.Equal("validation_failed", (await JsonAsync(control)).GetProperty("code").GetString());
            Assert.Equal(beforeControl, (await ReadAsync(client, path)).GetRawText());
        }
        Assert.Equal(1, await test.ScalarAsync<int>(
            "SELECT COUNT(*) FROM [audit].[AuditEvents] WHERE [EventType]=@Event;", party));
        Assert.Equal(1, await test.ScalarAsync<int>(
            "SELECT COUNT(*) FROM [idempotency].[CommandExecutions] WHERE [CommandName]=@Command;", party));
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
        var interleave = new NoOpSaveInterleave();
        await using var test = new ContactTest(beforeSave: interleave);
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

        // The callback runs after the no-op has loaded/version-checked its account,
        // but before its guarded UPDATE. The winner uses the real HTTP command path.
        using var winner = test.Client();
        interleave.BeforeNoOpSave = async () =>
        {
            NewKey(winner);
            using var winningWrite = await PutAsync(winner, path,
                """{"expectedVersion":"1","changes":{"phone":"winner"}}""");
            Assert.Equal(HttpStatusCode.NoContent, winningWrite.StatusCode);
        };
        NewKey(client);
        using var raced = await PutAsync(client, path, """{"expectedVersion":"1","changes":{}}""");
        Assert.True(interleave.Triggered);
        Assert.Equal(HttpStatusCode.Conflict, raced.StatusCode);
        Assert.Equal("version_conflict", (await JsonAsync(raced)).GetProperty("code").GetString());
        const string conflictReceiptSql = "SELECT (SELECT * FROM [idempotency].[CommandExecutions] WHERE [CommandName]=@Command AND [Outcome]=N'VersionConflict' FOR JSON PATH);";
        var savedConflict = await test.ScalarAsync<string>(conflictReceiptSql, party);
        using var racedReplay = await PutAsync(client, path, """{"expectedVersion":"1","changes":{}}""");
        Assert.Equal(HttpStatusCode.Conflict, racedReplay.StatusCode);
        Assert.True(racedReplay.Headers.Contains("Idempotency-Replayed"));
        var originalProblem = await JsonAsync(raced);
        var replayProblem = await JsonAsync(racedReplay);
        Assert.False(string.IsNullOrWhiteSpace(originalProblem.GetProperty("traceId").GetString()));
        Assert.False(string.IsNullOrWhiteSpace(replayProblem.GetProperty("traceId").GetString()));
        Assert.Equal(originalProblem.EnumerateObject().Where(property => property.Name != "traceId")
                .Select(property => (property.Name, Value: property.Value.GetRawText())),
            replayProblem.EnumerateObject().Where(property => property.Name != "traceId")
                .Select(property => (property.Name, Value: property.Value.GetRawText())));
        Assert.Equal(savedConflict, await test.ScalarAsync<string>(conflictReceiptSql, party));
        var final = await ReadAsync(client, path);
        Assert.Equal("2", final.GetProperty("version").GetString());
        Assert.Equal("winner", final.GetProperty("information").GetProperty("phone").GetString());
        Assert.Equal(1, await test.ScalarAsync<int>(
            "SELECT COUNT(*) FROM [audit].[AuditEvents] WHERE [EventType]=@Event;", party));
        Assert.Equal(3, await test.ScalarAsync<int>(
            "SELECT COUNT(*) FROM [idempotency].[CommandExecutions] WHERE [CommandName]=@Command;", party));
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

        public ContactTest(
            bool grantBasic = true,
            SaveChangesInterceptor? beforeSave = null,
            IErpAuthorityRevalidationService? freshAuthority = null)
        {
            Factory = new SqlServerRelationalApiFactory(_baseConnection, services =>
            {
                if (beforeSave is not null)
                    services.AddDbContext<CounterpartyDbContext>((_, options) => options.AddInterceptors(beforeSave));
                if (freshAuthority is not null)
                {
                    services.RemoveAll<IErpAuthorityRevalidationService>();
                    services.AddSingleton(freshAuthority);
                }
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

    private sealed class SwitchableFreshAuthority(bool revoked = false) : IErpAuthorityRevalidationService
    {
        private int _revoked = revoked ? 1 : 0;
        private int _measurements;

        public int Measurements => Volatile.Read(ref _measurements);

        public void Revoke() => Volatile.Write(ref _revoked, 1);

        public Task<PermissionScope> RevalidatePermissionScopeAsync(
            string permission,
            Guid companyId,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Interlocked.Increment(ref _measurements);
            return Task.FromResult(Volatile.Read(ref _revoked) != 0
                ? PermissionScope.Denied
                : new PermissionScope(
                    true,
                    true,
                    new HashSet<string>(StringComparer.Ordinal) { "SENTETIK-CONTACT-AUTHORITY" },
                    new HashSet<Guid>(),
                    new HashSet<Guid>(),
                    new HashSet<Guid>()));
        }
    }

    private sealed class NoOpSaveInterleave : SaveChangesInterceptor
    {
        private int _triggered;
        public Func<Task>? BeforeNoOpSave { get; set; }
        public bool Triggered => Volatile.Read(ref _triggered) != 0;

        public override async ValueTask<InterceptionResult<int>> SavingChangesAsync(
            DbContextEventData eventData, InterceptionResult<int> result, CancellationToken cancellationToken = default)
        {
            if (BeforeNoOpSave is { } callback && eventData.Context is CounterpartyDbContext context
                && context.ChangeTracker.Entries<CompanyPartyAccount>().Any(entry =>
                    entry.Property(account => account.ContactInformationJson).IsModified
                    && entry.Entity.ContactInformationJson is null && entry.Entity.Version == 1)
                && Interlocked.CompareExchange(ref _triggered, 1, 0) == 0)
                await callback();
            return result;
        }
    }
}
