using System.Net;
using System.Net.Http.Json;
using GoymenERP.BuildingBlocks.Security;
using GoymenERP.Modules.Settings.Infrastructure.Persistence.Migrations;
using Microsoft.EntityFrameworkCore.Migrations.Operations;
using Microsoft.Data.SqlClient;
using GoymenERP.Modules.Settings.Application.Contracts.V1;

namespace GoymenERP.Api.IntegrationTests;

public sealed partial class SettingsCompanyPolicySqlServerRelationalAcceptanceTests
{
    [SqlServerFact]
    public async Task Operational_policy_versions_keep_decided_initial_values_company_isolation_replay_and_immutable_audit_on_real_sql()
    {
        await using var run = await HostRun.StartAsync();
        var subject = await run.GrantAsync(TestUsers.CompanyA, CompanyA, SettingsOperationalPolicyPermissionsV1.ScrapCostManage);
        await run.GrantAsync(subject, CompanyA, SettingsOperationalPolicyPermissionsV1.NotificationRetentionManage);
        using var editor = run.Client(subject, CompanyA);
        using var foreign = run.Client(subject, CompanyB);
        const string root = "/api/v1/settings/company-policies";
        var guard = Assert.IsType<SqlOperation>(new AddDocumentNumberSeriesAndOperationalPolicies().DownOperations[0]).Sql;
        await run.ExecuteAsync(guard);
        var initial = await editor.GetFromJsonAsync<ManufacturingScrapCostPolicyViewV1>(root + "/manufacturing-scrap-cost");
        Assert.Equal((CompanyA, true, "0"), (initial!.CompanyId, initial.IncludeScrapCost, initial.Version));
        Assert.Empty(initial.Versions);
        var initialDays = await editor.GetFromJsonAsync<NotificationRetentionPolicyViewV1>(root + "/notification-retention");
        Assert.Equal((CompanyA, 90, "0"), (initialDays!.CompanyId, initialDays.ActiveDays, initialDays.Version));
        using (var denied = await foreign.GetAsync(root + "/notification-retention")) Assert.Equal(HttpStatusCode.Forbidden, denied.StatusCode);
        var request = new RecordManufacturingScrapCostRequestV1("0", false, "SENTETIK fire maliyeti");
        await run.CommandAsync(editor, root + "/manufacturing-scrap-cost/versions", "scrap-first", request);
        await run.CommandAsync(editor, root + "/manufacturing-scrap-cost/versions", "scrap-first", request, replay: true);
        using (var conflict = await editor.SendAsync(Json(root + "/manufacturing-scrap-cost/versions", "scrap-first", request with { IncludeScrapCost = true })))
            await ProblemAsync(conflict, HttpStatusCode.Conflict, CompanyPolicyProblemCodesV1.IdempotencyConflict);
        await run.CommandAsync(editor, root + "/notification-retention/versions", "notice-first", new RecordNotificationRetentionRequestV1("0", 30, "SENTETIK etkinlik süresi"));
        var current = await editor.GetFromJsonAsync<ManufacturingScrapCostPolicyViewV1>(root + "/manufacturing-scrap-cost");
        Assert.Equal((false, "1", 1), (current!.IncludeScrapCost, current.Version, current.Versions.Count));
        Assert.Equal(2, await run.CountAsync("SELECT COUNT(*) FROM [audit].[AuditEvents] WHERE [EntityType] IN (N'Settings.ManufacturingScrapCost',N'Settings.NotificationRetention')"));
        Assert.Equal(0, await run.CountAsync("SELECT COUNT(*) FROM [settings].[CompanyPolicyVersions] WHERE [CompanyId] = '" + CompanyB.ToString("D") + "'"));
        var downRefused = await Assert.ThrowsAsync<SqlException>(() => run.ExecuteAsync(guard));
        Assert.Equal(51954, downRefused.Number);
        Assert.Equal(2, await run.CountAsync("SELECT COUNT(*) FROM [settings].[CompanyPolicyVersions]"));
    }

    [Fact]
    public void Operational_policy_permissions_are_registered_once_with_no_seeded_role_or_critical_assignment()
    {
        foreach (var name in new[] { SettingsOperationalPolicyPermissionsV1.ScrapCostManage, SettingsOperationalPolicyPermissionsV1.NotificationRetentionManage, SettingsDocumentNumberSeriesPermissionsV1.Manage })
        {
            Assert.Single(ErpPermissions.All, item => item == name);
            Assert.True(ErpPermissions.RequiresCompanyWideScope(name));
            Assert.False(ErpPermissions.IsCritical(name));
        }
    }
}
