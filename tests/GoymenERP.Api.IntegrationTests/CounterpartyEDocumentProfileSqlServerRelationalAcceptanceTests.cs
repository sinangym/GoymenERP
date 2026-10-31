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

namespace GoymenERP.Api.IntegrationTests;

/// <summary>
/// SAT-D102 / SAT-D119 (Sinan, 2026-10-28 and 2026-10-31) — the customer's e-belge profile, shared across the group by
/// VKN/TCKN, against real SQL Server through the real API: the explicit Unknown status, the recorded check (time +
/// checker), the VKN-keyed sharing (a check in one company is seen and editable on the same-VKN customer in another),
/// the scenario override that only a Settings-listed code may fill (SAT-D120 seeds the standard set), the effective
/// scenario (override → Settings default → Unresolved), the critical-command shape (company-wide permission,
/// Idempotency-Key replay, profile-version fence, one audit per real change), the no-VKN fail-closed, company reach via
/// the caller's own account, the read port, and the refused rollback.
/// </summary>
public sealed class CounterpartyEDocumentProfileSqlServerRelationalAcceptanceTests
{
    private const string SharedVkn = "1234567890";
    private const string OtherVkn = "9876543217";
    private const string StatusEvent = "counterparty.customer-e-document.status.recorded.v1";
    private const string ScenarioEvent = "counterparty.customer-e-document.e-invoice-scenario.changed.v1";

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
        Assert.Equal("0", initial.GetProperty("version").GetString());
        Assert.True(initial.GetProperty("viewer").GetProperty("canUpdate").GetBoolean());
        // SAT-D120: the standard code list is served; SAT-D102: the default is empty, so the scenario is unresolved.
        Assert.Equal("Set", initial.GetProperty("scenarioCodes").GetProperty("state").GetString());
        Assert.Equal(["TEMELFATURA", "TICARIFATURA", "EARSIVFATURA", "IHRACAT"],
            initial.GetProperty("scenarioCodes").GetProperty("codes").EnumerateArray().Select(code => code.GetString()!).ToArray());
        Assert.Equal("NotSet", initial.GetProperty("settingsDefault").GetProperty("state").GetString());
        AssertEffective(initial, "Unresolved", "None", null, "SettingsDefaultNotSet");

        // "0" creates the shared profile; a replay repeats the stored 204; the same key with another body is a conflict.
        NewKey(admin);
        var record = Body(new { expectedVersion = "0", status = "EInvoice" });
        using (var created = await PutAsync(admin, StatusPath(party), record))
        {
            Assert.Equal(HttpStatusCode.NoContent, created.StatusCode);
        }

        using (var replay = await PutAsync(admin, StatusPath(party), record))
        {
            Assert.Equal(HttpStatusCode.NoContent, replay.StatusCode);
            Assert.True(replay.Headers.Contains("Idempotency-Replayed"));
        }

        using (var reused = await PutAsync(admin, StatusPath(party), Body(new { expectedVersion = "0", status = "EArchive" })))
        {
            await AssertProblemAsync(reused, HttpStatusCode.Conflict, "idempotency_conflict");
        }

        var checkedOnce = await ReadAsync(test.Client(), ProfilePath(party));
        Assert.Equal("EInvoice", checkedOnce.GetProperty("status").GetString());
        Assert.True(Guid.TryParse(checkedOnce.GetProperty("statusCheckedByUserId").GetString(), out _));
        var firstCheck = checkedOnce.GetProperty("statusCheckedAtUtc").GetDateTimeOffset();
        Assert.Equal("1", checkedOnce.GetProperty("version").GetString());

        // The PROFILE version fences the command: the stale "0" is now a conflict.
        NewKey(admin);
        using (var stale = await PutAsync(admin, StatusPath(party), Body(new { expectedVersion = "0", status = "EArchive" })))
        {
            await AssertProblemAsync(stale, HttpStatusCode.Conflict, "version_conflict");
        }

        await RecordAsync(admin, party, "1", "EInvoice");
        var rechecked = await ReadAsync(admin, ProfilePath(party));
        Assert.True(rechecked.GetProperty("statusCheckedAtUtc").GetDateTimeOffset() >= firstCheck);
        Assert.Equal("2", rechecked.GetProperty("version").GetString());

        await RecordAsync(admin, party, "2", "EArchive");
        await RecordAsync(admin, party, "3", "Unknown");
        var reset = await ReadAsync(admin, ProfilePath(party));
        Assert.Equal("Unknown", reset.GetProperty("status").GetString());
        Assert.Equal(JsonValueKind.Null, reset.GetProperty("statusCheckedAtUtc").ValueKind);
        Assert.Equal("4", reset.GetProperty("version").GetString());

        // Unknown → Unknown is a fenced no-op: no version move, no audit.
        await RecordAsync(admin, party, "4", "Unknown");
        Assert.Equal("4", (await ReadAsync(admin, ProfilePath(party))).GetProperty("version").GetString());
        Assert.Equal(4, await test.ScalarAsync<int>(
            "SELECT COUNT(*) FROM [audit].[AuditEvents] WHERE [EventType]=@Event;", StatusEvent));
        Assert.Equal(1, await test.ScalarAsync<int>("SELECT COUNT(*) FROM [counterparty].[CustomerEDocumentProfiles];"));

        foreach (var invalid in new[]
                 {
                     """{"expectedVersion":"4","status":"Maybe"}""",
                     """{"expectedVersion":"4","status":7}""",
                     """{"expectedVersion":"4"}""",
                     """{"expectedVersion":"04","status":"EInvoice"}""",
                     """{"expectedVersion":"4","status":"EInvoice","checkedBy":"someone"}""",
                 })
        {
            NewKey(admin);
            using var refused = await PutAsync(admin, StatusPath(party), invalid);
            Assert.Equal(HttpStatusCode.BadRequest, refused.StatusCode);
        }
    }

    [SqlServerFact]
    public async Task A_check_in_one_company_is_seen_and_editable_on_the_same_VKN_customer_in_another_company()
    {
        await using var test = new EDocumentTest();
        using var adminA = test.Client();
        using var adminB = test.Client(TestCompanyIds.CompanyB);
        // The same taxpayer held as a separate account (here a separate party) with the same VKN in each company.
        var partyA = await CreateCustomerAsync(adminA, "A", SharedVkn);
        var partyB = await CreateCustomerAsync(adminB, "B", SharedVkn);

        // Company A records the check; the group-level profile is created once.
        NewKey(adminA);
        using (var recorded = await PutAsync(adminA, StatusPath(partyA), Body(new { expectedVersion = "0", status = "EInvoice" })))
        {
            Assert.Equal(HttpStatusCode.NoContent, recorded.StatusCode);
        }

        // Company B sees the same status on its own same-VKN customer (SAT-D119), with the same profile version.
        var seenByB = await ReadAsync(adminB, ProfilePath(partyB));
        Assert.Equal("EInvoice", seenByB.GetProperty("status").GetString());
        Assert.Equal("1", seenByB.GetProperty("version").GetString());
        Assert.Equal(partyB, seenByB.GetProperty("partyId").GetGuid());

        // Company B sets the scenario on the shared profile; company A sees it.
        NewKey(adminB);
        using (var scenario = await PutAsync(adminB, ScenarioPath(partyB), Body(new { expectedVersion = "1", scenarioCode = "TICARIFATURA" })))
        {
            Assert.Equal(HttpStatusCode.NoContent, scenario.StatusCode);
        }

        var seenByA = await ReadAsync(adminA, ProfilePath(partyA));
        Assert.Equal("TICARIFATURA", seenByA.GetProperty("scenarioOverrideCode").GetString());
        AssertEffective(seenByA, "Resolved", "CustomerOverride", "TICARIFATURA", "None");
        Assert.Equal("2", seenByA.GetProperty("version").GetString());

        // One shared row, not one per company; both companies' accounts resolve to it through the port.
        Assert.Equal(1, await test.ScalarAsync<int>("SELECT COUNT(*) FROM [counterparty].[CustomerEDocumentProfiles];"));
        var accountB = seenByB.GetProperty("accountId").GetGuid();
        var portB = await test.ReadPortAsync(accountB, new DateOnly(2026, 11, 2), company: TestCompanyIds.CompanyB, context: TestCompanyIds.CompanyB);
        Assert.Equal(CounterpartyAccountEDocumentProfileOutcomeV1.Found, portB.Outcome);
        Assert.Equal(CounterpartyEDocumentStatusV1.EInvoice, portB.Account!.Status);
        Assert.True(portB.Account.TaxIdentityNumberPresent);
        Assert.Equal("TICARIFATURA", portB.Account.EffectiveScenario.ScenarioCode);

        // A different customer with a different VKN has its own (absent) profile — the sharing is by VKN only.
        var other = await CreateCustomerAsync(adminA, "Other", OtherVkn);
        Assert.Equal("Unknown", (await ReadAsync(adminA, ProfilePath(other))).GetProperty("status").GetString());
        Assert.Equal(2, await test.ScalarAsync<int>(
            "SELECT COUNT(*) FROM [audit].[AuditEvents] WHERE [EventType] IN (@Event, @Scenario);",
            StatusEvent, ScenarioEvent));
    }

    [SqlServerFact]
    public async Task The_override_must_be_a_Settings_listed_code_and_resolves_override_then_default_then_unresolved()
    {
        var settings = new SettingsDouble { Codes = ["TEMELFATURA", "TICARIFATURA"] };
        await using var test = new EDocumentTest(settings: settings);
        using var admin = test.Client();
        var party = await CreateCustomerAsync(admin, "Senaryo", SharedVkn);

        NewKey(admin);
        using (var notListed = await PutAsync(admin, ScenarioPath(party), Body(new { expectedVersion = "0", scenarioCode = "IHRACAT" })))
        {
            await AssertProblemAsync(notListed, HttpStatusCode.UnprocessableEntity, "einvoice_scenario_not_listed");
        }

        foreach (var malformed in new[] { "temelfatura", "", "CODE-A", new string('A', 33) })
        {
            NewKey(admin);
            using var refused = await PutAsync(admin, ScenarioPath(party), Body(new { expectedVersion = "0", scenarioCode = malformed }));
            Assert.Equal(HttpStatusCode.BadRequest, refused.StatusCode);
        }

        NewKey(admin);
        using (var omitted = await PutAsync(admin, ScenarioPath(party), Body(new { expectedVersion = "0" })))
        {
            Assert.Equal(HttpStatusCode.BadRequest, omitted.StatusCode);
        }

        // "0" creates the profile with the override (status stays Unknown).
        NewKey(admin);
        var set = Body(new { expectedVersion = "0", scenarioCode = "TEMELFATURA" });
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
        await SetScenarioAsync(admin, party, "1", null);
        AssertEffective(await ReadAsync(admin, ProfilePath(party)), "Resolved", "SettingsDefault", "TICARIFATURA", "None");

        // An override the list later drops is NOT honoured and does NOT fall back to the default.
        await SetScenarioAsync(admin, party, "2", "TEMELFATURA");
        settings.Codes = ["TICARIFATURA"];
        AssertEffective(await ReadAsync(admin, ProfilePath(party)), "Unresolved", "CustomerOverride", null, "CustomerOverrideNotListed");

        // Settings that cannot be read: the read says Unavailable; a new override stores nothing (503); the same key
        // succeeds once Settings answers again.
        settings.Fail = true;
        AssertEffective(await ReadAsync(admin, ProfilePath(party)), "Unavailable", "None", null, "None");
        const string ScenarioExecutions =
            "SELECT COUNT(*) FROM [idempotency].[CommandExecutions] WHERE [CommandName]=N'counterparty.customer-e-document.e-invoice-scenario.set.v1';";
        var storedBefore = await test.ScalarAsync<int>(ScenarioExecutions);
        var retryKey = NewKey(admin);
        var change = Body(new { expectedVersion = "3", scenarioCode = "TICARIFATURA" });
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

        NewKey(admin);
        using (var status = await PutAsync(admin, StatusPath(noVkn), Body(new { expectedVersion = "0", status = "EInvoice" })))
        {
            await AssertProblemAsync(status, HttpStatusCode.UnprocessableEntity, "e_document_tax_identity_required");
        }

        NewKey(admin);
        using (var scenario = await PutAsync(admin, ScenarioPath(noVkn), Body(new { expectedVersion = "0", scenarioCode = "TEMELFATURA" })))
        {
            await AssertProblemAsync(scenario, HttpStatusCode.UnprocessableEntity, "e_document_tax_identity_required");
        }

        // A party with no account in the company is 404; the GET of an unknown party is 404.
        NewKey(admin);
        using (var missing = await PutAsync(admin, StatusPath(Guid.NewGuid()), Body(new { expectedVersion = "0", status = "EInvoice" })))
        {
            await AssertProblemAsync(missing, HttpStatusCode.NotFound, "not_found");
        }

        using (var unknown = await admin.GetAsync(ProfilePath(Guid.NewGuid())))
        {
            Assert.Equal(HttpStatusCode.NotFound, unknown.StatusCode);
        }

        // The port reaches the profile only through the caller's own account.
        Assert.Equal(CounterpartyAccountEDocumentProfileOutcomeV1.NotFound,
            (await test.ReadPortAsync(Guid.NewGuid(), new DateOnly(2026, 11, 2))).Outcome);
        Assert.Equal(CounterpartyAccountEDocumentProfileOutcomeV1.Denied,
            (await test.ReadPortAsync(Guid.NewGuid(), new DateOnly(2026, 11, 2), company: TestCompanyIds.CompanyB)).Outcome);

        // The database repeats the rules: a known status needs its check, Unknown carries none, the override's shape,
        // the VKN digits — a direct malformed insert is refused.
        foreach (var insert in new[]
                 {
                     "INSERT INTO [counterparty].[CustomerEDocumentProfiles] ([Id],[TaxIdentityNumber],[EDocumentStatus],[Version],[CreatedAtUtc],[UpdatedAtUtc]) VALUES (NEWID(), N'1234567890', 1, 1, SYSDATETIMEOFFSET(), SYSDATETIMEOFFSET());",
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
        using (var status = await PutAsync(reader, StatusPath(party), Body(new { expectedVersion = "0", status = "EInvoice" })))
        {
            Assert.Equal(HttpStatusCode.Forbidden, status.StatusCode);
        }

        NewKey(reader);
        using (var scenario = await PutAsync(reader, ScenarioPath(party), Body(new { expectedVersion = "0", scenarioCode = "TEMELFATURA" })))
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
        await RecordAsync(admin, party, "0", "EArchive");

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

    private static async Task RecordAsync(HttpClient client, Guid party, string version, string status)
    {
        NewKey(client);
        using var response = await PutAsync(client, StatusPath(party), Body(new { expectedVersion = version, status }));
        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
    }

    private static async Task SetScenarioAsync(HttpClient client, Guid party, string version, string? scenarioCode)
    {
        NewKey(client);
        using var response = await PutAsync(client, ScenarioPath(party), Body(new { expectedVersion = version, scenarioCode }));
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
        client.DefaultRequestHeaders.Remove("Idempotency-Key");
        client.DefaultRequestHeaders.Add("Idempotency-Key", key);
        client.DefaultRequestHeaders.Remove("X-Correlation-Id");
        client.DefaultRequestHeaders.Add("X-Correlation-Id", Guid.NewGuid().ToString("N"));
        return key;
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

        public EDocumentTest(bool grant = true, SettingsDouble? settings = null)
        {
            Factory = new SqlServerRelationalApiFactory(_baseConnection, services =>
            {
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

        public async Task<T> ScalarAsync<T>(string sql, string? eventType = null, string? scenario = null)
        {
            await using var connection = new SqlConnection(Factory.DatabaseConnectionString);
            await connection.OpenAsync();
            await using var command = connection.CreateCommand();
            command.CommandText = sql;
            if (sql.Contains("@Event", StringComparison.Ordinal)) command.Parameters.AddWithValue("@Event", eventType!);
            if (sql.Contains("@Scenario", StringComparison.Ordinal)) command.Parameters.AddWithValue("@Scenario", scenario!);
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
