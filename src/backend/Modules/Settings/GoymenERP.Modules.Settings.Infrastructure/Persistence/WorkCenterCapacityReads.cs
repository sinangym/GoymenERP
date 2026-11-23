using Microsoft.EntityFrameworkCore;

namespace GoymenERP.Modules.Settings.Infrastructure.Persistence;

/// <summary>
/// The reads the work center capacity provider and administration share. Every query runs under the company query filter, so
/// only the server-resolved company's rows are ever seen.
/// </summary>
internal static class WorkCenterCapacityReads
{
    /// <summary>The company's every work center history, by work center id and newest first within one work center.</summary>
    public static Task<WorkCenterCapacityVersionRow[]> LoadAsync(SettingsDbContext database,
        CancellationToken cancellationToken) =>
        database.WorkCenterCapacityVersions.AsNoTracking()
            .OrderBy(row => row.WorkCenterId)
            .ThenByDescending(row => row.Version)
            .ToArrayAsync(cancellationToken);

    /// <summary>The latest version of one work center (its capacity in force), or null when none is recorded.</summary>
    public static Task<WorkCenterCapacityVersionRow?> LatestAsync(SettingsDbContext database, Guid workCenterId,
        CancellationToken cancellationToken) =>
        database.WorkCenterCapacityVersions.AsNoTracking()
            .Where(row => row.WorkCenterId == workCenterId)
            .OrderByDescending(row => row.Version)
            .FirstOrDefaultAsync(cancellationToken);
}
