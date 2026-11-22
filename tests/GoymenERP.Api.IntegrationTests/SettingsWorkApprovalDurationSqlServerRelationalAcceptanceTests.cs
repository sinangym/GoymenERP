using System.Net;
using System.Net.Http.Json;
using GoymenERP.BuildingBlocks.Security;
using GoymenERP.Modules.Settings.Application.Contracts.V1;
using GoymenERP.Modules.Settings.Infrastructure.Persistence.Migrations;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore.Migrations.Operations;

namespace GoymenERP.Api.IntegrationTests;

public sealed partial class SettingsCompanyPolicySqlServerRelationalAcceptanceTests
{
    [SqlServerFact]
    public async Task Work_approval_durations_start_undefined_record_versions_per_class_and_the_migrations_Down_refuses_while_they_exist()
    {
        await using var run = await HostRun.StartAsync();
        var subject = await run.GrantAsync(TestUsers.CompanyA, CompanyA, SettingsOperationalPolicyPermissionsV1.WorkApprovalDurationManage);
        using var editor = run.Client(subject, CompanyA);
        using var foreign = run.Client(subject, CompanyB);
        const string root = "/api/v1/settings/company-policies/work-approval-duration";
        var guard = Assert.IsType<SqlOperation>(new AddWorkApprovalDurationPolicies().DownOperations[0]).Sql;
        await run.ExecuteAsync(guard);

        var initial = await editor.GetFromJsonAsync<WorkApprovalDurationPolicyViewV1>(root);
        Assert.Equal((CompanyA, (int?)null, "0", (int?)null, "0"),
            (initial!.CompanyId, initial.Hours.Value, initial.Hours.Version, initial.BusinessDays.Value, initial.BusinessDays.Version));
        using (var denied = await foreign.GetAsync(root)) Assert.Equal(HttpStatusCode.Forbidden, denied.StatusCode);

        using (var invalid = await editor.SendAsync(Json(root + "/hours/versions", "hours-zero",
                   new RecordWorkApprovalDurationRequestV1("0", 0, "SENTETIK süre"))))
            await ProblemAsync(invalid, HttpStatusCode.BadRequest, CompanyPolicyProblemCodesV1.RequestInvalid);
        var hours = new RecordWorkApprovalDurationRequestV1("0", 4, "SENTETIK saat sınıfı");
        await run.CommandAsync(editor, root + "/hours/versions", "hours-first", hours);
        await run.CommandAsync(editor, root + "/hours/versions", "hours-first", hours, replay: true);
        await run.CommandAsync(editor, root + "/business-days/versions", "days-first",
            new RecordWorkApprovalDurationRequestV1("0", 2, "SENTETIK iş günü sınıfı"));

        var current = await editor.GetFromJsonAsync<WorkApprovalDurationPolicyViewV1>(root);
        Assert.Equal((4, "1", 2, "1"), (current!.Hours.Value, current.Hours.Version, current.BusinessDays.Value, current.BusinessDays.Version));
        Assert.Equal(2, await run.CountAsync(
            "SELECT COUNT(*) FROM [audit].[AuditEvents] WHERE [EntityType] IN (N'Settings.WorkApprovalDurationHours',N'Settings.WorkApprovalDurationBusinessDays')"));
        var downRefused = await Assert.ThrowsAsync<SqlException>(() => run.ExecuteAsync(guard));
        Assert.Equal(52084, downRefused.Number);
        Assert.Equal(2, await run.CountAsync("SELECT COUNT(*) FROM [settings].[CompanyPolicyVersions] WHERE [PolicyKey] LIKE 'work.approval-duration-%'"));
    }

    [Fact]
    public void Work_approval_duration_permission_is_registered_once_company_wide_and_not_critical()
    {
        const string name = SettingsOperationalPolicyPermissionsV1.WorkApprovalDurationManage;
        Assert.Equal(ErpPermissions.SettingsWorkApprovalDurationManage, name);
        Assert.Single(ErpPermissions.All, item => item == name);
        Assert.True(ErpPermissions.RequiresCompanyWideScope(name));
        Assert.False(ErpPermissions.IsCritical(name));
    }

    [Fact]
    public void Work_approval_duration_migration_is_constraint_only_and_Down_first_guards_recorded_durations()
    {
        var migration = new AddWorkApprovalDurationPolicies();
        Assert.DoesNotContain(migration.UpOperations, operation => operation is SqlOperation);
        var guard = Assert.IsType<SqlOperation>(migration.DownOperations[0]).Sql;
        Assert.Contains("THROW 52084", guard, StringComparison.Ordinal);
        Assert.Contains("'work.approval-duration-hours'", guard, StringComparison.Ordinal);
        Assert.Contains("'work.approval-duration-business-days'", guard, StringComparison.Ordinal);
        Assert.Single(migration.DownOperations.OfType<SqlOperation>());
    }
}
