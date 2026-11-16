using Microsoft.EntityFrameworkCore;

namespace GoymenERP.Modules.Settings.Infrastructure.Persistence;

/// <summary>
/// The reads the collateral type administration and reader share. Every query runs under the company query filter, so only
/// the server-resolved company's rows are ever seen. A snapshot whose entry has no version is malformed data; the caller
/// decides how that is answered.
/// </summary>
internal static class CollateralTypeReads
{
    /// <summary>Every entry of one list with its latest version, ordered by code (ordinal).</summary>
    public static async Task<CollateralTypeSnapshot[]> LoadAsync(SettingsDbContext database,
        CancellationToken cancellationToken)
    {
        var entries = await database.CollateralTypes.AsNoTracking()
            .ToArrayAsync(cancellationToken);
        if (entries.Length == 0)
        {
            return [];
        }

        var versions = await database.CollateralTypeVersions.AsNoTracking()
            .Join(database.CollateralTypes.AsNoTracking(),
                version => new { version.CompanyId, version.EntryId },
                entry => new { entry.CompanyId, entry.EntryId },
                (version, _) => version)
            .ToArrayAsync(cancellationToken);
        var byEntry = versions
            .GroupBy(version => version.EntryId)
            .ToDictionary(group => group.Key, group => group.MaxBy(version => version.Version)!);
        return entries
            .OrderBy(entry => entry.Code, StringComparer.Ordinal)
            .Select(entry => new CollateralTypeSnapshot(entry, byEntry.GetValueOrDefault(entry.EntryId)))
            .ToArray();
    }

    /// <summary>One entry of one list with its latest version, or null when the list has no such entry.</summary>
    public static async Task<CollateralTypeSnapshot?> FindAsync(SettingsDbContext database, Guid entryId,
        CancellationToken cancellationToken)
    {
        var entry = await database.CollateralTypes.AsNoTracking()
            .SingleOrDefaultAsync(row => row.EntryId == entryId, cancellationToken);
        if (entry is null)
        {
            return null;
        }

        var latest = await database.CollateralTypeVersions.AsNoTracking()
            .Where(version => version.EntryId == entryId)
            .OrderByDescending(version => version.Version)
            .FirstOrDefaultAsync(cancellationToken);
        return new CollateralTypeSnapshot(entry, latest);
    }

    /// <summary>Whether the list already holds the code (exact, binary comparison in the store).</summary>
    public static Task<bool> CodeExistsAsync(SettingsDbContext database, string code,
        CancellationToken cancellationToken) =>
        database.CollateralTypes.AsNoTracking()
            .AnyAsync(row => row.Code == code, cancellationToken);
}
