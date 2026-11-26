using GoymenERP.BuildingBlocks.Multitenancy;
using GoymenERP.Modules.Identity.Application.Contracts;
using GoymenERP.Modules.Settings.Application.Contracts.V1;
using GoymenERP.Modules.Work.Domain;
using GoymenERP.Modules.Work.Infrastructure.ApprovalFollowUp;
using GoymenERP.Modules.Work.Infrastructure.ApprovalRules;

namespace GoymenERP.Api.WorkRuntime;

/// <summary>
/// WP2 host composition: the owner ports Work's approval follow-up cycle and digest release need, over the owners' own
/// automation surfaces (no user is impersonated; the resolved company is the only fence). The cycle itself runs only from the
/// collaboration scheduler and only when <c>Work:CollaborationScheduler:ApprovalFollowUpEnabled</c> is true.
/// </summary>
public static class WorkApprovalFollowUpHostV1
{
    public static IServiceCollection AddWorkApprovalFollowUpHostV1(this IServiceCollection services)
    {
        services.AddScoped<IWorkApprovalTimingReaderV1, WorkSettingsApprovalTimingReaderV1>();
        services.AddScoped<IWorkPermissionHolderDirectoryV1, WorkIdentityPermissionHolderDirectoryV1>();
        return services;
    }
}

/// <summary>Settings' automation read of the approval durations and holiday lists (YOL-D18, YOL-D22); null when Settings fails.</summary>
internal sealed class WorkSettingsApprovalTimingReaderV1(ISettingsWorkTimingAutomationV1 settings, ICompanyContext company)
    : IWorkApprovalTimingReaderV1
{
    public async Task<WorkApprovalTimingV1?> ReadAsync(Guid companyId, int fromYear, int toYear, CancellationToken cancellationToken = default)
    {
        if (!company.IsResolved || company.CompanyId != companyId)
        {
            return null;
        }

        try
        {
            var durations = await settings.GetWorkApprovalDurationAsync(cancellationToken);
            var from = Math.Max(fromYear, SettingsWorkHolidayCalendarLimitsV1.MinimumYear);
            var to = Math.Min(toYear, SettingsWorkHolidayCalendarLimitsV1.MaximumYear);
            var holidays = from <= to ? await settings.GetWorkHolidayCalendarsAsync(from, to, cancellationToken) : null;
            if (durations.CompanyId != companyId || holidays is not null && holidays.CompanyId != companyId)
            {
                return null;
            }

            var years = new Dictionary<int, IReadOnlySet<DateOnly>>();
            foreach (var year in holidays?.DefinedYears ?? [])
            {
                if (!years.TryAdd(year.Year, year.Days.ToHashSet()))
                {
                    return null;
                }
            }

            return new WorkApprovalTimingV1(companyId, durations.Hours, durations.HoursVersionText, durations.BusinessDays,
                durations.BusinessDaysVersionText, years);
        }
        catch (SettingsPrerequisiteException)
        {
            return null;
        }
    }
}

/// <summary>Identity's system read of the holders of one permission (company-wide or scoped); unavailable on any refusal.</summary>
internal sealed class WorkIdentityPermissionHolderDirectoryV1(IPermissionHolderListPortV1 identity) : IWorkPermissionHolderDirectoryV1
{
    public async Task<WorkPermissionHoldersV1> ListHoldersAsync(Guid companyId, string permission, CancellationToken cancellationToken = default)
    {
        var answer = await identity.ListAsync(new PermissionHolderListRequestV1(companyId, permission), cancellationToken);
        return answer.Outcome != UserDirectoryOutcomeV1.Found
            ? WorkPermissionHoldersV1.Unavailable
            : new WorkPermissionHoldersV1(true, answer.Holders.Select(holder => new WorkPermissionHolderV1(holder.UserId,
                holder.IsCompanyWide, holder.SiteIds.ToHashSet(), holder.LocationIds.ToHashSet(), holder.WorkCenterIds.ToHashSet(),
                holder.IsGeneralManager, holder.ModuleManagerScope is { } manager ? new WorkPermissionHolderScopeV1(manager.IsCompanyWide,
                    manager.SiteIds.ToHashSet(), manager.LocationIds.ToHashSet(), manager.WorkCenterIds.ToHashSet()) : null)).ToArray());
    }
}

/// <summary>
/// YOL-D25 (AI-D29): the company's digest times from Settings (09:00, 13:00, 17:00 as version 0), released on business days
/// only in the business time zone (D6), skipping the recorded holidays and eves of this and the previous year (YOL-D22; a year
/// without a list deducts weekends only). Settings or the zone rules unavailable → no digest is released (fail closed).
/// </summary>
internal sealed class WorkSettingsDigestScheduleReaderV1(ISettingsWorkTimingAutomationV1 settings, ICompanyContext company,
    IWorkBusinessTimeZoneSourceV1 timeZone, TimeProvider time) : IWorkDigestScheduleReaderV1
{
    public async Task<WorkDigestScheduleV1> ReadAsync(CancellationToken cancellationToken = default)
    {
        var companyId = company.IsResolved ? company.CompanyId : Guid.Empty;
        if (companyId == Guid.Empty || timeZone.BusinessTimeZone is not { } zone)
        {
            return WorkDigestScheduleV1.Unavailable(companyId);
        }

        try
        {
            var schedule = await settings.GetWorkDigestScheduleAsync(cancellationToken);
            if (schedule.CompanyId != companyId || schedule.Times.Count == 0)
            {
                return WorkDigestScheduleV1.Unavailable(companyId);
            }

            var year = TimeZoneInfo.ConvertTime(time.GetUtcNow(), zone).Year;
            HashSet<DateOnly> holidays = [];
            if (year - 1 >= SettingsWorkHolidayCalendarLimitsV1.MinimumYear && year <= SettingsWorkHolidayCalendarLimitsV1.MaximumYear)
            {
                var calendars = await settings.GetWorkHolidayCalendarsAsync(year - 1, year, cancellationToken);
                if (calendars.CompanyId != companyId)
                {
                    return WorkDigestScheduleV1.Unavailable(companyId);
                }

                holidays = calendars.DefinedYears.SelectMany(defined => defined.Days).ToHashSet();
            }

            var times = schedule.Times.Select(entry => TimeOnly.FromTimeSpan(TimeSpan.FromMinutes(entry.Minute)))
                .Distinct().Order().ToArray();
            return new WorkDigestScheduleV1(true, companyId, times, zone.Id, schedule.VersionText, true, holidays);
        }
        catch (SettingsPrerequisiteException)
        {
            return WorkDigestScheduleV1.Unavailable(companyId);
        }
    }
}
