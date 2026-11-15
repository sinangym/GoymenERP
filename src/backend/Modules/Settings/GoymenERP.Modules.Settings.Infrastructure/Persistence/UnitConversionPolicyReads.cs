using GoymenERP.Modules.Settings.Application;
using Microsoft.EntityFrameworkCore;

namespace GoymenERP.Modules.Settings.Infrastructure.Persistence;

/// <summary>
/// The reads the unit conversion policy provider and administration share. Every query runs under the company query filter,
/// so only the server-resolved company's rows are ever seen.
/// </summary>
internal static class UnitConversionPolicyReads
{
    public static Task<UnitConversionPolicyVersionRow[]> LoadAsync(SettingsDbContext database,
        CancellationToken cancellationToken) =>
        database.UnitConversionPolicyVersions.AsNoTracking().OrderBy(row => row.Version).ToArrayAsync(cancellationToken);

    /// <summary>The company's timeline (the same window rule as every versioned Settings policy).</summary>
    public static IReadOnlyList<SalesPolicyTimelineEntryV1<UnitConversionPolicyVersionRow>> Timeline(
        IEnumerable<UnitConversionPolicyVersionRow> rows) =>
        SalesPolicyTimelineV1.Build(rows, row => row.Version, row => row.EffectiveFrom);
}
