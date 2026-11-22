using System.Globalization;
using GoymenERP.BuildingBlocks.Multitenancy;
using GoymenERP.BuildingBlocks.Security;
using GoymenERP.BuildingBlocks.Work;
using GoymenERP.Modules.Settings.Application;
using GoymenERP.Modules.Settings.Application.Contracts.V1;

namespace GoymenERP.Modules.Settings.Infrastructure.WorkDeadlines;

/// <summary>
/// WP15 (DALGA_PLANI_v2 1.16, YOL-D4 (c)): the legal deadline counters as a Work deadline source, owned by Settings. The caller
/// needs <see cref="SettingsLegalDeadlinePermissionsV1.DeadlinesRead"/> company-wide, measured before the read and fresh after
/// it. Every catalogue obligation is listed: a recorded entry gives its next due date and days left from
/// <see cref="LegalDeadlineCalculatorV1"/>; an obligation without an entry (or a company without any calendar version, the
/// shipped state until the financial advisor confirms the values) is "undefined" and has no counter. Today is the business
/// date in Turkey (<c>Europe/Istanbul</c>); a time zone that cannot be resolved is unavailable, never a guessed offset.
/// Defined items come first by due date, then undefined ones in catalogue order. Nothing is written.
/// </summary>
internal sealed class SettingsLegalDeadlineWorkSourceV1(
    ICompanyContext companyContext,
    ICurrentUser currentUser,
    IErpAuthorityRevalidationService authority,
    ISettingsLegalDeadlineCalendarProviderV1 calendars,
    TimeProvider timeProvider) : IWorkDeadlineSourceV1
{
    public const string ModuleName = "settings";
    public const string KindName = "settings.legal-deadline.v1";

    /// <summary>The calendar definition screen (<c>/ayarlar/yasal-sure-takvimi</c>).</summary>
    public const string CalendarScreenPath = "/ayarlar/yasal-sure-takvimi";

    private const string BusinessTimeZoneId = "Europe/Istanbul";

    public string Module => ModuleName;

    public string Kind => KindName;

    public async Task<WorkDeadlineSourceResultV1> ListUpcomingAsync(WorkDeadlineSourceContextV1 context, int maxCount,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(context);
        if (maxCount < 1) throw new ArgumentOutOfRangeException(nameof(maxCount));
        if (await CheckAsync(context, cancellationToken) is { } refused) return refused;
        SettingsLegalDeadlineCalendarV1 calendar;
        DateOnly today;
        try
        {
            calendar = await calendars.GetLegalDeadlineCalendarAsync(context.CompanyId, cancellationToken);
            var zone = TimeZoneInfo.FindSystemTimeZoneById(BusinessTimeZoneId);
            today = DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(timeProvider.GetUtcNow(), zone).DateTime);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception)
        {
            return WorkDeadlineSourceResultV1.Unavailable();
        }

        if (await CheckAsync(context, cancellationToken) is { } refusedAfter) return refusedAfter;
        if (calendar.CompanyId != context.CompanyId) return WorkDeadlineSourceResultV1.Unavailable();
        IReadOnlyList<LegalDeadlineCounterV1> counters;
        try { counters = LegalDeadlineCalculatorV1.Evaluate(calendar, today); }
        catch (ArgumentException) { return WorkDeadlineSourceResultV1.Unavailable(); }
        catch (InvalidOperationException) { return WorkDeadlineSourceResultV1.Unavailable(); }

        var version = calendar.IsDefined ? calendar.VersionText : null;
        var ordered = counters
            .OrderBy(counter => counter.State == LegalDeadlineCounterStateV1.Defined ? 0 : 1)
            .ThenBy(counter => counter.DueDate ?? DateOnly.MaxValue)
            .ThenBy(counter => counter.Obligation)
            .Select(counter => new WorkDeadlineItemV1(ModuleName, KindName, Code(counter.Obligation),
                counter.State == LegalDeadlineCounterStateV1.Defined ? WorkDeadlineStatesV1.Defined : WorkDeadlineStatesV1.Undefined,
                counter.PeriodStart, counter.PeriodEnd, counter.DueDate, counter.DaysRemaining,
                counter.State == LegalDeadlineCounterStateV1.Defined ? version : null, CalendarScreenPath))
            .ToArray();
        return WorkDeadlineSourceResultV1.Found(ordered.Take(maxCount).ToArray(), ordered.Length > maxCount);
    }

    /// <summary><c>settings.legal-deadline.{obligation}</c> in kebab case, e.g. <c>settings.legal-deadline.vat-return</c>.</summary>
    public static string Code(LegalDeadlineObligationV1 obligation)
    {
        var name = obligation.ToString();
        var builder = new System.Text.StringBuilder("settings.legal-deadline.", 48);
        for (var index = 0; index < name.Length; index++)
        {
            var character = name[index];
            if (char.IsUpper(character) && index > 0 && (char.IsLower(name[index - 1]) || index + 1 < name.Length && char.IsLower(name[index + 1])))
                builder.Append('-');
            builder.Append(char.ToLower(character, CultureInfo.InvariantCulture));
        }

        return builder.ToString();
    }

    private async Task<WorkDeadlineSourceResultV1?> CheckAsync(WorkDeadlineSourceContextV1 context, CancellationToken cancellationToken)
    {
        if (!SameContext(context)) return WorkDeadlineSourceResultV1.Denied();
        if (!ErpPermissions.IsKnown(SettingsLegalDeadlinePermissionsV1.DeadlinesRead)) return WorkDeadlineSourceResultV1.Unavailable();
        try
        {
            var scope = await authority.RevalidatePermissionScopeAsync(SettingsLegalDeadlinePermissionsV1.DeadlinesRead,
                context.CompanyId, cancellationToken);
            return scope is { IsGranted: true, IsCompanyWide: true } && SameContext(context) ? null : WorkDeadlineSourceResultV1.Denied();
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception)
        {
            return WorkDeadlineSourceResultV1.Unavailable();
        }
    }

    private bool SameContext(WorkDeadlineSourceContextV1 context) =>
        context.CompanyId != Guid.Empty
        && !string.IsNullOrWhiteSpace(context.UserId)
        && companyContext.IsResolved
        && companyContext.CompanyId == context.CompanyId
        && currentUser.IsAuthenticated
        && string.Equals(currentUser.UserId, context.UserId, StringComparison.Ordinal)
        && currentUser.CanAccessCompany(context.CompanyId);
}
