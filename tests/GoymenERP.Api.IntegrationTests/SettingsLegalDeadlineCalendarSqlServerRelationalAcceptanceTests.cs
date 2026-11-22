using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using GoymenERP.Modules.Settings.Application.Contracts.V1;
using GoymenERP.Modules.Settings.Infrastructure.Persistence.Migrations;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore.Migrations.Operations;

namespace GoymenERP.Api.IntegrationTests;

/// <summary>
/// WP15 (1.16): the legal deadline calendar on real SQL. Values are SENTETIK; the shipped calendar is empty until the financial
/// advisor confirms them.
/// </summary>
public sealed partial class SettingsCompanyPolicySqlServerRelationalAcceptanceTests
{
    [SqlServerFact]
    public async Task Legal_deadline_calendar_starts_undefined_records_versions_with_replay_and_conflict_and_the_migrations_Down_refuses_while_one_exists()
    {
        await using var run = await HostRun.StartAsync();
        var subject = await run.GrantAsync(TestUsers.CompanyA, CompanyA, SettingsLegalDeadlinePermissionsV1.CalendarManage);
        using var editor = run.Client(subject, CompanyA);
        using var foreign = run.Client(subject, CompanyB);
        const string root = "/api/v1/settings/company-policies/legal-deadline-calendar";
        var guard = Assert.IsType<SqlOperation>(new AddLegalDeadlineCalendars().DownOperations[0]).Sql;
        await run.ExecuteAsync(guard);

        var initial = await editor.GetFromJsonAsync<LegalDeadlineCalendarViewV1>(root);
        Assert.Equal((CompanyA, false, "0", 0, 0),
            (initial!.CompanyId, initial.IsDefined, initial.Version, initial.Entries.Count, initial.Versions.Count));
        using (var denied = await foreign.GetAsync(root)) Assert.Equal(HttpStatusCode.Forbidden, denied.StatusCode);

        using (var invalid = await editor.SendAsync(Json(root + "/versions", "legal-invalid", new RecordLegalDeadlineCalendarRequestV1("0",
                   [new RecordLegalDeadlineEntryV1("VatReturn", 2, 1, 28)], "SENTETIK takvim"))))
            await ProblemAsync(invalid, HttpStatusCode.BadRequest, CompanyPolicyProblemCodesV1.RequestInvalid);
        using (var duplicate = await editor.SendAsync(Json(root + "/versions", "legal-duplicate", new RecordLegalDeadlineCalendarRequestV1("0",
                   [new RecordLegalDeadlineEntryV1("VatReturn", 1, 1, 28), new RecordLegalDeadlineEntryV1("VatReturn", 1, 1, 26)], "SENTETIK"))))
            await ProblemAsync(duplicate, HttpStatusCode.BadRequest, CompanyPolicyProblemCodesV1.RequestInvalid);

        var request = new RecordLegalDeadlineCalendarRequestV1("0",
            [new RecordLegalDeadlineEntryV1("BaBsForms", 1, 1, 31), new RecordLegalDeadlineEntryV1("VatReturn", 1, 1, 28)],
            "SENTETIK yasal süre takvimi");
        await run.CommandAsync(editor, root + "/versions", "legal-first", request);
        await run.CommandAsync(editor, root + "/versions", "legal-first", request, replay: true);
        using (var stale = await editor.SendAsync(Json(root + "/versions", "legal-stale", request)))
            Assert.Equal(HttpStatusCode.Conflict, stale.StatusCode);

        var current = await editor.GetFromJsonAsync<LegalDeadlineCalendarViewV1>(root);
        Assert.Equal((true, "1"), (current!.IsDefined, current.Version));
        Assert.Equal([("VatReturn", 1, 1, 28), ("BaBsForms", 1, 1, 31)],
            current.Entries.Select(entry => (entry.Obligation, entry.PeriodMonths, entry.MonthsAfterPeriodEnd, entry.DueDayOfMonth)).ToArray());
        Assert.Equal(1, await run.CountAsync("SELECT COUNT(*) FROM [audit].[AuditEvents] WHERE [EntityType] = N'Settings.LegalDeadlineCalendar'"));
        var downRefused = await Assert.ThrowsAsync<SqlException>(() => run.ExecuteAsync(guard));
        Assert.Equal(52280, downRefused.Number);
        Assert.Equal(2, await run.CountAsync("SELECT COUNT(*) FROM [settings].[LegalDeadlineCalendarEntries]"));
    }

    [SqlServerFact]
    public async Task Work_deadlines_show_every_obligation_undefined_until_a_calendar_is_recorded_and_hide_the_source_without_the_read_name()
    {
        await using var run = await HostRun.StartAsync();
        var reader = await run.GrantAsync(TestUsers.CompanyA, CompanyA, SettingsLegalDeadlinePermissionsV1.DeadlinesRead);
        using var client = run.Client(reader, CompanyA);
        using var response = await client.GetAsync("/api/v1/work/deadlines");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var page = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var items = page.RootElement.GetProperty("items").EnumerateArray().ToArray();
        Assert.Equal(Enum.GetValues<LegalDeadlineObligationV1>().Length, items.Length);
        Assert.All(items, item =>
        {
            Assert.Equal("undefined", item.GetProperty("state").GetString());
            Assert.Equal(JsonValueKind.Null, item.GetProperty("dueDate").ValueKind);
            Assert.Equal(JsonValueKind.Null, item.GetProperty("daysRemaining").ValueKind);
        });

        // The same user in a company where the read name is not granted: no deadline leaks (either refused or no source).
        using var other = run.Client(reader, CompanyB);
        using var hidden = await other.GetAsync("/api/v1/work/deadlines");
        if (hidden.StatusCode == HttpStatusCode.OK)
        {
            using var empty = JsonDocument.Parse(await hidden.Content.ReadAsStringAsync());
            Assert.Empty(empty.RootElement.GetProperty("items").EnumerateArray());
            Assert.Empty(empty.RootElement.GetProperty("sources").EnumerateArray());
        }
        else
        {
            Assert.Equal(HttpStatusCode.Forbidden, hidden.StatusCode);
        }
    }
}
