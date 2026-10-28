using GoymenERP.Modules.Settings.Application;
using Microsoft.EntityFrameworkCore;

namespace GoymenERP.Modules.Settings.Infrastructure.Persistence;

/// <summary>Reads of the discount limit store, always through the context's company filter.</summary>
internal static class SalesDiscountLimitReads
{
    /// <summary>Every group of the context's company with every limit record, keyed by group id, for the calculator.</summary>
    public static async Task<IReadOnlyDictionary<Guid, SalesDiscountLimitGroupDataV1>> LoadGroupDataAsync(
        SettingsDbContext database, CancellationToken cancellationToken)
    {
        var groups = await database.SalesDiscountLimitGroups.AsNoTracking().ToListAsync(cancellationToken);
        var versions = await database.SalesDiscountLimitVersions.AsNoTracking().ToListAsync(cancellationToken);
        var withdrawals = await database.SalesDiscountLimitWithdrawals.AsNoTracking().ToListAsync(cancellationToken);
        return ToGroupData(groups, versions, withdrawals);
    }

    /// <summary>
    /// The calculator's view of already-loaded rows (versions of unknown groups ignored; a version with a withdrawal row is
    /// withdrawn, SAT-D84).
    /// </summary>
    public static IReadOnlyDictionary<Guid, SalesDiscountLimitGroupDataV1> ToGroupData(
        IEnumerable<SalesDiscountLimitGroupRow> groups, IEnumerable<SalesDiscountLimitVersionRow> versions,
        IEnumerable<SalesDiscountLimitWithdrawalRow> withdrawals)
    {
        var withdrawn = withdrawals.Select(row => (row.LimitGroupId, row.LimitVersion)).ToHashSet();
        var byGroup = versions
            .GroupBy(version => version.LimitGroupId)
            .ToDictionary(grouping => grouping.Key,
                grouping => grouping.Select(row => ToData(row, withdrawn.Contains((row.LimitGroupId, row.Version)))).ToArray());
        return groups.ToDictionary(
            group => group.Id,
            group => new SalesDiscountLimitGroupDataV1(group.Id, group.IsActive,
                byGroup.TryGetValue(group.Id, out var records) ? records : []));
    }

    public static SalesDiscountLimitVersionDataV1 ToData(SalesDiscountLimitVersionRow row, bool isWithdrawn) =>
        new SalesDiscountLimitVersionDataV1(row.LimitGroupId, row.Version, row.ProfileId, row.Kind, row.Percent,
            row.LinkedGroupId, row.ExtraPoints, row.EffectiveFrom, isWithdrawn);
}

/// <summary>
/// SAT-D10: business dates are Europe/Istanbul days. A limit record takes effect from today's business date unless a later
/// one is given (SAT-D78); a date before today is refused.
/// </summary>
internal static class SettingsBusinessDate
{
    private const string TimeZoneId = "Europe/Istanbul";

    public static DateOnly Today(TimeProvider timeProvider)
    {
        ArgumentNullException.ThrowIfNull(timeProvider);
        var zone = TimeZoneInfo.FindSystemTimeZoneById(TimeZoneId);
        return DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(timeProvider.GetUtcNow(), zone).DateTime);
    }
}
