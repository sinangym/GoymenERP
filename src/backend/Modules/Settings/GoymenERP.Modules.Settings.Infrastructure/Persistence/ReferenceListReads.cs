using Microsoft.EntityFrameworkCore;

namespace GoymenERP.Modules.Settings.Infrastructure.Persistence;

/// <summary>
/// The reads the reference list administration and reader share. Every query runs under the company query filter, so only
/// the server-resolved company's rows are ever seen. A snapshot whose entry has no version is malformed data; the caller
/// decides how that is answered.
/// </summary>
internal static class ReferenceListReads
{
    /// <summary>Every entry of one list with its latest version, ordered by code (ordinal).</summary>
    public static async Task<ReferenceListEntrySnapshot[]> LoadAsync(SettingsDbContext database, string kindCode,
        CancellationToken cancellationToken)
    {
        var entries = await database.ReferenceListEntries.AsNoTracking()
            .Where(row => row.Kind == kindCode)
            .ToArrayAsync(cancellationToken);
        if (entries.Length == 0)
        {
            return [];
        }

        var versions = await database.ReferenceListEntryVersions.AsNoTracking()
            .Join(database.ReferenceListEntries.AsNoTracking().Where(entry => entry.Kind == kindCode),
                version => new { version.CompanyId, version.EntryId },
                entry => new { entry.CompanyId, entry.EntryId },
                (version, _) => version)
            .ToArrayAsync(cancellationToken);
        var byEntry = versions
            .GroupBy(version => version.EntryId)
            .ToDictionary(group => group.Key, group => group.MaxBy(version => version.Version)!);
        return entries
            .OrderBy(entry => entry.Code, StringComparer.Ordinal)
            .Select(entry => new ReferenceListEntrySnapshot(entry, byEntry.GetValueOrDefault(entry.EntryId)))
            .ToArray();
    }

    /// <summary>One entry of one list with its latest version, or null when the list has no such entry.</summary>
    public static async Task<ReferenceListEntrySnapshot?> FindAsync(SettingsDbContext database, string kindCode, Guid entryId,
        CancellationToken cancellationToken)
    {
        var entry = await database.ReferenceListEntries.AsNoTracking()
            .SingleOrDefaultAsync(row => row.EntryId == entryId && row.Kind == kindCode, cancellationToken);
        if (entry is null)
        {
            return null;
        }

        var latest = await database.ReferenceListEntryVersions.AsNoTracking()
            .Where(version => version.EntryId == entryId)
            .OrderByDescending(version => version.Version)
            .FirstOrDefaultAsync(cancellationToken);
        return new ReferenceListEntrySnapshot(entry, latest);
    }

    /// <summary>Whether the list already holds the code (exact, binary comparison in the store).</summary>
    public static Task<bool> CodeExistsAsync(SettingsDbContext database, string kindCode, string code,
        CancellationToken cancellationToken) =>
        database.ReferenceListEntries.AsNoTracking()
            .AnyAsync(row => row.Kind == kindCode && row.Code == code, cancellationToken);
}
