using GoymenERP.BuildingBlocks.Multitenancy;
using GoymenERP.Modules.Settings.Application;
using GoymenERP.Modules.Settings.Application.Contracts.V1;
using Microsoft.EntityFrameworkCore;

namespace GoymenERP.Modules.Settings.Infrastructure.Persistence;

/// <summary>
/// WP2: the automation-only read of the Work timing keys (approval durations, holiday lists, digest times) for the
/// server-resolved company — the Work scheduler has no user. Like <see cref="SettingsNotificationRetentionAutomationV1"/> it
/// reads only its own keys under the company query filter, refuses any stored shape the record command would not have written,
/// and turns a store failure into <see cref="CompanyPolicyProblemCodesV1.ProviderUnavailable"/> (never a guessed value).
/// </summary>
internal sealed class SettingsWorkTimingAutomationV1(SettingsDbContext database, ICompanyContext company)
    : ISettingsWorkTimingAutomationV1
{
    public async Task<SettingsWorkApprovalDurationPolicyV1> GetWorkApprovalDurationAsync(CancellationToken cancellationToken = default)
    {
        var companyId = RequireCompany();
        var hours = await LatestAsync(CompanyPolicyKeysV1.WorkApprovalDurationHours, cancellationToken);
        var days = await LatestAsync(CompanyPolicyKeysV1.WorkApprovalDurationBusinessDays, cancellationToken);
        RequireSame(companyId);
        return new(companyId,
            Whole(hours, 1, SettingsWorkApprovalDurationLimitsV1.MaximumHours) ?? SettingsWorkApprovalDurationLimitsV1.DecidedDefaultHours,
            hours?.Version ?? 0,
            Whole(days, 1, SettingsWorkApprovalDurationLimitsV1.MaximumBusinessDays) ?? SettingsWorkApprovalDurationLimitsV1.DecidedDefaultBusinessDays,
            days?.Version ?? 0);
    }

    public async Task<SettingsWorkHolidayCalendarsV1> GetWorkHolidayCalendarsAsync(int fromYear, int toYear,
        CancellationToken cancellationToken = default)
    {
        var companyId = RequireCompany();
        if (fromYear < SettingsWorkHolidayCalendarLimitsV1.MinimumYear || toYear > SettingsWorkHolidayCalendarLimitsV1.MaximumYear
            || fromYear > toYear)
            throw new SettingsPrerequisiteException(CompanyPolicyProblemCodesV1.RequestInvalid);
        WorkHolidayCalendarVersionRow[] latest;
        WorkHolidayCalendarDayRow[] days;
        try
        {
            latest = (await database.WorkHolidayCalendarVersions.AsNoTracking()
                    .Where(row => row.CompanyId == companyId && row.Year >= fromYear && row.Year <= toYear)
                    .ToArrayAsync(cancellationToken))
                .GroupBy(row => row.Year)
                .Select(group => group.OrderByDescending(row => row.Version).First())
                .OrderBy(row => row.Year)
                .ToArray();
            var keys = latest.Select(row => (row.Year, row.Version)).ToHashSet();
            days = (await database.WorkHolidayCalendarDays.AsNoTracking()
                    .Where(row => row.CompanyId == companyId && row.Year >= fromYear && row.Year <= toYear)
                    .ToArrayAsync(cancellationToken))
                .Where(row => keys.Contains((row.Year, row.Version)))
                .ToArray();
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            throw new SettingsPrerequisiteException(CompanyPolicyProblemCodesV1.ProviderUnavailable);
        }

        RequireSame(companyId);
        var years = new List<SettingsWorkHolidayCalendarYearV1>(latest.Length);
        foreach (var version in latest)
        {
            var listed = days.Where(day => day.Year == version.Year && day.Version == version.Version).OrderBy(day => day.Date).ToArray();
            if (version.CompanyId != companyId || version.Version <= 0 || listed.Length != version.DayCount
                || listed.Any(day => day.CompanyId != companyId || day.Date.Year != version.Year || day.Kind is not (1 or 2)))
                throw new SettingsPrerequisiteException(CompanyPolicyProblemCodesV1.ProfileInvalid);
            years.Add(new(version.Year, version.Version, listed.Select(day => day.Date).ToArray()));
        }

        return new(companyId, years);
    }

    public async Task<SettingsWorkDigestScheduleV1> GetWorkDigestScheduleAsync(CancellationToken cancellationToken = default)
    {
        var companyId = RequireCompany();
        var times = new List<SettingsWorkDigestTimeV1>(SettingsWorkDigestScheduleLimitsV1.Slots.Count);
        foreach (var (slot, key, decidedDefault) in SettingsWorkDigestScheduleLimitsV1.Slots)
        {
            var row = await LatestAsync(key, cancellationToken);
            times.Add(new(slot, Whole(row, 0, SettingsWorkDigestScheduleLimitsV1.MaximumMinute) ?? decidedDefault, row?.Version ?? 0));
        }

        RequireSame(companyId);
        return new(companyId, times);
    }

    private async Task<CompanyPolicyVersionRow?> LatestAsync(string key, CancellationToken cancellationToken)
    {
        try
        {
            var row = await CompanyPolicyReads.LatestAsync(database, key, cancellationToken);
            if (row is not null && (row.PolicyKey != key || row.Version < 1))
                throw new SettingsPrerequisiteException(CompanyPolicyProblemCodesV1.ProfileInvalid);
            return row;
        }
        catch (Exception exception) when (exception is not OperationCanceledException and not SettingsPrerequisiteException)
        {
            throw new SettingsPrerequisiteException(CompanyPolicyProblemCodesV1.ProviderUnavailable);
        }
    }

    private static int? Whole(CompanyPolicyVersionRow? row, int minimum, int maximum)
    {
        if (row is null) return null;
        if (row.WholeNumberValue is not { } value || value < minimum || value > maximum || row.BooleanValue is not null
            || row.PercentValue is not null || row.VatApplies is not null || row.VatPercent is not null || row.ExemptionCode is not null
            || row.WithholdingApplies is not null || row.WithholdingPercent is not null || row.WithholdingCode is not null)
            throw new SettingsPrerequisiteException(CompanyPolicyProblemCodesV1.ProfileInvalid);
        return value;
    }

    private Guid RequireCompany() => company.IsResolved && company.CompanyId != Guid.Empty
        ? company.CompanyId
        : throw new SettingsPrerequisiteException(CompanyPolicyProblemCodesV1.AccessDenied);

    private void RequireSame(Guid companyId)
    {
        if (!company.IsResolved || company.CompanyId != companyId)
            throw new SettingsPrerequisiteException(CompanyPolicyProblemCodesV1.AccessDenied);
    }
}
