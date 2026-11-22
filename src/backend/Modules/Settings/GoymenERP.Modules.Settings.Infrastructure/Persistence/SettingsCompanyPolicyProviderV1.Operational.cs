using Microsoft.EntityFrameworkCore;
using GoymenERP.Modules.Settings.Application;
using GoymenERP.Modules.Settings.Application.Contracts.V1;

namespace GoymenERP.Modules.Settings.Infrastructure.Persistence;

public sealed partial class SettingsCompanyPolicyProviderV1 : ISettingsManufacturingScrapCostPolicyProviderV1,
    ISettingsNotificationRetentionPolicyProviderV1, ISettingsWorkApprovalDurationPolicyProviderV1,
    ISettingsWorkHolidayCalendarProviderV1
{
    public async Task<SettingsManufacturingScrapCostPolicyV1> GetScrapCostPolicyAsync(Guid companyId, CancellationToken cancellationToken = default)
    {
        var actor = _currentUser.UserId;
        var row = await LatestAsync(companyId, CompanyPolicyKeysV1.ManufacturingIncludeScrapCost, cancellationToken);
        RequireAccess(companyId, cancellationToken);
        if (actor != _currentUser.UserId) throw new SettingsPrerequisiteException(CompanyPolicyProblemCodesV1.AccessDenied);
        if (row is null) return new(companyId, true, 0); // SAT-D305 decided initial value, not a store-failure fallback.
        if (row.BooleanValue is not { } include || row.WholeNumberValue is not null || !NoTaxOrRate(row))
            throw new SettingsPrerequisiteException(CompanyPolicyProblemCodesV1.ProfileInvalid);
        return new(companyId, include, row.Version);
    }
    public async Task<SettingsNotificationRetentionPolicyV1> GetNotificationRetentionAsync(Guid companyId, CancellationToken cancellationToken = default)
    {
        var actor = _currentUser.UserId;
        var row = await LatestAsync(companyId, CompanyPolicyKeysV1.NotificationActiveDays, cancellationToken);
        RequireAccess(companyId, cancellationToken);
        if (actor != _currentUser.UserId) throw new SettingsPrerequisiteException(CompanyPolicyProblemCodesV1.AccessDenied);
        if (row is null) return new(companyId, 90, 0); // KLN-D114/D115 initial value; no deletion is implied.
        if (row.WholeNumberValue is not { } days || days < 0 || row.BooleanValue is not null || !NoTaxOrRate(row))
            throw new SettingsPrerequisiteException(CompanyPolicyProblemCodesV1.ProfileInvalid);
        return new(companyId, days, row.Version);
    }
    public async Task<SettingsWorkApprovalDurationPolicyV1> GetWorkApprovalDurationAsync(Guid companyId,
        CancellationToken cancellationToken = default)
    {
        var actor = _currentUser.UserId;
        var hours = await LatestAsync(companyId, CompanyPolicyKeysV1.WorkApprovalDurationHours, cancellationToken);
        var days = await LatestAsync(companyId, CompanyPolicyKeysV1.WorkApprovalDurationBusinessDays, cancellationToken);
        RequireAccess(companyId, cancellationToken);
        if (actor != _currentUser.UserId) throw new SettingsPrerequisiteException(CompanyPolicyProblemCodesV1.AccessDenied);
        // YOL-D18 (WP1 D3): no record is the decided default as version 0 (4 hours / 2 business days), not a store-failure
        // fallback; a recorded version replaces it.
        return new(companyId, DurationValue(hours) ?? SettingsWorkApprovalDurationLimitsV1.DecidedDefaultHours, hours?.Version ?? 0,
            DurationValue(days) ?? SettingsWorkApprovalDurationLimitsV1.DecidedDefaultBusinessDays, days?.Version ?? 0);
    }
    public async Task<SettingsWorkHolidayCalendarsV1> GetWorkHolidayCalendarsAsync(Guid companyId, int fromYear, int toYear,
        CancellationToken cancellationToken = default)
    {
        if (fromYear < SettingsWorkHolidayCalendarLimitsV1.MinimumYear || toYear > SettingsWorkHolidayCalendarLimitsV1.MaximumYear
            || fromYear > toYear)
            throw new SettingsPrerequisiteException(CompanyPolicyProblemCodesV1.RequestInvalid);
        var actor = _currentUser.UserId;
        RequireAccess(companyId, cancellationToken);
        WorkHolidayCalendarVersionRow[] latest;
        WorkHolidayCalendarDayRow[] days;
        try
        {
            latest = (await _database.WorkHolidayCalendarVersions.AsNoTracking()
                    .Where(row => row.CompanyId == companyId && row.Year >= fromYear && row.Year <= toYear)
                    .ToArrayAsync(cancellationToken))
                .GroupBy(row => row.Year)
                .Select(group => group.OrderByDescending(row => row.Version).First())
                .OrderBy(row => row.Year)
                .ToArray();
            var keys = latest.Select(row => (row.Year, row.Version)).ToHashSet();
            days = (await _database.WorkHolidayCalendarDays.AsNoTracking()
                    .Where(row => row.CompanyId == companyId && row.Year >= fromYear && row.Year <= toYear)
                    .ToArrayAsync(cancellationToken))
                .Where(row => keys.Contains((row.Year, row.Version)))
                .ToArray();
        }
        catch (Exception exception) when (exception is not OperationCanceledException and not SettingsPrerequisiteException)
        {
            throw new SettingsPrerequisiteException(CompanyPolicyProblemCodesV1.ProviderUnavailable);
        }
        RequireAccess(companyId, cancellationToken);
        if (actor != _currentUser.UserId) throw new SettingsPrerequisiteException(CompanyPolicyProblemCodesV1.AccessDenied);
        var years = new List<SettingsWorkHolidayCalendarYearV1>(latest.Length);
        foreach (var version in latest)
        {
            var listed = days.Where(day => day.Year == version.Year && day.Version == version.Version)
                .OrderBy(day => day.Date).ToArray();
            if (version.CompanyId != companyId || version.Version <= 0 || listed.Length != version.DayCount
                || listed.Any(day => day.CompanyId != companyId || day.Date.Year != version.Year || day.Kind is not (1 or 2)))
                throw new SettingsPrerequisiteException(CompanyPolicyProblemCodesV1.ProfileInvalid);
            years.Add(new(version.Year, version.Version, listed.Select(day => day.Date).ToArray()));
        }
        return new(companyId, years);
    }
    private static int? DurationValue(CompanyPolicyVersionRow? row)
    {
        if (row is null) return null;
        if (row.WholeNumberValue is not { } value || value < 1 || row.BooleanValue is not null || !NoTaxOrRate(row))
            throw new SettingsPrerequisiteException(CompanyPolicyProblemCodesV1.ProfileInvalid);
        return value;
    }
    private static bool NoTaxOrRate(CompanyPolicyVersionRow row) => row.PercentValue is null && row.VatApplies is null
        && row.VatPercent is null && row.ExemptionCode is null && row.WithholdingApplies is null
        && row.WithholdingPercent is null && row.WithholdingCode is null;
}
