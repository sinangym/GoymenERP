using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using GoymenERP.Api.Endpoints;
using GoymenERP.Api.Security;
using GoymenERP.BuildingBlocks.Multitenancy;
using GoymenERP.Modules.Identity.Domain.Assignments;
using GoymenERP.Modules.Identity.Domain.Roles;
using GoymenERP.Modules.Identity.Domain.Users;
using GoymenERP.Modules.Identity.Infrastructure.Persistence;
using GoymenERP.Modules.Identity.Infrastructure.Services;
using GoymenERP.Modules.Settings.Application;
using GoymenERP.Modules.Settings.Application.Contracts.V1;
using GoymenERP.Modules.Settings.Infrastructure.Persistence;
using GoymenERP.Modules.Settings.Infrastructure.Persistence.Migrations;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.EntityFrameworkCore.Migrations.Operations;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace GoymenERP.Api.IntegrationTests;

/// <summary>
/// F-029 S5/S6 (URUN_F029 spec §5.4(2); URN-D2…D5) against a REAL SQL Server through the REAL host: the quote loss reason list
/// (SAT-D25) / default validity (SAT-D39) and the e-Fatura scenario code list / default (SAT-D102, SAT-D120) answer from the
/// compiled catalog until the one-time, audited baseline transfer (values unchanged, the S6 default EMPTY), then from the
/// version tables; a reasoned record is one save (rows + execution + audit), the same key replays, a stale version is 409, a
/// past date 422, a stored reason keeps its id and code, a default stays inside every list it covers, another company sees
/// nothing, 403 without the family's name, and the migration's Down refuses (51312) while any row of the two families
/// exists. Every grant is a fresh synthetic role; every reason is SENTETIK.
/// </summary>
public sealed class SettingsSalesQuoteAndEInvoicePolicySqlServerRelationalAcceptanceTests
{
    private static readonly Guid CompanyA = TestCompanyIds.CompanyA;
    private static readonly Guid CompanyB = TestCompanyIds.CompanyB;
    private const string Root = SettingsSalesPolicyEndpoints.SalesPoliciesPath;
    private const string Migration = "AddSalesQuoteAndEInvoiceScenarioPolicies";
    private const string Quote = SettingsSalesQuoteAndEInvoicePolicyPermissionManifestV1.SalesQuotePolicyManage;
    private const string Scenario = SettingsSalesQuoteAndEInvoicePolicyPermissionManifestV1.EInvoiceScenarioManage;
    private const string CountRows = "SELECT (SELECT COUNT(*) FROM [settings].[SalesPolicyBaselineTransfers] "
        + "WHERE [PolicyFamily] IN ('SalesQuotePolicy','EInvoiceScenario')) "
        + "+ (SELECT COUNT(*) FROM [settings].[SalesQuoteLossReasonListVersions]) "
        + "+ (SELECT COUNT(*) FROM [settings].[SalesQuoteLossReasonEntries]) "
        + "+ (SELECT COUNT(*) FROM [settings].[SalesQuoteDefaultValidityVersions]) "
        + "+ (SELECT COUNT(*) FROM [settings].[EInvoiceScenarioCodeListVersions]) "
        + "+ (SELECT COUNT(*) FROM [settings].[EInvoiceScenarioCodes]) "
        + "+ (SELECT COUNT(*) FROM [settings].[EInvoiceScenarioDefaultVersions])";

    [SqlServerFact]
    public async Task Quote_policy_baseline_transfer_record_replay_identity_isolation_and_audit_on_real_sql()
    {
        await using var run = await HostRun.StartAsync();
        var (editorSubject, editorActor) = await run.GrantAsync(TestUsers.CompanyA, CompanyA, Quote);
        var (viewerSubject, _) = await run.GrantAsync(TestUsers.CompanyAViewer, CompanyA, Scenario);
        var (editorBSubject, _) = await run.GrantAsync(TestUsers.CompanyB, CompanyB, Quote);
        using var editor = run.Client(editorSubject, CompanyA);
        using var viewer = run.Client(viewerSubject, CompanyA);
        using var editorB = run.Client(editorBSubject, CompanyB);
        var transfer = new TransferSalesPolicyBaselineRequestV1("SENTETIK teklif politikası devri");

        // Without the family's name (the scenario name does not open the quote family): 403, nothing is written.
        using (var forbidden = await viewer.GetAsync($"{Root}/quote-policy"))
        {
            Assert.Equal(HttpStatusCode.Forbidden, forbidden.StatusCode);
        }

        using (var forbidden = await viewer.SendAsync(Json(HttpMethod.Post, $"{Root}/quote-policy/baseline-transfer",
                   "q-forbidden", transfer)))
        {
            Assert.Equal(HttpStatusCode.Forbidden, forbidden.StatusCode);
        }

        Assert.Equal(0, await run.ScalarAsync<int>(CountRows));

        // Before the transfer the compiled catalog answers: its list version 1 (five reasons) and its 15-day validity.
        var before = await run.JsonAsync(editor, $"{Root}/quote-policy");
        Assert.False(before.GetProperty("isBaselineTransferred").GetBoolean());
        Assert.Equal("0", before.GetProperty("lossReasonListVersion").GetString());
        Assert.Equal("0", before.GetProperty("defaultValidityVersion").GetString());
        var catalogList = Assert.Single(before.GetProperty("lossReasonLists").EnumerateArray());
        Assert.Equal("1", catalogList.GetProperty("version").GetString());
        Assert.Equal(5, catalogList.GetProperty("lossReasons").GetArrayLength());
        var catalogValidity = Assert.Single(before.GetProperty("defaultValidities").EnumerateArray());
        Assert.Equal(15, catalogValidity.GetProperty("validityDays").GetInt32());

        // A record before the transfer is a stored 409 (the same key replays it).
        var early = new RecordSalesQuoteDefaultValidityRequestV1("0", 30, "SENTETIK erken kayıt");
        using (var refused = await editor.SendAsync(Json(HttpMethod.Post, $"{Root}/quote-policy/default-validity/versions",
                   "q-early", early)))
        {
            await AssertProblemAsync(refused, HttpStatusCode.Conflict, SalesPolicyProblemCodesV1.BaselineRequired);
        }

        using (var replayed = await editor.SendAsync(Json(HttpMethod.Post, $"{Root}/quote-policy/default-validity/versions",
                   "q-early", early)))
        {
            await AssertProblemAsync(replayed, HttpStatusCode.Conflict, SalesPolicyProblemCodesV1.BaselineRequired);
            Assert.True(replayed.Headers.Contains("Idempotency-Replayed"));
        }

        // The transfer: one marker (two catalog entries) + the catalog's rows unchanged, one audit event; replay; second is 409.
        await run.CommandAsync(editor, $"{Root}/quote-policy/baseline-transfer", "q-t-1", transfer, HttpStatusCode.NoContent);
        await run.CommandAsync(editor, $"{Root}/quote-policy/baseline-transfer", "q-t-1", transfer, HttpStatusCode.NoContent,
            replayed: true);
        using (var again = await editor.SendAsync(Json(HttpMethod.Post, $"{Root}/quote-policy/baseline-transfer", "q-t-2",
                   transfer)))
        {
            await AssertProblemAsync(again, HttpStatusCode.Conflict, SalesPolicyProblemCodesV1.AlreadyTransferred);
        }

        Assert.Equal(1, await run.ScalarAsync<int>(
            "SELECT COUNT(*) FROM [settings].[SalesPolicyBaselineTransfers] WHERE [CompanyId] = @c "
            + "AND [PolicyFamily] = 'SalesQuotePolicy' AND [EntryCount] = 2", ("@c", CompanyA)));
        Assert.Equal(1, await run.ScalarAsync<int>(
            "SELECT COUNT(*) FROM [settings].[SalesQuoteLossReasonListVersions] WHERE [CompanyId] = @c AND [Version] = 1 "
            + "AND [IsActive] = 1 AND [SourceKind] = 'CompiledBaselineV0'", ("@c", CompanyA)));
        Assert.Equal(5, await run.ScalarAsync<int>(
            "SELECT COUNT(*) FROM [settings].[SalesQuoteLossReasonEntries] WHERE [CompanyId] = @c AND [ListVersion] = 1",
            ("@c", CompanyA)));
        Assert.Equal(1, await run.ScalarAsync<int>(
            "SELECT COUNT(*) FROM [settings].[SalesQuoteDefaultValidityVersions] WHERE [CompanyId] = @c AND [Version] = 1 "
            + "AND [ValidityDays] = 15 AND [SourceKind] = 'CompiledBaselineV0'", ("@c", CompanyA)));
        Assert.Equal(1, await run.ScalarAsync<int>(
            "SELECT COUNT(*) FROM [audit].[AuditEvents] WHERE [EventType] = N'settings.sales-quote-policy.baseline-transferred.v1' "
            + "AND [CompanyId] = @c AND [PermissionCode] = @p", ("@c", CompanyA), ("@p", Quote)));

        // After the transfer the stored rows answer with the catalog's ids unchanged.
        var transferred = await run.JsonAsync(editor, $"{Root}/quote-policy");
        Assert.True(transferred.GetProperty("isBaselineTransferred").GetBoolean());
        Assert.Equal("1", transferred.GetProperty("lossReasonListVersion").GetString());
        var storedList = Assert.Single(transferred.GetProperty("lossReasonLists").EnumerateArray());
        Assert.Equal(
            catalogList.GetProperty("lossReasons").EnumerateArray().Select(reason => reason.GetProperty("reasonId").GetGuid()),
            storedList.GetProperty("lossReasons").EnumerateArray().Select(reason => reason.GetProperty("reasonId").GetGuid()));

        // A new list keeps four stored reasons by id (one relabelled) and adds one; a past date is 422.
        var kept = storedList.GetProperty("lossReasons").EnumerateArray().Take(4)
            .Select((reason, index) => new RecordSalesQuoteLossReasonInputV1(reason.GetProperty("reasonId").GetGuid(),
                reason.GetProperty("code").GetString(),
                index == 0 ? "SENTETIK yeni ad" : reason.GetProperty("label").GetString(),
                reason.GetProperty("requiresExplanation").GetBoolean(), reason.GetProperty("isActive").GetBoolean()))
            .ToList();
        var added = new RecordSalesQuoteLossReasonInputV1(null, "synthetic_reason", "SENTETIK neden", true, true);
        var list = new RecordSalesQuoteLossReasonsRequestV1("1", [.. kept, added], "SENTETIK liste güncellemesi");
        using (var past = await editor.SendAsync(Json(HttpMethod.Post, $"{Root}/quote-policy/loss-reasons/versions", "q-past",
                   list with { EffectiveFrom = run.Today.AddDays(-1) })))
        {
            await AssertProblemAsync(past, HttpStatusCode.UnprocessableEntity, SalesPolicyProblemCodesV1.EffectiveFromPast);
        }

        await run.CommandAsync(editor, $"{Root}/quote-policy/loss-reasons/versions", "q-l-1", list, HttpStatusCode.NoContent);
        await run.CommandAsync(editor, $"{Root}/quote-policy/loss-reasons/versions", "q-l-1", list, HttpStatusCode.NoContent,
            replayed: true);
        using (var stale = await editor.SendAsync(Json(HttpMethod.Post, $"{Root}/quote-policy/loss-reasons/versions", "q-l-stale",
                   list)))
        {
            await AssertProblemAsync(stale, HttpStatusCode.Conflict, SalesPolicyProblemCodesV1.VersionConflict);
        }

        Assert.Equal(1, await run.ScalarAsync<int>(
            "SELECT COUNT(*) FROM [settings].[SalesQuoteLossReasonListVersions] WHERE [CompanyId] = @c AND [Version] = 2 "
            + "AND [EffectiveFrom] = @d AND [SourceKind] = 'UserMaintained' AND [RecordedBy] = @a",
            ("@c", CompanyA), ("@d", run.Today.ToDateTime(TimeOnly.MinValue)), ("@a", editorActor)));
        Assert.Equal(5, await run.ScalarAsync<int>(
            "SELECT COUNT(*) FROM [settings].[SalesQuoteLossReasonEntries] WHERE [CompanyId] = @c AND [ListVersion] = 2",
            ("@c", CompanyA)));
        Assert.Equal(1, await run.ScalarAsync<int>(
            "SELECT COUNT(*) FROM [settings].[SalesQuoteLossReasonEntries] WHERE [CompanyId] = @c AND [ListVersion] = 2 "
            + "AND [ReasonId] = @r AND [Label] = N'SENTETIK yeni ad'", ("@c", CompanyA), ("@r", kept[0].ReasonId!.Value)));
        Assert.Equal(1, await run.ScalarAsync<int>(
            "SELECT COUNT(*) FROM [audit].[AuditEvents] WHERE [EventType] = N'settings.sales-quote-policy.loss-reasons.recorded.v1' "
            + "AND [CompanyId] = @c", ("@c", CompanyA)));

        // A stored code cannot be given to a new reason, and an unknown id cannot be kept: stored 422s, nothing written.
        var stolen = new RecordSalesQuoteLossReasonsRequestV1("2",
            [new RecordSalesQuoteLossReasonInputV1(null, kept[0].Code, "SENTETIK kopya", false, true)], "SENTETIK kod çakışması");
        using (var conflict = await editor.SendAsync(Json(HttpMethod.Post, $"{Root}/quote-policy/loss-reasons/versions",
                   "q-l-code", stolen)))
        {
            await AssertProblemAsync(conflict, HttpStatusCode.UnprocessableEntity,
                SalesQuoteAndEInvoicePolicyProblemCodesV1.LossReasonIdentityConflict);
        }

        var unknown = new RecordSalesQuoteLossReasonsRequestV1("2",
            [new RecordSalesQuoteLossReasonInputV1(Guid.NewGuid(), "synthetic_unknown", "SENTETIK bilinmeyen", false, true)],
            "SENTETIK bilinmeyen kimlik");
        using (var conflict = await editor.SendAsync(Json(HttpMethod.Post, $"{Root}/quote-policy/loss-reasons/versions",
                   "q-l-unknown", unknown)))
        {
            await AssertProblemAsync(conflict, HttpStatusCode.UnprocessableEntity,
                SalesQuoteAndEInvoicePolicyProblemCodesV1.LossReasonIdentityConflict);
        }

        Assert.Equal(0, await run.ScalarAsync<int>(
            "SELECT COUNT(*) FROM [settings].[SalesQuoteLossReasonListVersions] WHERE [CompanyId] = @c AND [Version] = 3",
            ("@c", CompanyA)));

        // The default validity is its own key: version 1 → 2 with 30 days; a malformed day count is 400 and not stored.
        using (var invalid = await editor.SendAsync(Json(HttpMethod.Post, $"{Root}/quote-policy/default-validity/versions",
                   "q-v-0", new RecordSalesQuoteDefaultValidityRequestV1("1", 0, "SENTETIK sıfır gün"))))
        {
            Assert.Equal(HttpStatusCode.BadRequest, invalid.StatusCode);
        }

        await run.CommandAsync(editor, $"{Root}/quote-policy/default-validity/versions", "q-v-1",
            new RecordSalesQuoteDefaultValidityRequestV1("1", 30, "SENTETIK geçerlilik güncellemesi"), HttpStatusCode.NoContent);
        Assert.Equal(1, await run.ScalarAsync<int>(
            "SELECT COUNT(*) FROM [settings].[SalesQuoteDefaultValidityVersions] WHERE [CompanyId] = @c AND [Version] = 2 "
            + "AND [ValidityDays] = 30 AND [SourceKind] = 'UserMaintained'", ("@c", CompanyA)));
        Assert.Equal(1, await run.ScalarAsync<int>(
            "SELECT COUNT(*) FROM [audit].[AuditEvents] WHERE [EventType] = N'settings.sales-quote-policy.default-validity.recorded.v1' "
            + "AND [CompanyId] = @c AND [PermissionCode] = @p", ("@c", CompanyA), ("@p", Quote)));
        var recorded = await run.JsonAsync(editor, $"{Root}/quote-policy");
        Assert.Equal("2", recorded.GetProperty("lossReasonListVersion").GetString());
        Assert.Equal("2", recorded.GetProperty("defaultValidityVersion").GetString());
        Assert.Contains(recorded.GetProperty("defaultValidities").EnumerateArray(), version =>
            version.GetProperty("version").GetString() == "2" && version.GetProperty("validityDays").GetInt32() == 30
            && version.GetProperty("state").GetString() == SalesPolicyVersionStatesV1.InForce);

        // Another company sees nothing of A's: its quote policy is still answered by the catalog.
        var other = await run.JsonAsync(editorB, $"{Root}/quote-policy");
        Assert.False(other.GetProperty("isBaselineTransferred").GetBoolean());
        Assert.Equal("0", other.GetProperty("lossReasonListVersion").GetString());
    }

    [SqlServerFact]
    public async Task EInvoice_scenario_baseline_transfer_default_inside_lists_replay_and_Down_guard_on_real_sql()
    {
        await using var run = await HostRun.StartAsync();
        var (editorSubject, _) = await run.GrantAsync(TestUsers.CompanyA, CompanyA, Scenario);
        var (viewerSubject, _) = await run.GrantAsync(TestUsers.CompanyAViewer, CompanyA, Quote);
        using var editor = run.Client(editorSubject, CompanyA);
        using var viewer = run.Client(viewerSubject, CompanyA);
        var transfer = new TransferSalesPolicyBaselineRequestV1("SENTETIK senaryo devri");

        using (var forbidden = await viewer.GetAsync($"{Root}/einvoice-scenarios"))
        {
            Assert.Equal(HttpStatusCode.Forbidden, forbidden.StatusCode);
        }

        // Before the transfer: the SAT-D120 standard list and NO default (SAT-D102: "Boş kalsın, ben gireyim").
        var before = await run.JsonAsync(editor, $"{Root}/einvoice-scenarios");
        Assert.False(before.GetProperty("isBaselineTransferred").GetBoolean());
        var catalogList = Assert.Single(before.GetProperty("codeLists").EnumerateArray());
        Assert.Equal(ApprovedEInvoiceScenarioCatalog.StandardScenarioCodes,
            catalogList.GetProperty("codes").EnumerateArray().Select(code => code.GetString()!).ToArray());
        Assert.Equal(0, before.GetProperty("defaults").GetArrayLength());
        Assert.Equal("0", before.GetProperty("defaultVersion").GetString());

        // The transfer copies the list unchanged and no default: one marker with one entry, one audit event.
        await run.CommandAsync(editor, $"{Root}/einvoice-scenarios/baseline-transfer", "s-t-1", transfer, HttpStatusCode.NoContent);
        await run.CommandAsync(editor, $"{Root}/einvoice-scenarios/baseline-transfer", "s-t-1", transfer, HttpStatusCode.NoContent,
            replayed: true);
        Assert.Equal(1, await run.ScalarAsync<int>(
            "SELECT COUNT(*) FROM [settings].[SalesPolicyBaselineTransfers] WHERE [CompanyId] = @c "
            + "AND [PolicyFamily] = 'EInvoiceScenario' AND [EntryCount] = 1", ("@c", CompanyA)));
        Assert.Equal(4, await run.ScalarAsync<int>(
            "SELECT COUNT(*) FROM [settings].[EInvoiceScenarioCodes] WHERE [CompanyId] = @c AND [ListVersion] = 1",
            ("@c", CompanyA)));
        Assert.Equal(0, await run.ScalarAsync<int>("SELECT COUNT(*) FROM [settings].[EInvoiceScenarioDefaultVersions]"));
        Assert.Equal(1, await run.ScalarAsync<int>(
            "SELECT COUNT(*) FROM [audit].[AuditEvents] WHERE [EventType] = N'settings.einvoice-scenario.baseline-transferred.v1' "
            + "AND [CompanyId] = @c AND [PermissionCode] = @p", ("@c", CompanyA), ("@p", Scenario)));

        // A malformed code is 400; a default outside the list in force is a stored 422; a listed one records.
        using (var invalid = await editor.SendAsync(Json(HttpMethod.Post, $"{Root}/einvoice-scenarios/defaults/versions", "s-d-0",
                   new RecordEInvoiceScenarioDefaultRequestV1("0", "temelfatura", "SENTETIK küçük harf"))))
        {
            Assert.Equal(HttpStatusCode.BadRequest, invalid.StatusCode);
        }

        using (var outside = await editor.SendAsync(Json(HttpMethod.Post, $"{Root}/einvoice-scenarios/defaults/versions", "s-d-x",
                   new RecordEInvoiceScenarioDefaultRequestV1("0", "SENTETIKSENARYO", "SENTETIK listede yok"))))
        {
            await AssertProblemAsync(outside, HttpStatusCode.UnprocessableEntity,
                SalesQuoteAndEInvoicePolicyProblemCodesV1.EInvoiceDefaultNotInList);
        }

        var firstDefault = new RecordEInvoiceScenarioDefaultRequestV1("0", "IHRACAT", "SENTETIK varsayılan senaryo");
        await run.CommandAsync(editor, $"{Root}/einvoice-scenarios/defaults/versions", "s-d-1", firstDefault,
            HttpStatusCode.NoContent);
        await run.CommandAsync(editor, $"{Root}/einvoice-scenarios/defaults/versions", "s-d-1", firstDefault,
            HttpStatusCode.NoContent, replayed: true);
        Assert.Equal(1, await run.ScalarAsync<int>(
            "SELECT COUNT(*) FROM [settings].[EInvoiceScenarioDefaultVersions] WHERE [CompanyId] = @c AND [Version] = 1 "
            + "AND [ScenarioCode] = 'IHRACAT' AND [SourceKind] = 'UserMaintained'", ("@c", CompanyA)));

        // A new list that drops the default's code is refused while that default covers its dates; a list keeping it records.
        using (var dropped = await editor.SendAsync(Json(HttpMethod.Post, $"{Root}/einvoice-scenarios/code-lists/versions", "s-l-x",
                   new RecordEInvoiceScenarioCodeListRequestV1("1", ["TEMELFATURA", "TICARIFATURA"], "SENTETIK daraltma"))))
        {
            await AssertProblemAsync(dropped, HttpStatusCode.UnprocessableEntity,
                SalesQuoteAndEInvoicePolicyProblemCodesV1.EInvoiceDefaultNotInList);
        }

        var widened = new RecordEInvoiceScenarioCodeListRequestV1("1",
            ["TEMELFATURA", "TICARIFATURA", "EARSIVFATURA", "IHRACAT", "SENTETIKSENARYO"], "SENTETIK genişletme");
        await run.CommandAsync(editor, $"{Root}/einvoice-scenarios/code-lists/versions", "s-l-1", widened, HttpStatusCode.NoContent);
        using (var stale = await editor.SendAsync(Json(HttpMethod.Post, $"{Root}/einvoice-scenarios/code-lists/versions", "s-l-stale",
                   widened)))
        {
            await AssertProblemAsync(stale, HttpStatusCode.Conflict, SalesPolicyProblemCodesV1.VersionConflict);
        }

        await run.CommandAsync(editor, $"{Root}/einvoice-scenarios/defaults/versions", "s-d-2",
            new RecordEInvoiceScenarioDefaultRequestV1("1", "SENTETIKSENARYO", "SENTETIK yeni varsayılan"), HttpStatusCode.NoContent);
        Assert.Equal(5, await run.ScalarAsync<int>(
            "SELECT COUNT(*) FROM [settings].[EInvoiceScenarioCodes] WHERE [CompanyId] = @c AND [ListVersion] = 2",
            ("@c", CompanyA)));
        Assert.Equal(1, await run.ScalarAsync<int>(
            "SELECT COUNT(*) FROM [audit].[AuditEvents] WHERE [EventType] = N'settings.einvoice-scenario.code-list.recorded.v1' "
            + "AND [CompanyId] = @c", ("@c", CompanyA)));
        Assert.Equal(2, await run.ScalarAsync<int>(
            "SELECT COUNT(*) FROM [audit].[AuditEvents] WHERE [EventType] = N'settings.einvoice-scenario.default.recorded.v1' "
            + "AND [CompanyId] = @c", ("@c", CompanyA)));
        var recorded = await run.JsonAsync(editor, $"{Root}/einvoice-scenarios");
        Assert.Equal("2", recorded.GetProperty("codeListVersion").GetString());
        Assert.Equal("2", recorded.GetProperty("defaultVersion").GetString());

        // The rollback refuses while any row of the two families exists (forward fix); the rows stay.
        var guard = Assert.IsType<SqlOperation>(new AddSalesQuoteAndEInvoiceScenarioPolicies().DownOperations[0]).Sql;
        var rowsBefore = await run.ScalarAsync<int>(CountRows);
        var refusedRollback = await Assert.ThrowsAsync<SqlException>(() => run.ExecuteAsync(guard));
        Assert.Equal(51312, refusedRollback.Number);
        Assert.Equal(rowsBefore, await run.ScalarAsync<int>(CountRows));

        // A marker alone also blocks it; an S4b marker does not (its own migration guards it); only that leaves it free.
        await run.ExecuteAsync("DELETE FROM [settings].[EInvoiceScenarioDefaultVersions]; "
            + "DELETE FROM [settings].[EInvoiceScenarioCodes]; DELETE FROM [settings].[EInvoiceScenarioCodeListVersions];");
        var markerOnly = await Assert.ThrowsAsync<SqlException>(() => run.ExecuteAsync(guard));
        Assert.Equal(51312, markerOnly.Number);
        await run.ExecuteAsync("DELETE FROM [settings].[SalesPolicyBaselineTransfers]; "
            + "INSERT INTO [settings].[SalesPolicyBaselineTransfers] ([CompanyId], [PolicyFamily], [EntryCount], [TransferredAtUtc], "
            + "[TransferredBy], [Reason]) VALUES ('" + CompanyA.ToString("D") + "', 'DeliveryTolerance', 0, SYSUTCDATETIME(), "
            + "N'SENTETIK', N'SENTETIK S4b işareti');");
        await run.ExecuteAsync(guard);
    }

    [SqlServerFact]
    public async Task The_quote_and_scenario_migrations_Down_rolls_back_and_reapplies_freely_on_an_empty_store_on_real_sql()
    {
        await using var run = await HostRun.StartAsync();
        Assert.Equal(0, await run.ScalarAsync<int>(CountRows));
        const string tables = "SELECT COUNT(*) FROM sys.tables WHERE [name] IN (N'SalesQuoteLossReasonListVersions', "
            + "N'SalesQuoteLossReasonEntries', N'SalesQuoteDefaultValidityVersions', N'EInvoiceScenarioCodeListVersions', "
            + "N'EInvoiceScenarioCodes', N'EInvoiceScenarioDefaultVersions') AND SCHEMA_NAME([schema_id]) = N'settings'";

        await using var context = run.SettingsContext(CompanyA);
        var applied = (await context.Database.GetAppliedMigrationsAsync()).ToArray();
        var index = Array.FindIndex(applied, name => name.EndsWith("_" + Migration, StringComparison.Ordinal));
        Assert.True(index > 0, "the migration is applied after an earlier Settings migration");
        await context.GetService<IMigrator>().MigrateAsync(applied[index - 1]);
        Assert.Equal(0, await run.ScalarAsync<int>(tables));
        Assert.Equal(1, await run.ScalarAsync<int>(
            "SELECT COUNT(*) FROM sys.tables WHERE [name] = N'SalesPolicyBaselineTransfers' AND SCHEMA_NAME([schema_id]) = N'settings'"));
        await context.GetService<IMigrator>().MigrateAsync(applied[^1]);
        Assert.Equal(6, await run.ScalarAsync<int>(tables));
    }

    private static HttpRequestMessage Json(HttpMethod method, string path, string key, object body)
    {
        var request = new HttpRequestMessage(method, path)
        {
            Content = new StringContent(JsonSerializer.Serialize(body, JsonSerializerOptions.Web), Encoding.UTF8, "application/json"),
        };
        request.Headers.Add("Idempotency-Key", key);
        request.Headers.Add("X-Correlation-Id", $"quote-scenario-policy-{key}");
        return request;
    }

    private static async Task AssertProblemAsync(HttpResponseMessage response, HttpStatusCode status, string code)
    {
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.True(status == response.StatusCode, $"{response.StatusCode}: {body}");
        Assert.Equal(code, body.GetProperty("code").GetString());
    }

    private static DateOnly IstanbulToday(TimeProvider clock) => DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(clock.GetUtcNow(),
        TimeZoneInfo.FindSystemTimeZoneById("Europe/Istanbul")).DateTime);

    private sealed class FixedClock(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }

    /// <summary>The REAL host over a fresh database, with Identity grants for the HTTP routes.</summary>
    private sealed class HostRun : IAsyncDisposable
    {
        private readonly string baseConnectionString;

        private HostRun(string baseConnectionString, SqlServerRelationalApiFactory factory, TimeProvider clock)
        {
            this.baseConnectionString = baseConnectionString;
            Factory = factory;
            Today = IstanbulToday(clock);
        }

        public SqlServerRelationalApiFactory Factory { get; }

        /// <summary>The business date of the administrations' fixed server clock (stable across Istanbul midnight).</summary>
        public DateOnly Today { get; }

        public static async Task<HostRun> StartAsync()
        {
            var baseConnectionString = Environment.GetEnvironmentVariable(SqlServerRelationalAcceptanceTests.ConnectionStringEnvironmentVariable)!;
            // Only the quote / e-Fatura scenario administrations take the fixed clock; Identity keeps the system clock (grants in force).
            TimeProvider clock = new FixedClock(DateTimeOffset.UtcNow);
            var factory = new SqlServerRelationalApiFactory(baseConnectionString, services =>
            {
                services.Replace(ServiceDescriptor.Scoped<ISettingsSalesQuotePolicyAdministrationV1>(provider =>
                    ActivatorUtilities.CreateInstance<SettingsSalesPolicyAdministration>(provider, clock)));
                services.Replace(ServiceDescriptor.Scoped<ISettingsEInvoiceScenarioPolicyAdministrationV1>(provider =>
                    ActivatorUtilities.CreateInstance<SettingsSalesPolicyAdministration>(provider, clock)));
            });
            var run = new HostRun(baseConnectionString, factory, clock);
            using var client = run.Factory.CreateClient();
            using var health = await client.GetAsync("/health/live");
            health.EnsureSuccessStatusCode();
            return run;
        }

        public HttpClient Client(string subject, Guid company)
        {
            var client = Factory.CreateClientFor(subject, company);
            client.DefaultRequestHeaders.Remove("Idempotency-Key");
            client.DefaultRequestHeaders.Remove("X-Correlation-Id");
            return client;
        }

        public async Task CommandAsync(HttpClient client, string path, string key, object body, HttpStatusCode expected,
            bool replayed = false)
        {
            using var response = await client.SendAsync(Json(HttpMethod.Post, path, key, body));
            var text = await response.Content.ReadAsStringAsync();
            Assert.True(expected == response.StatusCode, $"{response.StatusCode}: {text}");
            Assert.Equal(replayed, response.Headers.Contains("Idempotency-Replayed"));
        }

        public async Task<JsonElement> JsonAsync(HttpClient client, string path)
        {
            using var response = await client.GetAsync(path);
            var body = await response.Content.ReadFromJsonAsync<JsonElement>();
            Assert.True(response.StatusCode == HttpStatusCode.OK, $"{response.StatusCode}: {body}");
            return body;
        }

        public SettingsDbContext SettingsContext(Guid company)
        {
            var companyContext = new CompanyContext();
            companyContext.Set(company);
            return new SettingsDbContext(new DbContextOptionsBuilder<SettingsDbContext>()
                .UseSqlServer(Factory.DatabaseConnectionString,
                    sql => sql.MigrationsHistoryTable("__EFMigrationsHistory", SettingsDbContext.Schema))
                .Options, companyContext);
        }

        public async Task<(string Subject, string ActorId)> GrantAsync(string developmentUser, Guid company, params string[] permissions)
        {
            await using var scope = Factory.Services.CreateAsyncScope();
            var dbContext = scope.ServiceProvider.GetRequiredService<IdentityDbContext>();
            var link = await dbContext.ExternalIdentities.SingleOrDefaultAsync(
                x => x.Issuer == BootstrapIdentityOptions.Issuer && x.Subject == developmentUser);
            Guid actor;
            if (link is null)
            {
                actor = Guid.NewGuid();
                dbContext.Users.Add(new UserAccount(actor, "SENTETIK teklif/senaryo politikası kullanıcısı", null));
                dbContext.ExternalIdentities.Add(new ExternalIdentityLink(Guid.NewGuid(), actor, BootstrapIdentityOptions.Issuer,
                    developmentUser));
            }
            else
            {
                actor = link.UserId;
            }

            var roleId = Guid.NewGuid();
            var role = new RoleDefinition(roleId, $"QPOL-{roleId:N}", "SENTETIK teklif/senaryo politikası rolü", true);
            foreach (var permission in permissions)
            {
                role.AddPermission(permission);
            }

            var assignment = new RoleAssignment(Guid.NewGuid(), actor, roleId, company, DateTimeOffset.UtcNow.AddMinutes(-1), null,
                approvedByUserId: null);
            assignment.AddScope(AccessScopeType.Company, company);
            dbContext.Roles.Add(role);
            dbContext.RoleAssignments.Add(assignment);
            await dbContext.SaveChangesAsync();
            return (developmentUser, actor.ToString("D"));
        }

        public async Task<T> ScalarAsync<T>(string sql, params (string Name, object Value)[] parameters)
        {
            await using var connection = new SqlConnection(Factory.DatabaseConnectionString);
            await connection.OpenAsync();
            await using var command = new SqlCommand(sql, connection);
            foreach (var (name, value) in parameters)
            {
                command.Parameters.AddWithValue(name, value);
            }

            return (T)(await command.ExecuteScalarAsync())!;
        }

        public async Task ExecuteAsync(string sql)
        {
            await using var connection = new SqlConnection(Factory.DatabaseConnectionString);
            await connection.OpenAsync();
            await using var command = new SqlCommand(sql, connection);
            await command.ExecuteNonQueryAsync();
        }

        public async ValueTask DisposeAsync()
        {
            await Factory.DisposeAsync();
            SqlConnection.ClearAllPools();
            await using var connection = new SqlConnection(baseConnectionString);
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
