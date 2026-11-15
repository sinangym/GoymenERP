using Microsoft.EntityFrameworkCore;

namespace GoymenERP.Modules.Settings.Infrastructure.Persistence;

/// <summary>
/// The reads the segregation of duties provider and administration share. Every query runs under the company query filter,
/// so only the server-resolved company's rows are ever seen.
/// </summary>
internal static class SegregationOfDutiesPolicyReads
{
    /// <summary>The company's history, newest first.</summary>
    public static Task<SegregationOfDutiesPolicyVersionRow[]> LoadAsync(SettingsDbContext database,
        CancellationToken cancellationToken) =>
        database.SegregationOfDutiesPolicyVersions.AsNoTracking().OrderByDescending(row => row.Version)
            .ToArrayAsync(cancellationToken);

    /// <summary>The latest version (the setting in force), or null when none is recorded.</summary>
    public static Task<SegregationOfDutiesPolicyVersionRow?> LatestAsync(SettingsDbContext database,
        CancellationToken cancellationToken) =>
        database.SegregationOfDutiesPolicyVersions.AsNoTracking().OrderByDescending(row => row.Version)
            .FirstOrDefaultAsync(cancellationToken);
}
