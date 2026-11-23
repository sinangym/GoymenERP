using GoymenERP.Modules.Settings.Application;
using GoymenERP.Modules.Settings.Application.Contracts.V1;
using Microsoft.EntityFrameworkCore;

namespace GoymenERP.Modules.Settings.Infrastructure.Persistence;

/// <summary>
/// WP15 (1.16): the latest recorded legal deadline calendar for a company. No version answers version 0 with no entries:
/// "not defined", never a default. A stored version that does not hold together is a profile error (fail closed).
/// </summary>
public sealed partial class SettingsCompanyPolicyProviderV1 : ISettingsLegalDeadlineCalendarProviderV1
{
    public async Task<SettingsLegalDeadlineCalendarV1> GetLegalDeadlineCalendarAsync(Guid companyId,
        CancellationToken cancellationToken = default)
    {
        var actor = _currentUser.UserId;
        RequireAccess(companyId, cancellationToken);
        LegalDeadlineCalendarVersionRow? latest;
        LegalDeadlineCalendarEntryRow[] entries;
        try
        {
            latest = await _database.LegalDeadlineCalendarVersions.AsNoTracking()
                .Where(row => row.CompanyId == companyId)
                .OrderByDescending(row => row.Version).FirstOrDefaultAsync(cancellationToken);
            entries = latest is null
                ? []
                : await _database.LegalDeadlineCalendarEntries.AsNoTracking()
                    .Where(row => row.CompanyId == companyId && row.Version == latest.Version)
                    .OrderBy(row => row.Obligation).ToArrayAsync(cancellationToken);
        }
        catch (Exception exception) when (exception is not OperationCanceledException and not SettingsPrerequisiteException)
        {
            throw new SettingsPrerequisiteException(CompanyPolicyProblemCodesV1.ProviderUnavailable);
        }
        RequireAccess(companyId, cancellationToken);
        if (actor != _currentUser.UserId) throw new SettingsPrerequisiteException(CompanyPolicyProblemCodesV1.AccessDenied);
        if (latest is null) return new(companyId, 0, []);
        var read = entries.Select(row => new SettingsLegalDeadlineEntryV1((LegalDeadlineObligationV1)row.Obligation,
            row.PeriodMonths, row.MonthsAfterPeriodEnd, row.DueDayOfMonth)).ToArray();
        if (latest.CompanyId != companyId || latest.Version <= 0 || read.Length != latest.EntryCount
            || entries.Any(row => row.CompanyId != companyId) || read.Any(entry => !LegalDeadlineCalculatorV1.IsValid(entry))
            || read.Select(entry => entry.Obligation).Distinct().Count() != read.Length)
            throw new SettingsPrerequisiteException(CompanyPolicyProblemCodesV1.ProfileInvalid);
        return new(companyId, latest.Version, read);
    }
}
