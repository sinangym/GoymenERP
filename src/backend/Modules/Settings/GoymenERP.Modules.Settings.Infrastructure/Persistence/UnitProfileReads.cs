using GoymenERP.Modules.Settings.Application;
using Microsoft.EntityFrameworkCore;

namespace GoymenERP.Modules.Settings.Infrastructure.Persistence;

/// <summary>
/// The reads the unit profile provider and administration share. Every query runs under the company query filter, so only
/// the server-resolved company's rows are ever seen.
/// </summary>
internal static class UnitProfileReads
{
    public static Task<bool> IsTransferredAsync(SettingsDbContext database, CancellationToken cancellationToken) =>
        database.UnitProfileBaselineTransfers.AsNoTracking().AnyAsync(cancellationToken);

    public static Task<UnitProfileVersionRow[]> LoadAsync(SettingsDbContext database, CancellationToken cancellationToken) =>
        database.UnitProfileVersions.AsNoTracking()
            .OrderBy(row => row.UnitId)
            .ThenBy(row => row.Version)
            .ToArrayAsync(cancellationToken);

    public static Task<UnitProfileVersionRow[]> LoadUnitAsync(SettingsDbContext database, Guid unitId,
        CancellationToken cancellationToken) =>
        database.UnitProfileVersions.AsNoTracking()
            .Where(row => row.UnitId == unitId)
            .OrderBy(row => row.Version)
            .ToArrayAsync(cancellationToken);

    /// <summary>One unit's timeline (the same window rule as every versioned Settings policy).</summary>
    public static IReadOnlyList<SalesPolicyTimelineEntryV1<UnitProfileVersionRow>> Timeline(
        IEnumerable<UnitProfileVersionRow> rows) =>
        SalesPolicyTimelineV1.Build(rows, row => row.Version, row => row.EffectiveFrom);
}
