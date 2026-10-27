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
        var groups = await database.SalesDiscountLimitGroups.AsNoTracking()
            .Select(group => new { group.Id, group.IsActive })
            .ToListAsync(cancellationToken);
        var versions = await database.SalesDiscountLimitVersions.AsNoTracking()
            .ToListAsync(cancellationToken);
        var byGroup = versions
            .GroupBy(version => version.LimitGroupId)
            .ToDictionary(grouping => grouping.Key, grouping => grouping.Select(ToData).ToArray());
        return groups.ToDictionary(
            group => group.Id,
            group => new SalesDiscountLimitGroupDataV1(group.Id, group.IsActive,
                byGroup.TryGetValue(group.Id, out var records) ? records : []));
    }

    public static SalesDiscountLimitVersionDataV1 ToData(SalesDiscountLimitVersionRow row) =>
        new SalesDiscountLimitVersionDataV1(row.LimitGroupId, row.Version, row.ProfileId, row.Kind, row.Percent, row.LinkedGroupId, row.ExtraPoints,
            row.EffectiveFrom);
}

/// <summary>
/// SAT-D10: business dates are Europe/Istanbul days. A limit record written now takes effect from today's business date
/// (reversible technical rule; EK10/EK11 leave "when a change takes effect" to the existing Settings versioning).
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
