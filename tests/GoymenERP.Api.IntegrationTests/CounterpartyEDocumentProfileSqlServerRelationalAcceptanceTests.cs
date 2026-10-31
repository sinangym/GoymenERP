using System.Collections.Concurrent;
using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using GoymenERP.BuildingBlocks.Multitenancy;
using GoymenERP.BuildingBlocks.Security;
using GoymenERP.Modules.Counterparty.Application.Contracts;
using GoymenERP.Modules.Counterparty.Infrastructure.Persistence;
using GoymenERP.Modules.Identity.Infrastructure.Seeding;
using GoymenERP.Modules.Settings.Application;
using GoymenERP.Modules.Settings.Application.Contracts.V1;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;

namespace GoymenERP.Api.IntegrationTests;

/// <summary>
/// SAT-D102 / SAT-D119 / SAT-D120 (Sinan, 2026-10-28 and 2026-10-31) — the customer's e-belge profile, shared across the
/// group by VKN/TCKN, against real SQL Server through the real API: the explicit Unknown status, the recorded check, the
/// VKN-keyed sharing and its narrow reach (only through the caller's own account), the profile-bound fence (token +
/// version: a VKN change is 409, never an ABA overwrite), the checker shown only inside the checking company, the
/// Settings-listed override and the effective scenario, the critical-command shape (permission, Idempotency-Key per
/// company, one audit per real change), concurrent first creation from two companies, the no-VKN fail-closed, the read
/// port, and the refused rollback.
/// </summary>
public sealed class CounterpartyEDocumentProfileSqlServerRelationalAcceptanceTests
{
    private const string SharedVkn = "1234567890";
    private const string OtherVkn = "9876543217";
    private const string StatusEvent = "counterparty.customer-e-document.status.recorded.v1";
    private const string ScenarioEvent = "counterparty.customer-e-document.e-invoice-scenario.changed.v1";
    private static readonly string NoToken = new('0', 64);

    [SqlServerFact]
    public async Task The_status_is_recorded_rechecked_and_reset_on_the_shared_profile_with_replay_and_version_fence()
    {
        await using var test = new EDocumentTest();
        using var admin = test.Client();
        var party = await CreateCustomerAsync(admin, "Durum", SharedVkn);

        var initial = await ReadAsync(admin, ProfilePath(party));
        Assert.True(initial.GetProperty("taxIdentityNumberPresent").GetBoolean());
        Assert.Equal("Unknown", initial.GetProperty("status").GetString());
        Assert.Equal(JsonValueKind.Null, initial.GetProperty("statusCheckedAtUtc").ValueKind);
        Assert.Equal(JsonValueKind.Null, initial.GetProperty("statusCheckedInActiveCompany").ValueKind);
        Assert.Equal("0", initial.GetProperty("version").GetString());
        Assert.Matches("^[0-9a-f]{64}$", initial.GetProperty("profileToken").GetString());
        Assert.True(initial.GetProperty("viewer").GetProperty("canUpdate").GetBoolean());
        // SAT-D120: the standard code list is served; SAT-D102: the default is empty, so the scenario is unresolved.
        Assert.Equal(["TEMELFATURA", "TICARIFATURA", "EARSIVFATURA", "IHRACAT"],
            initial.GetProperty("scenarioCodes").GetProperty("codes").EnumerateArray().Select(code => code.GetString()!).ToArray());
        Assert.Equal("NotSet", initial.GetProperty("settingsDefault").GetProperty("state").GetString());
        AssertEffective(initial, "Unresolved", "None", null, "SettingsDefaultNotSet");
        var noneToken = initial.GetProperty("profileToken").GetString()!;

        // "0" + the "no profile" token creates the shared profile; a replay repeats the stored 204; the same key with
        // another body is a conflict.
        NewKey(admin);
        var record = Body(new { expectedVersion = "0", expectedProfileToken = noneToken, status = "EInvoice" });
        using (var created = await PutAsync(admin, StatusPath(party), record))
        {
            Assert.Equal(HttpStatusCode.NoContent, created.StatusCode);
        }

        using (var replay = await PutAsync(admin, StatusPath(party), record))
        {
            Assert.Equal(HttpStatusCode.NoContent, replay.StatusCode);
            Assert.True(replay.Headers.Contains("Idempotency-Replayed"));
        }

        using (var reused = await PutAsync(admin, StatusPath(party),
                   Body(new { expectedVersion = "0", expectedProfileToken = noneToken, status = "EArchive" })))
        {
            await AssertProblemAsync(reused, HttpStatusCode.Conflict, "idempotency_conflict");
        }

        var checkedOnce = await ReadAsync(test.Client(), ProfilePath(party));
        Assert.Equal("EInvoice", checkedOnce.GetProperty("status").GetString());
        // Checked in this company: the reader is told so and sees the checker by display name (never the raw id).
        Assert.True(checkedOnce.GetProperty("statusCheckedInActiveCompany").GetBoolean());
        Assert.False(string.IsNullOrWhiteSpace(checkedOnce.GetProperty("statusCheckedByDisplayName").GetString()));
        Assert.False(checkedOnce.TryGetProperty("statusCheckedByUserId", out _));
        var firstCheck = checkedOnce.GetProperty("statusCheckedAtUtc").GetDateTimeOffset();
        Assert.Equal("1", checkedOnce.GetProperty("version").GetString());
        // The profile now exists, so its token is no longer the "no profile" token.
        Assert.NotEqual(noneToken, checkedOnce.GetProperty("profileToken").GetString());

        // The stale "0" with the old token is a conflict.
        NewKey(admin);
        using (var stale = await PutAsync(admin, StatusPath(party),
                   Body(new { expectedVersion = "0", expectedProfileToken = noneToken, status = "EArchive" })))
        {
            await AssertProblemAsync(stale, HttpStatusCode.Conflict, "version_conflict");
        }

        await RecordAsync(admin, party, "EInvoice");
        var rechecked = await ReadAsync(admin, ProfilePath(party));
        Assert.True(rechecked.GetProperty("statusCheckedAtUtc").GetDateTimeOffset() >= firstCheck);
        Assert.Equal("2", rechecked.GetProperty("version").GetString());

        await RecordAsync(admin, party, "EArchive");
        await RecordAsync(admin, party, "Unknown");
        var reset = await ReadAsync(admin, ProfilePath(party));
        Assert.Equal("Unknown", reset.GetProperty("status").GetString());
        Assert.Equal(JsonValueKind.Null, reset.GetProperty("statusCheckedAtUtc").ValueKind);
        Assert.Equal(JsonValueKind.Null, reset.GetProperty("statusCheckedInActiveCompany").ValueKind);
        Assert.Equal("4", reset.GetProperty("version").GetString());

        // Unknown → Unknown is a fenced no-op: no version move, no audit.
        await RecordAsync(admin, party, "Unknown");
        Assert.Equal("4", (await ReadAsync(admin, ProfilePath(party))).GetProperty("version").GetString());
        Assert.Equal(4, await test.ScalarAsync<int>(
            "SELECT COUNT(*) FROM [audit].[AuditEvents] WHERE [EventType]=@Event;", StatusEvent));
        Assert.Equal(1, await test.ScalarAsync<int>("SELECT COUNT(*) FROM [counterparty].[CustomerEDocumentProfiles];"));
        // The VKN never reaches the audit payload.
        Assert.Equal(0, await test.ScalarAsync<int>(
            "SELECT COUNT(*) FROM [audit].[AuditEvents] WHERE [EventType]=@Event AND ([BeforeJson] LIKE N'%1234567890%' OR [AfterJson] LIKE N'%1234567890%');",
            StatusEvent));

        var token = (await ReadAsync(admin, ProfilePath(party))).GetProperty("profileToken").GetString()!;
        foreach (var invalid in new[]
                 {
                     $$"""{"expectedVersion":"4","expectedProfileToken":"{{token}}","status":"Maybe"}""",
                     $$"""{"expectedVersion":"4","expectedProfileToken":"{{token}}","status":7}""",
                     $$"""{"expectedVersion":"4","expectedProfileToken":"{{token}}"}""",
                     $$"""{"expectedVersion":"04","expectedProfileToken":"{{token}}","status":"EInvoice"}""",
                     """{"expectedVersion":"4","status":"EInvoice"}""",
                     """{"expectedVersion":"4","expectedProfileToken":"ABC","status":"EInvoice"}""",
                     $$"""{"expectedVersion":"4","expectedProfileToken":"{{token.ToUpperInvariant()}}","status":"EInvoice"}""",
                     $$"""{"expectedVersion":"4","expectedProfileToken":"{{token}}","status":"EInvoice","checkedBy":"someone"}""",
                 })
        {
            NewKey(admin);
            using var refused = await PutAsync(admin, StatusPath(party), invalid);
            Assert.Equal(HttpStatusCode.BadRequest, refused.StatusCode);
        }
    }

    [SqlServerFact]
    public async Task A_check_in_one_company_is_seen_and_editable_on_the_same_VKN_customer_in_another_company_without_the_checker()
    {
        await using var test = new EDocumentTest();
        using var adminA = test.Client();
        using var adminB = test.Client(TestCompanyIds.CompanyB);
        // The same taxpayer held as a separate account (here a separate party) with the same VKN in each company.
        var partyA = await CreateCustomerAsync(adminA, "A", SharedVkn);
        var partyB = await CreateCustomerAsync(adminB, "B", SharedVkn);

        // Company A records the check; the group-level profile is created once.
        await RecordAsync(adminA, partyA, "EInvoice");

        // Company B sees the same status, date and version on its own same-VKN customer (SAT-D119) — but not the checker
        // (P3-1): only that the check was made in another group company.
        var seenByB = await ReadAsync(adminB, ProfilePath(partyB));
        Assert.Equal("EInvoice", seenByB.GetProperty("status").GetString());
        Assert.Equal("1", seenByB.GetProperty("version").GetString());
        Assert.Equal(partyB, seenByB.GetProperty("partyId").GetGuid());
        Assert.Equal(JsonValueKind.String, seenByB.GetProperty("statusCheckedAtUtc").ValueKind);
        Assert.False(seenByB.GetProperty("statusCheckedInActiveCompany").GetBoolean());
        Assert.Equal(JsonValueKind.Null, seenByB.GetProperty("statusCheckedByDisplayName").ValueKind);
        var seenByA = await ReadAsync(adminA, ProfilePath(partyA));
        Assert.True(seenByA.GetProperty("statusCheckedInActiveCompany").GetBoolean());
        // Both companies read the same profile, so they hold the same token.
        Assert.Equal(seenByA.GetProperty("profileToken").GetString(), seenByB.GetProperty("profileToken").GetString());

        // Company B sets the scenario on the shared profile; company A sees it. B's check-of-record is still A's.
        await SetScenarioAsync(adminB, partyB, "TICARIFATURA");
        seenByA = await ReadAsync(adminA, ProfilePath(partyA));
        Assert.Equal("TICARIFATURA", seenByA.GetProperty("scenarioOverrideCode").GetString());
        AssertEffective(seenByA, "Resolved", "CustomerOverride", "TICARIFATURA", "None");
        Assert.Equal("2", seenByA.GetProperty("version").GetString());
        Assert.True(seenByA.GetProperty("statusCheckedInActiveCompany").GetBoolean());

        // When B records a new check, the checker moves to B: B sees its own checker, A no longer does.
        await RecordAsync(adminB, partyB, "EArchive");
        Assert.True((await ReadAsync(adminB, ProfilePath(partyB))).GetProperty("statusCheckedInActiveCompany").GetBoolean());
        var afterB = await ReadAsync(adminA, ProfilePath(partyA));
        Assert.False(afterB.GetProperty("statusCheckedInActiveCompany").GetBoolean());
        Assert.Equal(JsonValueKind.Null, afterB.GetProperty("statusCheckedByDisplayName").ValueKind);

        // One shared row, not one per company; both companies' accounts resolve to it through the port.
        Assert.Equal(1, await test.ScalarAsync<int>("SELECT COUNT(*) FROM [counterparty].[CustomerEDocumentProfiles];"));
        var accountB = seenByB.GetProperty("accountId").GetGuid();
        var portB = await test.ReadPortAsync(accountB, new DateOnly(2026, 11, 2), company: TestCompanyIds.CompanyB, context: TestCompanyIds.CompanyB);
        Assert.Equal(CounterpartyAccountEDocumentProfileOutcomeV1.Found, portB.Outcome);
        Assert.Equal(CounterpartyEDocumentStatusV1.EArchive, portB.Account!.Status);
        Assert.True(portB.Account.TaxIdentityNumberPresent);
        Assert.Equal("TICARIFATURA", portB.Account.EffectiveScenario.ScenarioCode);
        Assert.Equal("3", portB.Account.ProfileVersion);
        Assert.Equal(seenByA.GetProperty("profileToken").GetString(), portB.Account.ProfileToken);

        // A different customer with a different VKN has its own (absent) profile — the sharing is by VKN only.
        var other = await CreateCustomerAsync(adminA, "Other", OtherVkn);
        Assert.Equal("Unknown", (await ReadAsync(adminA, ProfilePath(other))).GetProperty("status").GetString());
        // Audit is written in the acting company: A's check in A, B's scenario and check in B.
        Assert.Equal(1, await test.ScalarAsync<int>(
            "SELECT COUNT(*) FROM [audit].[AuditEvents] WHERE [EventType]=@Event AND [CompanyId]=@CompanyA;",
            StatusEvent, companyA: TestCompanyIds.CompanyA));
        Assert.Equal(2, await test.ScalarAsync<int>(
            "SELECT COUNT(*) FROM [audit].[AuditEvents] WHERE [EventType] IN (@Event, @Scenario) AND [CompanyId]=@CompanyA;",
            StatusEvent, ScenarioEvent, companyA: TestCompanyIds.CompanyB));
    }

    [SqlServerFact]
    public async Task A_VKN_change_moves_the_card_to_the_new_number_keeps_the_old_profile_and_a_stale_post_is_409()
    {
        await using var test = new EDocumentTest(grantApprove: true);
        using var adminA = test.Client();
        using var checkerA = test.Client(TestCompanyIds.CompanyA, TestUsers.CompanyA);
        using var adminB = test.Client(TestCompanyIds.CompanyB);
        var party = await CreateCustomerAsync(adminA, "VKN değişen", SharedVkn);
        var otherTaxpayer = await CreateCustomerAsync(adminB, "Diğer mükellef", OtherVkn);

        // X (SharedVkn) and Y (OtherVkn) both get a profile at version 1 — the ABA set-up: equal numbers, other records.
        await RecordAsync(adminA, party, "EInvoice");
        await RecordAsync(adminB, otherTaxpayer, "EArchive");
        var stale = await ReadAsync(adminA, ProfilePath(party));
        Assert.Equal("1", stale.GetProperty("version").GetString());
        var staleToken = stale.GetProperty("profileToken").GetString()!;

        // AT-009 maker-checker changes the party's VKN from X to Y (OPEN-030).
        var partyVersion = (await ReadAsync(adminA, $"/api/v1/counterparties/{party}/tax-identity")).GetProperty("version").GetString();
        NewKey(adminA);
        using (var requested = await PutAsync(adminA, $"/api/v1/counterparties/{party}/tax-identity/change-request",
                   Body(new { expectedVersion = partyVersion, proposedTaxIdentityNumber = OtherVkn, reason = "Levha değişti" })))
        {
            Assert.Equal(HttpStatusCode.NoContent, requested.StatusCode);
        }

        var requestId = (await ReadAsync(checkerA, $"/api/v1/counterparties/{party}/tax-identity"))
            .GetProperty("pendingChange").GetProperty("id").GetGuid();
        NewKey(checkerA);
        using (var approved = await PutAsync(checkerA, $"/api/v1/counterparties/{party}/tax-identity/change-request/decision",
                   Body(new { expectedVersion = partyVersion, changeRequestId = requestId, decision = "approve", note = "Levha görüldü" })))
        {
            Assert.Equal(HttpStatusCode.NoContent, approved.StatusCode);
        }

        // A client still showing X posts with X's fence: the numbers match Y's profile (version 1), but the token names
        // X's profile, so the command is refused — Y's record is not overwritten.
        NewKey(adminA);
        using (var abaPost = await PutAsync(adminA, StatusPath(party),
                   Body(new { expectedVersion = "1", expectedProfileToken = staleToken, status = "EInvoice" })))
        {
            await AssertProblemAsync(abaPost, HttpStatusCode.Conflict, "version_conflict");
        }

        // The card now reads Y's profile; X's profile stays as it was (not moved, not deleted).
        var afterChange = await ReadAsync(adminA, ProfilePath(party));
        Assert.Equal("EArchive", afterChange.GetProperty("status").GetString());
        Assert.Equal("1", afterChange.GetProperty("version").GetString());
        Assert.NotEqual(staleToken, afterChange.GetProperty("profileToken").GetString());
        Assert.Equal(2, await test.ScalarAsync<int>("SELECT COUNT(*) FROM [counterparty].[CustomerEDocumentProfiles];"));
        Assert.Equal(1, await test.ScalarAsync<int>(
            "SELECT COUNT(*) FROM [counterparty].[CustomerEDocumentProfiles] WHERE [TaxIdentityNumber]=N'1234567890' AND [EDocumentStatus]=1 AND [Version]=1;"));
        Assert.Equal(1, await test.ScalarAsync<int>(
            "SELECT COUNT(*) FROM [counterparty].[CustomerEDocumentProfiles] WHERE [TaxIdentityNumber]=N'9876543217' AND [EDocumentStatus]=2 AND [Version]=1;"));
    }

    [SqlServerFact]
    public async Task An_existing_party_held_only_by_company_A_is_404_for_company_B_and_the_port_never_reaches_it()
    {
        await using var test = new EDocumentTest();
        using var adminA = test.Client();
        using var adminB = test.Client(TestCompanyIds.CompanyB);
        var party = await CreateCustomerAsync(adminA, "Yalnız A", SharedVkn);
        await RecordAsync(adminA, party, "EInvoice");
        var view = await ReadAsync(adminA, ProfilePath(party));
        var token = view.GetProperty("profileToken").GetString()!;
        var accountA = view.GetProperty("accountId").GetGuid();
        var auditBefore = await test.ScalarAsync<int>("SELECT COUNT(*) FROM [audit].[AuditEvents] WHERE [EventType] LIKE N'counterparty.customer-e-document.%';");

        // B names A's REAL party id (not a random one): every route answers the same 404, nothing is written.
        using (var read = await adminB.GetAsync(ProfilePath(party)))
        {
            await AssertProblemAsync(read, HttpStatusCode.NotFound, "not_found");
        }

        foreach (var (path, body) in new[]
                 {
                     (StatusPath(party), Body(new { expectedVersion = "1", expectedProfileToken = token, status = "EArchive" })),
                     (StatusPath(party), Body(new { expectedVersion = "1", expectedProfileToken = token, status = "Unknown" })),
                     (ScenarioPath(party), Body(new { expectedVersion = "1", expectedProfileToken = token, scenarioCode = "TEMELFATURA" })),
                 })
        {
            NewKey(adminB);
            using var refused = await PutAsync(adminB, path, body);
            await AssertProblemAsync(refused, HttpStatusCode.NotFound, "not_found");
        }

        var unchanged = await ReadAsync(adminA, ProfilePath(party));
        Assert.Equal("EInvoice", unchanged.GetProperty("status").GetString());
        Assert.Equal("1", unchanged.GetProperty("version").GetString());
        Assert.Equal(JsonValueKind.Null, unchanged.GetProperty("scenarioOverrideCode").ValueKind);
        Assert.Equal(auditBefore, await test.ScalarAsync<int>("SELECT COUNT(*) FROM [audit].[AuditEvents] WHERE [EventType] LIKE N'counterparty.customer-e-document.%';"));

        // The port: company B's context with A's real account id is NotFound; asking for company B from A's context is
        // Denied. Neither reveals the profile.
        Assert.Equal(CounterpartyAccountEDocumentProfileOutcomeV1.NotFound,
            (await test.ReadPortAsync(accountA, new DateOnly(2026, 11, 2), company: TestCompanyIds.CompanyB, context: TestCompanyIds.CompanyB)).Outcome);
        Assert.Equal(CounterpartyAccountEDocumentProfileOutcomeV1.Denied,
            (await test.ReadPortAsync(accountA, new DateOnly(2026, 11, 2), company: TestCompanyIds.CompanyB)).Outcome);
        Assert.Equal(CounterpartyAccountEDocumentProfileOutcomeV1.Found,
            (await test.ReadPortAsync(accountA, new DateOnly(2026, 11, 2))).Outcome);
    }

    [SqlServerFact]
    public async Task Concurrent_first_creation_from_two_companies_leaves_one_row_and_answers_204_and_409()
    {
        var logs = new ConcurrentQueue<string>();
        await using var test = new EDocumentTest(logs: logs);
        using var adminA = test.Client();
        using var adminB = test.Client(TestCompanyIds.CompanyB);
        var partyA = await CreateCustomerAsync(adminA, "Eşzamanlı A", SharedVkn);
        var partyB = await CreateCustomerAsync(adminB, "Eşzamanlı B", SharedVkn);
        var tokenA = (await ReadAsync(adminA, ProfilePath(partyA))).GetProperty("profileToken").GetString()!;
        var tokenB = (await ReadAsync(adminB, ProfilePath(partyB))).GetProperty("profileToken").GetString()!;
        Assert.Equal(tokenA, tokenB);

        NewKey(adminA);
        NewKey(adminB);
        var responses = await Task.WhenAll(
            PutAsync(adminA, StatusPath(partyA), Body(new { expectedVersion = "0", expectedProfileToken = tokenA, status = "EInvoice" })),
            PutAsync(adminB, StatusPath(partyB), Body(new { expectedVersion = "0", expectedProfileToken = tokenB, status = "EArchive" })));
        try
        {
            Assert.Equal([HttpStatusCode.NoContent, HttpStatusCode.Conflict],
                responses.Select(response => response.StatusCode).OrderBy(code => (int)code).ToArray());
            var loser = responses.Single(response => response.StatusCode == HttpStatusCode.Conflict);
            await AssertProblemAsync(loser, HttpStatusCode.Conflict, "version_conflict");
        }
        finally
        {
            foreach (var response in responses)
            {
                response.Dispose();
            }
        }

        Assert.Equal(1, await test.ScalarAsync<int>("SELECT COUNT(*) FROM [counterparty].[CustomerEDocumentProfiles];"));
        Assert.Equal(1, await test.ScalarAsync<int>(
            "SELECT COUNT(*) FROM [audit].[AuditEvents] WHERE [EventType]=@Event;", StatusEvent));
        Assert.Equal("1", (await ReadAsync(adminA, ProfilePath(partyA))).GetProperty("version").GetString());

        // B-P2-2 / AT-009: the loser waits on the create's key-range lock instead of meeting the unique index, so no
        // duplicate-key message — which would name the VKN — is ever logged (by EF Core or anyone else).
        Assert.NotEmpty(logs);
        Assert.DoesNotContain(logs, entry => entry.Contains(SharedVkn, StringComparison.Ordinal));
        Assert.DoesNotContain(logs, entry => entry.Contains("duplicate key", StringComparison.OrdinalIgnoreCase));
    }

    [SqlServerFact]
    public async Task Another_company_s_audit_never_carries_the_checker_or_the_checking_company_but_the_checking_company_s_does()
    {
        await using var test = new EDocumentTest();
        using var adminA = test.Client();
        using var adminB = test.Client(TestCompanyIds.CompanyB);
        var partyA = await CreateCustomerAsync(adminA, "Denetim A", SharedVkn);
        var partyB = await CreateCustomerAsync(adminB, "Denetim B", SharedVkn);
        await RecordAsync(adminA, partyA, "EInvoice");
        var checker = await test.ScalarAsync<string>(
            "SELECT [EDocumentStatusCheckedByUserId] FROM [counterparty].[CustomerEDocumentProfiles];");
        var companyA = TestCompanyIds.CompanyA.ToString("D");

        // B changes the scenario (A's check is in Before and After) and resets the status (A's check is in Before).
        await SetScenarioAsync(adminB, partyB, "TICARIFATURA");
        await RecordAsync(adminB, partyB, "Unknown");

        // B-P2-1: B's audit API shows that a check existed and when, never who made it or in which company.
        const string history = "/api/v1/audit/events?entityType=Counterparty.CustomerEDocumentProfile";
        var itemsB = (await ReadAsync(adminB, history)).GetProperty("items").EnumerateArray().ToArray();
        Assert.Equal(2, itemsB.Length);
        foreach (var item in itemsB)
        {
            Assert.Equal(TestCompanyIds.CompanyB, item.GetProperty("companyId").GetGuid());
            foreach (var image in new[] { item.GetProperty("beforeJson").GetString()!, item.GetProperty("afterJson").GetString()! })
            {
                Assert.DoesNotContain(checker, image, StringComparison.OrdinalIgnoreCase);
                Assert.DoesNotContain(companyA, image, StringComparison.OrdinalIgnoreCase);
                Assert.DoesNotContain(SharedVkn, image, StringComparison.Ordinal);
            }
        }

        var scenarioB = itemsB.Single(item => item.GetProperty("eventType").GetString() == ScenarioEvent);
        using (var after = JsonDocument.Parse(scenarioB.GetProperty("afterJson").GetString()!))
        {
            Assert.False(after.RootElement.GetProperty("EDocumentStatusCheckedInActingCompany").GetBoolean());
            Assert.Equal(JsonValueKind.Null, after.RootElement.GetProperty("EDocumentStatusCheckedByUserId").ValueKind);
            Assert.Equal(JsonValueKind.Null, after.RootElement.GetProperty("EDocumentStatusCheckedInCompanyId").ValueKind);
            Assert.Equal(JsonValueKind.String, after.RootElement.GetProperty("EDocumentStatusCheckedAtUtc").ValueKind);
        }

        // The checking company's own audit keeps its check of record.
        var itemA = Assert.Single((await ReadAsync(adminA, history)).GetProperty("items").EnumerateArray());
        using (var after = JsonDocument.Parse(itemA.GetProperty("afterJson").GetString()!))
        {
            Assert.True(after.RootElement.GetProperty("EDocumentStatusCheckedInActingCompany").GetBoolean());
            Assert.Equal(checker, after.RootElement.GetProperty("EDocumentStatusCheckedByUserId").GetString());
            Assert.Equal(TestCompanyIds.CompanyA, after.RootElement.GetProperty("EDocumentStatusCheckedInCompanyId").GetGuid());
        }

        // The stored rows say the same as the API: nothing of A's check under company B (@Event = the checker,
        // @Scenario = company A, @CompanyA = company B here).
        Assert.Equal(0, await test.ScalarAsync<int>(
            "SELECT COUNT(*) FROM [audit].[AuditEvents] WHERE [EntityType]=N'Counterparty.CustomerEDocumentProfile' "
            + "AND [CompanyId]=@CompanyA AND ([BeforeJson] LIKE N'%' + @Event + N'%' OR [AfterJson] LIKE N'%' + @Event + N'%' "
            + "OR [BeforeJson] LIKE N'%' + @Scenario + N'%' OR [AfterJson] LIKE N'%' + @Scenario + N'%');",
            checker, companyA, companyA: TestCompanyIds.CompanyB));
    }

    [SqlServerFact]
    public async Task A_passive_account_reads_the_shared_profile_but_both_commands_are_a_stored_422_that_change_nothing()
    {
        await using var test = new EDocumentTest();
        using var admin = test.Client();
        var party = await CreateCustomerAsync(admin, "Pasif", SharedVkn);
        await RecordAsync(admin, party, "EInvoice");
        var fence = await ReadAsync(admin, ProfilePath(party));
        await test.ExecuteAsync(
            $"UPDATE [counterparty].[CompanyPartyAccounts] SET [IsActive]=0 WHERE [PartyId]='{party:D}';");

        var passive = await ReadAsync(admin, ProfilePath(party));
        Assert.False(passive.GetProperty("accountIsActive").GetBoolean());
        Assert.Equal("EInvoice", passive.GetProperty("status").GetString());
        Assert.Equal("1", passive.GetProperty("version").GetString());

        var key = NewKey(admin);
        var body = Body(new
        {
            expectedVersion = "1", expectedProfileToken = fence.GetProperty("profileToken").GetString(), status = "EArchive",
        });
        using (var refused = await PutAsync(admin, StatusPath(party), body))
        {
            await AssertProblemAsync(refused, HttpStatusCode.UnprocessableEntity, "e_document_account_inactive");
        }

        UseKey(admin, key);
        using (var replay = await PutAsync(admin, StatusPath(party), body))
        {
            await AssertProblemAsync(replay, HttpStatusCode.UnprocessableEntity, "e_document_account_inactive");
            Assert.True(replay.Headers.Contains("Idempotency-Replayed"));
        }

        NewKey(admin);
        using (var scenario = await PutAsync(admin, ScenarioPath(party), Body(new
               {
                   expectedVersion = "1", expectedProfileToken = fence.GetProperty("profileToken").GetString(),
                   scenarioCode = "TICARIFATURA",
               })))
        {
            await AssertProblemAsync(scenario, HttpStatusCode.UnprocessableEntity, "e_document_account_inactive");
        }

        var unchanged = await ReadAsync(admin, ProfilePath(party));
        Assert.Equal("1", unchanged.GetProperty("version").GetString());
        Assert.Equal("EInvoice", unchanged.GetProperty("status").GetString());
        Assert.Equal(JsonValueKind.Null, unchanged.GetProperty("scenarioOverrideCode").ValueKind);
        Assert.Equal(1, await test.ScalarAsync<int>(
            "SELECT COUNT(*) FROM [audit].[AuditEvents] WHERE [EntityType]=N'Counterparty.CustomerEDocumentProfile';"));
    }

    [SqlServerFact]
    public async Task The_same_Idempotency_Key_in_two_companies_is_two_commands_and_the_second_is_a_stale_version_409()
    {
        await using var test = new EDocumentTest();
        using var adminA = test.Client();
        using var adminB = test.Client(TestCompanyIds.CompanyB);
        var partyA = await CreateCustomerAsync(adminA, "Anahtar A", SharedVkn);
        var partyB = await CreateCustomerAsync(adminB, "Anahtar B", SharedVkn);
        var token = (await ReadAsync(adminA, ProfilePath(partyA))).GetProperty("profileToken").GetString()!;
        var key = Guid.NewGuid().ToString("N");
        UseKey(adminA, key);
        UseKey(adminB, key);
        var body = Body(new { expectedVersion = "0", expectedProfileToken = token, status = "EInvoice" });

        using (var first = await PutAsync(adminA, StatusPath(partyA), body))
        {
            Assert.Equal(HttpStatusCode.NoContent, first.StatusCode);
        }

        // Same key, same body, other company: not a replay of A's answer — B's own command, refused on the now-stale fence.
        using (var second = await PutAsync(adminB, StatusPath(partyB), body))
        {
            await AssertProblemAsync(second, HttpStatusCode.Conflict, "version_conflict");
            Assert.False(second.Headers.Contains("Idempotency-Replayed"));
        }

        Assert.Equal(1, await test.ScalarAsync<int>("SELECT COUNT(*) FROM [counterparty].[CustomerEDocumentProfiles];"));
        Assert.Equal(2, await test.ScalarAsync<int>(
            "SELECT COUNT(DISTINCT [CompanyId]) FROM [idempotency].[CommandExecutions] WHERE [CommandName]=N'counterparty.customer-e-document.status.record.v1';"));
    }

    [SqlServerFact]
    public async Task The_override_must_be_a_Settings_listed_code_and_resolves_override_then_default_then_unresolved()
    {
        var settings = new SettingsDouble { Codes = ["TEMELFATURA", "TICARIFATURA"] };
        await using var test = new EDocumentTest(settings: settings);
        using var admin = test.Client();
        var party = await CreateCustomerAsync(admin, "Senaryo", SharedVkn);
        var noneToken = (await ReadAsync(admin, ProfilePath(party))).GetProperty("profileToken").GetString()!;

        NewKey(admin);
        using (var notListed = await PutAsync(admin, ScenarioPath(party),
                   Body(new { expectedVersion = "0", expectedProfileToken = noneToken, scenarioCode = "IHRACAT" })))
        {
            await AssertProblemAsync(notListed, HttpStatusCode.UnprocessableEntity, "einvoice_scenario_not_listed");
        }

        foreach (var malformed in new[] { "temelfatura", "", "CODE-A", new string('A', 33) })
        {
            NewKey(admin);
            using var refused = await PutAsync(admin, ScenarioPath(party),
                Body(new { expectedVersion = "0", expectedProfileToken = noneToken, scenarioCode = malformed }));
            Assert.Equal(HttpStatusCode.BadRequest, refused.StatusCode);
        }

        NewKey(admin);
        using (var omitted = await PutAsync(admin, ScenarioPath(party), Body(new { expectedVersion = "0", expectedProfileToken = noneToken })))
        {
            Assert.Equal(HttpStatusCode.BadRequest, omitted.StatusCode);
        }

        // "0" creates the profile with the override (status stays Unknown).
        NewKey(admin);
        var set = Body(new { expectedVersion = "0", expectedProfileToken = noneToken, scenarioCode = "TEMELFATURA" });
        using (var applied = await PutAsync(admin, ScenarioPath(party), set))
        {
            Assert.Equal(HttpStatusCode.NoContent, applied.StatusCode);
        }

        using (var replay = await PutAsync(admin, ScenarioPath(party), set))
        {
            Assert.Equal(HttpStatusCode.NoContent, replay.StatusCode);
            Assert.True(replay.Headers.Contains("Idempotency-Replayed"));
        }

        var overridden = await ReadAsync(admin, ProfilePath(party));
        Assert.Equal("TEMELFATURA", overridden.GetProperty("scenarioOverrideCode").GetString());
        Assert.Equal("Unknown", overridden.GetProperty("status").GetString());
        AssertEffective(overridden, "Resolved", "CustomerOverride", "TEMELFATURA", "None");

        // The Settings default applies once the override is cleared.
        settings.Default = "TICARIFATURA";
        await SetScenarioAsync(admin, party, null);
        AssertEffective(await ReadAsync(admin, ProfilePath(party)), "Resolved", "SettingsDefault", "TICARIFATURA", "None");

        // An override the list later drops is NOT honoured and does NOT fall back to the default.
        await SetScenarioAsync(admin, party, "TEMELFATURA");
        settings.Codes = ["TICARIFATURA"];
        AssertEffective(await ReadAsync(admin, ProfilePath(party)), "Unresolved", "CustomerOverride", null, "CustomerOverrideNotListed");

        // Settings that cannot be read: the read says Unavailable; a new override stores nothing (503); the same key
        // succeeds once Settings answers again.
        var fence = await ReadAsync(admin, ProfilePath(party));
        settings.Fail = true;
        AssertEffective(await ReadAsync(admin, ProfilePath(party)), "Unavailable", "None", null, "None");
        const string ScenarioExecutions =
            "SELECT COUNT(*) FROM [idempotency].[CommandExecutions] WHERE [CommandName]=N'counterparty.customer-e-document.e-invoice-scenario.set.v1';";
        var storedBefore = await test.ScalarAsync<int>(ScenarioExecutions);
        var retryKey = NewKey(admin);
        var change = Body(new
        {
            expectedVersion = fence.GetProperty("version").GetString(),
            expectedProfileToken = fence.GetProperty("profileToken").GetString(),
            scenarioCode = "TICARIFATURA",
        });
        using (var outage = await PutAsync(admin, ScenarioPath(party), change))
        {
            await AssertProblemAsync(outage, HttpStatusCode.ServiceUnavailable, "service_unavailable");
        }

        Assert.Equal(storedBefore, await test.ScalarAsync<int>(ScenarioExecutions));
        settings.Fail = false;
        Assert.Equal(retryKey, admin.DefaultRequestHeaders.GetValues("Idempotency-Key").Single());
        using (var retried = await PutAsync(admin, ScenarioPath(party), change))
        {
            Assert.Equal(HttpStatusCode.NoContent, retried.StatusCode);
        }

        AssertEffective(await ReadAsync(admin, ProfilePath(party)), "Resolved", "CustomerOverride", "TICARIFATURA", "None");
        // Set TEMELFATURA, clear, set TEMELFATURA, set TICARIFATURA: four real changes.
        Assert.Equal(4, await test.ScalarAsync<int>(
            "SELECT COUNT(*) FROM [audit].[AuditEvents] WHERE [EventType]=@Event;", ScenarioEvent));
    }

    [SqlServerFact]
    public async Task A_party_without_a_VKN_cannot_have_a_profile_and_a_missing_account_or_malformed_row_is_refused()
    {
        await using var test = new EDocumentTest();
        using var admin = test.Client();
        var noVkn = await CreatePartyAsync(admin, "VKN'siz");

        var view = await ReadAsync(admin, ProfilePath(noVkn));
        Assert.False(view.GetProperty("taxIdentityNumberPresent").GetBoolean());
        Assert.Equal("Unknown", view.GetProperty("status").GetString());
        Assert.Equal("0", view.GetProperty("version").GetString());
        Assert.Equal(JsonValueKind.Null, view.GetProperty("profileToken").ValueKind);

        NewKey(admin);
        using (var status = await PutAsync(admin, StatusPath(noVkn),
                   Body(new { expectedVersion = "0", expectedProfileToken = NoToken, status = "EInvoice" })))
        {
            await AssertProblemAsync(status, HttpStatusCode.UnprocessableEntity, "e_document_tax_identity_required");
        }

        NewKey(admin);
        using (var scenario = await PutAsync(admin, ScenarioPath(noVkn),
                   Body(new { expectedVersion = "0", expectedProfileToken = NoToken, scenarioCode = "TEMELFATURA" })))
        {
            await AssertProblemAsync(scenario, HttpStatusCode.UnprocessableEntity, "e_document_tax_identity_required");
        }

        // A party with no account in the company is 404; the GET of an unknown party is 404.
        NewKey(admin);
        using (var missing = await PutAsync(admin, StatusPath(Guid.NewGuid()),
                   Body(new { expectedVersion = "0", expectedProfileToken = NoToken, status = "EInvoice" })))
        {
            await AssertProblemAsync(missing, HttpStatusCode.NotFound, "not_found");
        }

        using (var unknown = await admin.GetAsync(ProfilePath(Guid.NewGuid())))
        {
            Assert.Equal(HttpStatusCode.NotFound, unknown.StatusCode);
        }

        // The database repeats the rules: a known status needs its check and company, the override's shape, the VKN digits.
        foreach (var insert in new[]
                 {
                     "INSERT INTO [counterparty].[CustomerEDocumentProfiles] ([Id],[TaxIdentityNumber],[EDocumentStatus],[Version],[CreatedAtUtc],[UpdatedAtUtc]) VALUES (NEWID(), N'1234567890', 1, 1, SYSDATETIMEOFFSET(), SYSDATETIMEOFFSET());",
                     "INSERT INTO [counterparty].[CustomerEDocumentProfiles] ([Id],[TaxIdentityNumber],[EDocumentStatus],[EDocumentStatusCheckedAtUtc],[EDocumentStatusCheckedByUserId],[Version],[CreatedAtUtc],[UpdatedAtUtc]) VALUES (NEWID(), N'1234567890', 1, SYSDATETIMEOFFSET(), N'x', 1, SYSDATETIMEOFFSET(), SYSDATETIMEOFFSET());",
                     "INSERT INTO [counterparty].[CustomerEDocumentProfiles] ([Id],[TaxIdentityNumber],[EDocumentStatus],[EInvoiceScenarioOverrideCode],[Version],[CreatedAtUtc],[UpdatedAtUtc]) VALUES (NEWID(), N'1234567890', 0, N'temel', 1, SYSDATETIMEOFFSET(), SYSDATETIMEOFFSET());",
                     "INSERT INTO [counterparty].[CustomerEDocumentProfiles] ([Id],[TaxIdentityNumber],[EDocumentStatus],[Version],[CreatedAtUtc],[UpdatedAtUtc]) VALUES (NEWID(), N'12A4567890', 0, 1, SYSDATETIMEOFFSET(), SYSDATETIMEOFFSET());",
                 })
        {
            var error = await Assert.ThrowsAsync<SqlException>(() => test.ExecuteAsync(insert));
            Assert.Equal(547, error.Number);
        }

        Assert.Equal(0, await test.ScalarAsync<int>("SELECT COUNT(*) FROM [counterparty].[CustomerEDocumentProfiles];"));
    }

    [SqlServerFact]
    public async Task Without_the_grant_both_commands_are_403_and_the_port_is_denied_or_unavailable()
    {
        await using var test = new EDocumentTest(grant: false);
        using var reader = test.Client();
        var party = await CreateCustomerAsync(reader, "Yetkisiz", SharedVkn, grantTaxIdentity: false);

        var view = await ReadAsync(reader, ProfilePath(party));
        Assert.False(view.GetProperty("viewer").GetProperty("canUpdate").GetBoolean());
        var accountId = view.GetProperty("accountId").GetGuid();

        NewKey(reader);
        using (var status = await PutAsync(reader, StatusPath(party),
                   Body(new { expectedVersion = "0", expectedProfileToken = NoToken, status = "EInvoice" })))
        {
            Assert.Equal(HttpStatusCode.Forbidden, status.StatusCode);
        }

        NewKey(reader);
        using (var scenario = await PutAsync(reader, ScenarioPath(party),
                   Body(new { expectedVersion = "0", expectedProfileToken = NoToken, scenarioCode = "TEMELFATURA" })))
        {
            Assert.Equal(HttpStatusCode.Forbidden, scenario.StatusCode);
        }

        // Without counterparty.parties.read the port is Denied; an owner failure is Unavailable — never "Unknown".
        Assert.Equal(CounterpartyAccountEDocumentProfileOutcomeV1.Denied,
            (await test.ReadPortAsync(accountId, new DateOnly(2026, 11, 2), granted: false)).Outcome);
        Assert.Equal(CounterpartyAccountEDocumentProfileOutcomeV1.Unavailable,
            (await test.ReadPortAsync(accountId, new DateOnly(2026, 11, 2), failing: true)).Outcome);

        Assert.Equal(0, await test.ScalarAsync<int>("SELECT COUNT(*) FROM [counterparty].[CustomerEDocumentProfiles];"));
        Assert.Equal(0, await test.ScalarAsync<int>(
            "SELECT COUNT(*) FROM [idempotency].[CommandExecutions] WHERE [CommandName] LIKE N'counterparty.customer-e-document.%';"));
    }

    [SqlServerFact]
    public async Task Rollback_is_refused_while_a_profile_exists_and_allowed_on_an_empty_table()
    {
        await using var test = new EDocumentTest();
        using var admin = test.Client();
        var party = await CreateCustomerAsync(admin, "Geri Alma", SharedVkn);
        await RecordAsync(admin, party, "EArchive");

        var error = await Assert.ThrowsAnyAsync<SqlException>(
            () => test.MigrateCounterpartyToAsync("20261027202534_AddCounterpartyCustomerGroups"));
        Assert.Equal(51122, error.Number);
        Assert.Equal(1, await test.ScalarAsync<int>("SELECT COUNT(*) FROM [counterparty].[CustomerEDocumentProfiles];"));

        await test.ExecuteAsync("DELETE FROM [counterparty].[CustomerEDocumentProfiles];");
        await test.MigrateCounterpartyToAsync("20261027202534_AddCounterpartyCustomerGroups");
        Assert.Equal(0, await test.ScalarAsync<int>(
            "SELECT COUNT(*) FROM sys.tables WHERE [name]=N'CustomerEDocumentProfiles' AND SCHEMA_NAME([schema_id])=N'counterparty';"));
        await test.MigrateCounterpartyToAsync(null);
        Assert.Equal(1, await test.ScalarAsync<int>(
            "SELECT COUNT(*) FROM sys.tables WHERE [name]=N'CustomerEDocumentProfiles' AND SCHEMA_NAME([schema_id])=N'counterparty';"));
    }

    private static void AssertEffective(JsonElement view, string outcome, string source, string? code, string reason)
    {
        var effective = view.GetProperty("effectiveScenario");
        Assert.Equal(outcome, effective.GetProperty("outcome").GetString());
        Assert.Equal(source, effective.GetProperty("source").GetString());
        Assert.Equal(code, effective.GetProperty("scenarioCode").ValueKind == JsonValueKind.Null
            ? null
            : effective.GetProperty("scenarioCode").GetString());
        Assert.Equal(reason, effective.GetProperty("unresolvedReason").GetString());
    }

    private static string ProfilePath(Guid party) => $"/api/v1/counterparties/{party}/company-account/e-document";

    private static string StatusPath(Guid party) => ProfilePath(party) + "/status";

    private static string ScenarioPath(Guid party) => ProfilePath(party) + "/scenario";

    private static string Body(object value) => JsonSerializer.Serialize(value);

    /// <summary>Records a status on the fence the latest read returned (version + token).</summary>
    private static async Task RecordAsync(HttpClient client, Guid party, string status)
    {
        var fence = await ReadAsync(client, ProfilePath(party));
        NewKey(client);
        using var response = await PutAsync(client, StatusPath(party), Body(new
        {
            expectedVersion = fence.GetProperty("version").GetString(),
            expectedProfileToken = fence.GetProperty("profileToken").GetString(),
            status,
        }));
        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
    }

    /// <summary>Sets (or clears) the override on the fence the latest read returned.</summary>
    private static async Task SetScenarioAsync(HttpClient client, Guid party, string? scenarioCode)
    {
        var fence = await ReadAsync(client, ProfilePath(party));
        NewKey(client);
        using var response = await PutAsync(client, ScenarioPath(party), Body(new
        {
            expectedVersion = fence.GetProperty("version").GetString(),
            expectedProfileToken = fence.GetProperty("profileToken").GetString(),
            scenarioCode,
        }));
        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
    }

    private static async Task<Guid> CreatePartyAsync(HttpClient client, string legalName)
    {
        NewKey(client);
        using var response = await client.PostAsJsonAsync("/api/v1/counterparties", new { kind = 1, legalName = $"{legalName} {Guid.NewGuid():N}", roles = 1 });
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return (await JsonAsync(response)).GetProperty("id").GetGuid();
    }

    /// <summary>Creates a party and enters its VKN (AT-009), so it can carry a VKN-keyed e-belge profile.</summary>
    private static async Task<Guid> CreateCustomerAsync(HttpClient client, string legalName, string vkn, bool grantTaxIdentity = true)
    {
        var party = await CreatePartyAsync(client, legalName);
        if (!grantTaxIdentity)
        {
            return party;
        }

        NewKey(client);
        using var response = await PutAsync(client, $"/api/v1/counterparties/{party}/tax-identity",
            Body(new { expectedVersion = "1", taxIdentityNumber = vkn }));
        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        return party;
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

    private static string NewKey(HttpClient client)
    {
        var key = Guid.NewGuid().ToString("N");
        UseKey(client, key);
        return key;
    }

    private static void UseKey(HttpClient client, string key)
    {
        client.DefaultRequestHeaders.Remove("Idempotency-Key");
        client.DefaultRequestHeaders.Add("Idempotency-Key", key);
        client.DefaultRequestHeaders.Remove("X-Correlation-Id");
        client.DefaultRequestHeaders.Add("X-Correlation-Id", Guid.NewGuid().ToString("N"));
    }


    /// <summary>
    /// Test-only stand-in for the Settings owner's e-Fatura scenario keys, so a test can control the list/default or
    /// simulate an outage. When absent, the real SAT-D120 catalog is used (which serves the standard set).
    /// </summary>
    private sealed class SettingsDouble
    {
        public string[]? Codes { get; set; }

        public string? Default { get; set; }

        public bool Fail { get; set; }

        public ISettingsEInvoiceScenarioPolicyProviderV1 For(ICompanyContext company) => new Port(this, company);

        private sealed class Port(SettingsDouble state, ICompanyContext company) : ISettingsEInvoiceScenarioPolicyProviderV1
        {
            private static readonly DateOnly From = new(2026, 10, 28);
            private static readonly ProfileVersionV1 Profile = new(Guid.Parse("71000000-0000-4000-8000-0000000000c1"), 1);

            public Task<EInvoiceScenarioCodeListV1> GetEInvoiceScenarioCodesAsync(
                DateOnly effectiveOn, CancellationToken cancellationToken = default)
            {
                if (state.Fail) throw new SettingsPrerequisiteException("settings_provider_unavailable");
                return state.Codes is { } codes
                    ? Task.FromResult(new EInvoiceScenarioCodeListV1(company.CompanyId, Profile, codes, From, null))
                    : throw new SettingsPrerequisiteException(SettingsPrerequisiteCodesV1.EInvoiceScenarioCodeListNotFound);
            }

            public Task<EInvoiceScenarioDefaultV1> GetEInvoiceScenarioDefaultAsync(
                DateOnly effectiveOn, CancellationToken cancellationToken = default)
            {
                if (state.Fail) throw new SettingsPrerequisiteException("settings_provider_unavailable");
                return state.Default is { } code
                    ? Task.FromResult(new EInvoiceScenarioDefaultV1(company.CompanyId, Profile, code, From, null))
                    : throw new SettingsPrerequisiteException(SettingsPrerequisiteCodesV1.EInvoiceScenarioDefaultNotFound);
            }
        }
    }

    private sealed class EDocumentTest : IAsyncDisposable
    {
        private readonly string _baseConnection = Environment.GetEnvironmentVariable(
            SqlServerRelationalAcceptanceTests.ConnectionStringEnvironmentVariable)!;

        public EDocumentTest(
            bool grant = true, SettingsDouble? settings = null, bool grantApprove = false, ConcurrentQueue<string>? logs = null)
        {
            Factory = new SqlServerRelationalApiFactory(_baseConnection, services =>
            {
                if (logs is not null)
                {
                    services.AddSingleton<ILoggerProvider>(new CapturingLoggerProvider(logs));
                }

                if (settings is not null)
                {
                    services.RemoveAll<ISettingsEInvoiceScenarioPolicyProviderV1>();
                    services.AddScoped(provider => settings.For(provider.GetRequiredService<ICompanyContext>()));
                }

                if (!grant)
                {
                    return;
                }

                // Test-only grants on the disposable database; no shipped seed role carries these names.
                services.PostConfigure<IdentitySeedOptions>(options =>
                {
                    var administrator = options.Roles.Single(role => role.Code == "SYSTEM_ADMINISTRATOR");
                    administrator.Permissions.Add(CounterpartyEDocumentPermissionsV1.Update);
                    administrator.Permissions.Add(CounterpartyTaxIdentityPermissionsV1.Update);
                    if (grantApprove)
                    {
                        // The AT-009 checker (a different person) approves the VKN change.
                        var operatorRole = options.Roles.Single(role => role.Code == "ORGANIZATION_OPERATOR");
                        operatorRole.Permissions.Add(ErpPermissions.CounterpartyPartiesRead);
                        operatorRole.Permissions.Add(CounterpartyTaxIdentityPermissionsV1.Approve);
                    }
                });
            });
        }

        public SqlServerRelationalApiFactory Factory { get; }

        public HttpClient Client(Guid? company = null, string user = TestUsers.GroupAdmin) =>
            Factory.CreateClientFor(user, company ?? TestCompanyIds.CompanyA);

        /// <summary>The consumer port on the real database, for a fixed actor and company context.</summary>
        public async Task<CounterpartyAccountEDocumentProfileAnswerV1> ReadPortAsync(
            Guid accountId,
            DateOnly effectiveOn,
            bool granted = true,
            bool failing = false,
            Guid? company = null,
            Guid? context = null)
        {
            _ = Factory.Server;
            var companyContext = new FixedCompanyContext(context ?? TestCompanyIds.CompanyA);
            await using var dbContext = new CounterpartyDbContext(
                new DbContextOptionsBuilder<CounterpartyDbContext>()
                    .UseSqlServer(Factory.DatabaseConnectionString)
                    .Options,
                companyContext);
            var user = new FixedUser("e-document-port-reader", TestCompanyIds.CompanyA, TestCompanyIds.CompanyB);
            // Without a double the port reads the real (SAT-D120) Settings catalog, as the host does.
            var source = new CounterpartyAccountEDocumentProfileSourceV1(
                dbContext,
                companyContext,
                user,
                new FixedAuthorization(granted, failing),
                new ApprovedSettingsPrerequisiteProvider(companyContext, user));
            return await source.GetAuthorizedAsync(company ?? companyContext.CompanyId, accountId, effectiveOn);
        }

        public async Task MigrateCounterpartyToAsync(string? migration)
        {
            await using var scope = Factory.Services.CreateAsyncScope();
            var dbContext = scope.ServiceProvider.GetRequiredService<CounterpartyDbContext>();
            await dbContext.GetService<IMigrator>().MigrateAsync(migration);
        }

        public async Task<T> ScalarAsync<T>(string sql, string? eventType = null, string? scenario = null, Guid? companyA = null)
        {
            await using var connection = new SqlConnection(Factory.DatabaseConnectionString);
            await connection.OpenAsync();
            await using var command = connection.CreateCommand();
            command.CommandText = sql;
            if (sql.Contains("@Event", StringComparison.Ordinal)) command.Parameters.AddWithValue("@Event", eventType!);
            if (sql.Contains("@Scenario", StringComparison.Ordinal)) command.Parameters.AddWithValue("@Scenario", scenario!);
            if (sql.Contains("@CompanyA", StringComparison.Ordinal)) command.Parameters.AddWithValue("@CompanyA", companyA!.Value);
            return Assert.IsType<T>(await command.ExecuteScalarAsync());
        }

        public async Task ExecuteAsync(string sql)
        {
            await using var connection = new SqlConnection(Factory.DatabaseConnectionString);
            await connection.OpenAsync();
            await using var command = connection.CreateCommand();
            command.CommandText = sql;
            await command.ExecuteNonQueryAsync();
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

    /// <summary>Every log line the host writes (message, exception and structured values), for the AT-009 assertion.</summary>
    private sealed class CapturingLoggerProvider(ConcurrentQueue<string> entries) : ILoggerProvider
    {
        public ILogger CreateLogger(string categoryName) => new CapturingLogger(entries, categoryName);

        public void Dispose()
        {
        }

        private sealed class CapturingLogger(ConcurrentQueue<string> entries, string categoryName) : ILogger
        {
            public IDisposable? BeginScope<TState>(TState state)
                where TState : notnull => null;

            public bool IsEnabled(LogLevel logLevel) => logLevel != LogLevel.None;

            public void Log<TState>(
                LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
            {
                var values = state is IEnumerable<KeyValuePair<string, object?>> pairs
                    ? string.Join(" ", pairs.Select(pair => $"{pair.Key}={pair.Value}"))
                    : string.Empty;
                entries.Enqueue($"{categoryName}[{eventId.Id}] {logLevel}: {formatter(state, exception)} {values} {exception}");
            }
        }
    }

    private sealed class FixedCompanyContext(Guid companyId) : ICompanyContext
    {
        public bool IsResolved => true;

        public Guid CompanyId { get; } = companyId;

        public void Set(Guid companyId) => throw new InvalidOperationException("Sabit test bağlamı.");
    }

    private sealed class FixedUser(string actorId, params Guid[] companies) : ICurrentUser
    {
        public bool IsAuthenticated => true;

        public string? UserId => actorId;

        public IReadOnlySet<Guid> AllowedCompanyIds { get; } = companies.ToHashSet();

        public bool CanAccessCompany(Guid companyId) => AllowedCompanyIds.Contains(companyId);
    }

    private sealed class FixedAuthorization(bool granted, bool failing) : IErpAuthorizationService
    {
        public Task<AuthorizationSnapshot> GetAuthorizationSnapshotAsync(
            Guid companyId, IReadOnlyCollection<string> permissions, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<bool> HasPermissionAsync(
            string permission, Guid companyId, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<PermissionScope> GetPermissionScopeAsync(
            string permission, Guid companyId, CancellationToken cancellationToken = default)
        {
            if (failing)
            {
                throw new InvalidOperationException("simulated outage");
            }

            Assert.Equal(ErpPermissions.CounterpartyPartiesRead, permission);
            return Task.FromResult(granted
                ? new PermissionScope(
                    true,
                    true,
                    new HashSet<string>(StringComparer.Ordinal) { "SENTETIK-CARI-OKUMA" },
                    new HashSet<Guid>(),
                    new HashSet<Guid>(),
                    new HashSet<Guid>())
                : PermissionScope.Denied);
        }
    }
}
