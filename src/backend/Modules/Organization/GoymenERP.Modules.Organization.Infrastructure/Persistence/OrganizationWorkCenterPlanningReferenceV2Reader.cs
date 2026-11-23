using GoymenERP.BuildingBlocks.Multitenancy;
using GoymenERP.Modules.Organization.Application.Contracts;
using GoymenERP.Modules.Organization.Domain.WorkCenters;
using Microsoft.EntityFrameworkCore;

namespace GoymenERP.Modules.Organization.Infrastructure.Persistence;

/// <summary>
/// Tenant-filtered, no-tracking reads of the selected work centre's kind and its CURRENT calendar revision (the rows
/// whose revision equals the calendar head's version), or the URT-D11 default at version 0 when none was saved. The
/// DbContext query filter remains in force and no user permission is inferred here.
/// </summary>
internal sealed class OrganizationWorkCenterPlanningReferenceV2Reader(
    OrganizationDbContext dbContext,
    ICompanyContext companyContext)
    : IOrganizationWorkCenterPlanningReferenceV2
{
    public async Task<OrganizationWorkCenterPlanningReferenceV2?> ReadAsync(
        Guid companyId,
        Guid workCenterId,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!companyContext.IsResolved || companyContext.CompanyId == Guid.Empty
            || companyId != companyContext.CompanyId)
        {
            throw new OrganizationCompanyContextMismatchException();
        }

        if (workCenterId == Guid.Empty)
        {
            return null;
        }

        var stored = await dbContext.WorkCenters
            .AsNoTracking()
            .Where(workCenter => workCenter.Id == workCenterId)
            .Select(workCenter => new { workCenter.Id, workCenter.Kind })
            .SingleOrDefaultAsync(cancellationToken);
        if (stored is null)
        {
            return null;
        }

        var calendar = await OrganizationWorkCenterCalendarQueries.ReadCurrentAsync(
            dbContext,
            stored.Id,
            cancellationToken);
        return new OrganizationWorkCenterPlanningReferenceV2(
            stored.Id,
            stored.Kind.ToString(),
            calendar.Version,
            calendar.Source,
            WorkCenterCalendarDefinition.BusinessTimeZoneId,
            calendar.Definition.Shifts
                .Select(shift => new OrganizationWorkCenterShiftV2(shift.Day, shift.Start, shift.End))
                .ToArray(),
            calendar.Definition.ClosedDates);
    }
}

/// <summary>The current calendar of a work centre as the Organization reads share it.</summary>
internal sealed record OrganizationCurrentWorkCenterCalendar(
    long Version,
    string Source,
    WorkCenterCalendarDefinition Definition);

/// <summary>
/// The one current-revision read shared by the planning reference V2, the calendar GET and the calendar command, so
/// the three never disagree about which rows are current. The caller has already established that the work centre
/// exists in its company (and, for user reads, in its scope).
/// </summary>
internal static class OrganizationWorkCenterCalendarQueries
{
    public static async Task<OrganizationCurrentWorkCenterCalendar> ReadCurrentAsync(
        OrganizationDbContext dbContext,
        Guid workCenterId,
        CancellationToken cancellationToken)
    {
        var head = await dbContext.WorkCenterCalendars
            .AsNoTracking()
            .Where(calendar => calendar.WorkCenterId == workCenterId)
            .Select(calendar => new { calendar.Id, calendar.Version })
            .SingleOrDefaultAsync(cancellationToken);
        if (head is null)
        {
            return new OrganizationCurrentWorkCenterCalendar(
                OrganizationWorkCenterCalendarRules.DefaultVersion,
                OrganizationWorkCenterCalendarRules.DefaultSource,
                WorkCenterCalendarDefinition.Urtd11Default);
        }

        return new OrganizationCurrentWorkCenterCalendar(
            head.Version,
            OrganizationWorkCenterCalendarRules.WorkCenterSource,
            await ReadRevisionAsync(dbContext, head.Id, head.Version, cancellationToken));
    }

    public static async Task<WorkCenterCalendarDefinition> ReadRevisionAsync(
        OrganizationDbContext dbContext,
        Guid calendarId,
        long revision,
        CancellationToken cancellationToken)
    {
        var shifts = await dbContext.WorkCenterCalendarShifts
            .AsNoTracking()
            .Where(row => row.CalendarId == calendarId && row.Revision == revision)
            .Select(row => new WorkCenterShift(row.Day, row.StartTime, row.EndTime))
            .ToListAsync(cancellationToken);
        var closedDates = await dbContext.WorkCenterCalendarClosedDates
            .AsNoTracking()
            .Where(row => row.CalendarId == calendarId && row.Revision == revision)
            .Select(row => row.Date)
            .ToListAsync(cancellationToken);

        // The rows were validated as one definition when they were written; a stored revision that no longer forms one
        // is corrupt data, and failing closed is safer than publishing a calendar no command could have saved.
        var error = WorkCenterCalendarDefinition.TryCreate(shifts, closedDates, out var definition);
        return error == WorkCenterCalendarDefinitionError.None
            ? definition!
            : throw new InvalidOperationException(
                $"Kayıtlı iş merkezi takvimi revizyonu geçersiz: {error}.");
    }
}
