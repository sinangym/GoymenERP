using GoymenERP.Modules.Settings.Application.Contracts.V1;

namespace GoymenERP.Modules.Settings.Application;

/// <summary>One reviewed Manufacturing tolerance profile version.</summary>
public sealed record ApprovedManufacturingToleranceEntry(
    Guid CompanyId,
    string Process,
    Guid ProfileId,
    long Version,
    decimal MaximumFirePercent,
    bool IsActive,
    DateOnly EffectiveFrom,
    DateOnly? EffectiveThrough,
    string Source);

/// <summary>
/// Settings-owned immutable Manufacturing tolerance source. No production value is approved yet,
/// so <see cref="Approved"/> is deliberately empty and yields settings_provider_unavailable.
/// Tests may construct synthetic entries through the internal seam; consumers cannot.
/// </summary>
public sealed class ApprovedManufacturingToleranceCatalog
{
    public static ApprovedManufacturingToleranceCatalog Approved { get; } = new([]);

    private readonly IReadOnlyDictionary<(Guid CompanyId, string Process), ApprovedManufacturingToleranceEntry[]> _versions;
    private readonly IReadOnlySet<Guid> _companies;

    internal ApprovedManufacturingToleranceCatalog(IEnumerable<ApprovedManufacturingToleranceEntry> entries)
    {
        ArgumentNullException.ThrowIfNull(entries);
        var all = entries.ToArray();
        if (all.Any(entry => entry is null
                || entry.CompanyId == Guid.Empty || entry.ProfileId == Guid.Empty
                || !SettingsManufacturingProcessesV1.IsCanonical(entry.Process)
                || entry.Version <= 0 || entry.MaximumFirePercent < 0m
                || entry.EffectiveThrough is DateOnly through && through < entry.EffectiveFrom
                || string.IsNullOrWhiteSpace(entry.Source)))
        {
            throw Invalid();
        }

        var versions = new Dictionary<(Guid, string), ApprovedManufacturingToleranceEntry[]>();
        foreach (var profile in all.GroupBy(entry => (entry.CompanyId, entry.Process)))
        {
            var ordered = profile.OrderBy(entry => entry.EffectiveFrom).ToArray();
            if (ordered.Select(entry => entry.Version).Distinct().Count() != ordered.Length)
            {
                throw Invalid();
            }

            for (var index = 1; index < ordered.Length; index++)
            {
                if (ordered[index - 1].EffectiveThrough is not DateOnly previousEnd
                    || previousEnd >= ordered[index].EffectiveFrom)
                {
                    throw Invalid();
                }
            }

            versions[profile.Key] = ordered;
        }

        _versions = versions;
        _companies = all.Select(entry => entry.CompanyId).ToHashSet();
    }

    public IReadOnlyCollection<ApprovedManufacturingToleranceEntry> Entries =>
        _versions.Values.SelectMany(profile => profile).ToArray();

    public bool Serves(Guid companyId) => _companies.Contains(companyId);

    /// <summary>
    /// Returns only the exact company/process profile family. Outside all windows, its nearest
    /// version is returned with the real window so Manufacturing can refuse it; another process
    /// or a fabricated default is never substituted.
    /// </summary>
    public ApprovedManufacturingToleranceEntry? VersionFor(Guid companyId, string process, DateOnly effectiveOn)
    {
        if (!_versions.TryGetValue((companyId, process), out var ordered))
        {
            return null;
        }

        return ordered.LastOrDefault(entry => entry.EffectiveFrom <= effectiveOn) ?? ordered[0];
    }

    private static SettingsPrerequisiteException Invalid() => new("settings_profile_invalid");
}
