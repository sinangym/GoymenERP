using System.Data.Common;
using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using GoymenERP.BuildingBlocks.Security;
using GoymenERP.Modules.Counterparty.Application.Contracts;
using GoymenERP.Modules.Counterparty.Infrastructure.Persistence;
using GoymenERP.Modules.Identity.Infrastructure.Seeding;
using GoymenERP.Modules.Idempotency.Domain.Executions;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace GoymenERP.Api.IntegrationTests;

/// <summary>
/// AT-009 VKN/TCKN on the Cari card, against real SQL Server through the real API (user decision 2026-09-30):
/// checksum-invalid numbers are refused and never stored; the same number on another card of the company is a warning
/// and the save goes through; replacing an existing number is a pending change that only a DIFFERENT person may decide.
/// Actors: the group administrator is the maker (it ALSO holds approve, so its refusal is the maker-checker rule, not a
/// missing grant); company-a-user / company-b-user are the checkers of their companies.
/// </summary>
public sealed class CounterpartyTaxIdentitySqlServerRelationalAcceptanceTests
{
    private const string Vkn = "1234567890";
    private const string OtherVkn = "9876543217";
    private const string ThirdVkn = "4567890128";
    private const string Tckn = "10000000146";
    private const string EnteredEvent = "counterparty.party.tax-identity.entered.v1";
    private const string RequestedEvent = "counterparty.party.tax-identity.change-requested.v1";
    private const string ApprovedEvent = "counterparty.party.tax-identity.change-approved.v1";
    private const string RejectedEvent = "counterparty.party.tax-identity.change-rejected.v1";

    [SqlServerFact]
    public async Task Entered_number_reopens_and_a_duplicate_in_the_same_company_warns_without_blocking()
    {
        await using var test = new TaxTest();
        using var maker = test.Client();
        var first = await CreateAsync(maker, "Birinci");
        var second = await CreateAsync(maker, "İkinci Şube");

        var empty = await ReadAsync(maker, PathFor(first));
        Assert.Equal(JsonValueKind.Null, empty.GetProperty("taxIdentityNumber").ValueKind);
        Assert.Equal("1", empty.GetProperty("version").GetString());
        Assert.True(empty.GetProperty("viewer").GetProperty("canUpdate").GetBoolean());

        // Refused and never stored: a wrong check digit, a wrong length and cleaned-up text.
        foreach (var invalid in new[] { "1234567891", "123456789", "123 4567890", "10000000145" })
        {
            NewKey(maker);
            using var refused = await PutAsync(maker, PathFor(first), Body(new { expectedVersion = "1", taxIdentityNumber = invalid }));
            Assert.Equal(HttpStatusCode.BadRequest, refused.StatusCode);
            Assert.Equal("tax_identity_invalid", (await JsonAsync(refused)).GetProperty("code").GetString());
        }

        Assert.Equal(0, await test.ScalarAsync<int>(
            "SELECT COUNT(*) FROM [counterparty].[Parties] WHERE [TaxIdentityNumber] IS NOT NULL;", first));

        NewKey(maker);
        var enter = Body(new { expectedVersion = "1", taxIdentityNumber = Vkn });
        using (var saved = await PutAsync(maker, PathFor(first), enter))
        {
            Assert.Equal(HttpStatusCode.NoContent, saved.StatusCode);
        }

        using (var replay = await PutAsync(maker, PathFor(first), enter))
        {
            Assert.Equal(HttpStatusCode.NoContent, replay.StatusCode);
            Assert.True(replay.Headers.Contains("Idempotency-Replayed"));
        }

        // Close and reopen: a brand-new client reads the stored number.
        using (var reopened = test.Client())
        {
            var view = await ReadAsync(reopened, PathFor(first));
            Assert.Equal(Vkn, view.GetProperty("taxIdentityNumber").GetString());
            Assert.Equal("vkn", view.GetProperty("kind").GetString());
            Assert.Equal("2", view.GetProperty("version").GetString());
            Assert.Empty(view.GetProperty("duplicates").EnumerateArray());
        }

        // The warning before saving the same number on the second card…
        var check = await CheckDuplicatesAsync(maker, second, Vkn);
        var warned = Assert.Single(check.GetProperty("duplicates").EnumerateArray());
        Assert.Equal(first, warned.GetProperty("partyId").GetGuid());
        using (var badCheck = await maker.PostAsJsonAsync($"{PathFor(second)}/duplicates",
                   new { taxIdentityNumber = "1234567891" }))
        {
            Assert.Equal(HttpStatusCode.BadRequest, badCheck.StatusCode);
            Assert.Equal("tax_identity_invalid", (await JsonAsync(badCheck)).GetProperty("code").GetString());
        }

        // …and the save is allowed; both cards now carry the warning.
        NewKey(maker);
        using (var duplicate = await PutAsync(maker, PathFor(second), Body(new { expectedVersion = "1", taxIdentityNumber = Vkn })))
        {
            Assert.Equal(HttpStatusCode.NoContent, duplicate.StatusCode);
        }

        Assert.Equal(first, Assert.Single((await ReadAsync(maker, PathFor(second))).GetProperty("duplicates").EnumerateArray())
            .GetProperty("partyId").GetGuid());
        Assert.Equal(second, Assert.Single((await ReadAsync(maker, PathFor(first))).GetProperty("duplicates").EnumerateArray())
            .GetProperty("partyId").GetGuid());

        // A second direct entry never replaces an existing number.
        NewKey(maker);
        using (var overwrite = await PutAsync(maker, PathFor(first), Body(new { expectedVersion = "2", taxIdentityNumber = OtherVkn })))
        {
            Assert.Equal(HttpStatusCode.Conflict, overwrite.StatusCode);
            Assert.Equal("tax_identity_change_requires_approval", (await JsonAsync(overwrite)).GetProperty("code").GetString());
        }

        Assert.Equal(Vkn, (await ReadAsync(maker, PathFor(first))).GetProperty("taxIdentityNumber").GetString());

        // Company fence: company B sees neither card until linked, and then not A's other card as a duplicate.
        using var companyB = test.Client(TestCompanyIds.CompanyB);
        using (var hidden = await companyB.GetAsync(PathFor(second)))
        {
            Assert.Equal(HttpStatusCode.NotFound, hidden.StatusCode);
        }

        using (var link = await companyB.PostAsJsonAsync($"/api/v1/counterparties/{second}/company-account", new { roles = 0 }))
        {
            Assert.Equal(HttpStatusCode.Created, link.StatusCode);
        }

        var fromB = await ReadAsync(companyB, PathFor(second));
        Assert.Equal(Vkn, fromB.GetProperty("taxIdentityNumber").GetString());
        Assert.Empty(fromB.GetProperty("duplicates").EnumerateArray());

        Assert.Equal(2, await test.ScalarAsync<int>(
            "SELECT COUNT(*) FROM [audit].[AuditEvents] WHERE [EventType]=@Event;", first, EnteredEvent));
        Assert.Equal(0, await test.ScalarAsync<int>(
            $"SELECT COUNT(*) FROM [audit].[AuditEvents] WHERE [EventType]=@Event AND [AfterJson] LIKE N'%{Vkn}%';",
            first, EnteredEvent));
    }

    [SqlServerFact]
    public async Task A_change_stays_pending_until_a_different_person_approves_it()
    {
        await using var test = new TaxTest();
        using var maker = test.Client();
        using var checker = test.Client(TestCompanyIds.CompanyA, TestUsers.CompanyA);
        var party = await CreateAsync(maker, "Onaylı");
        await EnterAsync(maker, party, Tckn);

        // A change of a party without a number, or to the same number, is not a request.
        var bare = await CreateAsync(maker, "Numarasız");
        NewKey(maker);
        using (var notSet = await PutAsync(maker, $"{PathFor(bare)}/change-request",
                   Body(new { expectedVersion = "1", proposedTaxIdentityNumber = Vkn, reason = "Yeni levha" })))
        {
            Assert.Equal("tax_identity_not_set", (await JsonAsync(notSet)).GetProperty("code").GetString());
        }

        NewKey(maker);
        using (var same = await PutAsync(maker, $"{PathFor(party)}/change-request",
                   Body(new { expectedVersion = "2", proposedTaxIdentityNumber = Tckn, reason = "Aynı" })))
        {
            Assert.Equal("tax_identity_unchanged", (await JsonAsync(same)).GetProperty("code").GetString());
        }

        NewKey(maker);
        using (var requested = await PutAsync(maker, $"{PathFor(party)}/change-request",
                   Body(new { expectedVersion = "2", proposedTaxIdentityNumber = Vkn, reason = "Şahıs şirketi tüzel kişiye dönüştü" })))
        {
            Assert.Equal(HttpStatusCode.NoContent, requested.StatusCode);
        }

        var pendingView = await ReadAsync(maker, PathFor(party));
        Assert.Equal(Tckn, pendingView.GetProperty("taxIdentityNumber").GetString());
        var pending = pendingView.GetProperty("pendingChange");
        Assert.Equal("pending", pending.GetProperty("status").GetString());
        Assert.Equal(Vkn, pending.GetProperty("proposedTaxIdentityNumber").GetString());
        Assert.True(pendingView.GetProperty("viewer").GetProperty("isRequesterOfPending").GetBoolean());
        var requestId = pending.GetProperty("id").GetGuid();
        var checkerView = await ReadAsync(checker, PathFor(party));
        Assert.False(checkerView.GetProperty("viewer").GetProperty("isRequesterOfPending").GetBoolean());
        Assert.True(checkerView.GetProperty("viewer").GetProperty("canApprove").GetBoolean());

        NewKey(maker);
        using (var second = await PutAsync(maker, $"{PathFor(party)}/change-request",
                   Body(new { expectedVersion = "2", proposedTaxIdentityNumber = OtherVkn, reason = "İkinci" })))
        {
            Assert.Equal("tax_identity_change_pending", (await JsonAsync(second)).GetProperty("code").GetString());
        }

        // The maker holds approve too, and is still refused: the rule, not the grant.
        foreach (var decision in new[] { "approve", "reject" })
        {
            NewKey(maker);
            using var self = await PutAsync(maker, $"{PathFor(party)}/change-request/decision",
                Body(new { expectedVersion = "2", changeRequestId = requestId, decision }));
            Assert.Equal(HttpStatusCode.Conflict, self.StatusCode);
            Assert.Equal("tax_identity_self_decision", (await JsonAsync(self)).GetProperty("code").GetString());
        }

        NewKey(checker);
        using (var stale = await PutAsync(checker, $"{PathFor(party)}/change-request/decision",
                   Body(new { expectedVersion = "1", changeRequestId = requestId, decision = "approve" })))
        {
            Assert.Equal("version_conflict", (await JsonAsync(stale)).GetProperty("code").GetString());
        }

        Assert.Equal(Tckn, (await ReadAsync(maker, PathFor(party))).GetProperty("taxIdentityNumber").GetString());

        NewKey(checker);
        var approve = Body(new { expectedVersion = "2", changeRequestId = requestId, decision = "approve", note = "Levha görüldü" });
        using (var approved = await PutAsync(checker, $"{PathFor(party)}/change-request/decision", approve))
        {
            Assert.Equal(HttpStatusCode.NoContent, approved.StatusCode);
        }

        using (var replay = await PutAsync(checker, $"{PathFor(party)}/change-request/decision", approve))
        {
            Assert.Equal(HttpStatusCode.NoContent, replay.StatusCode);
            Assert.True(replay.Headers.Contains("Idempotency-Replayed"));
        }

        using var reopened = test.Client();
        var after = await ReadAsync(reopened, PathFor(party));
        Assert.Equal(Vkn, after.GetProperty("taxIdentityNumber").GetString());
        Assert.Equal("vkn", after.GetProperty("kind").GetString());
        Assert.Equal("3", after.GetProperty("version").GetString());
        Assert.Equal(JsonValueKind.Null, after.GetProperty("pendingChange").ValueKind);
        var decided = after.GetProperty("lastDecidedChange");
        Assert.Equal("approved", decided.GetProperty("status").GetString());
        Assert.Equal("Levha görüldü", decided.GetProperty("decisionNote").GetString());
        Assert.NotEqual(decided.GetProperty("requestedBy").GetString(), decided.GetProperty("decidedBy").GetString());

        NewKey(checker);
        using (var again = await PutAsync(checker, $"{PathFor(party)}/change-request/decision",
                   Body(new { expectedVersion = "3", changeRequestId = requestId, decision = "reject" })))
        {
            Assert.Equal("tax_identity_change_not_pending", (await JsonAsync(again)).GetProperty("code").GetString());
        }

        // A rejected request leaves the number as it is.
        NewKey(maker);
        using (var next = await PutAsync(maker, $"{PathFor(party)}/change-request",
                   Body(new { expectedVersion = "3", proposedTaxIdentityNumber = ThirdVkn, reason = "Yanlış giriş" })))
        {
            Assert.Equal(HttpStatusCode.NoContent, next.StatusCode);
        }

        var nextId = (await ReadAsync(maker, PathFor(party))).GetProperty("pendingChange").GetProperty("id").GetGuid();
        NewKey(checker);
        using (var rejected = await PutAsync(checker, $"{PathFor(party)}/change-request/decision",
                   Body(new { expectedVersion = "3", changeRequestId = nextId, decision = "reject" })))
        {
            Assert.Equal(HttpStatusCode.NoContent, rejected.StatusCode);
        }

        var final = await ReadAsync(maker, PathFor(party));
        Assert.Equal(Vkn, final.GetProperty("taxIdentityNumber").GetString());
        Assert.Equal("3", final.GetProperty("version").GetString());
        Assert.Equal("rejected", final.GetProperty("lastDecidedChange").GetProperty("status").GetString());

        Assert.Equal(2, await test.ScalarAsync<int>(
            "SELECT COUNT(*) FROM [audit].[AuditEvents] WHERE [EventType]=@Event;", party, RequestedEvent));
        Assert.Equal(1, await test.ScalarAsync<int>(
            "SELECT COUNT(*) FROM [audit].[AuditEvents] WHERE [EventType]=@Event AND [ApprovalContextJson] IS NOT NULL;",
            party, ApprovedEvent));
        Assert.Equal(1, await test.ScalarAsync<int>(
            "SELECT COUNT(*) FROM [audit].[AuditEvents] WHERE [EventType]=@Event;", party, RejectedEvent));
    }

    [SqlServerFact]
    public async Task A_request_whose_number_moved_elsewhere_cannot_be_approved()
    {
        await using var test = new TaxTest();
        using var makerA = test.Client();
        using var makerB = test.Client(TestCompanyIds.CompanyB);
        using var checkerA = test.Client(TestCompanyIds.CompanyA, TestUsers.CompanyA);
        using var checkerB = test.Client(TestCompanyIds.CompanyB, TestUsers.CompanyB);
        var party = await CreateAsync(makerA, "İki şirketli");
        using (var link = await makerB.PostAsJsonAsync($"/api/v1/counterparties/{party}/company-account", new { roles = 0 }))
        {
            Assert.Equal(HttpStatusCode.Created, link.StatusCode);
        }

        await EnterAsync(makerA, party, Vkn);
        await RequestAsync(makerA, party, "2", OtherVkn);
        await RequestAsync(makerB, party, "2", ThirdVkn);
        var idA = (await ReadAsync(makerA, PathFor(party))).GetProperty("pendingChange").GetProperty("id").GetGuid();
        var idB = (await ReadAsync(makerB, PathFor(party))).GetProperty("pendingChange").GetProperty("id").GetGuid();
        Assert.NotEqual(idA, idB);

        // A company never decides another company's request.
        NewKey(checkerB);
        using (var foreign = await PutAsync(checkerB, $"{PathFor(party)}/change-request/decision",
                   Body(new { expectedVersion = "2", changeRequestId = idA, decision = "approve" })))
        {
            Assert.Equal(HttpStatusCode.NotFound, foreign.StatusCode);
        }

        NewKey(checkerB);
        using (var approvedB = await PutAsync(checkerB, $"{PathFor(party)}/change-request/decision",
                   Body(new { expectedVersion = "2", changeRequestId = idB, decision = "approve" })))
        {
            Assert.Equal(HttpStatusCode.NoContent, approvedB.StatusCode);
        }

        NewKey(checkerA);
        using (var stale = await PutAsync(checkerA, $"{PathFor(party)}/change-request/decision",
                   Body(new { expectedVersion = "3", changeRequestId = idA, decision = "approve" })))
        {
            Assert.Equal(HttpStatusCode.Conflict, stale.StatusCode);
            Assert.Equal("tax_identity_change_stale", (await JsonAsync(stale)).GetProperty("code").GetString());
        }

        Assert.Equal(ThirdVkn, (await ReadAsync(makerA, PathFor(party))).GetProperty("taxIdentityNumber").GetString());
        NewKey(checkerA);
        using (var rejected = await PutAsync(checkerA, $"{PathFor(party)}/change-request/decision",
                   Body(new { expectedVersion = "3", changeRequestId = idA, decision = "reject" })))
        {
            Assert.Equal(HttpStatusCode.NoContent, rejected.StatusCode);
        }
    }

    [SqlServerFact]
    public async Task Without_the_grants_nothing_is_written_and_an_old_client_is_refused()
    {
        await using (var defaults = new TaxTest(grant: false))
        {
            using var admin = defaults.Client();
            var party = await CreateAsync(admin, "Yetkisiz");
            var view = await ReadAsync(admin, PathFor(party));
            Assert.False(view.GetProperty("viewer").GetProperty("canUpdate").GetBoolean());
            Assert.False(view.GetProperty("viewer").GetProperty("canApprove").GetBoolean());
            NewKey(admin);
            using var denied = await PutAsync(admin, PathFor(party), Body(new { expectedVersion = "1", taxIdentityNumber = Vkn }));
            Assert.Equal(HttpStatusCode.Forbidden, denied.StatusCode);
            Assert.Equal(0, await defaults.ScalarAsync<int>(
                "SELECT COUNT(*) FROM [counterparty].[Parties] WHERE [TaxIdentityNumber] IS NOT NULL;", party));
        }

        await using var test = new TaxTest();
        using var maker = test.Client();
        var target = await CreateAsync(maker, "Eski istemci");
        using var viewer = test.Client(TestCompanyIds.CompanyA, TestUsers.CompanyAViewer);
        using (var deniedRead = await viewer.GetAsync(PathFor(target)))
        {
            Assert.Equal(HttpStatusCode.Forbidden, deniedRead.StatusCode);
        }

        maker.DefaultRequestHeaders.Remove("X-ERP-Counterparty-Contract");
        NewKey(maker);
        using (var oldClient = await PutAsync(maker, PathFor(target), Body(new { expectedVersion = "1", taxIdentityNumber = Vkn })))
        {
            Assert.Equal(HttpStatusCode.Conflict, oldClient.StatusCode);
            Assert.Equal("client_contract_required", (await JsonAsync(oldClient)).GetProperty("code").GetString());
        }

        Assert.Equal(0, await test.ScalarAsync<int>(
            "SELECT COUNT(*) FROM [counterparty].[Parties] WHERE [Id]=@Party AND [TaxIdentityNumber] IS NOT NULL;", target));
    }

    [SqlServerFact]
    public async Task Authority_revoked_after_the_duplicate_read_refuses_without_disclosing_candidates_or_writing()
    {
        var authority = new RevocableAuthority();
        var interleave = new DuplicateReadRevocationInterceptor(authority);
        await using var test = new TaxTest(freshAuthority: authority, interceptors: [interleave]);
        using var maker = test.Client();
        var existing = await CreateAsync(maker, "Gizli çakışan cari");
        var target = await CreateAsync(maker, "Yetkisi düşen cari");
        await EnterAsync(maker, existing, Vkn);

        interleave.Arm();
        using var response = await maker.PostAsJsonAsync($"{PathFor(target)}/duplicates",
            new { taxIdentityNumber = Vkn });

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.True(interleave.Triggered);
        Assert.True(authority.DeniedMeasurements > 0);
        var body = await response.Content.ReadAsStringAsync();
        Assert.DoesNotContain(existing.ToString("D"), body, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Gizli çakışan cari", body, StringComparison.Ordinal);
        Assert.Equal(0, await test.ScalarAsync<int>(
            "SELECT COUNT(*) FROM [counterparty].[Parties] WHERE [Id]=@Party AND [TaxIdentityNumber] IS NOT NULL;", target));
        Assert.Equal(0, await test.ScalarAsync<int>(
            "SELECT COUNT(*) FROM [audit].[AuditEvents] WHERE [EntityId]=CONVERT(nvarchar(36), @Party);", target));
    }

    [SqlServerFact]
    public async Task Same_key_race_revalidates_authority_after_reading_the_winner_and_refuses_the_loser()
    {
        var authority = new RevocableAuthority();
        using var barrier = new Barrier(2);
        var interleave = new WinnerFailureRevocationInterceptor(barrier, authority);
        await using var test = new TaxTest(freshAuthority: authority, interceptors: [interleave]);
        using var first = test.Client();
        using var second = test.Client();
        var party = await CreateAsync(first, "Yarışan cari");
        var key = Guid.NewGuid().ToString("N");
        NewKey(first, key);
        NewKey(second, key);
        var body = Body(new { expectedVersion = "1", taxIdentityNumber = Vkn });

        interleave.Arm();
        var responses = await Task.WhenAll(
            PutAsync(first, PathFor(party), body),
            PutAsync(second, PathFor(party), body));
        try
        {
            Assert.Contains(responses, response => response.StatusCode == HttpStatusCode.NoContent);
            var refused = Assert.Single(responses, response => response.StatusCode == HttpStatusCode.Forbidden);
            Assert.False(refused.Headers.Contains("Idempotency-Replayed"));
            Assert.True(interleave.FailureObserved);
            Assert.True(authority.DeniedMeasurements > 0);
            Assert.Equal(1, await test.ScalarAsync<int>(
                "SELECT COUNT(*) FROM [counterparty].[Parties] WHERE [Id]=@Party AND [TaxIdentityNumber]=N'1234567890';", party));
            Assert.Equal(1, await test.ScalarAsync<int>(
                "SELECT COUNT(*) FROM [audit].[AuditEvents] WHERE [EntityId]=CONVERT(nvarchar(36), @Party) AND [EventType]=@Event;",
                party, EnteredEvent));
            Assert.Equal(1, await test.ScalarAsync<int>(
                "SELECT COUNT(*) FROM [idempotency].[CommandExecutions] WHERE [CompanyId]=@Company AND [CommandName]=@Command;",
                party, commandName: "counterparty.party.tax-identity.enter.v1"));
        }
        finally
        {
            foreach (var response in responses) response.Dispose();
        }
    }

    [SqlServerFact]
    public async Task The_database_refuses_malformed_numbers_self_decisions_and_a_second_pending_change()
    {
        await using var test = new TaxTest();
        using var maker = test.Client();
        var party = await CreateAsync(maker, "Kısıt");
        foreach (var malformed in new[] { "12345", "12345678901234", "12345 7890", "123456789A" })
        {
            var check = await Assert.ThrowsAsync<SqlException>(() => test.ExecuteAsync(
                $"UPDATE [counterparty].[Parties] SET [TaxIdentityNumber]='{malformed}' WHERE [Id]=@Party;", party));
            Assert.Equal(547, check.Number);
        }

        await EnterAsync(maker, party, Vkn);
        const string insert = """
            INSERT INTO [counterparty].[PartyTaxIdentityChangeRequests]
                ([Id],[CompanyId],[PartyId],[CurrentTaxIdentityNumber],[ProposedTaxIdentityNumber],[PartyVersionAtRequest],
                 [Reason],[Status],[RequestedBy],[RequestedAtUtc],[DecidedBy],[DecidedAtUtc],[DecisionNote],[Version])
            VALUES (NEWID(),'10000000-0000-0000-0000-000000000001',@Party,'1234567890','9876543217',2,
                 N'r',N'{0}',N'maker',SYSDATETIMEOFFSET(),{1},{2},NULL,1);
            """;
        var self = await Assert.ThrowsAsync<SqlException>(() => test.ExecuteAsync(
            string.Format(System.Globalization.CultureInfo.InvariantCulture, insert, "Approved", "N'maker'", "SYSDATETIMEOFFSET()"),
            party));
        Assert.Equal(547, self.Number);
        await test.ExecuteAsync(
            string.Format(System.Globalization.CultureInfo.InvariantCulture, insert, "Pending", "NULL", "NULL"), party);
        var second = await Assert.ThrowsAsync<SqlException>(() => test.ExecuteAsync(
            string.Format(System.Globalization.CultureInfo.InvariantCulture, insert, "Pending", "NULL", "NULL"), party));
        Assert.Equal(2601, second.Number);
        // A decided request does not occupy the pending slot.
        await test.ExecuteAsync(
            string.Format(System.Globalization.CultureInfo.InvariantCulture, insert, "Rejected", "N'checker'", "SYSDATETIMEOFFSET()"),
            party);
    }

    [SqlServerFact]
    public async Task The_migration_rolls_back_empty_and_refuses_with_51059_while_a_number_exists()
    {
        await using var test = new TaxTest();
        using var maker = test.Client();
        var party = await CreateAsync(maker, "Geri alma");
        await using var scope = test.Factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<CounterpartyDbContext>();
        var migrator = db.GetService<IMigrator>();
        await migrator.MigrateAsync("20260912003000_AddCompanyAccountContactInformation");
        await migrator.MigrateAsync();
        Assert.False(db.Database.HasPendingModelChanges());

        await EnterAsync(maker, party, Vkn);
        var before = (await ReadAsync(maker, PathFor(party))).GetRawText();
        var down = await Assert.ThrowsAsync<SqlException>(() =>
            migrator.MigrateAsync("20260912003000_AddCompanyAccountContactInformation"));
        Assert.Equal(51059, down.Number);
        Assert.Equal(before, (await ReadAsync(maker, PathFor(party))).GetRawText());
    }

    private static string PathFor(Guid party) => $"/api/v1/counterparties/{party}/tax-identity";

    private static string Body(object value) => JsonSerializer.Serialize(value, new JsonSerializerOptions(JsonSerializerDefaults.Web));

    private static async Task<Guid> CreateAsync(HttpClient client, string name)
    {
        NewKey(client);
        using var response = await client.PostAsJsonAsync("/api/v1/counterparties",
            new { kind = 1, legalName = $"AT-009 {name} {Guid.NewGuid():N}", roles = 0 });
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return (await JsonAsync(response)).GetProperty("id").GetGuid();
    }

    private static async Task EnterAsync(HttpClient client, Guid party, string number)
    {
        var version = (await ReadAsync(client, PathFor(party))).GetProperty("version").GetString();
        NewKey(client);
        using var response = await PutAsync(client, PathFor(party), Body(new { expectedVersion = version, taxIdentityNumber = number }));
        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
    }

    private static async Task RequestAsync(HttpClient client, Guid party, string version, string proposed)
    {
        NewKey(client);
        using var response = await PutAsync(client, $"{PathFor(party)}/change-request",
            Body(new { expectedVersion = version, proposedTaxIdentityNumber = proposed, reason = "Test talebi" }));
        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
    }

    private static async Task<JsonElement> ReadAsync(HttpClient client, string path)
    {
        using var response = await client.GetAsync(path);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return await JsonAsync(response);
    }

    private static async Task<JsonElement> CheckDuplicatesAsync(HttpClient client, Guid party, string number)
    {
        using var response = await client.PostAsJsonAsync($"{PathFor(party)}/duplicates", new { taxIdentityNumber = number });
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

    private sealed class TaxTest : IAsyncDisposable
    {
        private readonly string _baseConnection = Environment.GetEnvironmentVariable(
            SqlServerRelationalAcceptanceTests.ConnectionStringEnvironmentVariable)!;

        public TaxTest(
            bool grant = true,
            IErpAuthorityRevalidationService? freshAuthority = null,
            params IInterceptor[] interceptors)
        {
            Factory = new SqlServerRelationalApiFactory(_baseConnection, services =>
            {
                if (interceptors.Length > 0)
                {
                    services.AddDbContext<CounterpartyDbContext>((_, options) => options.AddInterceptors(interceptors));
                }
                if (freshAuthority is not null)
                {
                    services.RemoveAll<IErpAuthorityRevalidationService>();
                    services.AddSingleton(freshAuthority);
                }
                if (!grant)
                {
                    return;
                }

                services.PostConfigure<IdentitySeedOptions>(options =>
                {
                    var administrator = options.Roles.Single(role => role.Code == "SYSTEM_ADMINISTRATOR");
                    administrator.Permissions.Add(CounterpartyTaxIdentityPermissionsV1.Update);
                    administrator.Permissions.Add(CounterpartyTaxIdentityPermissionsV1.Approve);
                    var operatorRole = options.Roles.Single(role => role.Code == "ORGANIZATION_OPERATOR");
                    operatorRole.Permissions.Add(ErpPermissions.CounterpartyPartiesRead);
                    operatorRole.Permissions.Add(CounterpartyTaxIdentityPermissionsV1.Approve);
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
            string? commandName = null)
        {
            await using var connection = new SqlConnection(Factory.DatabaseConnectionString);
            await connection.OpenAsync();
            await using var command = CreateCommand(connection, sql, party, eventType, commandName);
            return Assert.IsType<T>(await command.ExecuteScalarAsync());
        }

        public async Task ExecuteAsync(string sql, Guid party)
        {
            await using var connection = new SqlConnection(Factory.DatabaseConnectionString);
            await connection.OpenAsync();
            await using var command = CreateCommand(connection, sql, party, null, null);
            await command.ExecuteNonQueryAsync();
        }

        private static SqlCommand CreateCommand(
            SqlConnection connection,
            string sql,
            Guid party,
            string? eventType,
            string? commandName)
        {
            var command = connection.CreateCommand();
            command.CommandText = sql;
            if (sql.Contains("@Party", StringComparison.Ordinal)) command.Parameters.AddWithValue("@Party", party);
            if (sql.Contains("@Event", StringComparison.Ordinal)) command.Parameters.AddWithValue("@Event", eventType!);
            if (sql.Contains("@Command", StringComparison.Ordinal)) command.Parameters.AddWithValue("@Command", commandName!);
            if (sql.Contains("@Company", StringComparison.Ordinal)) command.Parameters.AddWithValue("@Company", TestCompanyIds.CompanyA);
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

    private sealed class RevocableAuthority : IErpAuthorityRevalidationService
    {
        private int _revoked;
        private int _deniedMeasurements;

        public int DeniedMeasurements => Volatile.Read(ref _deniedMeasurements);

        public void Revoke() => Volatile.Write(ref _revoked, 1);

        public Task<PermissionScope> RevalidatePermissionScopeAsync(
            string permission,
            Guid companyId,
            CancellationToken cancellationToken = default)
        {
            if (Volatile.Read(ref _revoked) != 0)
            {
                Interlocked.Increment(ref _deniedMeasurements);
                return Task.FromResult(PermissionScope.Denied);
            }

            return Task.FromResult(new PermissionScope(
                true,
                true,
                new HashSet<string>(StringComparer.Ordinal) { "SENTETIK-TAX-IDENTITY" },
                new HashSet<Guid>(),
                new HashSet<Guid>(),
                new HashSet<Guid>()));
        }
    }

    private sealed class DuplicateReadRevocationInterceptor(RevocableAuthority authority) : DbCommandInterceptor
    {
        private int _armed;
        private int _triggered;

        public bool Triggered => Volatile.Read(ref _triggered) != 0;

        public void Arm() => Volatile.Write(ref _armed, 1);

        public override ValueTask<DbDataReader> ReaderExecutedAsync(
            DbCommand command,
            CommandExecutedEventData eventData,
            DbDataReader result,
            CancellationToken cancellationToken = default)
        {
            if (Volatile.Read(ref _armed) != 0
                && command.CommandText.Contains("[counterparty].[CompanyPartyAccounts]", StringComparison.Ordinal)
                && command.CommandText.Contains("INNER JOIN", StringComparison.OrdinalIgnoreCase)
                && command.CommandText.Contains("[TaxIdentityNumber]", StringComparison.Ordinal)
                && Interlocked.CompareExchange(ref _triggered, 1, 0) == 0)
            {
                authority.Revoke();
            }

            return ValueTask.FromResult(result);
        }
    }

    private sealed class WinnerFailureRevocationInterceptor(
        Barrier barrier,
        RevocableAuthority authority) : SaveChangesInterceptor
    {
        private int _armed;
        private int _arrivals;
        private int _failureObserved;

        public bool FailureObserved => Volatile.Read(ref _failureObserved) != 0;

        public void Arm() => Volatile.Write(ref _armed, 1);

        public override ValueTask<InterceptionResult<int>> SavingChangesAsync(
            DbContextEventData eventData,
            InterceptionResult<int> result,
            CancellationToken cancellationToken = default)
        {
            if (Volatile.Read(ref _armed) != 0
                && eventData.Context is CounterpartyDbContext context
                && context.ChangeTracker.Entries<CommandExecution>().Any(entry =>
                    entry.Entity.CommandName == "counterparty.party.tax-identity.enter.v1")
                && Interlocked.Increment(ref _arrivals) <= 2)
            {
                barrier.SignalAndWait(TimeSpan.FromSeconds(30), cancellationToken);
            }

            return ValueTask.FromResult(result);
        }

        public override Task SaveChangesFailedAsync(
            DbContextErrorEventData eventData,
            CancellationToken cancellationToken = default)
        {
            if (Volatile.Read(ref _armed) != 0)
            {
                Interlocked.Exchange(ref _failureObserved, 1);
                authority.Revoke();
            }

            return Task.CompletedTask;
        }
    }
}
