using System.Globalization;
using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using GoymenERP.BuildingBlocks.Security;
using GoymenERP.Modules.Counterparty.Application.Contracts;
using GoymenERP.Modules.Counterparty.Infrastructure.Persistence;
using GoymenERP.Modules.Counterparty.Infrastructure.Persistence.Migrations;
using GoymenERP.Modules.Identity.Infrastructure.Seeding;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.EntityFrameworkCore.Migrations.Operations;
using Microsoft.Extensions.DependencyInjection;

namespace GoymenERP.Api.IntegrationTests;

/// <summary>
/// F-027 / EK-18 #76-#77 (SINAN_KARARLARI_2026-10-21, ALC-S12) — the duplicate Cari party merge against real SQL Server
/// through the real API. The approver needs <c>counterparty.parties.merge.approve</c> (granted here only on the disposable
/// database; no shipped role carries it) and <c>counterparty.parties.read</c> in EVERY company holding a card of either
/// party. One Idempotency-Key, both PARTY versions fenced, one append-only merge record, one audit event in the approving
/// company and one <c>counterparty.party.merged.v1</c> outbox row per holding company, all in one transaction. The
/// duplicate becomes passive and points at the survivor for good; there is no unmerge (#77) and the database refuses a
/// second merge record for the same duplicate.
/// </summary>
public sealed class CounterpartyPartyMergeSqlServerRelationalAcceptanceTests
{
    private const string PreviousMigration = "20261115132003_AddCounterpartyOutbox";
    private const string CommandName = "counterparty.party.merge.approve.v1";
    private const string Vkn = "1234567890";
    private const string OtherVkn = "9876543217";
    private const int DownGuardNumber = 51453;
    private const int CheckViolation = 547;
    private const int UniqueIndexViolation = 2601;

    [SqlServerFact]
    public async Task A_group_manager_merges_a_duplicate_once_with_replay_one_record_one_audit_and_an_event_per_holding_company()
    {
        await using var test = new MergeTest(grantMerge: true);
        using var adminA = test.Client();
        using var adminB = test.Client(TestCompanyIds.CompanyB);
        var survivor = await CreatePartyAsync(adminA, "Birleşen Ticaret A.Ş.");
        var duplicate = await CreatePartyAsync(adminA, "Birlesen Ticaret AS");
        await EnterTaxIdentityAsync(adminA, survivor, Vkn);
        await EnterTaxIdentityAsync(adminA, duplicate, Vkn);
        await LinkAsync(adminB, duplicate);

        var view = await ReadAsync(adminA, PathFor(survivor));
        Assert.Equal(survivor, view.GetProperty("partyId").GetGuid());
        Assert.Equal(TestCompanyIds.CompanyA, view.GetProperty("companyId").GetGuid());
        Assert.Equal(JsonValueKind.Null, view.GetProperty("mergedInto").ValueKind);
        Assert.True(view.GetProperty("viewer").GetProperty("canApproveMerge").GetBoolean());
        var candidate = Assert.Single(view.GetProperty("candidates").EnumerateArray());
        Assert.Equal(duplicate, candidate.GetProperty("partyId").GetGuid());
        var survivorVersion = view.GetProperty("version").GetString()!;
        var duplicateVersion = candidate.GetProperty("version").GetString()!;

        NewKey(adminA);
        var body = Body(new
        {
            mergedPartyId = duplicate,
            expectedVersion = survivorVersion,
            mergedPartyExpectedVersion = duplicateVersion,
            reason = "  Aynı VKN ile açılmış mükerrer kart  ",
        });
        using (var merged = await PutAsync(adminA, PathFor(survivor), body))
        {
            Assert.Equal(HttpStatusCode.NoContent, merged.StatusCode);
        }

        using (var replay = await PutAsync(adminA, PathFor(survivor), body))
        {
            Assert.Equal(HttpStatusCode.NoContent, replay.StatusCode);
            Assert.True(replay.Headers.Contains("Idempotency-Replayed"));
        }

        // The same key with another body is not a replay.
        using (var reused = await PutAsync(adminA, PathFor(survivor), Body(new
               {
                   mergedPartyId = duplicate,
                   expectedVersion = survivorVersion,
                   mergedPartyExpectedVersion = duplicateVersion,
                   reason = "Başka gerekçe",
               })))
        {
            await AssertProblemAsync(reused, HttpStatusCode.Conflict, "idempotency_conflict");
        }

        // The duplicate is passive, points at the survivor and moved one version; the survivor did not move.
        Assert.Equal(survivor, await test.ScalarAsync<Guid>(
            "SELECT [MergedIntoPartyId] FROM [counterparty].[Parties] WHERE [Id]=@Party;", duplicate));
        Assert.False(await test.ScalarAsync<bool>(
            "SELECT [IsActive] FROM [counterparty].[Parties] WHERE [Id]=@Party;", duplicate));
        Assert.Equal(long.Parse(duplicateVersion, CultureInfo.InvariantCulture) + 1, await test.ScalarAsync<long>(
            "SELECT [Version] FROM [counterparty].[Parties] WHERE [Id]=@Party;", duplicate));
        Assert.Equal(long.Parse(survivorVersion, CultureInfo.InvariantCulture), await test.ScalarAsync<long>(
            "SELECT [Version] FROM [counterparty].[Parties] WHERE [Id]=@Party;", survivor));
        Assert.Equal(0, await test.ScalarAsync<int>(
            "SELECT COUNT(*) FROM [counterparty].[Parties] WHERE [Id]=@Party AND [MergedIntoPartyId] IS NOT NULL;", survivor));

        // One append-only record, approved in company A, naming both versions the approval was given on.
        Assert.Equal(1, await test.ScalarAsync<int>("SELECT COUNT(*) FROM [counterparty].[PartyMerges];", duplicate));
        var mergeId = await test.ScalarAsync<Guid>(
            "SELECT [Id] FROM [counterparty].[PartyMerges] WHERE [MergedPartyId]=@Party AND [SurvivorPartyId]=@Other "
            + "AND [ApprovalCompanyId]=@CompanyA AND [Reason]=N'Aynı VKN ile açılmış mükerrer kart';",
            duplicate, survivor);
        Assert.Equal(long.Parse(survivorVersion, CultureInfo.InvariantCulture), await test.ScalarAsync<long>(
            "SELECT [SurvivorPartyVersion] FROM [counterparty].[PartyMerges] WHERE [MergedPartyId]=@Party;", duplicate));

        // One merged event per holding company (A holds both, B holds the duplicate), each row its own company's.
        Assert.Equal(2, await test.ScalarAsync<int>(
            "SELECT COUNT(*) FROM [counterparty].[OutboxMessages] WHERE [EventType]=@Event;", duplicate));
        foreach (var company in new[] { TestCompanyIds.CompanyA, TestCompanyIds.CompanyB })
        {
            var payload = await test.ScalarAsync<string>(
                "SELECT [PayloadJson] FROM [counterparty].[OutboxMessages] WHERE [EventType]=@Event AND [CompanyId]=@ReceivingCompany "
                + "AND [AggregateId]=@Merge AND [AggregateType]=@AggregateType AND [Sequence]=1 AND [AggregateVersion]=1;",
                duplicate, company: company, merge: mergeId);
            using var document = JsonDocument.Parse(payload);
            var root = document.RootElement;
            Assert.Equal(company, root.GetProperty("companyId").GetGuid());
            Assert.Equal(mergeId, root.GetProperty("mergeId").GetGuid());
            Assert.Equal(survivor, root.GetProperty("survivorPartyId").GetGuid());
            Assert.Equal(duplicate, root.GetProperty("mergedPartyId").GetGuid());
            Assert.Equal(TestCompanyIds.CompanyA, root.GetProperty("approvalCompanyId").GetGuid());
            Assert.False(root.TryGetProperty("reason", out _));
        }

        // One audit event in the approving company, under the merge permission, with no approval-chain context.
        Assert.Equal(1, await test.ScalarAsync<int>(
            "SELECT COUNT(*) FROM [audit].[AuditEvents] WHERE [EventType]=@Event;", duplicate));
        Assert.Equal(1, await test.ScalarAsync<int>(
            "SELECT COUNT(*) FROM [audit].[AuditEvents] WHERE [EventType]=@Event AND [CompanyId]=@CompanyA "
            + "AND [EntityId]=LOWER(CONVERT(nvarchar(36), @Party)) AND [PermissionCode]=@Permission "
            + "AND [ApprovalContextJson] IS NULL;", duplicate));
        Assert.Equal(1, await test.ScalarAsync<int>(
            "SELECT COUNT(*) FROM [idempotency].[CommandExecutions] WHERE [CommandName]=@Command;", duplicate));

        // The merged card now shows where it went: A holds the survivor and sees its code; B does not, so no code.
        var fromA = await ReadAsync(adminA, PathFor(duplicate));
        Assert.Equal(survivor, fromA.GetProperty("mergedInto").GetProperty("partyId").GetGuid());
        Assert.Equal(view.GetProperty("code").GetString(), fromA.GetProperty("mergedInto").GetProperty("code").GetString());
        Assert.Empty(fromA.GetProperty("candidates").EnumerateArray());
        Assert.False(fromA.GetProperty("viewer").GetProperty("canApproveMerge").GetBoolean());
        var fromB = await ReadAsync(adminB, PathFor(duplicate));
        Assert.Equal(survivor, fromB.GetProperty("mergedInto").GetProperty("partyId").GetGuid());
        Assert.Equal(JsonValueKind.Null, fromB.GetProperty("mergedInto").GetProperty("code").ValueKind);
        Assert.Empty((await ReadAsync(adminA, PathFor(survivor))).GetProperty("candidates").EnumerateArray());
    }

    [SqlServerFact]
    public async Task Stale_versions_merged_parties_and_different_numbers_are_replayable_409s_that_write_nothing()
    {
        await using var test = new MergeTest(grantMerge: true);
        using var admin = test.Client();
        var survivor = await CreatePartyAsync(admin, "Ana Kart");
        var duplicate = await CreatePartyAsync(admin, "Mükerrer Kart");
        var third = await CreatePartyAsync(admin, "Üçüncü Kart");
        var foreignNumber = await CreatePartyAsync(admin, "Farklı Numara");
        await EnterTaxIdentityAsync(admin, survivor, Vkn);
        await EnterTaxIdentityAsync(admin, foreignNumber, OtherVkn);

        // Stale survivor and stale duplicate versions are version conflicts and replay for the same key.
        var staleSurvivor = MergeBody(duplicate, "99", await VersionAsync(admin, duplicate));
        NewKey(admin);
        using (var conflict = await PutAsync(admin, PathFor(survivor), staleSurvivor))
        {
            await AssertProblemAsync(conflict, HttpStatusCode.Conflict, "version_conflict");
        }

        using (var conflictReplay = await PutAsync(admin, PathFor(survivor), staleSurvivor))
        {
            await AssertProblemAsync(conflictReplay, HttpStatusCode.Conflict, "version_conflict");
            Assert.True(conflictReplay.Headers.Contains("Idempotency-Replayed"));
        }

        NewKey(admin);
        using (var staleDuplicate = await PutAsync(admin, PathFor(survivor),
                   MergeBody(duplicate, await VersionAsync(admin, survivor), "99")))
        {
            await AssertProblemAsync(staleDuplicate, HttpStatusCode.Conflict, "version_conflict");
        }

        // Two different VKN/TCKN values are not duplicates.
        NewKey(admin);
        using (var mismatch = await PutAsync(admin, PathFor(survivor),
                   MergeBody(foreignNumber, await VersionAsync(admin, survivor), await VersionAsync(admin, foreignNumber))))
        {
            await AssertProblemAsync(mismatch, HttpStatusCode.Conflict, "party_merge_tax_identity_mismatch");
        }

        // An unknown duplicate is a 404; nothing has been merged so far.
        NewKey(admin);
        using (var missing = await PutAsync(admin, PathFor(survivor),
                   MergeBody(Guid.NewGuid(), await VersionAsync(admin, survivor), "1")))
        {
            await AssertProblemAsync(missing, HttpStatusCode.NotFound, "not_found");
        }

        Assert.Equal(0, await test.ScalarAsync<int>("SELECT COUNT(*) FROM [counterparty].[PartyMerges];", survivor));
        Assert.Equal(0, await test.ScalarAsync<int>(
            "SELECT COUNT(*) FROM [counterparty].[Parties] WHERE [MergedIntoPartyId] IS NOT NULL;", survivor));

        await MergeAsync(admin, survivor, duplicate);

        // The duplicate cannot be merged again (into any card), and it cannot become a merge target either.
        NewKey(admin);
        using (var again = await PutAsync(admin, PathFor(third),
                   MergeBody(duplicate, await VersionAsync(admin, third), await VersionAsync(admin, duplicate))))
        {
            await AssertProblemAsync(again, HttpStatusCode.Conflict, "party_already_merged");
        }

        NewKey(admin);
        using (var intoMerged = await PutAsync(admin, PathFor(duplicate),
                   MergeBody(third, await VersionAsync(admin, duplicate), await VersionAsync(admin, third))))
        {
            await AssertProblemAsync(intoMerged, HttpStatusCode.Conflict, "party_merge_target_merged");
        }

        Assert.Equal(1, await test.ScalarAsync<int>("SELECT COUNT(*) FROM [counterparty].[PartyMerges];", survivor));
        Assert.Equal(1, await test.ScalarAsync<int>(
            "SELECT COUNT(*) FROM [counterparty].[OutboxMessages] WHERE [EventType]=@Event;", survivor));
        Assert.Equal(1, await test.ScalarAsync<int>(
            "SELECT COUNT(*) FROM [audit].[AuditEvents] WHERE [EventType]=@Event;", survivor));
        Assert.True(await test.ScalarAsync<bool>("SELECT [IsActive] FROM [counterparty].[Parties] WHERE [Id]=@Party;", third));
        Assert.Equal(0, await test.ScalarAsync<int>(
            "SELECT COUNT(*) FROM [counterparty].[Parties] WHERE [Id]=@Party AND [MergedIntoPartyId] IS NOT NULL;", third));
    }

    [SqlServerFact]
    public async Task Self_merges_blank_reasons_and_malformed_bodies_are_400_and_write_nothing()
    {
        await using var test = new MergeTest(grantMerge: true);
        using var admin = test.Client();
        var survivor = await CreatePartyAsync(admin, "Ana Kart");
        var duplicate = await CreatePartyAsync(admin, "Mükerrer Kart");
        var survivorVersion = await VersionAsync(admin, survivor);
        var duplicateVersion = await VersionAsync(admin, duplicate);

        foreach (var invalid in new[]
                 {
                     MergeBody(survivor, survivorVersion, survivorVersion),
                     MergeBody(Guid.Empty, survivorVersion, duplicateVersion),
                     MergeBody(duplicate, survivorVersion, duplicateVersion, "   "),
                     MergeBody(duplicate, survivorVersion, duplicateVersion, new string('x', 1001)),
                     MergeBody(duplicate, survivorVersion, "0"),
                     MergeBody(duplicate, "01", duplicateVersion),
                     Body(new { mergedPartyId = duplicate, expectedVersion = survivorVersion, reason = "Eksik sürüm" }),
                     Body(new { mergedPartyId = duplicate, expectedVersion = 1, mergedPartyExpectedVersion = "1", reason = "Sayı" }),
                     Body(new
                     {
                         mergedPartyId = duplicate, expectedVersion = survivorVersion,
                         mergedPartyExpectedVersion = duplicateVersion, reason = "Gizli alan", unmerge = true,
                     }),
                 })
        {
            NewKey(admin);
            using var refused = await PutAsync(admin, PathFor(survivor), invalid);
            Assert.Equal(HttpStatusCode.BadRequest, refused.StatusCode);
        }

        using (var query = await admin.GetAsync($"{PathFor(survivor)}?companyId={TestCompanyIds.CompanyB}"))
        {
            Assert.Equal(HttpStatusCode.BadRequest, query.StatusCode);
        }

        Assert.Equal(0, await test.ScalarAsync<int>("SELECT COUNT(*) FROM [counterparty].[PartyMerges];", survivor));
        Assert.Equal(0, await test.ScalarAsync<int>(
            "SELECT COUNT(*) FROM [counterparty].[OutboxMessages] WHERE [EventType]=@Event;", survivor));
        Assert.Equal(duplicateVersion, await VersionAsync(admin, duplicate));
    }

    [SqlServerFact]
    public async Task Without_the_merge_grant_in_every_holding_company_nothing_is_merged()
    {
        // No shipped role carries the merge permission: the group administrator is refused by the endpoint fence.
        await using (var ungranted = new MergeTest(grantMerge: false))
        {
            using var admin = ungranted.Client();
            var survivor = await CreatePartyAsync(admin, "Yetkisiz Ana Kart");
            var duplicate = await CreatePartyAsync(admin, "Yetkisiz Mükerrer Kart");
            var view = await ReadAsync(admin, PathFor(survivor));
            Assert.False(view.GetProperty("viewer").GetProperty("canApproveMerge").GetBoolean());

            NewKey(admin);
            using var denied = await PutAsync(admin, PathFor(survivor),
                MergeBody(duplicate, view.GetProperty("version").GetString()!, await VersionAsync(admin, duplicate)));
            Assert.Equal(HttpStatusCode.Forbidden, denied.StatusCode);
            Assert.Equal(0, await ungranted.ScalarAsync<int>("SELECT COUNT(*) FROM [counterparty].[PartyMerges];", survivor));
        }

        // Company A's operator holds the grant in A only: a duplicate that company B also holds is not reachable (404),
        // while a pair held by A alone is.
        await using var test = new MergeTest(grantMerge: true);
        using var groupAdmin = test.Client();
        using var groupAdminB = test.Client(TestCompanyIds.CompanyB);
        using var operatorA = test.Client(TestCompanyIds.CompanyA, TestUsers.CompanyA);
        var anchor = await CreatePartyAsync(groupAdmin, "A Ana Kart");
        var shared = await CreatePartyAsync(groupAdmin, "A ve B Ortak Kart");
        var local = await CreatePartyAsync(groupAdmin, "Yalnız A Kartı");
        await LinkAsync(groupAdminB, shared);

        NewKey(operatorA);
        using (var hidden = await PutAsync(operatorA, PathFor(anchor),
                   MergeBody(shared, await VersionAsync(operatorA, anchor), await VersionAsync(groupAdmin, shared))))
        {
            await AssertProblemAsync(hidden, HttpStatusCode.NotFound, "not_found");
        }

        Assert.Equal(0, await test.ScalarAsync<int>("SELECT COUNT(*) FROM [counterparty].[PartyMerges];", anchor));

        NewKey(operatorA);
        using (var allowed = await PutAsync(operatorA, PathFor(anchor),
                   MergeBody(local, await VersionAsync(operatorA, anchor), await VersionAsync(operatorA, local))))
        {
            Assert.Equal(HttpStatusCode.NoContent, allowed.StatusCode);
        }

        Assert.Equal(1, await test.ScalarAsync<int>(
            "SELECT COUNT(*) FROM [counterparty].[OutboxMessages] WHERE [EventType]=@Event AND [CompanyId]=@CompanyA;", anchor));
        Assert.Equal(1, await test.ScalarAsync<int>(
            "SELECT COUNT(*) FROM [counterparty].[OutboxMessages] WHERE [EventType]=@Event;", anchor));

        // Company B holds no card of the anchor: it can neither read nor approve a merge into it.
        using (var notHeld = await groupAdminB.GetAsync(PathFor(anchor)))
        {
            Assert.Equal(HttpStatusCode.NotFound, notHeld.StatusCode);
        }

        NewKey(groupAdminB);
        using (var notHeldWrite = await PutAsync(groupAdminB, PathFor(anchor), MergeBody(shared, "1", "1")))
        {
            await AssertProblemAsync(notHeldWrite, HttpStatusCode.NotFound, "not_found");
        }

        Assert.Equal(1, await test.ScalarAsync<int>("SELECT COUNT(*) FROM [counterparty].[PartyMerges];", anchor));
        Assert.Equal(0, await test.ScalarAsync<int>(
            "SELECT COUNT(*) FROM [counterparty].[Parties] WHERE [Id]=@Party AND [MergedIntoPartyId] IS NOT NULL;", shared));
    }

    [SqlServerFact]
    public async Task The_database_refuses_a_self_pointing_party_an_active_merged_party_and_a_second_merge_record()
    {
        await using var test = new MergeTest(grantMerge: true);
        using var admin = test.Client();
        var survivor = await CreatePartyAsync(admin, "Ana Kart");
        var duplicate = await CreatePartyAsync(admin, "Mükerrer Kart");
        var third = await CreatePartyAsync(admin, "Üçüncü Kart");

        var selfPointing = await Assert.ThrowsAsync<SqlException>(() => test.ExecuteAsync(
            "UPDATE [counterparty].[Parties] SET [MergedIntoPartyId]=[Id], [IsActive]=0 WHERE [Id]=@Party;", third));
        Assert.Equal(CheckViolation, selfPointing.Number);
        var activeMerged = await Assert.ThrowsAsync<SqlException>(() => test.ExecuteAsync(
            "UPDATE [counterparty].[Parties] SET [MergedIntoPartyId]=@Other WHERE [Id]=@Party;", third, survivor));
        Assert.Equal(CheckViolation, activeMerged.Number);

        await MergeAsync(admin, survivor, duplicate);

        var second = await Assert.ThrowsAsync<SqlException>(() => test.ExecuteAsync(
            """
            INSERT INTO [counterparty].[PartyMerges]
                ([Id], [ApprovalCompanyId], [SurvivorPartyId], [SurvivorPartyVersion], [MergedPartyId],
                 [MergedPartyVersion], [Reason], [ApprovedBy], [ApprovedAtUtc], [CorrelationId])
            VALUES (NEWID(), @CompanyA, @Other, 1, @Party, 9, N'İkinci kayıt', N'sql-raw', SYSDATETIMEOFFSET(), N'sql-raw');
            """, duplicate, third));
        Assert.Equal(UniqueIndexViolation, second.Number);
        var selfMerge = await Assert.ThrowsAsync<SqlException>(() => test.ExecuteAsync(
            """
            INSERT INTO [counterparty].[PartyMerges]
                ([Id], [ApprovalCompanyId], [SurvivorPartyId], [SurvivorPartyVersion], [MergedPartyId],
                 [MergedPartyVersion], [Reason], [ApprovedBy], [ApprovedAtUtc], [CorrelationId])
            VALUES (NEWID(), @CompanyA, @Party, 1, @Party, 1, N'Kendisi', N'sql-raw', SYSDATETIMEOFFSET(), N'sql-raw');
            """, third));
        Assert.Equal(CheckViolation, selfMerge.Number);

        Assert.Equal(1, await test.ScalarAsync<int>("SELECT COUNT(*) FROM [counterparty].[PartyMerges];", survivor));
        Assert.Equal(0, await test.ScalarAsync<int>(
            "SELECT COUNT(*) FROM [counterparty].[Parties] WHERE [Id]=@Party AND [MergedIntoPartyId] IS NOT NULL;", third));
    }

    [SqlServerFact]
    public async Task The_party_merge_migrations_Down_passes_with_no_merge_and_refuses_while_a_merge_exists()
    {
        await using var test = new MergeTest(grantMerge: true);
        using var admin = test.Client();
        var guard = Assert.IsType<SqlOperation>(new AddCounterpartyPartyMerge().DownOperations[0]).Sql;

        // No merge yet: the guard passes, the migration rolls back and migrates forward again.
        await test.ExecuteAsync(guard, Guid.Empty);
        await test.MigrateCounterpartyToAsync(PreviousMigration);
        Assert.Equal(0, await test.ScalarAsync<int>(
            "SELECT COUNT(*) FROM sys.tables WHERE [object_id] = OBJECT_ID(N'[counterparty].[PartyMerges]');", Guid.Empty));
        await test.MigrateCounterpartyToAsync(null);

        var survivor = await CreatePartyAsync(admin, "Geri Alma Korumalı Ana Kart");
        var duplicate = await CreatePartyAsync(admin, "Geri Alma Korumalı Mükerrer Kart");
        await MergeAsync(admin, survivor, duplicate);

        var refused = await Assert.ThrowsAsync<SqlException>(() => test.ExecuteAsync(guard, Guid.Empty));
        Assert.Equal(DownGuardNumber, refused.Number);
        var migratorRefused = await Assert.ThrowsAnyAsync<SqlException>(() =>
            test.MigrateCounterpartyToAsync(PreviousMigration));
        Assert.Equal(DownGuardNumber, migratorRefused.Number);
        Assert.Equal(1, await test.ScalarAsync<int>("SELECT COUNT(*) FROM [counterparty].[PartyMerges];", duplicate));
        Assert.Equal(survivor, await test.ScalarAsync<Guid>(
            "SELECT [MergedIntoPartyId] FROM [counterparty].[Parties] WHERE [Id]=@Party;", duplicate));
    }

    private static string PathFor(Guid party) => $"/api/v1/counterparties/{party}/merge";

    private static string Body(object value) => JsonSerializer.Serialize(value);

    private static string MergeBody(
        Guid mergedPartyId, string expectedVersion, string mergedPartyExpectedVersion,
        string reason = "Mükerrer kart birleştirmesi") =>
        Body(new { mergedPartyId, expectedVersion, mergedPartyExpectedVersion, reason });

    private static async Task<Guid> CreatePartyAsync(HttpClient client, string legalName)
    {
        NewKey(client);
        using var response = await client.PostAsJsonAsync("/api/v1/counterparties",
            new { kind = 1, legalName = $"{legalName} {Guid.NewGuid():N}", roles = 1 });
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return (await JsonAsync(response)).GetProperty("id").GetGuid();
    }

    private static async Task LinkAsync(HttpClient client, Guid party)
    {
        NewKey(client);
        using var response = await client.PostAsJsonAsync($"/api/v1/counterparties/{party}/company-account", new { roles = 0 });
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
    }

    private static async Task EnterTaxIdentityAsync(HttpClient client, Guid party, string number)
    {
        var path = $"/api/v1/counterparties/{party}/tax-identity";
        var version = (await ReadAsync(client, path)).GetProperty("version").GetString();
        NewKey(client);
        using var response = await PutAsync(client, path, Body(new { expectedVersion = version, taxIdentityNumber = number }));
        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
    }

    private static async Task<string> VersionAsync(HttpClient client, Guid party) =>
        (await ReadAsync(client, PathFor(party))).GetProperty("version").GetString()!;

    private static async Task MergeAsync(HttpClient client, Guid survivor, Guid duplicate)
    {
        var body = MergeBody(duplicate, await VersionAsync(client, survivor), await VersionAsync(client, duplicate));
        NewKey(client);
        using var response = await PutAsync(client, PathFor(survivor), body);
        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
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

    private sealed class MergeTest : IAsyncDisposable
    {
        private readonly string _baseConnection = Environment.GetEnvironmentVariable(
            SqlServerRelationalAcceptanceTests.ConnectionStringEnvironmentVariable)!;

        public MergeTest(bool grantMerge)
        {
            Factory = new SqlServerRelationalApiFactory(_baseConnection, services =>
            {
                // Test-only grants on the disposable database; no shipped seed role is changed.
                services.PostConfigure<IdentitySeedOptions>(options =>
                {
                    var administrator = options.Roles.Single(role => role.Code == "SYSTEM_ADMINISTRATOR");
                    administrator.Permissions.Add(CounterpartyTaxIdentityPermissionsV1.Update);
                    if (!grantMerge)
                    {
                        return;
                    }

                    administrator.Permissions.Add(ErpPermissions.CounterpartyPartiesMergeApprove);
                    var operatorRole = options.Roles.Single(role => role.Code == "ORGANIZATION_OPERATOR");
                    operatorRole.Permissions.Add(ErpPermissions.CounterpartyPartiesRead);
                    operatorRole.Permissions.Add(ErpPermissions.CounterpartyPartiesMergeApprove);
                });
            });
        }

        public SqlServerRelationalApiFactory Factory { get; }

        public HttpClient Client(Guid? company = null, string user = TestUsers.GroupAdmin) =>
            Factory.CreateClientFor(user, company ?? TestCompanyIds.CompanyA);

        public async Task<T> ScalarAsync<T>(
            string sql, Guid party, Guid? other = null, Guid? company = null, Guid? merge = null)
        {
            await using var connection = new SqlConnection(Factory.DatabaseConnectionString);
            await connection.OpenAsync();
            await using var command = CreateCommand(connection, sql, party, other, company, merge);
            return Assert.IsType<T>(await command.ExecuteScalarAsync());
        }

        public async Task ExecuteAsync(string sql, Guid party, Guid? other = null)
        {
            await using var connection = new SqlConnection(Factory.DatabaseConnectionString);
            await connection.OpenAsync();
            await using var command = CreateCommand(connection, sql, party, other, null, null);
            await command.ExecuteNonQueryAsync();
        }

        public async Task MigrateCounterpartyToAsync(string? migration)
        {
            await using var scope = Factory.Services.CreateAsyncScope();
            var dbContext = scope.ServiceProvider.GetRequiredService<CounterpartyDbContext>();
            await dbContext.GetService<IMigrator>().MigrateAsync(migration);
        }

        private static SqlCommand CreateCommand(
            SqlConnection connection, string sql, Guid party, Guid? other, Guid? company, Guid? merge)
        {
            var command = connection.CreateCommand();
            command.CommandText = sql;
            if (sql.Contains("@Party", StringComparison.Ordinal)) command.Parameters.AddWithValue("@Party", party);
            if (sql.Contains("@Other", StringComparison.Ordinal)) command.Parameters.AddWithValue("@Other", other!.Value);
            if (sql.Contains("@CompanyA", StringComparison.Ordinal))
            {
                command.Parameters.AddWithValue("@CompanyA", TestCompanyIds.CompanyA);
            }

            if (sql.Contains("@ReceivingCompany", StringComparison.Ordinal))
            {
                command.Parameters.AddWithValue("@ReceivingCompany", company!.Value);
            }

            if (sql.Contains("@Merge", StringComparison.Ordinal)) command.Parameters.AddWithValue("@Merge", merge!.Value);
            if (sql.Contains("@Event", StringComparison.Ordinal))
            {
                command.Parameters.AddWithValue("@Event", CounterpartyPartyMergedOutboxV1.EventType);
            }

            if (sql.Contains("@AggregateType", StringComparison.Ordinal))
            {
                command.Parameters.AddWithValue("@AggregateType", CounterpartyPartyMergedOutboxV1.AggregateType);
            }

            if (sql.Contains("@Command", StringComparison.Ordinal)) command.Parameters.AddWithValue("@Command", CommandName);
            if (sql.Contains("@Permission", StringComparison.Ordinal))
            {
                command.Parameters.AddWithValue("@Permission", ErpPermissions.CounterpartyPartiesMergeApprove);
            }

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
