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
/// SAT-D102 (Sinan, 2026-10-28) — the e-belge profile on the Cari card's company account, against real SQL Server
/// through the real API: the explicit Unknown status, the recorded check (time + checker), the scenario override that
/// only a Settings-listed code may fill, the effective scenario (override → Settings default → Unresolved), the
/// critical-command shape (company-wide permission, Idempotency-Key replay, version fence, one audit per real change),
/// company isolation, the read port consumers use, and the refused rollback. The real Settings catalog is EMPTY
/// (Sinan enters the default; no code list is decided), so the scenario codes below come from a test-only Settings
/// double that stands in for the Settings owner; they are not a policy.
/// </summary>
public sealed class CounterpartyEDocumentProfileSqlServerRelationalAcceptanceTests
{
    private const string StatusEvent = "counterparty.company-account.e-document-status.recorded.v1";
    private const string ScenarioEvent = "counterparty.company-account.e-invoice-scenario.changed.v1";

    [SqlServerFact]
    public async Task The_status_starts_Unknown_and_is_recorded_rechecked_and_reset_with_replay_audit_and_version_fence()
    {
        await using var test = new EDocumentTest();
        using var admin = test.Client();
        var party = await CreatePartyAsync(admin, "e-Belge Müşterisi");

        var initial = await ReadAsync(admin, ProfilePath(party));
        Assert.Equal("Unknown", initial.GetProperty("status").GetString());
        Assert.Equal(JsonValueKind.Null, initial.GetProperty("statusCheckedAtUtc").ValueKind);
        Assert.Equal(JsonValueKind.Null, initial.GetProperty("statusCheckedByUserId").ValueKind);
        Assert.Equal(JsonValueKind.Null, initial.GetProperty("scenarioOverrideCode").ValueKind);
        Assert.Equal("1", initial.GetProperty("version").GetString());
        Assert.True(initial.GetProperty("viewer").GetProperty("canUpdate").GetBoolean());
        // The real Settings keys are empty: no list, no default — the scenario is unresolved, never guessed.
        Assert.Equal("NotSet", initial.GetProperty("scenarioCodes").GetProperty("state").GetString());
        Assert.Empty(initial.GetProperty("scenarioCodes").GetProperty("codes").EnumerateArray());
        Assert.Equal("NotSet", initial.GetProperty("settingsDefault").GetProperty("state").GetString());
        AssertEffective(initial, "Unresolved", "None", null, "SettingsDefaultNotSet");
        var accountId = initial.GetProperty("accountId").GetGuid();

        NewKey(admin);
        var record = Body(new { expectedVersion = "1", status = "EInvoice" });
        using (var recorded = await PutAsync(admin, StatusPath(party), record))
        {
            Assert.Equal(HttpStatusCode.NoContent, recorded.StatusCode);
        }

        using (var replay = await PutAsync(admin, StatusPath(party), record))
        {
            Assert.Equal(HttpStatusCode.NoContent, replay.StatusCode);
            Assert.True(replay.Headers.Contains("Idempotency-Replayed"));
        }

        using (var reused = await PutAsync(admin, StatusPath(party), Body(new { expectedVersion = "1", status = "EArchive" })))
        {
            await AssertProblemAsync(reused, HttpStatusCode.Conflict, "idempotency_conflict");
        }

        var checkedOnce = await ReadAsync(test.Client(), ProfilePath(party));
        Assert.Equal("EInvoice", checkedOnce.GetProperty("status").GetString());
        // The checker is the authenticated actor's server-side user id, never a client-supplied name.
        Assert.True(Guid.TryParse(checkedOnce.GetProperty("statusCheckedByUserId").GetString(), out _));
        var firstCheck = checkedOnce.GetProperty("statusCheckedAtUtc").GetDateTimeOffset();
        Assert.Equal("2", checkedOnce.GetProperty("version").GetString());

        // The ACCOUNT version fences the command.
        NewKey(admin);
        using (var stale = await PutAsync(admin, StatusPath(party), Body(new { expectedVersion = "1", status = "EArchive" })))
        {
            await AssertProblemAsync(stale, HttpStatusCode.Conflict, "version_conflict");
        }

        // The same status again is a new check: the time moves, the version moves, a second audit is written.
        await RecordAsync(admin, party, "2", "EInvoice");
        var rechecked = await ReadAsync(admin, ProfilePath(party));
        Assert.True(rechecked.GetProperty("statusCheckedAtUtc").GetDateTimeOffset() >= firstCheck);
        Assert.Equal("3", rechecked.GetProperty("version").GetString());

        await RecordAsync(admin, party, "3", "EArchive");
        await RecordAsync(admin, party, "4", "Unknown");
        var reset = await ReadAsync(admin, ProfilePath(party));
        Assert.Equal("Unknown", reset.GetProperty("status").GetString());
        Assert.Equal(JsonValueKind.Null, reset.GetProperty("statusCheckedAtUtc").ValueKind);
        Assert.Equal(JsonValueKind.Null, reset.GetProperty("statusCheckedByUserId").ValueKind);
        Assert.Equal("5", reset.GetProperty("version").GetString());

        // Unknown → Unknown is a fenced no-op: no version move, no audit.
        await RecordAsync(admin, party, "5", "Unknown");
        Assert.Equal("5", (await ReadAsync(admin, ProfilePath(party))).GetProperty("version").GetString());
        Assert.Equal(4, await test.ScalarAsync<int>(
            "SELECT COUNT(*) FROM [audit].[AuditEvents] WHERE [EventType]=@Event;", StatusEvent));

        foreach (var invalid in new[]
                 {
                     """{"expectedVersion":"5","status":"Maybe"}""",
                     """{"expectedVersion":"5","status":7}""",
                     """{"expectedVersion":"5"}""",
                     """{"expectedVersion":"5","status":"EInvoice","checkedBy":"someone"}""",
                 })
        {
            NewKey(admin);
            using var refused = await PutAsync(admin, StatusPath(party), invalid);
            Assert.Equal(HttpStatusCode.BadRequest, refused.StatusCode);
        }

        NewKey(admin);
        using (var missing = await PutAsync(admin, StatusPath(Guid.NewGuid()), Body(new { expectedVersion = "1", status = "EInvoice" })))
        {
            await AssertProblemAsync(missing, HttpStatusCode.NotFound, "not_found");
        }

        // With the real (empty) Settings no override can be chosen ("kaynak yok"); clearing is a no-op.
        NewKey(admin);
        using (var noList = await PutAsync(admin, ScenarioPath(party), Body(new { expectedVersion = "5", scenarioCode = "TEMELFATURA" })))
        {
            await AssertProblemAsync(noList, HttpStatusCode.UnprocessableEntity, "einvoice_scenario_code_list_missing");
        }

        NewKey(admin);
        using (var clear = await PutAsync(admin, ScenarioPath(party), Body(new { expectedVersion = "5", scenarioCode = (string?)null })))
        {
            Assert.Equal(HttpStatusCode.NoContent, clear.StatusCode);
        }

        var port = await test.ReadPortAsync(accountId, new DateOnly(2026, 11, 2));
        Assert.Equal(CounterpartyAccountEDocumentProfileOutcomeV1.Found, port.Outcome);
        Assert.Equal(CounterpartyEDocumentStatusV1.Unknown, port.Account!.Status);
        Assert.Equal(CounterpartyEInvoiceScenarioOutcomeV1.Unresolved, port.Account.EffectiveScenario.Outcome);
        Assert.Equal(CounterpartyEInvoiceScenarioUnresolvedReasonV1.SettingsDefaultNotSet,
            port.Account.EffectiveScenario.UnresolvedReason);
        Assert.Equal("5", port.Account.AccountVersion);
        Assert.Equal(0, await test.ScalarAsync<int>(
            "SELECT COUNT(*) FROM [audit].[AuditEvents] WHERE [EventType]=@Event;", ScenarioEvent));
    }

    [SqlServerFact]
    public async Task The_override_must_be_a_Settings_listed_code_and_the_effective_scenario_is_override_then_default_then_unresolved()
    {
        var settings = new SettingsDouble { Codes = ["CODE_A", "CODE_B"] };
        await using var test = new EDocumentTest(settings: settings);
        using var admin = test.Client();
        var party = await CreatePartyAsync(admin, "Senaryo Müşterisi");

        var listed = await ReadAsync(admin, ProfilePath(party));
        Assert.Equal("Set", listed.GetProperty("scenarioCodes").GetProperty("state").GetString());
        Assert.Equal(["CODE_A", "CODE_B"], listed.GetProperty("scenarioCodes").GetProperty("codes").EnumerateArray()
            .Select(code => code.GetString()!).ToArray());
        Assert.Equal("NotSet", listed.GetProperty("settingsDefault").GetProperty("state").GetString());
        AssertEffective(listed, "Unresolved", "None", null, "SettingsDefaultNotSet");
        var accountId = listed.GetProperty("accountId").GetGuid();

        NewKey(admin);
        using (var notListed = await PutAsync(admin, ScenarioPath(party), Body(new { expectedVersion = "1", scenarioCode = "CODE_C" })))
        {
            await AssertProblemAsync(notListed, HttpStatusCode.UnprocessableEntity, "einvoice_scenario_not_listed");
        }

        foreach (var malformed in new[] { "code_a", "", "CODE-A", new string('A', 33) })
        {
            NewKey(admin);
            using var refused = await PutAsync(admin, ScenarioPath(party), Body(new { expectedVersion = "1", scenarioCode = malformed }));
            Assert.Equal(HttpStatusCode.BadRequest, refused.StatusCode);
        }

        NewKey(admin);
        using (var omitted = await PutAsync(admin, ScenarioPath(party), Body(new { expectedVersion = "1" })))
        {
            Assert.Equal(HttpStatusCode.BadRequest, omitted.StatusCode);
        }

        NewKey(admin);
        var set = Body(new { expectedVersion = "1", scenarioCode = "CODE_A" });
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
        Assert.Equal("CODE_A", overridden.GetProperty("scenarioOverrideCode").GetString());
        AssertEffective(overridden, "Resolved", "CustomerOverride", "CODE_A", "None");

        // The default applies once the override is cleared (Sinan's value would come from Settings, never from Cari).
        settings.Default = "CODE_B";
        await SetScenarioAsync(admin, party, "2", null);
        var byDefault = await ReadAsync(admin, ProfilePath(party));
        Assert.Equal("Set", byDefault.GetProperty("settingsDefault").GetProperty("state").GetString());
        AssertEffective(byDefault, "Resolved", "SettingsDefault", "CODE_B", "None");
        var defaultPort = await test.ReadPortAsync(accountId, new DateOnly(2026, 11, 2), settings);
        Assert.Equal(CounterpartyEInvoiceScenarioSourceV1.SettingsDefault, defaultPort.Account!.EffectiveScenario.Source);
        Assert.Equal("CODE_B", defaultPort.Account.EffectiveScenario.ScenarioCode);

        // An override the list later drops is NOT honoured and does NOT fall back to the default.
        await SetScenarioAsync(admin, party, "3", "CODE_A");
        settings.Codes = ["CODE_B"];
        var dropped = await ReadAsync(admin, ProfilePath(party));
        Assert.Equal("CODE_A", dropped.GetProperty("scenarioOverrideCode").GetString());
        AssertEffective(dropped, "Unresolved", "CustomerOverride", null, "CustomerOverrideNotListed");
        var droppedPort = await test.ReadPortAsync(accountId, new DateOnly(2026, 11, 2), settings);
        Assert.Equal(CounterpartyEInvoiceScenarioOutcomeV1.Unresolved, droppedPort.Account!.EffectiveScenario.Outcome);
        // …and re-sending the stored (now unlisted) code is a no-op, never a new choice.
        await SetScenarioAsync(admin, party, "4", "CODE_A");

        // Settings that cannot be read: the read says Unavailable; a new override stores nothing (503), and the same
        // key succeeds once Settings answers again.
        settings.Fail = true;
        var unavailable = await ReadAsync(admin, ProfilePath(party));
        Assert.Equal("Unavailable", unavailable.GetProperty("scenarioCodes").GetProperty("state").GetString());
        AssertEffective(unavailable, "Unavailable", "None", null, "None");
        const string ScenarioExecutions =
            "SELECT COUNT(*) FROM [idempotency].[CommandExecutions] WHERE [CommandName]=N'counterparty.company-account.e-invoice-scenario.set.v1';";
        var storedBefore = await test.ScalarAsync<int>(ScenarioExecutions);
        var retryKey = NewKey(admin);
        var change = Body(new { expectedVersion = "4", scenarioCode = "CODE_B" });
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

        AssertEffective(await ReadAsync(admin, ProfilePath(party)), "Resolved", "CustomerOverride", "CODE_B", "None");
        // Set A, clear, set A, set B: four real changes; the no-op wrote nothing.
        Assert.Equal(4, await test.ScalarAsync<int>(
            "SELECT COUNT(*) FROM [audit].[AuditEvents] WHERE [EventType]=@Event;", ScenarioEvent));
    }

    [SqlServerFact]
    public async Task Another_company_never_reads_or_changes_the_account_and_the_database_refuses_a_malformed_profile()
    {
        var settings = new SettingsDouble { Codes = ["CODE_A"] };
        await using var test = new EDocumentTest(settings: settings);
        using var adminA = test.Client();
        using var adminB = test.Client(TestCompanyIds.CompanyB);
        var party = await CreatePartyAsync(adminA, "A Müşterisi");
        var accountId = (await ReadAsync(adminA, ProfilePath(party))).GetProperty("accountId").GetGuid();

        using (var read = await adminB.GetAsync(ProfilePath(party)))
        {
            await AssertProblemAsync(read, HttpStatusCode.NotFound, "not_found");
        }

        NewKey(adminB);
        using (var status = await PutAsync(adminB, StatusPath(party), Body(new { expectedVersion = "1", status = "EInvoice" })))
        {
            await AssertProblemAsync(status, HttpStatusCode.NotFound, "not_found");
        }

        NewKey(adminB);
        using (var scenario = await PutAsync(adminB, ScenarioPath(party), Body(new { expectedVersion = "1", scenarioCode = "CODE_A" })))
        {
            await AssertProblemAsync(scenario, HttpStatusCode.NotFound, "not_found");
        }

        // The port answers only for the server company: another company's id is Denied, A's account seen from B is NotFound.
        Assert.Equal(CounterpartyAccountEDocumentProfileOutcomeV1.Denied,
            (await test.ReadPortAsync(accountId, new DateOnly(2026, 11, 2), company: TestCompanyIds.CompanyB)).Outcome);
        Assert.Equal(CounterpartyAccountEDocumentProfileOutcomeV1.NotFound,
            (await test.ReadPortAsync(accountId, new DateOnly(2026, 11, 2), context: TestCompanyIds.CompanyB,
                company: TestCompanyIds.CompanyB)).Outcome);

        // The database repeats the rules: a known status needs its check, Unknown carries none, the override's shape.
        foreach (var update in new[]
                 {
                     "UPDATE [counterparty].[CompanyPartyAccounts] SET [EDocumentStatus]=1 WHERE [Id]=@Account;",
                     "UPDATE [counterparty].[CompanyPartyAccounts] SET [EDocumentStatus]=3, [EDocumentStatusCheckedAtUtc]=SYSDATETIMEOFFSET(), [EDocumentStatusCheckedByUserId]=N'x' WHERE [Id]=@Account;",
                     "UPDATE [counterparty].[CompanyPartyAccounts] SET [EDocumentStatusCheckedByUserId]=N'x' WHERE [Id]=@Account;",
                     "UPDATE [counterparty].[CompanyPartyAccounts] SET [EInvoiceScenarioOverrideCode]=N'code_a' WHERE [Id]=@Account;",
                     "UPDATE [counterparty].[CompanyPartyAccounts] SET [EInvoiceScenarioOverrideCode]=N'' WHERE [Id]=@Account;",
                     "UPDATE [counterparty].[CompanyPartyAccounts] SET [EInvoiceScenarioOverrideCode]=N'TİCARİ' WHERE [Id]=@Account;",
                 })
        {
            var error = await Assert.ThrowsAsync<SqlException>(() => test.ExecuteAsync(update, accountId));
            Assert.Equal(547, error.Number);
        }

        Assert.Equal(0, await test.ScalarAsync<int>(
            "SELECT COUNT(*) FROM [counterparty].[CompanyPartyAccounts] WHERE [EDocumentStatus]<>0 OR [EInvoiceScenarioOverrideCode] IS NOT NULL;"));
    }

    [SqlServerFact]
    public async Task Without_the_grant_the_profile_reads_but_both_commands_are_403_and_nothing_is_written()
    {
        await using var test = new EDocumentTest(grant: false, settings: new SettingsDouble { Codes = ["CODE_A"] });
        using var reader = test.Client();
        var party = await CreatePartyAsync(reader, "Yetkisiz");

        var view = await ReadAsync(reader, ProfilePath(party));
        Assert.False(view.GetProperty("viewer").GetProperty("canUpdate").GetBoolean());

        NewKey(reader);
        using (var status = await PutAsync(reader, StatusPath(party), Body(new { expectedVersion = "1", status = "EInvoice" })))
        {
            Assert.Equal(HttpStatusCode.Forbidden, status.StatusCode);
        }

        NewKey(reader);
        using (var scenario = await PutAsync(reader, ScenarioPath(party), Body(new { expectedVersion = "1", scenarioCode = "CODE_A" })))
        {
            Assert.Equal(HttpStatusCode.Forbidden, scenario.StatusCode);
        }

        // Without counterparty.parties.read the port is Denied; an owner failure is Unavailable — never "Unknown".
        var accountId = view.GetProperty("accountId").GetGuid();
        Assert.Equal(CounterpartyAccountEDocumentProfileOutcomeV1.Denied,
            (await test.ReadPortAsync(accountId, new DateOnly(2026, 11, 2), granted: false)).Outcome);
        Assert.Equal(CounterpartyAccountEDocumentProfileOutcomeV1.Unavailable,
            (await test.ReadPortAsync(accountId, new DateOnly(2026, 11, 2), failing: true)).Outcome);

        Assert.Equal(0, await test.ScalarAsync<int>(
            "SELECT COUNT(*) FROM [counterparty].[CompanyPartyAccounts] WHERE [EDocumentStatus]<>0 OR [EInvoiceScenarioOverrideCode] IS NOT NULL;"));
        Assert.Equal(0, await test.ScalarAsync<int>(
            "SELECT COUNT(*) FROM [idempotency].[CommandExecutions] WHERE [CommandName] LIKE N'counterparty.company-account.e-%';"));
    }

    [SqlServerFact]
    public async Task Rollback_is_refused_while_a_status_or_override_exists_and_allowed_when_every_account_is_Unknown()
    {
        var settings = new SettingsDouble { Codes = ["CODE_A"] };
        await using var test = new EDocumentTest(settings: settings);
        using var admin = test.Client();
        var party = await CreatePartyAsync(admin, "Geri Alma");
        await RecordAsync(admin, party, "1", "EArchive");

        var error = await Assert.ThrowsAnyAsync<SqlException>(
            () => test.MigrateCounterpartyToAsync("20261027202534_AddCounterpartyCustomerGroups"));
        Assert.Equal(51121, error.Number);
        Assert.Equal(1, await test.ScalarAsync<int>(
            "SELECT COUNT(*) FROM [counterparty].[CompanyPartyAccounts] WHERE [EDocumentStatus]=2;"));

        await RecordAsync(admin, party, "2", "Unknown");
        await SetScenarioAsync(admin, party, "3", "CODE_A");
        var overrideError = await Assert.ThrowsAnyAsync<SqlException>(
            () => test.MigrateCounterpartyToAsync("20261027202534_AddCounterpartyCustomerGroups"));
        Assert.Equal(51121, overrideError.Number);

        await SetScenarioAsync(admin, party, "4", null);
        await test.MigrateCounterpartyToAsync("20261027202534_AddCounterpartyCustomerGroups");
        Assert.Equal(0, await test.ScalarAsync<int>(
            "SELECT COUNT(*) FROM sys.columns WHERE [object_id]=OBJECT_ID(N'[counterparty].[CompanyPartyAccounts]') AND [name] LIKE N'E%Document%';"));
        await test.MigrateCounterpartyToAsync(null);
        Assert.Equal(2, await test.ScalarAsync<int>(
            "SELECT COUNT(*) FROM sys.columns WHERE [object_id]=OBJECT_ID(N'[counterparty].[CompanyPartyAccounts]') AND [name] IN (N'EDocumentStatus', N'EInvoiceScenarioOverrideCode');"));
        Assert.Equal("Unknown", (await ReadAsync(admin, ProfilePath(party))).GetProperty("status").GetString());
    }

    private static void AssertEffective(
        JsonElement view, string outcome, string source, string? code, string reason)
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
    /// Test-only stand-in for the Settings owner's e-Fatura scenario keys (the real catalog is empty). It answers for
    /// the server company exactly as the published port does: a value, Settings' own "not found" codes, or a failure.
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

                // Test-only grant on the disposable database; no shipped seed role carries this name.
                services.PostConfigure<IdentitySeedOptions>(options =>
                {
                    var administrator = options.Roles.Single(role => role.Code == "SYSTEM_ADMINISTRATOR");
                    administrator.Permissions.Add(CounterpartyEDocumentPermissionsV1.Update);
                });
            });
        }

        public SqlServerRelationalApiFactory Factory { get; }

        public HttpClient Client(Guid? company = null, string user = TestUsers.GroupAdmin) =>
            Factory.CreateClientFor(user, company ?? TestCompanyIds.CompanyA);

        /// <summary>The consumer port on the real database, for a fixed actor.</summary>
        public async Task<CounterpartyAccountEDocumentProfileAnswerV1> ReadPortAsync(
            Guid accountId,
            DateOnly effectiveOn,
            SettingsDouble? settings = null,
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
            // Without a double the port reads the real (empty) Settings catalog, as the host does.
            var source = new CounterpartyAccountEDocumentProfileSourceV1(
                dbContext,
                companyContext,
                user,
                new FixedAuthorization(granted, failing),
                settings?.For(companyContext) ?? new ApprovedSettingsPrerequisiteProvider(companyContext, user));
            return await source.GetAuthorizedAsync(company ?? companyContext.CompanyId, accountId, effectiveOn);
        }

        public async Task MigrateCounterpartyToAsync(string? migration)
        {
            await using var scope = Factory.Services.CreateAsyncScope();
            var dbContext = scope.ServiceProvider.GetRequiredService<CounterpartyDbContext>();
            await dbContext.GetService<IMigrator>().MigrateAsync(migration);
        }

        public async Task<T> ScalarAsync<T>(string sql, string? eventType = null)
        {
            await using var connection = new SqlConnection(Factory.DatabaseConnectionString);
            await connection.OpenAsync();
            await using var command = connection.CreateCommand();
            command.CommandText = sql;
            if (sql.Contains("@Event", StringComparison.Ordinal)) command.Parameters.AddWithValue("@Event", eventType!);
            return Assert.IsType<T>(await command.ExecuteScalarAsync());
        }

        public async Task ExecuteAsync(string sql, Guid account)
        {
            await using var connection = new SqlConnection(Factory.DatabaseConnectionString);
            await connection.OpenAsync();
            await using var command = connection.CreateCommand();
            command.CommandText = sql;
            command.Parameters.AddWithValue("@Account", account);
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
