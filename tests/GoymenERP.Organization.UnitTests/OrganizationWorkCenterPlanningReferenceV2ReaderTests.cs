using GoymenERP.BuildingBlocks.Multitenancy;
using GoymenERP.Modules.Organization.Application.Contracts;
using GoymenERP.Modules.Organization.Domain.Locations;
using GoymenERP.Modules.Organization.Domain.Sites;
using GoymenERP.Modules.Organization.Domain.WorkCenters;
using GoymenERP.Modules.Organization.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;

namespace GoymenERP.Organization.UnitTests;

public sealed class OrganizationWorkCenterPlanningReferenceV2ReaderTests
{
    private static readonly Guid CompanyA = Guid.Parse("0f000000-0000-4000-8000-00000000000a");
    private static readonly Guid CompanyB = Guid.Parse("0f000000-0000-4000-8000-00000000000b");
    private static readonly Guid WorkCenterA = Guid.Parse("0f310000-0000-4000-8000-000000000001");
    private static readonly Guid WorkCenterB = Guid.Parse("0f310000-0000-4000-8000-000000000002");
    private static readonly DateTimeOffset Now = new(2026, 11, 14, 9, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task A_work_center_without_a_saved_calendar_reads_the_urtd11_default_at_version_zero()
    {
        var database = $"organization-work-center-reference-v2-default-{Guid.NewGuid():N}";
        await SeedWorkCenterAsync(database, CompanyA, WorkCenterA, WorkCenterKind.Paint);

        var reference = await Reader(database, Resolved(CompanyA)).ReadAsync(CompanyA, WorkCenterA);

        Assert.NotNull(reference);
        Assert.Equal(WorkCenterA, reference!.WorkCenterId);
        Assert.Equal("Paint", reference.Kind);
        Assert.Equal(0, reference.Version);
        Assert.Equal("Default", reference.Source);
        Assert.Equal("Europe/Istanbul", reference.TimeZoneId);
        Assert.Equal(
            WorkCenterCalendarDefinition.Urtd11Default.Shifts
                .Select(shift => new OrganizationWorkCenterShiftV2(shift.Day, shift.Start, shift.End)),
            reference.Shifts);
        Assert.Empty(reference.Holidays);
    }

    [Fact]
    public async Task A_saved_calendar_reads_only_its_current_revision()
    {
        var database = $"organization-work-center-reference-v2-current-{Guid.NewGuid():N}";
        await SeedWorkCenterAsync(database, CompanyA, WorkCenterA, WorkCenterKind.Slitting);
        var first = WorkCenterCalendarDefinition.Create(
            [new WorkCenterShift(DayOfWeek.Monday, new TimeOnly(6, 0), new TimeOnly(14, 0))],
            [new DateOnly(2026, 10, 29)]);
        var second = WorkCenterCalendarDefinition.Create(
            [
                new WorkCenterShift(DayOfWeek.Saturday, new TimeOnly(8, 0), new TimeOnly(12, 0)),
                new WorkCenterShift(DayOfWeek.Monday, new TimeOnly(14, 0), new TimeOnly(22, 0)),
                new WorkCenterShift(DayOfWeek.Monday, new TimeOnly(6, 0), new TimeOnly(14, 0)),
            ],
            [new DateOnly(2027, 1, 1), new DateOnly(2026, 12, 31)]);
        await SeedCalendarAsync(database, CompanyA, WorkCenterA, first, second);

        var reference = await Reader(database, Resolved(CompanyA)).ReadAsync(CompanyA, WorkCenterA);

        Assert.NotNull(reference);
        Assert.Equal("Slitting", reference!.Kind);
        Assert.Equal(2, reference.Version);
        Assert.Equal("WorkCenter", reference.Source);
        Assert.Equal("Europe/Istanbul", reference.TimeZoneId);
        Assert.Equal(
            [
                new OrganizationWorkCenterShiftV2(DayOfWeek.Monday, new TimeOnly(6, 0), new TimeOnly(14, 0)),
                new OrganizationWorkCenterShiftV2(DayOfWeek.Monday, new TimeOnly(14, 0), new TimeOnly(22, 0)),
                new OrganizationWorkCenterShiftV2(DayOfWeek.Saturday, new TimeOnly(8, 0), new TimeOnly(12, 0)),
            ],
            reference.Shifts);
        Assert.Equal([new DateOnly(2026, 12, 31), new DateOnly(2027, 1, 1)], reference.Holidays);
    }

    [Fact]
    public async Task Another_companys_calendar_does_not_leak_into_the_read()
    {
        var database = $"organization-work-center-reference-v2-tenant-{Guid.NewGuid():N}";
        await SeedWorkCenterAsync(database, CompanyA, WorkCenterA, WorkCenterKind.Paint);
        await SeedWorkCenterAsync(database, CompanyB, WorkCenterB, WorkCenterKind.Paint);
        await SeedCalendarAsync(
            database,
            CompanyB,
            WorkCenterB,
            WorkCenterCalendarDefinition.Create(
                [new WorkCenterShift(DayOfWeek.Sunday, new TimeOnly(1, 0), new TimeOnly(2, 0))],
                []));

        var reference = await Reader(database, Resolved(CompanyA)).ReadAsync(CompanyA, WorkCenterA);

        Assert.NotNull(reference);
        Assert.Equal("Default", reference!.Source);
        Assert.Equal(0, reference.Version);
    }

    [Fact]
    public async Task Foreign_unknown_and_empty_work_centers_are_not_found()
    {
        var database = $"organization-work-center-reference-v2-scope-{Guid.NewGuid():N}";
        await SeedWorkCenterAsync(database, CompanyA, WorkCenterA, WorkCenterKind.Paint);
        await SeedWorkCenterAsync(database, CompanyB, WorkCenterB, WorkCenterKind.Slitting);

        var reader = Reader(database, Resolved(CompanyA));
        Assert.Null(await reader.ReadAsync(CompanyA, WorkCenterB));
        Assert.Null(await reader.ReadAsync(CompanyA, Guid.NewGuid()));
        Assert.Null(await reader.ReadAsync(CompanyA, Guid.Empty));
    }

    [Fact]
    public async Task A_company_outside_the_resolved_context_is_refused()
    {
        var database = $"organization-work-center-reference-v2-context-{Guid.NewGuid():N}";
        await SeedWorkCenterAsync(database, CompanyA, WorkCenterA, WorkCenterKind.ColdRolling);

        await Assert.ThrowsAsync<OrganizationCompanyContextMismatchException>(
            () => Reader(database, Resolved(CompanyB)).ReadAsync(CompanyA, WorkCenterA));
        await Assert.ThrowsAsync<OrganizationCompanyContextMismatchException>(
            () => Reader(database, new CompanyContext()).ReadAsync(CompanyA, WorkCenterA));
    }

    [Fact]
    public void The_module_read_depends_on_no_user_or_permission_service()
    {
        var parameters = typeof(OrganizationWorkCenterPlanningReferenceV2Reader).GetConstructors().Single()
            .GetParameters().Select(parameter => parameter.ParameterType);

        Assert.Equal([typeof(OrganizationDbContext), typeof(ICompanyContext)], parameters);
    }

    [Fact]
    public void Version_one_of_the_reference_stays_unchanged()
    {
        var properties = typeof(OrganizationWorkCenterPlanningReferenceV1).GetProperties()
            .Select(property => property.Name)
            .Order()
            .ToArray();

        Assert.Equal(["Kind", "WorkCenterId"], properties);
    }

    [Fact]
    public void Calendar_tables_are_tenant_scoped_with_their_checks_and_unique_keys()
    {
        // Table, check and column-type facts are relational: read them from the SQL Server design-time model (no
        // connection is opened), not from the in-memory provider the reader tests use.
        using var context = RelationalModelContext();
        var model = context.GetService<IDesignTimeModel>().Model;

        var head = model.FindEntityType(typeof(WorkCenterCalendar))!;
        Assert.Equal("WorkCenterCalendars", head.GetTableName());
        Assert.Equal("organization", head.GetSchema());
        Assert.Equal("[Version] >= 1", Checks(head)["CK_WorkCenterCalendars_Version"]);
        Assert.True(head.FindProperty(nameof(WorkCenterCalendar.Version))!.IsConcurrencyToken);
        Assert.Contains(
            head.GetIndexes(),
            index => index.IsUnique
                && index.Properties.Select(property => property.Name)
                    .SequenceEqual([nameof(WorkCenterCalendar.CompanyId), nameof(WorkCenterCalendar.WorkCenterId)]));
        Assert.NotEmpty(head.GetDeclaredQueryFilters());

        var shift = model.FindEntityType(typeof(WorkCenterCalendarShiftRecord))!;
        Assert.Equal("WorkCenterCalendarShifts", shift.GetTableName());
        var shiftChecks = Checks(shift);
        Assert.Equal("[Revision] >= 1", shiftChecks["CK_WorkCenterCalendarShifts_Revision"]);
        Assert.Equal("[Day] BETWEEN 0 AND 6", shiftChecks["CK_WorkCenterCalendarShifts_Day"]);
        Assert.Equal("[StartTime] <> [EndTime]", shiftChecks["CK_WorkCenterCalendarShifts_Window"]);
        Assert.Equal("time(0)", shift.FindProperty(nameof(WorkCenterCalendarShiftRecord.StartTime))!.GetColumnType());
        Assert.Equal("time(0)", shift.FindProperty(nameof(WorkCenterCalendarShiftRecord.EndTime))!.GetColumnType());
        Assert.NotEmpty(shift.GetDeclaredQueryFilters());

        var closedDate = model.FindEntityType(typeof(WorkCenterCalendarClosedDateRecord))!;
        Assert.Equal("WorkCenterCalendarClosedDates", closedDate.GetTableName());
        Assert.Equal("[Revision] >= 1", Checks(closedDate)["CK_WorkCenterCalendarClosedDates_Revision"]);
        Assert.Contains(
            closedDate.GetIndexes(),
            index => index.IsUnique
                && index.Properties.Select(property => property.Name).SequenceEqual(
                [
                    nameof(WorkCenterCalendarClosedDateRecord.CompanyId),
                    nameof(WorkCenterCalendarClosedDateRecord.CalendarId),
                    nameof(WorkCenterCalendarClosedDateRecord.Revision),
                    nameof(WorkCenterCalendarClosedDateRecord.Date),
                ]));
        Assert.NotEmpty(closedDate.GetDeclaredQueryFilters());
    }

    [Fact]
    public async Task A_stored_calendar_row_cannot_be_modified_or_deleted()
    {
        var database = $"organization-work-center-calendar-append-only-{Guid.NewGuid():N}";
        await SeedWorkCenterAsync(database, CompanyA, WorkCenterA, WorkCenterKind.Paint);
        await SeedCalendarAsync(database, CompanyA, WorkCenterA, WorkCenterCalendarDefinition.Urtd11Default);

        await using (var context = Context(database, Resolved(CompanyA)))
        {
            var row = await context.WorkCenterCalendarShifts.FirstAsync();
            context.Entry(row).State = EntityState.Modified;
            var exception = await Assert.ThrowsAsync<InvalidOperationException>(() => context.SaveChangesAsync());
            Assert.Equal("İş merkezi takvimi kayıtları, VKN değişiklik talepleri ve VKN düzeltmeleri silinemez; takvim satırları değiştirilemez.", exception.Message);
        }

        await using (var context = Context(database, Resolved(CompanyA)))
        {
            context.WorkCenterCalendars.Remove(await context.WorkCenterCalendars.SingleAsync());
            var exception = await Assert.ThrowsAsync<InvalidOperationException>(() => context.SaveChangesAsync());
            Assert.Equal("İş merkezi takvimi kayıtları, VKN değişiklik talepleri ve VKN düzeltmeleri silinemez; takvim satırları değiştirilemez.", exception.Message);
        }
    }

    [Fact]
    public async Task A_calendar_for_another_company_than_the_context_is_refused()
    {
        var database = $"organization-work-center-calendar-tenant-guard-{Guid.NewGuid():N}";
        await SeedWorkCenterAsync(database, CompanyA, WorkCenterA, WorkCenterKind.Paint);

        await using var context = Context(database, Resolved(CompanyB));
        context.WorkCenterCalendars.Add(WorkCenterCalendar.Create(Guid.NewGuid(), CompanyA, WorkCenterA, Now));

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() => context.SaveChangesAsync());
        Assert.Equal("Organizasyon kaydının CompanyId değeri aktif şirket bağlamıyla eşleşmelidir.", exception.Message);
    }

    [Fact]
    public async Task A_calendar_without_a_company_context_is_refused()
    {
        var database = $"organization-work-center-calendar-no-context-{Guid.NewGuid():N}";

        await using var context = Context(database, new CompanyContext());
        context.WorkCenterCalendars.Add(WorkCenterCalendar.Create(Guid.NewGuid(), CompanyA, WorkCenterA, Now));

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() => context.SaveChangesAsync());
        Assert.Equal("Organizasyon yazma işlemi için aktif şirket bağlamı zorunludur.", exception.Message);
    }

    private static Dictionary<string, string?> Checks(Microsoft.EntityFrameworkCore.Metadata.IEntityType entity) =>
        entity.GetCheckConstraints().ToDictionary(check => check.Name!, check => (string?)check.Sql);

    private static OrganizationWorkCenterPlanningReferenceV2Reader Reader(string database, ICompanyContext context) =>
        new(Context(database, context), context);

    private static async Task SeedWorkCenterAsync(string database, Guid companyId, Guid workCenterId, WorkCenterKind kind)
    {
        await using var context = Context(database, Resolved(companyId));
        var siteId = Guid.NewGuid();
        var locationId = Guid.NewGuid();
        context.Sites.Add(new Site(siteId, companyId, $"SITE-{workCenterId:N}", "Sentetik saha", SiteType.Factory));
        context.Locations.Add(new Location(locationId, companyId, siteId, $"LOC-{workCenterId:N}", "Sentetik hol", false));
        context.WorkCenters.Add(new WorkCenter(
            workCenterId, companyId, locationId, $"WC-{workCenterId:N}", "Sentetik iş merkezi", kind));
        await context.SaveChangesAsync();
    }

    /// <summary>Stores each definition as the next revision, the way the command does; the last one is current.</summary>
    private static async Task SeedCalendarAsync(
        string database,
        Guid companyId,
        Guid workCenterId,
        params WorkCenterCalendarDefinition[] revisions)
    {
        WorkCenterCalendar? calendar = null;
        foreach (var definition in revisions)
        {
            await using var context = Context(database, Resolved(companyId));
            if (calendar is null)
            {
                calendar = WorkCenterCalendar.Create(Guid.NewGuid(), companyId, workCenterId, Now);
                context.WorkCenterCalendars.Add(calendar);
            }
            else
            {
                calendar = await context.WorkCenterCalendars.SingleAsync(x => x.Id == calendar.Id);
                calendar.Revise(Now.AddMinutes(calendar.Version));
            }

            var rows = calendar.RowsFor(definition);
            context.WorkCenterCalendarShifts.AddRange(rows.Shifts);
            context.WorkCenterCalendarClosedDates.AddRange(rows.ClosedDates);
            await context.SaveChangesAsync();
        }
    }

    private static CompanyContext Resolved(Guid companyId)
    {
        var context = new CompanyContext();
        context.Set(companyId);
        return context;
    }

    private static OrganizationDbContext RelationalModelContext() => new(
        new DbContextOptionsBuilder<OrganizationDbContext>()
            .UseSqlServer("Server=(localdb)\\mssqllocaldb;Database=GoymenErpOrganizationCalendarModel;Trusted_Connection=True;")
            .Options,
        new CompanyContext(),
        new OrganizationBootstrapWriteScope());

    private static OrganizationDbContext Context(string database, ICompanyContext companyContext) => new(
        new DbContextOptionsBuilder<OrganizationDbContext>().UseInMemoryDatabase(database).Options,
        companyContext,
        new OrganizationBootstrapWriteScope());
}
