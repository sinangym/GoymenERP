using System.Globalization;
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

/// <summary>
/// ALS-D111 + ALS-D113 (replacing the one-step ALS-D95/ALS-D110 path) — the Cari official legal name change is
/// maker-checker, against real SQL Server through the real API: a proposal under
/// <c>counterparties.legal_name.propose</c> leaves the legal name unchanged; one approval under
/// <c>counterparties.legal_name.approve</c> by a different person applies it (the proposer is refused even holding both
/// names); a rejection or a stale request never changes it; at most one pending request per party and company; the
/// Idempotency-Key replay; the PARTY version fence; one audit per real change; a dated identity revision on approval;
/// the active-company account as the existence fence; and the forward-fix migration guard. Both permissions are granted
/// here only on the disposable database; no shipped role carries them.
/// </summary>
public sealed class CounterpartyLegalNameSqlServerRelationalAcceptanceTests
{
    private const string ProposeCommand = "counterparty.party.legal-name.change-request.v1";
    private const string DecideCommand = "counterparty.party.legal-name.change-decision.v1";
    private const string RequestedEvent = "counterparty.party.legal-name.change-requested.v1";
    private const string ApprovedEvent = "counterparty.party.legal-name.change-approved.v1";
    private const string RejectedEvent = "counterparty.party.legal-name.change-rejected.v1";
    private const string PreviousMigration = "20261115165158_AddCounterpartyPartyMerge";
    private const int LegalNameAttribute = 2;
    private const int DatedChangeKind = 4;

    [SqlServerFact]
    public async Task A_legal_name_change_stays_pending_until_a_different_person_approves_it()
    {
        await using var test = new LegalNameTest();
        using var maker = test.Client();
        using var checker = test.Client(TestCompanyIds.CompanyA, TestUsers.CompanyA);
        var party = await CreatePartyAsync(maker, "Eski Unvan Ticaret Anonim Şirketi");

        var before = await ReadAsync(maker, PathFor(party));
        Assert.Equal(party, before.GetProperty("partyId").GetGuid());
        Assert.Equal(TestCompanyIds.CompanyA, before.GetProperty("companyId").GetGuid());
        Assert.Equal("Eski Unvan Ticaret Anonim Şirketi", before.GetProperty("legalName").GetString());
        Assert.Equal(JsonValueKind.Null, before.GetProperty("pendingChange").ValueKind);
        Assert.True(before.GetProperty("viewer").GetProperty("canPropose").GetBoolean());
        var version = before.GetProperty("version").GetString()!;

        // The same name is not a request.
        NewKey(maker);
        using (var same = await PostAsync(maker, RequestsPath(party),
                   Body(new { expectedVersion = version, proposedLegalName = " Eski Unvan Ticaret Anonim Şirketi ", reason = "Aynı" })))
        {
            await AssertProblemAsync(same, HttpStatusCode.Conflict, "legal_name_unchanged");
        }

        NewKey(maker);
        var proposal = Body(new
        {
            expectedVersion = version,
            proposedLegalName = "  Yeni Unvan Sanayi Anonim Şirketi  ",
            reason = "Ticaret sicilinde unvan değişikliği tescil edildi",
        });
        using (var requested = await PostAsync(maker, RequestsPath(party), proposal))
        {
            Assert.Equal(HttpStatusCode.NoContent, requested.StatusCode);
        }

        using (var replay = await PostAsync(maker, RequestsPath(party), proposal))
        {
            Assert.Equal(HttpStatusCode.NoContent, replay.StatusCode);
            Assert.True(replay.Headers.Contains("Idempotency-Replayed"));
        }

        using (var reused = await PostAsync(maker, RequestsPath(party),
                   Body(new { expectedVersion = version, proposedLegalName = "Başka Unvan", reason = "Başka" })))
        {
            await AssertProblemAsync(reused, HttpStatusCode.Conflict, "idempotency_conflict");
        }

        // The proposal does not change the legal name or the PARTY version.
        var pendingView = await ReadAsync(maker, PathFor(party));
        Assert.Equal("Eski Unvan Ticaret Anonim Şirketi", pendingView.GetProperty("legalName").GetString());
        Assert.Equal(version, pendingView.GetProperty("version").GetString());
        var pending = pendingView.GetProperty("pendingChange");
        Assert.Equal("pending", pending.GetProperty("status").GetString());
        Assert.Equal("Eski Unvan Ticaret Anonim Şirketi", pending.GetProperty("currentLegalName").GetString());
        Assert.Equal("Yeni Unvan Sanayi Anonim Şirketi", pending.GetProperty("proposedLegalName").GetString());
        Assert.True(pendingView.GetProperty("viewer").GetProperty("isRequesterOfPending").GetBoolean());
        var requestId = pending.GetProperty("id").GetGuid();
        Assert.Equal("Eski Unvan Ticaret Anonim Şirketi", await test.ScalarAsync<string>(
            "SELECT [LegalName] FROM [counterparty].[Parties] WHERE [Id]=@Party;", party));

        var checkerView = await ReadAsync(checker, PathFor(party));
        Assert.False(checkerView.GetProperty("viewer").GetProperty("isRequesterOfPending").GetBoolean());
        Assert.True(checkerView.GetProperty("viewer").GetProperty("canApprove").GetBoolean());
        Assert.False(checkerView.GetProperty("viewer").GetProperty("canPropose").GetBoolean());

        // A second pending request on the same party is refused.
        NewKey(maker);
        using (var second = await PostAsync(maker, RequestsPath(party),
                   Body(new { expectedVersion = version, proposedLegalName = "Üçüncü Unvan", reason = "İkinci" })))
        {
            await AssertProblemAsync(second, HttpStatusCode.Conflict, "legal_name_change_pending");
        }

        // The proposer holds approve too, and is still refused: the rule, not the grant.
        foreach (var decision in new[] { CounterpartyLegalNameDecisionsV1.Approve, CounterpartyLegalNameDecisionsV1.Reject })
        {
            NewKey(maker);
            using var self = await PostAsync(maker, DecisionsPath(party),
                Body(new { expectedVersion = version, changeRequestId = requestId, decision }));
            await AssertProblemAsync(self, HttpStatusCode.Conflict, "legal_name_self_decision");
        }

        Assert.Equal("Eski Unvan Ticaret Anonim Şirketi", await test.ScalarAsync<string>(
            "SELECT [LegalName] FROM [counterparty].[Parties] WHERE [Id]=@Party;", party));

        NewKey(checker);
        var approval = Body(new
        {
            expectedVersion = version,
            changeRequestId = requestId,
            decision = CounterpartyLegalNameDecisionsV1.Approve,
            note = "Sicil gazetesi kontrol edildi",
        });
        using (var approved = await PostAsync(checker, DecisionsPath(party), approval))
        {
            Assert.Equal(HttpStatusCode.NoContent, approved.StatusCode);
        }

        using (var approvalReplay = await PostAsync(checker, DecisionsPath(party), approval))
        {
            Assert.Equal(HttpStatusCode.NoContent, approvalReplay.StatusCode);
            Assert.True(approvalReplay.Headers.Contains("Idempotency-Replayed"));
        }

        var after = await ReadAsync(checker, PathFor(party));
        Assert.Equal("Yeni Unvan Sanayi Anonim Şirketi", after.GetProperty("legalName").GetString());
        Assert.Equal(
            (long.Parse(version, CultureInfo.InvariantCulture) + 1).ToString(CultureInfo.InvariantCulture),
            after.GetProperty("version").GetString());
        Assert.Equal(JsonValueKind.Null, after.GetProperty("pendingChange").ValueKind);
        var decided = after.GetProperty("lastDecidedChange");
        Assert.Equal("approved", decided.GetProperty("status").GetString());
        Assert.Equal("Sicil gazetesi kontrol edildi", decided.GetProperty("decisionNote").GetString());
        Assert.Equal(1, await test.ScalarAsync<int>(
            "SELECT COUNT(*) FROM [counterparty].[Parties] WHERE [Id]=@Party AND [TradeName] IS NULL;", party));

        // A decided request cannot be decided again.
        NewKey(checker);
        using (var again = await PostAsync(checker, DecisionsPath(party),
                   Body(new { expectedVersion = after.GetProperty("version").GetString(), changeRequestId = requestId, decision = "reject" })))
        {
            await AssertProblemAsync(again, HttpStatusCode.Conflict, "legal_name_change_not_pending");
        }

        // One audit per real change: the request, then the approval with old and new names and the approval context.
        Assert.Equal(1, await test.ScalarAsync<int>(
            "SELECT COUNT(*) FROM [audit].[AuditEvents] WHERE [EventType]=@Event;", party, RequestedEvent));
        Assert.Equal(1, await test.ScalarAsync<int>(
            "SELECT COUNT(*) FROM [audit].[AuditEvents] WHERE [EventType]=@Event AND [EntityId]=LOWER(CONVERT(nvarchar(36), @Party)) "
            + "AND [PermissionCode]=@Permission AND [ApprovalContextJson] IS NOT NULL;",
            party, ApprovedEvent, permission: CounterpartyLegalNamePermissionsV1.Approve));
        Assert.Equal("Eski Unvan Ticaret Anonim Şirketi", await test.ScalarAsync<string>(
            "SELECT JSON_VALUE([BeforeJson], '$.LegalName') FROM [audit].[AuditEvents] WHERE [EventType]=@Event;", party, ApprovedEvent));
        Assert.Equal("Yeni Unvan Sanayi Anonim Şirketi", await test.ScalarAsync<string>(
            "SELECT JSON_VALUE([AfterJson], '$.LegalName') FROM [audit].[AuditEvents] WHERE [EventType]=@Event;", party, ApprovedEvent));
        Assert.Equal(1, await test.ScalarAsync<int>(
            "SELECT COUNT(*) FROM [counterparty].[PartyLegalNameChangeRequests] WHERE [PartyId]=@Party AND [Status]=N'Approved' "
            + "AND [DecidedBy] <> [RequestedBy];", party));

        // KLN-D38 (3): the approved name is kept as a dated identity revision.
        Assert.Equal(1, await test.ScalarAsync<int>(
            $"SELECT COUNT(*) FROM [counterparty].[PartyIdentityRevisions] WHERE [PartyId]=@Party AND [Attribute]={LegalNameAttribute} "
            + $"AND [Kind]={DatedChangeKind} AND [Value]=N'Yeni Unvan Sanayi Anonim Şirketi' AND [SourceChangeRequestId] IS NULL;", party));

        Assert.True(await test.ScalarAsync<int>(
            "SELECT COUNT(*) FROM [idempotency].[CommandExecutions] WHERE [CommandName]=@Command;", party, command: ProposeCommand) >= 1);
        Assert.True(await test.ScalarAsync<int>(
            "SELECT COUNT(*) FROM [idempotency].[CommandExecutions] WHERE [CommandName]=@Command;", party, command: DecideCommand) >= 1);
    }

    [SqlServerFact]
    public async Task A_rejected_or_stale_request_never_changes_the_legal_name()
    {
        await using var test = new LegalNameTest();
        using var maker = test.Client();
        using var checker = test.Client(TestCompanyIds.CompanyA, TestUsers.CompanyA);
        var party = await CreatePartyAsync(maker, "Örnek Unvan");
        var version = (await ReadAsync(maker, PathFor(party))).GetProperty("version").GetString()!;

        var rejectedId = await ProposeAsync(maker, party, version, "Reddedilecek Unvan");
        NewKey(checker);
        using (var rejected = await PostAsync(checker, DecisionsPath(party),
                   Body(new { expectedVersion = version, changeRequestId = rejectedId, decision = "reject", note = "Belge eksik" })))
        {
            Assert.Equal(HttpStatusCode.NoContent, rejected.StatusCode);
        }

        var afterReject = await ReadAsync(maker, PathFor(party));
        Assert.Equal("Örnek Unvan", afterReject.GetProperty("legalName").GetString());
        Assert.Equal(version, afterReject.GetProperty("version").GetString());
        Assert.Equal("rejected", afterReject.GetProperty("lastDecidedChange").GetProperty("status").GetString());
        Assert.Equal(1, await test.ScalarAsync<int>(
            "SELECT COUNT(*) FROM [audit].[AuditEvents] WHERE [EventType]=@Event;", party, RejectedEvent));
        Assert.Equal(0, await test.ScalarAsync<int>(
            "SELECT COUNT(*) FROM [audit].[AuditEvents] WHERE [EventType]=@Event;", party, ApprovedEvent));

        // A decided request frees the pending slot; a new request is accepted.
        var staleId = await ProposeAsync(maker, party, version, "Bayat Unvan");

        // Another change moves the PARTY version after the request: the request is stale and cannot be approved.
        await test.ExecuteAsync("UPDATE [counterparty].[Parties] SET [Version]=[Version]+1 WHERE [Id]=@Party;", party);
        var moved = (await ReadAsync(checker, PathFor(party))).GetProperty("version").GetString()!;
        Assert.NotEqual(version, moved);

        // The old version is a version conflict, replayed with the same key.
        NewKey(checker);
        var oldVersion = Body(new { expectedVersion = version, changeRequestId = staleId, decision = "approve" });
        using (var conflict = await PostAsync(checker, DecisionsPath(party), oldVersion))
        {
            await AssertProblemAsync(conflict, HttpStatusCode.Conflict, "version_conflict");
        }

        using (var conflictReplay = await PostAsync(checker, DecisionsPath(party), oldVersion))
        {
            await AssertProblemAsync(conflictReplay, HttpStatusCode.Conflict, "version_conflict");
            Assert.True(conflictReplay.Headers.Contains("Idempotency-Replayed"));
        }

        NewKey(checker);
        using (var stale = await PostAsync(checker, DecisionsPath(party),
                   Body(new { expectedVersion = moved, changeRequestId = staleId, decision = "approve" })))
        {
            await AssertProblemAsync(stale, HttpStatusCode.Conflict, "legal_name_change_stale");
        }

        // A stale request can still be rejected, so the slot can be freed.
        NewKey(checker);
        using (var rejectStale = await PostAsync(checker, DecisionsPath(party),
                   Body(new { expectedVersion = moved, changeRequestId = staleId, decision = "reject" })))
        {
            Assert.Equal(HttpStatusCode.NoContent, rejectStale.StatusCode);
        }

        Assert.Equal("Örnek Unvan", await test.ScalarAsync<string>(
            "SELECT [LegalName] FROM [counterparty].[Parties] WHERE [Id]=@Party;", party));
        Assert.Equal(0, await test.ScalarAsync<int>(
            $"SELECT COUNT(*) FROM [counterparty].[PartyIdentityRevisions] WHERE [PartyId]=@Party AND [Attribute]={LegalNameAttribute} "
            + $"AND [Kind]={DatedChangeKind};", party));
    }

    [SqlServerFact]
    public async Task Blank_too_long_or_malformed_bodies_are_400_and_change_nothing()
    {
        await using var test = new LegalNameTest();
        using var maker = test.Client();
        var party = await CreatePartyAsync(maker, "Örnek Unvan");
        var version = (await ReadAsync(maker, PathFor(party))).GetProperty("version").GetString()!;

        foreach (var invalid in new[]
                 {
                     Body(new { expectedVersion = version, proposedLegalName = "   ", reason = "Gerekçe" }),
                     Body(new { expectedVersion = version, proposedLegalName = "", reason = "Gerekçe" }),
                     Body(new { expectedVersion = version, proposedLegalName = (string?)null, reason = "Gerekçe" }),
                     Body(new { expectedVersion = version, proposedLegalName = new string('x', 251), reason = "Gerekçe" }),
                     Body(new { expectedVersion = version, proposedLegalName = "Unvan", reason = "   " }),
                     Body(new { expectedVersion = version, proposedLegalName = "Unvan", reason = new string('x', 1001) }),
                     Body(new { expectedVersion = version, proposedLegalName = "Unvan" }),
                     Body(new { proposedLegalName = "Eksik sürüm", reason = "Gerekçe" }),
                     Body(new { expectedVersion = version, proposedLegalName = "Unvan", reason = "Gerekçe", tradeName = "Gizli" }),
                     Body(new { expectedVersion = 1, proposedLegalName = "Sayısal sürüm", reason = "Gerekçe" }),
                 })
        {
            NewKey(maker);
            using var refused = await PostAsync(maker, RequestsPath(party), invalid);
            Assert.Equal(HttpStatusCode.BadRequest, refused.StatusCode);
        }

        var requestId = await ProposeAsync(maker, party, version, "Geçerli Unvan");
        using var checker = test.Client(TestCompanyIds.CompanyA, TestUsers.CompanyA);
        foreach (var invalid in new[]
                 {
                     Body(new { expectedVersion = version, changeRequestId = requestId, decision = "maybe" }),
                     Body(new { expectedVersion = version, changeRequestId = requestId }),
                     Body(new { expectedVersion = version, changeRequestId = requestId, decision = "approve", note = new string('x', 1001) }),
                     Body(new { expectedVersion = version, decision = "approve" }),
                 })
        {
            NewKey(checker);
            using var refused = await PostAsync(checker, DecisionsPath(party), invalid);
            Assert.Equal(HttpStatusCode.BadRequest, refused.StatusCode);
        }

        var after = await ReadAsync(maker, PathFor(party));
        Assert.Equal(version, after.GetProperty("version").GetString());
        Assert.Equal("Örnek Unvan", after.GetProperty("legalName").GetString());
        Assert.Equal("pending", after.GetProperty("pendingChange").GetProperty("status").GetString());
        Assert.Equal(1, await test.ScalarAsync<int>(
            "SELECT COUNT(*) FROM [counterparty].[PartyLegalNameChangeRequests] WHERE [PartyId]=@Party;", party));
        Assert.Equal(1, await test.ScalarAsync<int>(
            "SELECT COUNT(*) FROM [counterparty].[Parties] WHERE [Id]=@Party AND [TradeName] IS NULL;", party));
    }

    [SqlServerFact]
    public async Task Without_the_separate_grants_nothing_is_written_and_another_company_cannot_read_or_change_it()
    {
        // The basic Cari edit permission reaches neither the proposal nor the decision (ALS-D111: separate names).
        await using (var basicOnly = new LegalNameTest(grant: false))
        {
            using var editor = basicOnly.Client();
            var own = await CreatePartyAsync(editor, "Temel Yetkili Unvan");
            var view = await ReadAsync(editor, PathFor(own));
            Assert.False(view.GetProperty("viewer").GetProperty("canPropose").GetBoolean());
            Assert.False(view.GetProperty("viewer").GetProperty("canApprove").GetBoolean());

            NewKey(editor);
            using var denied = await PostAsync(editor, RequestsPath(own), Body(new
            {
                expectedVersion = view.GetProperty("version").GetString(),
                proposedLegalName = "Yetkisiz Unvan",
                reason = "Gerekçe",
            }));
            Assert.Equal(HttpStatusCode.Forbidden, denied.StatusCode);
            Assert.Equal(0, await basicOnly.ScalarAsync<int>(
                "SELECT COUNT(*) FROM [counterparty].[PartyLegalNameChangeRequests] WHERE [PartyId]=@Party;", own));
        }

        await using var test = new LegalNameTest();
        using var maker = test.Client();
        using var checker = test.Client(TestCompanyIds.CompanyA, TestUsers.CompanyA);
        using var adminB = test.Client(TestCompanyIds.CompanyB);
        var party = await CreatePartyAsync(maker, "A Müşterisi Unvanı");
        var version = (await ReadAsync(maker, PathFor(party))).GetProperty("version").GetString()!;

        // The approver alone cannot propose.
        NewKey(checker);
        using (var checkerProposes = await PostAsync(checker, RequestsPath(party),
                   Body(new { expectedVersion = version, proposedLegalName = "Onaycı Unvanı", reason = "Gerekçe" })))
        {
            Assert.Equal(HttpStatusCode.Forbidden, checkerProposes.StatusCode);
        }

        var requestId = await ProposeAsync(maker, party, version, "B Görmemeli");

        // The legal-name checker has no separate basic-edit grant for the trade name.
        NewKey(checker);
        using (var tradeNameDenied = await checker.PutAsync($"/api/v1/counterparties/{party}/trade-name",
                   new StringContent(Body(new { expectedVersion = version, tradeName = "Ticari Ad" }), Encoding.UTF8, "application/json")))
        {
            Assert.Equal(HttpStatusCode.Forbidden, tradeNameDenied.StatusCode);
        }

        // Company B has no account for A's party: the party exists, but not for B.
        using (var missing = await adminB.GetAsync(PathFor(party)))
        {
            Assert.Equal(HttpStatusCode.NotFound, missing.StatusCode);
        }

        NewKey(adminB);
        using (var missingDecision = await PostAsync(adminB, DecisionsPath(party),
                   Body(new { expectedVersion = version, changeRequestId = requestId, decision = "approve" })))
        {
            await AssertProblemAsync(missingDecision, HttpStatusCode.NotFound, "not_found");
        }

        NewKey(adminB);
        using (var missingProposal = await PostAsync(adminB, RequestsPath(party),
                   Body(new { expectedVersion = version, proposedLegalName = "Ele Geçirildi", reason = "Gerekçe" })))
        {
            await AssertProblemAsync(missingProposal, HttpStatusCode.NotFound, "not_found");
        }

        using var narrow = test.Client(TestCompanyIds.CompanyA, TestUsers.CompanyAViewer);
        NewKey(narrow);
        using (var deniedDecision = await PostAsync(narrow, DecisionsPath(party),
                   Body(new { expectedVersion = version, changeRequestId = requestId, decision = "approve" })))
        {
            Assert.Equal(HttpStatusCode.Forbidden, deniedDecision.StatusCode);
        }

        Assert.Equal("A Müşterisi Unvanı", await test.ScalarAsync<string>(
            "SELECT [LegalName] FROM [counterparty].[Parties] WHERE [Id]=@Party;", party));
        Assert.Equal(1, await test.ScalarAsync<int>(
            "SELECT COUNT(*) FROM [counterparty].[PartyLegalNameChangeRequests] WHERE [PartyId]=@Party AND [Status]=N'Pending';", party));
        Assert.Equal(0, await test.ScalarAsync<int>(
            "SELECT COUNT(*) FROM [audit].[AuditEvents] WHERE [EventType]=@Event;", party, ApprovedEvent));
    }

    [SqlServerFact]
    public async Task The_database_refuses_self_decisions_and_a_second_pending_request_and_the_down_refuses_with_51847()
    {
        await using var test = new LegalNameTest();
        using var maker = test.Client();
        var party = await CreatePartyAsync(maker, "Kısıt Unvan");

        const string insert = """
            INSERT INTO [counterparty].[PartyLegalNameChangeRequests]
                ([Id],[CompanyId],[PartyId],[CurrentLegalName],[ProposedLegalName],[PartyVersionAtRequest],
                 [Reason],[Status],[RequestedBy],[RequestedAtUtc],[DecidedBy],[DecidedAtUtc],[DecisionNote],[Version])
            VALUES (NEWID(),@Company,@Party,N'Kısıt Unvan',N'{0}',1,
                 N'r',N'{1}',N'maker',SYSDATETIMEOFFSET(),{2},{3},NULL,1);
            """;
        string Insert(string proposed, string status, string decidedBy, string decidedAt) =>
            string.Format(CultureInfo.InvariantCulture, insert, proposed, status, decidedBy, decidedAt);

        var self = await Assert.ThrowsAsync<SqlException>(() => test.ExecuteAsync(
            Insert("Yeni", "Approved", "N'maker'", "SYSDATETIMEOFFSET()"), party));
        Assert.Equal(547, self.Number);
        var unchanged = await Assert.ThrowsAsync<SqlException>(() => test.ExecuteAsync(
            Insert("Kısıt Unvan", "Pending", "NULL", "NULL"), party));
        Assert.Equal(547, unchanged.Number);
        var unknownStatus = await Assert.ThrowsAsync<SqlException>(() => test.ExecuteAsync(
            Insert("Yeni", "Applied", "NULL", "NULL"), party));
        Assert.Equal(547, unknownStatus.Number);

        await test.ExecuteAsync(Insert("Yeni", "Pending", "NULL", "NULL"), party);
        var second = await Assert.ThrowsAsync<SqlException>(() => test.ExecuteAsync(
            Insert("Başka", "Pending", "NULL", "NULL"), party));
        Assert.Equal(2601, second.Number);
        // A decided request does not occupy the pending slot.
        await test.ExecuteAsync(Insert("Reddedilen", "Rejected", "N'checker'", "SYSDATETIMEOFFSET()"), party);

        // Forward fix: the migration does not roll back while a request exists, and nothing is lost.
        await using var scope = test.Factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<CounterpartyDbContext>();
        var migrator = db.GetService<IMigrator>();
        Assert.False(db.Database.HasPendingModelChanges());
        var down = await Assert.ThrowsAsync<SqlException>(() => migrator.MigrateAsync(PreviousMigration));
        Assert.Equal(51847, down.Number);
        Assert.Equal(2, await test.ScalarAsync<int>(
            "SELECT COUNT(*) FROM [counterparty].[PartyLegalNameChangeRequests] WHERE [PartyId]=@Party;", party));

        // Without requests the migration rolls back and forward again.
        await test.ExecuteAsync("DELETE FROM [counterparty].[PartyLegalNameChangeRequests] WHERE [PartyId]=@Party;", party);
        await migrator.MigrateAsync(PreviousMigration);
        await migrator.MigrateAsync();
        Assert.False(db.Database.HasPendingModelChanges());
    }

    private static string PathFor(Guid party) => $"/api/v1/counterparties/{party}/legal-name";

    private static string RequestsPath(Guid party) => $"{PathFor(party)}/change-requests";

    private static string DecisionsPath(Guid party) => $"{PathFor(party)}/change-decisions";

    private static string Body(object value) =>
        JsonSerializer.Serialize(value, new JsonSerializerOptions(JsonSerializerDefaults.Web));

    private static async Task<Guid> CreatePartyAsync(HttpClient client, string legalName)
    {
        NewKey(client);
        using var response = await client.PostAsJsonAsync("/api/v1/counterparties", new { kind = 1, legalName, roles = 1 });
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return (await JsonAsync(response)).GetProperty("id").GetGuid();
    }

    private static async Task<Guid> ProposeAsync(HttpClient client, Guid party, string version, string proposed)
    {
        NewKey(client);
        using (var response = await PostAsync(client, RequestsPath(party),
                   Body(new { expectedVersion = version, proposedLegalName = proposed, reason = "Test talebi" })))
        {
            Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        }

        return (await ReadAsync(client, PathFor(party))).GetProperty("pendingChange").GetProperty("id").GetGuid();
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

    private static Task<HttpResponseMessage> PostAsync(HttpClient client, string path, string body) =>
        client.PostAsync(path, new StringContent(body, Encoding.UTF8, "application/json"));

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

        public LegalNameTest(bool grant = true)
        {
            Factory = new SqlServerRelationalApiFactory(_baseConnection, services =>
            {
                // Test-only grants on the disposable database; no shipped seed role is changed (grants are a human gate).
                services.PostConfigure<IdentitySeedOptions>(options =>
                {
                    var administrator = options.Roles.Single(role => role.Code == "SYSTEM_ADMINISTRATOR");
                    administrator.Permissions.Add(ErpPermissions.CounterpartyPartiesUpdateBasic);
                    if (!grant)
                    {
                        return;
                    }

                    administrator.Permissions.Add(CounterpartyLegalNamePermissionsV1.Propose);
                    administrator.Permissions.Add(CounterpartyLegalNamePermissionsV1.Approve);
                    var operatorRole = options.Roles.Single(role => role.Code == "ORGANIZATION_OPERATOR");
                    operatorRole.Permissions.Add(ErpPermissions.CounterpartyPartiesRead);
                    operatorRole.Permissions.Add(CounterpartyLegalNamePermissionsV1.Approve);
                });
            });
        }

        public SqlServerRelationalApiFactory Factory { get; }

        public HttpClient Client(Guid? company = null, string user = TestUsers.GroupAdmin) =>
            Factory.CreateClientFor(user, company ?? TestCompanyIds.CompanyA);

        public async Task<T> ScalarAsync<T>(
            string sql,
            Guid party,
            string? eventType = null,
            string? command = null,
            string? permission = null)
        {
            await using var connection = new SqlConnection(Factory.DatabaseConnectionString);
            await connection.OpenAsync();
            await using var sqlCommand = CreateCommand(connection, sql, party, eventType, command, permission);
            return Assert.IsType<T>(await sqlCommand.ExecuteScalarAsync());
        }

        public async Task ExecuteAsync(string sql, Guid party)
        {
            await using var connection = new SqlConnection(Factory.DatabaseConnectionString);
            await connection.OpenAsync();
            await using var sqlCommand = CreateCommand(connection, sql, party, null, null, null);
            await sqlCommand.ExecuteNonQueryAsync();
        }

        private static SqlCommand CreateCommand(
            SqlConnection connection,
            string sql,
            Guid party,
            string? eventType,
            string? command,
            string? permission)
        {
            var sqlCommand = connection.CreateCommand();
            sqlCommand.CommandText = sql;
            if (sql.Contains("@Party", StringComparison.Ordinal)) sqlCommand.Parameters.AddWithValue("@Party", party);
            if (sql.Contains("@Event", StringComparison.Ordinal)) sqlCommand.Parameters.AddWithValue("@Event", eventType!);
            if (sql.Contains("@Command", StringComparison.Ordinal)) sqlCommand.Parameters.AddWithValue("@Command", command!);
            if (sql.Contains("@Permission", StringComparison.Ordinal)) sqlCommand.Parameters.AddWithValue("@Permission", permission!);
            if (sql.Contains("@Company", StringComparison.Ordinal)) sqlCommand.Parameters.AddWithValue("@Company", TestCompanyIds.CompanyA);
            return sqlCommand;
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
