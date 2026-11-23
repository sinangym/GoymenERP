using System.Globalization;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using GoymenERP.Api.Contracts;
using GoymenERP.Api.Security;
using GoymenERP.BuildingBlocks.Multitenancy;
using GoymenERP.BuildingBlocks.Observability;
using GoymenERP.BuildingBlocks.Security;
using GoymenERP.Modules.Audit.Domain.Events;
using GoymenERP.Modules.Identity.Domain.Assignments;
using GoymenERP.Modules.Identity.Domain.Roles;
using GoymenERP.Modules.Identity.Domain.Users;
using GoymenERP.Modules.Identity.Infrastructure.Persistence;
using GoymenERP.Modules.Organization.Application.Contracts;
using GoymenERP.Modules.Organization.Infrastructure.Persistence;
using GoymenERP.Modules.Organization.Infrastructure.Persistence.Migrations;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.EntityFrameworkCore.Migrations.Operations;
using Microsoft.Extensions.DependencyInjection;

namespace GoymenERP.Api.IntegrationTests;

/// <summary>
/// B-15 work-centre shift/calendar master through the real host on a real SQL Server: a work centre with no saved
/// calendar reads the URT-D11 default at version 0, the first save at version 0 writes revision 1, a stale version is
/// a 409 with its conflict, a replayed key applies nothing twice, an unchanged save is an audited no-op, another
/// company's work centre is a code-less 404, and Manufacturing's V2 planning reference answers exactly the calendar the
/// GET shows (URT-D29: nothing is computed from it). The grants are synthetic, isolated role assignments in this test's
/// own database; they are not a Sinan or production grant and are never seeded.
/// </summary>
public sealed class OrganizationWorkCenterCalendarSqlServerRelationalAcceptanceTests
{
    private const string WorkCentersRoute = "/api/v1/organization/work-centers";
    private const string MigrationBeforeCalendar = "20261013155012_AddLocationOperatorParty";
    private const int ForwardFixRefusal = 51361;
    private const int OvernightForwardFixRefusal = 51444;
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    private static readonly string[] CalendarEditorPermissions =
    [
        ErpPermissions.OrganizationWorkCentersRead,
        OrganizationPermissionManifestV1.WorkCenterCalendarsWrite,
    ];

    private static readonly WorkCenterCalendarShiftV1[] TwoShiftWeek =
    [
        new("Saturday", "08:00", "12:00"),
        new("Monday", "16:00", "23:59"),
        new("Monday", "08:00", "16:00"),
    ];

    [SqlServerFact]
    public async Task Default_first_save_stale_replay_and_no_change_follow_the_command_protocol()
    {
        await WithDatabaseAsync(async factory =>
        {
            await GrantAsync(factory, TestUsers.NoAssignment, TestCompanyIds.CompanyA, CalendarEditorPermissions);
            using var editor = CreateClient(factory, TestUsers.NoAssignment, TestCompanyIds.CompanyA);
            var workCenterId = await FirstWorkCenterAsync(factory, TestCompanyIds.CompanyA);
            var path = $"{WorkCentersRoute}/{workCenterId}/calendar";
            var baseline = await ReadEffectsAsync(factory, TestCompanyIds.CompanyA);

            // No saved calendar: the URT-D11 default at version 0, the first save's expected version.
            var initial = await ReadCalendarAsync(editor, path);
            Assert.Equal(
                (workCenterId, "Default", 0L, "Europe/Istanbul"),
                (initial.WorkCenterId, initial.Source, initial.Version, initial.TimeZoneId));
            Assert.Equal(
                ["Monday", "Tuesday", "Wednesday", "Thursday", "Friday"],
                initial.Shifts.Select(shift => shift.Day));
            Assert.All(initial.Shifts, shift => Assert.Equal(("08:00", "16:00"), (shift.Start, shift.End)));
            Assert.Empty(initial.ClosedDates);

            // First save at version 0, then replayed under the same key without a second revision.
            var save = new SetWorkCenterCalendar(TwoShiftWeek, ["2026-12-31", "2026-10-29"], 0, "Hat iki vardiyaya geçti");
            var saveKey = Key();
            using (var response = await SendAsync(editor, path, save, saveKey))
            {
                await AssertNoContentAsync(response, replay: false);
            }

            using (var response = await SendAsync(editor, path, save, saveKey))
            {
                await AssertNoContentAsync(response, replay: true);
            }

            var saved = await ReadCalendarAsync(editor, path);
            Assert.Equal(("WorkCenter", 1L), (saved.Source, saved.Version));
            Assert.Equal(
                new WorkCenterCalendarShiftV1[]
                {
                    new("Monday", "08:00", "16:00"),
                    new("Monday", "16:00", "23:59"),
                    new("Saturday", "08:00", "12:00"),
                },
                saved.Shifts);
            Assert.Equal(["2026-10-29", "2026-12-31"], saved.ClosedDates);

            // The same calendar in another order under the same key is the same command: replayed, not a conflict.
            using (var response = await SendAsync(editor, path,
                save with { Shifts = Enumerable.Reverse(TwoShiftWeek).ToArray(), ClosedDates = ["2026-10-29", "2026-12-31"] }, saveKey))
            {
                await AssertNoContentAsync(response, replay: true);
            }

            // A stale first save (another screen still on the default) is refused with the version that won.
            using (var response = await SendAsync(editor, path,
                save with { ClosedDates = [], Reason = "Eski ekrandan" }, Key()))
            {
                await AssertVersionConflictAsync(response, expected: 0, current: 1);
            }

            // Saving the content already in force is an audited no-op: 204, no new revision.
            using (var response = await SendAsync(editor, path, save with { ExpectedVersion = 1, Reason = "Kontrol" }, Key()))
            {
                await AssertNoContentAsync(response, replay: false);
            }

            Assert.Equal(1L, (await ReadCalendarAsync(editor, path)).Version);

            // A real change writes revision 2; the earlier rows stay as they were.
            using (var response = await SendAsync(editor, path,
                new SetWorkCenterCalendar([new("Tuesday", "06:00", "14:00")], [], 1, "Tek vardiyaya dönüldü"), Key()))
            {
                await AssertNoContentAsync(response, replay: false);
            }

            var revised = await ReadCalendarAsync(editor, path);
            Assert.Equal(("WorkCenter", 2L), (revised.Source, revised.Version));
            Assert.Equal(new[] { new WorkCenterCalendarShiftV1("Tuesday", "06:00", "14:00") }, revised.Shifts);
            Assert.Empty(revised.ClosedDates);

            // Three applied commands (create, no-change, update) audit; four stored answers (the 409 too, replays none).
            var after = await ReadEffectsAsync(factory, TestCompanyIds.CompanyA);
            Assert.Equal(
                baseline with
                {
                    Calendars = baseline.Calendars + 1,
                    Shifts = baseline.Shifts + 4,
                    ClosedDates = baseline.ClosedDates + 2,
                    Audits = baseline.Audits + 3,
                    Executions = baseline.Executions + 4,
                },
                after);

            await using var scope = factory.Services.CreateAsyncScope();
            scope.ServiceProvider.GetRequiredService<ICompanyContext>().Set(TestCompanyIds.CompanyA);
            var db = scope.ServiceProvider.GetRequiredService<OrganizationDbContext>();
            var audits = await db.Set<AuditEvent>().AsNoTracking()
                .Where(x => x.EventType == "organization.work-center.calendar.changed.v1"
                    && x.EntityId == workCenterId.ToString("D"))
                .ToListAsync();
            Assert.Equal(
                new (string, long?, long?)[] { ("Create", null, 1L), ("NoChange", 1L, 1L), ("Update", 1L, 2L) },
                audits.Select(x => (x.Operation, x.EntityVersionBefore, x.EntityVersionAfter))
                    .OrderBy(x => x.EntityVersionAfter).ThenBy(x => x.Operation, StringComparer.Ordinal));
            Assert.All(audits, audit =>
            {
                Assert.Equal(TestCompanyIds.CompanyA, audit.CompanyId);
                Assert.Equal(SourceChannels.Web, audit.SourceChannel);
                Assert.Equal(OrganizationPermissionManifestV1.WorkCenterCalendarsWrite, audit.PermissionCode);
                Assert.NotEmpty(audit.Reason);
            });
        });
    }

    [SqlServerFact]
    public async Task Another_companys_work_center_is_a_code_less_not_found_and_writes_no_calendar()
    {
        await WithDatabaseAsync(async factory =>
        {
            await GrantAsync(factory, TestUsers.NoAssignment, TestCompanyIds.CompanyB, CalendarEditorPermissions);
            using var companyB = CreateClient(factory, TestUsers.NoAssignment, TestCompanyIds.CompanyB);
            var foreign = await FirstWorkCenterAsync(factory, TestCompanyIds.CompanyA);
            var path = $"{WorkCentersRoute}/{foreign}/calendar";
            var baseline = await ReadEffectsAsync(factory, TestCompanyIds.CompanyA);

            using (var response = await companyB.GetAsync(path))
            {
                await AssertCodeLessNotFoundAsync(response, foreign);
            }

            using (var response = await SendAsync(companyB, path,
                new SetWorkCenterCalendar(TwoShiftWeek, [], 0, "Yabancı hat"), Key()))
            {
                await AssertCodeLessNotFoundAsync(response, foreign);
            }

            // Company A's calendar tables and audit trail are untouched; the refusal is stored under B's own key.
            var after = await ReadEffectsAsync(factory, TestCompanyIds.CompanyA);
            Assert.Equal(
                (baseline.Calendars, baseline.Shifts, baseline.ClosedDates, baseline.Audits),
                (after.Calendars, after.Shifts, after.ClosedDates, after.Audits));
        });
    }

    [SqlServerFact]
    public async Task The_v2_planning_reference_answers_the_calendar_the_screen_shows()
    {
        await WithDatabaseAsync(async factory =>
        {
            await GrantAsync(factory, TestUsers.NoAssignment, TestCompanyIds.CompanyA, CalendarEditorPermissions);
            using var editor = CreateClient(factory, TestUsers.NoAssignment, TestCompanyIds.CompanyA);
            var workCenterId = await FirstWorkCenterAsync(factory, TestCompanyIds.CompanyA);
            var path = $"{WorkCentersRoute}/{workCenterId}/calendar";

            var beforeSave = await ReadReferenceAsync(factory, workCenterId);
            Assert.Equal((0L, "Default", "Europe/Istanbul"), (beforeSave.Version, beforeSave.Source, beforeSave.TimeZoneId));
            Assert.Equal(5, beforeSave.Shifts.Count);
            Assert.Empty(beforeSave.Holidays);

            using (var response = await SendAsync(editor, path,
                new SetWorkCenterCalendar(TwoShiftWeek, ["2027-01-01"], 0, "Yeni yıl kapalı"), Key()))
            {
                await AssertNoContentAsync(response, replay: false);
            }

            var screen = await ReadCalendarAsync(editor, path);
            var reference = await ReadReferenceAsync(factory, workCenterId);
            var v1 = await ReadReferenceV1Async(factory, workCenterId);
            Assert.Equal((v1.WorkCenterId, v1.Kind), (reference.WorkCenterId, reference.Kind));
            Assert.Equal((screen.Version, screen.Source, screen.TimeZoneId), (reference.Version, reference.Source, reference.TimeZoneId));
            Assert.Equal(
                screen.Shifts,
                reference.Shifts.Select(shift => new WorkCenterCalendarShiftV1(
                    shift.Day.ToString(),
                    shift.Start.ToString("HH:mm", CultureInfo.InvariantCulture),
                    shift.End.ToString("HH:mm", CultureInfo.InvariantCulture))));
            Assert.Equal(screen.ClosedDates, reference.Holidays.Select(date => date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)));
        });
    }

    [SqlServerFact]
    public async Task Rolling_back_the_calendar_migration_is_refused_while_a_calendar_exists()
    {
        await WithDatabaseAsync(async factory =>
        {
            await GrantAsync(factory, TestUsers.NoAssignment, TestCompanyIds.CompanyA, CalendarEditorPermissions);
            using var editor = CreateClient(factory, TestUsers.NoAssignment, TestCompanyIds.CompanyA);
            var workCenterId = await FirstWorkCenterAsync(factory, TestCompanyIds.CompanyA);
            using (var response = await SendAsync(editor, $"{WorkCentersRoute}/{workCenterId}/calendar",
                new SetWorkCenterCalendar(TwoShiftWeek, [], 0, "Geri alma koruması"), Key()))
            {
                await AssertNoContentAsync(response, replay: false);
            }

            string connectionString;
            await using (var scope = factory.Services.CreateAsyncScope())
            {
                connectionString = scope.ServiceProvider.GetRequiredService<OrganizationDbContext>()
                    .Database.GetConnectionString()!;
            }

            var options = new DbContextOptionsBuilder<OrganizationDbContext>()
                .UseSqlServer(connectionString, sql => sql.MigrationsHistoryTable("__EFMigrationsHistory", "organization"))
                .Options;
            await using var dbContext = new OrganizationDbContext(options, new CompanyContext(), new OrganizationBootstrapWriteScope());
            var exception = await Assert.ThrowsAsync<SqlException>(
                () => dbContext.GetService<IMigrator>().MigrateAsync(MigrationBeforeCalendar));
            Assert.Equal(ForwardFixRefusal, exception.Number);

            // The refusal rolled nothing back: the table and its row are still there.
            Assert.Contains(
                (await dbContext.Database.GetAppliedMigrationsAsync()),
                migration => migration.EndsWith("_AddWorkCenterCalendar", StringComparison.Ordinal));
        });
    }

    /// <summary>
    /// KIM-S25 (KIM-D33): an end earlier than the start spills into the next day. The overnight windows (including a
    /// Sunday one that runs into Monday and one that ends exactly at midnight) are stored as written, read back by the
    /// screen and published unchanged by the V2 planning reference; an empty window and a next-day part that overlaps
    /// the next day's shift are refused before anything is stored.
    /// </summary>
    [SqlServerFact]
    public async Task An_overnight_shift_is_stored_as_written_and_an_empty_or_overlapping_one_is_refused()
    {
        await WithDatabaseAsync(async factory =>
        {
            await GrantAsync(factory, TestUsers.NoAssignment, TestCompanyIds.CompanyA, CalendarEditorPermissions);
            using var editor = CreateClient(factory, TestUsers.NoAssignment, TestCompanyIds.CompanyA);
            var workCenterId = await FirstWorkCenterAsync(factory, TestCompanyIds.CompanyA);
            var path = $"{WorkCentersRoute}/{workCenterId}/calendar";
            var baseline = await ReadEffectsAsync(factory, TestCompanyIds.CompanyA);

            // Start = end is no window; Monday 22:00-06:00 runs into Tuesday 05:00-13:00.
            var refused = new[]
            {
                new SetWorkCenterCalendar([new("Monday", "08:00", "08:00")], [], 0, "Boş vardiya"),
                new SetWorkCenterCalendar(
                    [new("Monday", "22:00", "06:00"), new("Tuesday", "05:00", "13:00")], [], 0, "Çakışan gece vardiyası"),
                new SetWorkCenterCalendar(
                    [new("Sunday", "22:00", "06:00"), new("Monday", "05:30", "13:00")], [], 0, "Haftayı aşan çakışma"),
            };
            foreach (var body in refused)
            {
                using var response = await SendAsync(editor, path, body, Key());
                Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
            }

            var afterRefusals = await ReadEffectsAsync(factory, TestCompanyIds.CompanyA);
            Assert.Equal(
                (baseline.Calendars, baseline.Shifts, baseline.Audits),
                (afterRefusals.Calendars, afterRefusals.Shifts, afterRefusals.Audits));

            // Touching is allowed: Sunday's night shift ends at Monday 06:00, when Monday's day shift starts.
            WorkCenterCalendarShiftV1[] nightWeek =
            [
                new("Sunday", "22:00", "06:00"),
                new("Monday", "06:00", "14:00"),
                new("Monday", "16:00", "00:00"),
                new("Friday", "22:00", "06:00"),
            ];
            using (var response = await SendAsync(editor, path,
                new SetWorkCenterCalendar(nightWeek, [], 0, "Gece vardiyası açıldı"), Key()))
            {
                await AssertNoContentAsync(response, replay: false);
            }

            var screen = await ReadCalendarAsync(editor, path);
            Assert.Equal(("WorkCenter", 1L), (screen.Source, screen.Version));
            Assert.Equal(
                new WorkCenterCalendarShiftV1[]
                {
                    new("Monday", "06:00", "14:00"),
                    new("Monday", "16:00", "00:00"),
                    new("Friday", "22:00", "06:00"),
                    new("Sunday", "22:00", "06:00"),
                },
                screen.Shifts);

            var reference = await ReadReferenceAsync(factory, workCenterId);
            Assert.Equal(screen.Version, reference.Version);
            Assert.Equal(
                screen.Shifts,
                reference.Shifts.Select(shift => new WorkCenterCalendarShiftV1(
                    shift.Day.ToString(),
                    shift.Start.ToString("HH:mm", CultureInfo.InvariantCulture),
                    shift.End.ToString("HH:mm", CultureInfo.InvariantCulture))));
            Assert.Equal(3, reference.Shifts.Count(shift => shift.End < shift.Start));
        });
    }

    /// <summary>
    /// KIM-S25 (KIM-D33): rolling AllowWorkCenterOvernightShifts back restores the same-day check, which cannot hold an
    /// overnight row. Its guard passes while every stored shift is same-day and refuses (forward fix) once any revision
    /// holds an overnight shift, leaving the rows as they were.
    /// </summary>
    [SqlServerFact]
    public async Task The_overnight_shift_migrations_Down_passes_with_same_day_shifts_and_refuses_while_an_overnight_shift_exists()
    {
        await WithDatabaseAsync(async factory =>
        {
            await GrantAsync(factory, TestUsers.NoAssignment, TestCompanyIds.CompanyA, CalendarEditorPermissions);
            using var editor = CreateClient(factory, TestUsers.NoAssignment, TestCompanyIds.CompanyA);
            var workCenterId = await FirstWorkCenterAsync(factory, TestCompanyIds.CompanyA);
            var path = $"{WorkCentersRoute}/{workCenterId}/calendar";
            var guard = Assert.IsType<SqlOperation>(new AllowWorkCenterOvernightShifts().DownOperations[0]).Sql;

            using (var response = await SendAsync(editor, path,
                new SetWorkCenterCalendar(TwoShiftWeek, [], 0, "Gündüz vardiyaları"), Key()))
            {
                await AssertNoContentAsync(response, replay: false);
            }

            await ExecuteOrganizationSqlAsync(factory, guard);

            using (var response = await SendAsync(editor, path,
                new SetWorkCenterCalendar([new("Monday", "22:00", "06:00")], [], 1, "Gece vardiyası"), Key()))
            {
                await AssertNoContentAsync(response, replay: false);
            }

            // Revision 2 holds the overnight row; the guard refuses even though revision 1 is same-day.
            var exception = await Assert.ThrowsAsync<SqlException>(() => ExecuteOrganizationSqlAsync(factory, guard));
            Assert.Equal(OvernightForwardFixRefusal, exception.Number);

            var reference = await ReadReferenceAsync(factory, workCenterId);
            Assert.Equal(2L, reference.Version);
            var overnight = Assert.Single(reference.Shifts);
            Assert.Equal((DayOfWeek.Monday, new TimeOnly(22, 0), new TimeOnly(6, 0)), (overnight.Day, overnight.Start, overnight.End));
        });
    }

    private static async Task ExecuteOrganizationSqlAsync(SqlServerRelationalApiFactory factory, string sql)
    {
        string connectionString;
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            connectionString = scope.ServiceProvider.GetRequiredService<OrganizationDbContext>()
                .Database.GetConnectionString()!;
        }

        await using var connection = new SqlConnection(connectionString);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        await command.ExecuteNonQueryAsync();
    }

    private static async Task<WorkCenterCalendarDetail> ReadCalendarAsync(HttpClient client, string path)
    {
        using var response = await client.GetAsync(path);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal(
            ["closedDates", "shifts", "source", "timeZoneId", "version", "workCenterId"],
            body.RootElement.EnumerateObject().Select(property => property.Name).Order(StringComparer.Ordinal));
        Assert.Equal(JsonValueKind.Number, body.RootElement.GetProperty("version").ValueKind);
        return Assert.IsType<WorkCenterCalendarDetail>(body.RootElement.Deserialize<WorkCenterCalendarDetail>(Json));
    }

    private static async Task<OrganizationWorkCenterPlanningReferenceV2> ReadReferenceAsync(
        SqlServerRelationalApiFactory factory, Guid workCenterId)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        scope.ServiceProvider.GetRequiredService<ICompanyContext>().Set(TestCompanyIds.CompanyA);
        var reference = await scope.ServiceProvider.GetRequiredService<IOrganizationWorkCenterPlanningReferenceV2>()
            .ReadAsync(TestCompanyIds.CompanyA, workCenterId);
        return Assert.IsType<OrganizationWorkCenterPlanningReferenceV2>(reference);
    }

    private static async Task<OrganizationWorkCenterPlanningReferenceV1> ReadReferenceV1Async(
        SqlServerRelationalApiFactory factory, Guid workCenterId)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        scope.ServiceProvider.GetRequiredService<ICompanyContext>().Set(TestCompanyIds.CompanyA);
        var reference = await scope.ServiceProvider.GetRequiredService<IOrganizationWorkCenterPlanningReferenceV1>()
            .ReadAsync(TestCompanyIds.CompanyA, workCenterId);
        return Assert.IsType<OrganizationWorkCenterPlanningReferenceV1>(reference);
    }

    private static async Task<Guid> FirstWorkCenterAsync(SqlServerRelationalApiFactory factory, Guid company)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        scope.ServiceProvider.GetRequiredService<ICompanyContext>().Set(company);
        return await scope.ServiceProvider.GetRequiredService<OrganizationDbContext>().WorkCenters
            .AsNoTracking()
            .OrderBy(workCenter => workCenter.Code)
            .Select(workCenter => workCenter.Id)
            .FirstAsync();
    }

    private static string Key() => Guid.NewGuid().ToString("D");

    private static HttpClient CreateClient(SqlServerRelationalApiFactory factory, string user, Guid company)
    {
        var client = factory.CreateClientFor(user, company);
        // The shared factory's Cari handshake is not an Organization contract.
        client.DefaultRequestHeaders.Remove(CounterpartyClientContract.HeaderName);
        client.DefaultRequestHeaders.Remove("Idempotency-Key");
        return client;
    }

    private static async Task<HttpResponseMessage> SendAsync(
        HttpClient client, string path, SetWorkCenterCalendar body, string key)
    {
        using var request = new HttpRequestMessage(HttpMethod.Put, path);
        request.Content = JsonContent.Create(body, options: Json);
        request.Headers.Add("Idempotency-Key", key);
        request.Headers.Add("X-Correlation-Id", Key());
        return await client.SendAsync(request);
    }

    private static async Task AssertNoContentAsync(HttpResponseMessage response, bool replay)
    {
        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        Assert.Empty(await response.Content.ReadAsByteArrayAsync());
        Assert.Equal(replay, response.Headers.Contains("Idempotency-Replayed"));
    }

    private static async Task AssertVersionConflictAsync(HttpResponseMessage response, long expected, long current)
    {
        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.False(body.RootElement.TryGetProperty("code", out _));
        var conflict = body.RootElement.GetProperty("conflict");
        Assert.Equal(expected, conflict.GetProperty("expectedVersion").GetInt64());
        Assert.Equal(current, conflict.GetProperty("currentVersion").GetInt64());
    }

    private static async Task AssertCodeLessNotFoundAsync(HttpResponseMessage response, Guid id)
    {
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        var text = await response.Content.ReadAsStringAsync();
        Assert.DoesNotContain(id.ToString(), text, StringComparison.OrdinalIgnoreCase);
        using var body = JsonDocument.Parse(text);
        Assert.False(body.RootElement.TryGetProperty("code", out _));
    }

    private static async Task<Effects> ReadEffectsAsync(SqlServerRelationalApiFactory factory, Guid company)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        scope.ServiceProvider.GetRequiredService<ICompanyContext>().Set(company);
        var db = scope.ServiceProvider.GetRequiredService<OrganizationDbContext>();
        Assert.True(db.Database.IsSqlServer());
        return new Effects(
            await db.WorkCenterCalendars.CountAsync(),
            await db.WorkCenterCalendarShifts.CountAsync(),
            await db.WorkCenterCalendarClosedDates.CountAsync(),
            await db.Set<AuditEvent>().CountAsync(),
            await db.CommandExecutions.CountAsync());
    }

    /// <summary>A synthetic role with exactly these permissions in one company, assigned company-wide to a development user.</summary>
    private static async Task GrantAsync(
        SqlServerRelationalApiFactory factory, string developmentUser, Guid company, IReadOnlyCollection<string> permissions)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<IdentityDbContext>();
        var link = await dbContext.ExternalIdentities.SingleOrDefaultAsync(
            x => x.Issuer == BootstrapIdentityOptions.Issuer && x.Subject == developmentUser);
        Guid actor;
        if (link is null)
        {
            actor = Guid.NewGuid();
            dbContext.Users.Add(new UserAccount(actor, "Synthetic work-centre calendar editor", null));
            dbContext.ExternalIdentities.Add(new ExternalIdentityLink(Guid.NewGuid(), actor, BootstrapIdentityOptions.Issuer, developmentUser));
        }
        else
        {
            actor = link.UserId;
        }

        var roleId = Guid.NewGuid();
        var role = new RoleDefinition(roleId, $"ORG-CALENDAR-{roleId:N}", "Synthetic work-centre calendar role", true);
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
    }

    private static async Task WithDatabaseAsync(Func<SqlServerRelationalApiFactory, Task> test)
    {
        var connection = Environment.GetEnvironmentVariable(SqlServerRelationalAcceptanceTests.ConnectionStringEnvironmentVariable)!;
        var databaseName = $"GoymenERP_Organization_Calendar_{Guid.NewGuid():N}";
        await using var factory = new SqlServerRelationalApiFactory(connection,
            applyTestCompanyBTopologyTemplates: false, databaseName: databaseName);
        await using var cleanupScope = factory.Services.CreateAsyncScope();
        var cleanupDb = cleanupScope.ServiceProvider.GetRequiredService<OrganizationDbContext>();
        // Validate the immutable cleanup target before running assertions, not in their finally path.
        Assert.Equal(databaseName, cleanupDb.Database.GetDbConnection().Database);
        try
        {
            await test(factory);
        }
        finally
        {
            await cleanupDb.Database.EnsureDeletedAsync();
        }
    }

    private sealed record Effects(int Calendars, int Shifts, int ClosedDates, int Audits, int Executions);
}
