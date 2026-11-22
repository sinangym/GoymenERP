using System.Net;
using System.Net.Http.Json;
using GoymenERP.Api.Ai.Gateway;
using GoymenERP.BuildingBlocks.Ai;
using GoymenERP.BuildingBlocks.Multitenancy;
using GoymenERP.Modules.Settings.Application.Contracts.V1;
using GoymenERP.Modules.Settings.Infrastructure.Persistence;
using GoymenERP.Modules.Settings.Infrastructure.Persistence.Migrations;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Migrations.Operations;

namespace GoymenERP.Api.IntegrationTests;

/// <summary>
/// WP-A1 real SQL: the versioned AI gateway policy starts closed (version 0, no feature, no route, no budget), records a
/// full snapshot through the K3 path with replay and audit, refuses a cloud route above Internal in the command AND in the
/// table check, and the migration's Down refuses with THROW 52180 while a version or ledger row exists. The usage ledger
/// reserves only within the ceiling of the version in force, settles once, and closes the month at the ceiling.
/// </summary>
public sealed partial class SettingsCompanyPolicySqlServerRelationalAcceptanceTests
{
    private const string AiGatewayRoot = "/api/v1/settings/company-policies/ai-gateway";

    [SqlServerFact]
    public async Task Ai_gateway_policy_starts_closed_records_a_snapshot_refuses_cloud_above_internal_and_Down_refuses_while_it_exists()
    {
        await using var run = await HostRun.StartAsync();
        var subject = await run.GrantAsync(TestUsers.CompanyA, CompanyA, SettingsAiGatewayPermissionsV1.Manage);
        using var editor = run.Client(subject, CompanyA);
        using var foreign = run.Client(subject, CompanyB);
        var guard = Assert.IsType<SqlOperation>(new AddAiGatewayPolicyAndBudgetLedger().DownOperations[0]).Sql;
        await run.ExecuteAsync(guard);

        var initial = await editor.GetFromJsonAsync<AiGatewayPolicyViewV1>(AiGatewayRoot);
        Assert.Equal((CompanyA, "0", (long?)null, (int?)null, 0, 0),
            (initial!.CompanyId, initial.Version, initial.MonthlyUsageCeiling, initial.MaxUnitsPerRequest, initial.EnabledFeatures.Count,
                initial.Routes.Count));
        using (var denied = await foreign.GetAsync(AiGatewayRoot)) Assert.Equal(HttpStatusCode.Forbidden, denied.StatusCode);

        using (var cloudConfidential = await editor.SendAsync(Json(AiGatewayRoot + "/versions", "ai-cloud-confidential",
                   new RecordAiGatewayPolicyRequestV1("0", 1000, 100, ["Panel"],
                       [new RecordAiModelRouteV1("Answer", "Confidential", "Cloud", "SENTETIK-bulut")], "SENTETIK gizli bulut"))))
            await ProblemAsync(cloudConfidential, HttpStatusCode.BadRequest, CompanyPolicyProblemCodesV1.RequestInvalid);
        using (var unknownFeature = await editor.SendAsync(Json(AiGatewayRoot + "/versions", "ai-unknown-feature",
                   new RecordAiGatewayPolicyRequestV1("0", 1000, 100, ["panel"], [], "SENTETIK küçük harf"))))
            await ProblemAsync(unknownFeature, HttpStatusCode.BadRequest, CompanyPolicyProblemCodesV1.RequestInvalid);

        var request = new RecordAiGatewayPolicyRequestV1("0", 1000, 400, ["Query", "Panel"],
            [
                new RecordAiModelRouteV1("Answer", "Internal", "OnPremises", "SENTETIK-yerel"),
                new RecordAiModelRouteV1("Answer", "Public", "Cloud", "SENTETIK-bulut"),
            ], "SENTETIK ilk ağ geçidi politikası");
        await run.CommandAsync(editor, AiGatewayRoot + "/versions", "ai-first", request);
        await run.CommandAsync(editor, AiGatewayRoot + "/versions", "ai-first", request, replay: true);
        using (var stale = await editor.SendAsync(Json(AiGatewayRoot + "/versions", "ai-stale", request with { Reason = "SENTETIK eski" })))
            await ProblemAsync(stale, HttpStatusCode.Conflict, CompanyPolicyProblemCodesV1.VersionConflict);

        var current = await editor.GetFromJsonAsync<AiGatewayPolicyViewV1>(AiGatewayRoot);
        Assert.Equal(("1", (long?)1000, (int?)400), (current!.Version, current.MonthlyUsageCeiling, current.MaxUnitsPerRequest));
        Assert.Equal(["Panel", "Query"], current.EnabledFeatures);
        Assert.Equal([("Answer", "Public", "Cloud"), ("Answer", "Internal", "OnPremises")],
            current.Routes.Select(route => (route.TaskKind, route.DataClass, route.Placement)).ToArray());
        Assert.Equal(1, await run.CountAsync("SELECT COUNT(*) FROM [audit].[AuditEvents] WHERE [EntityType] = N'Settings.AiGatewayPolicy'"));

        var checkRefused = await Assert.ThrowsAsync<SqlException>(() => run.ExecuteAsync($"""
            INSERT INTO [settings].[AiGatewayModelRoutes] ([CompanyId],[Version],[TaskKind],[DataClass],[Placement],[ModelDeployment])
            VALUES ('{CompanyA:D}', 1, 2, 3, 1, N'SENTETIK-bulut');
            """));
        Assert.Equal(547, checkRefused.Number);
        var downRefused = await Assert.ThrowsAsync<SqlException>(() => run.ExecuteAsync(guard));
        Assert.Equal(52180, downRefused.Number);
        Assert.Equal(1, await run.CountAsync("SELECT COUNT(*) FROM [settings].[AiGatewayPolicyVersions]"));
    }

    [SqlServerFact]
    public async Task Ai_budget_ledger_reserves_within_the_version_ceiling_settles_once_and_closes_the_month_at_the_ceiling()
    {
        await using var run = await HostRun.StartAsync();
        var subject = await run.GrantAsync(TestUsers.CompanyA, CompanyA, SettingsAiGatewayPermissionsV1.Manage);
        using var editor = run.Client(subject, CompanyA);
        await run.CommandAsync(editor, AiGatewayRoot + "/versions", "ai-budget",
            new RecordAiGatewayPolicyRequestV1("0", 600, 500, ["Panel"],
                [new RecordAiModelRouteV1("Answer", "Internal", "OnPremises", "SENTETIK-yerel")], "SENTETIK bütçe"));
        var context = new CompanyContext();
        context.Set(CompanyA);
        await using var database = new SettingsDbContext(new DbContextOptionsBuilder<SettingsDbContext>()
            .UseSqlServer(run.DatabaseConnectionString, sql => sql.MigrationsHistoryTable("__EFMigrationsHistory", SettingsDbContext.Schema)).Options,
            context);
        var store = new SettingsAiGatewayStoreV1(database, context, new FixedActor(CompanyA), TimeProvider.System);

        var policy = await store.GetAiGatewayPolicyAsync(CompanyA);
        Assert.Equal((1L, (long?)600, (int?)500), (policy.Version, policy.MonthlyUsageCeiling, policy.MaxUnitsPerRequest));
        var stale = await store.ReserveAsync(Reservation(policyVersion: 2));
        Assert.Equal(SettingsAiBudgetReservationOutcomeV1.PolicyChanged, stale.Outcome);
        var first = await store.ReserveAsync(Reservation(policyVersion: 1));
        Assert.Equal((SettingsAiBudgetReservationOutcomeV1.Reserved, 500), (first.Outcome, first.ReservedUnits));
        var overCeiling = await store.ReserveAsync(Reservation(policyVersion: 1));
        Assert.Equal(SettingsAiBudgetReservationOutcomeV1.CeilingReached, overCeiling.Outcome);

        await store.SettleAsync(CompanyA, first.EntryId!.Value, 120);
        await store.SettleAsync(CompanyA, first.EntryId!.Value, 999);
        var usage = await store.GetCurrentMonthUsageAsync(CompanyA);
        Assert.Equal((120L, 1), (usage.CommittedUnits, usage.EntryCount));
        Assert.Equal(1, await run.CountAsync(
            $"SELECT COUNT(*) FROM [settings].[AiBudgetLedgerEntries] WHERE [EntryId] = '{first.EntryId:D}' AND [State] = 2 AND [UsedUnits] = 120"));

        var second = await store.ReserveAsync(Reservation(policyVersion: 1));
        Assert.Equal(SettingsAiBudgetReservationOutcomeV1.CeilingReached, second.Outcome);
        var guard = Assert.IsType<SqlOperation>(new AddAiGatewayPolicyAndBudgetLedger().DownOperations[0]).Sql;
        var downRefused = await Assert.ThrowsAsync<SqlException>(() => run.ExecuteAsync(guard));
        Assert.Equal(52180, downRefused.Number);
    }

    /// <summary>
    /// WP-A5 real SQL: no feature can be switched on before both budget values are entered (YOL-D21), and the management
    /// screen's usage read shows the current month per feature from the ledger — a settled entry with its used units, an
    /// open reservation with its reserved units — with the acceptance counts marked as not measured (no WP-C9 source).
    /// </summary>
    [SqlServerFact]
    public async Task Ai_gateway_refuses_features_without_a_budget_and_reads_the_month_usage_per_feature_from_the_ledger()
    {
        await using var run = await HostRun.StartAsync();
        var subject = await run.GrantAsync(TestUsers.CompanyA, CompanyA, SettingsAiGatewayPermissionsV1.Manage);
        using var editor = run.Client(subject, CompanyA);
        using var foreign = run.Client(subject, CompanyB);
        RecordAiModelRouteV1[] routes = [new("Answer", "Internal", "OnPremises", "SENTETIK-yerel")];

        using (var noCeiling = await editor.SendAsync(Json(AiGatewayRoot + "/versions", "ai-no-ceiling",
                   new RecordAiGatewayPolicyRequestV1("0", null, 100, ["Panel"], routes, "SENTETIK tavansız"))))
            await ProblemAsync(noCeiling, HttpStatusCode.BadRequest, CompanyPolicyProblemCodesV1.RequestInvalid);
        using (var zeroCeiling = await editor.SendAsync(Json(AiGatewayRoot + "/versions", "ai-zero-ceiling",
                   new RecordAiGatewayPolicyRequestV1("0", 0, 100, ["Panel"], routes, "SENTETIK sıfır tavan"))))
            await ProblemAsync(zeroCeiling, HttpStatusCode.BadRequest, CompanyPolicyProblemCodesV1.RequestInvalid);
        using (var noReservation = await editor.SendAsync(Json(AiGatewayRoot + "/versions", "ai-no-reservation",
                   new RecordAiGatewayPolicyRequestV1("0", 1000, null, ["Panel"], routes, "SENTETIK rezervsiz"))))
            await ProblemAsync(noReservation, HttpStatusCode.BadRequest, CompanyPolicyProblemCodesV1.RequestInvalid);
        Assert.Equal(0, await run.CountAsync("SELECT COUNT(*) FROM [settings].[AiGatewayPolicyVersions]"));

        // All closed without a budget is a valid record (the routes may be prepared ahead of the budget decision).
        await run.CommandAsync(editor, AiGatewayRoot + "/versions", "ai-closed",
            new RecordAiGatewayPolicyRequestV1("0", null, null, [], routes, "SENTETIK kapalı hazırlık"));
        await run.CommandAsync(editor, AiGatewayRoot + "/versions", "ai-open",
            new RecordAiGatewayPolicyRequestV1("1", 1000, 300, ["Panel", "Summary"], routes, "SENTETIK bütçeli açılış"));

        var empty = await editor.GetFromJsonAsync<AiGatewayUsageResponseV1>(AiGatewayRoot + "/usage");
        Assert.Equal((CompanyA, 0L, 0, false), (empty!.CompanyId, empty.CommittedUnits, empty.EntryCount, empty.AcceptanceMeasured));
        Assert.Empty(empty.Features);
        Assert.Empty(empty.Acceptance);
        using (var denied = await foreign.GetAsync(AiGatewayRoot + "/usage")) Assert.Equal(HttpStatusCode.Forbidden, denied.StatusCode);
        using (var query = await editor.GetAsync(AiGatewayRoot + "/usage?companyId=" + CompanyB.ToString("D")))
            await ProblemAsync(query, HttpStatusCode.BadRequest, CompanyPolicyProblemCodesV1.RequestInvalid);

        var context = new CompanyContext();
        context.Set(CompanyA);
        await using var database = new SettingsDbContext(new DbContextOptionsBuilder<SettingsDbContext>()
            .UseSqlServer(run.DatabaseConnectionString, sql => sql.MigrationsHistoryTable("__EFMigrationsHistory", SettingsDbContext.Schema)).Options,
            context);
        var store = new SettingsAiGatewayStoreV1(database, context, new FixedActor(CompanyA), TimeProvider.System);
        var settled = await store.ReserveAsync(Reservation(policyVersion: 2));
        await store.SettleAsync(CompanyA, settled.EntryId!.Value, 40);
        var open = await store.ReserveAsync(Reservation(policyVersion: 2) with { Feature = AiFeatureV1.Summary });
        Assert.Equal(SettingsAiBudgetReservationOutcomeV1.Reserved, open.Outcome);

        var usage = await editor.GetFromJsonAsync<AiGatewayUsageResponseV1>(AiGatewayRoot + "/usage");
        Assert.Equal((340L, 2, false), (usage!.CommittedUnits, usage.EntryCount, usage.AcceptanceMeasured));
        Assert.Equal([("Panel", 1, 0, 40L), ("Summary", 1, 1, 300L)],
            usage.Features.Select(line => (line.Feature, line.EntryCount, line.OpenReservationCount, line.CommittedUnits)).ToArray());
        Assert.Equal(SettingsAiBudgetMonthV1.Of(DateTimeOffset.UtcNow), usage.BudgetMonth);
    }

    private static SettingsAiBudgetReservationRequestV1 Reservation(long policyVersion) =>
        new(CompanyA, policyVersion, AiFeatureV1.Panel, AiTaskKindV1.Answer, AiModelPlacementV1.OnPremises, "SENTETIK-yerel",
            "SENTETIK-reference-reader", "corr-ai-ledger-sentetik");
}
