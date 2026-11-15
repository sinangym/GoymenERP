using Microsoft.EntityFrameworkCore;

namespace GoymenERP.Modules.Settings.Infrastructure.Persistence;

/// <summary>
/// The reads the login attempt limit provider and administration share. The record is group-wide: there is no company filter
/// on its table, and nothing else is read through these queries.
/// </summary>
internal static class LoginRateLimitReads
{
    /// <summary>Every version, newest first.</summary>
    public static Task<LoginRateLimitVersionRow[]> LoadAsync(SettingsDbContext database, CancellationToken cancellationToken) =>
        database.LoginRateLimitVersions.AsNoTracking().OrderByDescending(row => row.Version).ToArrayAsync(cancellationToken);

    /// <summary>The latest version, or null when none is recorded.</summary>
    public static Task<LoginRateLimitVersionRow?> LatestAsync(SettingsDbContext database, CancellationToken cancellationToken) =>
        database.LoginRateLimitVersions.AsNoTracking().OrderByDescending(row => row.Version)
            .FirstOrDefaultAsync(cancellationToken);
}
