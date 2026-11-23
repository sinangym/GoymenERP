using System.Net;
using System.Net.Http.Json;
using GoymenERP.Modules.Settings.Application.Contracts.V1;
using GoymenERP.Modules.Settings.Infrastructure.Persistence.Migrations;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore.Migrations.Operations;

namespace GoymenERP.Api.IntegrationTests;

public sealed partial class SettingsCompanyPolicySqlServerRelationalAcceptanceTests
{
    [SqlServerFact]
    public async Task Work_digest_times_start_at_09_13_17_record_versions_per_slot_and_the_migrations_Down_refuses_while_they_exist()
    {
        await using var run = await HostRun.StartAsync();
        var subject = await run.GrantAsync(TestUsers.CompanyA, CompanyA, SettingsOperationalPolicyPermissionsV1.WorkApprovalDurationManage);
        using var editor = run.Client(subject, CompanyA);
        using var foreign = run.Client(subject, CompanyB);
        const string root = "/api/v1/settings/company-policies/work-digest-schedule";
        var guard = Assert.IsType<SqlOperation>(new Wp2WorkDigestTimes().DownOperations[0]).Sql;
        await run.ExecuteAsync(guard);

        var initial = await editor.GetFromJsonAsync<WorkDigestScheduleViewV1>(root);
        // YOL-D25: without a recorded version 09:00, 13:00 and 17:00 are in force as version 0, on business days only.
        Assert.Equal((CompanyA, true), (initial!.CompanyId, initial.BusinessDaysOnly));
        Assert.Equal([("morning", 540, "0"), ("midday", 780, "0"), ("evening", 1020, "0")],
            initial.Times.Select(time => (time.Slot, time.Minute, time.Version)).ToArray());
        using (var denied = await foreign.GetAsync(root)) Assert.Equal(HttpStatusCode.Forbidden, denied.StatusCode);
        using (var invalid = await editor.SendAsync(Json(root + "/morning/versions", "digest-too-late",
                   new RecordWorkDigestTimeRequestV1("0", 1440, "SENTETIK saat"))))
            await ProblemAsync(invalid, HttpStatusCode.BadRequest, CompanyPolicyProblemCodesV1.RequestInvalid);
        using (var unknownSlot = await editor.SendAsync(Json(root + "/night/versions", "digest-night",
                   new RecordWorkDigestTimeRequestV1("0", 60, "SENTETIK saat"))))
            await ProblemAsync(unknownSlot, HttpStatusCode.BadRequest, CompanyPolicyProblemCodesV1.RequestInvalid);
        var morning = new RecordWorkDigestTimeRequestV1("0", 510, "SENTETIK sabah özeti 08:30");
        await run.CommandAsync(editor, root + "/morning/versions", "digest-morning", morning);
        await run.CommandAsync(editor, root + "/morning/versions", "digest-morning", morning, replay: true);

        var current = await editor.GetFromJsonAsync<WorkDigestScheduleViewV1>(root);
        Assert.Equal([("morning", 510, "1"), ("midday", 780, "0"), ("evening", 1020, "0")],
            current!.Times.Select(time => (time.Slot, time.Minute, time.Version)).ToArray());
        Assert.Equal(1, await run.CountAsync("SELECT COUNT(*) FROM [audit].[AuditEvents] WHERE [EntityType] = N'Settings.WorkDigestTime'"));
        var downRefused = await Assert.ThrowsAsync<SqlException>(() => run.ExecuteAsync(guard));
        Assert.Equal(52112, downRefused.Number);
        Assert.Equal(1, await run.CountAsync("SELECT COUNT(*) FROM [settings].[CompanyPolicyVersions] WHERE [PolicyKey] LIKE 'work.digest-time-%'"));
    }

    [Fact]
    public void Work_digest_times_migration_is_constraint_only_and_Down_first_guards_recorded_times()
    {
        var migration = new Wp2WorkDigestTimes();
        Assert.DoesNotContain(migration.UpOperations, operation => operation is SqlOperation);
        var guard = Assert.IsType<SqlOperation>(migration.DownOperations[0]).Sql;
        Assert.Contains("THROW 52112", guard, StringComparison.Ordinal);
        Assert.Contains("work.digest-time-morning", guard, StringComparison.Ordinal);
        Assert.Single(migration.DownOperations.OfType<SqlOperation>());
    }
}
