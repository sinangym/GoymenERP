using GoymenERP.Modules.Settings.Application.Contracts.V1;

namespace GoymenERP.Modules.Settings.Application;

/// <summary>One approved, immutable unit-profile version and the decision it comes from.</summary>
public sealed record ApprovedUnitProfileEntry(
    Guid CompanyId,
    Guid UnitId,
    string UnitCode,
    string DimensionCode,
    bool IsKilogram,
    Guid ProfileId,
    long Version,
    int Scale,
    bool IsActive,
    DateOnly EffectiveFrom,
    DateOnly? EffectiveThrough,
    string Source);

/// <summary>
/// The approved unit profiles, as versioned, reviewed source data (T02-a).
/// <para>
/// <b>Why source data and not a table:</b> one profile is approved today and nothing may edit it
/// at runtime — a new approval is a new version, reviewed like code. Keeping it here means no
/// migration, no seed write, no DbContext and no host change, and every value is visible in the
/// diff that introduces it. When profiles become user-maintained, a persistent provider replaces
/// this class behind the same port.
/// </para>
/// <para>
/// A malformed catalog is refused as a whole with <c>settings_profile_invalid</c>: an empty
/// identity, blank code or dimension, a negative scale, a non-positive version, a window that
/// ends before it starts, a repeated (company, unit, version) or two versions of one unit whose
/// windows overlap. Nothing is repaired or skipped.
/// </para>
/// </summary>
public sealed class ApprovedUnitProfileCatalog
{
    /// <summary>GDC, Göymen Demir Çelik — the permanent company identity pinned by the
    /// Organization seed and its <c>AddCompanyFiscalRuntimeSeed</c> migration guard.</summary>
    public static readonly Guid GdcCompanyId = Guid.Parse("10000000-0000-0000-0000-000000000001");

    /// <summary>
    /// GDC kg, version 1. Values and sources:
    /// <list type="bullet">
    /// <item>company GDC and unit kg: Ö1 (GDC band/coil, kg→kg) and Ö2 "first acceptance
    /// profile GDC/TRY", Sinan's decisions of 2026-09-18 (#66 5722637822; #275
    /// <c>docs/decisions/SINAN_KARARLARI_2026-09-18_O1_O8_VE_SON_ASAMA.md</c>);</item>
    /// <item>dimension <c>weight</c>: Master §28.1, "Boyut: Ağırlık → iç birim: kg";</item>
    /// <item>scale 3: Ö2, "kg ... en fazla 3 ondalık";</item>
    /// <item>effective from 2026-09-18 with no end: Ö2, "profil onaydan itibaren sürümlü,
    /// bitişsizse açıkça süresiz" — the approval is that decision's date;</item>
    /// <item>profile id and version: technical, the implementer's work (Ö2); UUIDv5 name
    /// "unit-profile:10000000-0000-0000-0000-000000000001:kg:v1" in the namespace documented on
    /// <see cref="SettingsUnitIdentitiesV1"/>.</item>
    /// </list>
    /// No rounding method is implied: Ö2 leaves the final commercial rounding open.
    /// </summary>
    public static readonly ApprovedUnitProfileEntry GdcKilogramV1 = new(
        GdcCompanyId,
        SettingsUnitIdentitiesV1.Kilogram,
        UnitCode: "kg",
        DimensionCode: "weight",
        IsKilogram: true,
        ProfileId: Guid.Parse("4a790fdc-2a6b-5126-835f-d3a13d28b71c"),
        Version: 1,
        Scale: 3,
        IsActive: true,
        EffectiveFrom: new DateOnly(2026, 9, 18),
        EffectiveThrough: null,
        Source: "Sinan Ö1/Ö2 2026-09-18 (#66 5722637822); Master §28.1");

    public static ApprovedUnitProfileCatalog Approved { get; } = new([GdcKilogramV1]);

    private readonly IReadOnlyDictionary<(Guid CompanyId, Guid UnitId), ApprovedUnitProfileEntry[]> _versions;
    private readonly IReadOnlySet<Guid> _companies;

    public ApprovedUnitProfileCatalog(IEnumerable<ApprovedUnitProfileEntry> entries)
    {
        ArgumentNullException.ThrowIfNull(entries);
        var all = entries.ToArray();
        if (all.Any(entry => entry is null
                || entry.CompanyId == Guid.Empty || entry.UnitId == Guid.Empty || entry.ProfileId == Guid.Empty
                || string.IsNullOrWhiteSpace(entry.UnitCode) || string.IsNullOrWhiteSpace(entry.DimensionCode)
                || string.IsNullOrWhiteSpace(entry.Source)
                || entry.Scale < 0 || entry.Version <= 0
                || entry.EffectiveThrough is DateOnly through && through < entry.EffectiveFrom))
        {
            throw Invalid();
        }

        var versions = new Dictionary<(Guid, Guid), ApprovedUnitProfileEntry[]>();
        foreach (var unit in all.GroupBy(entry => (entry.CompanyId, entry.UnitId)))
        {
            var ordered = unit.OrderBy(entry => entry.EffectiveFrom).ToArray();
            if (ordered.Select(entry => entry.Version).Distinct().Count() != ordered.Length)
            {
                throw Invalid();
            }

            for (var index = 1; index < ordered.Length; index++)
            {
                // The earlier version must close before the later one opens.
                if (ordered[index - 1].EffectiveThrough is not DateOnly previousEnd
                    || previousEnd >= ordered[index].EffectiveFrom)
                {
                    throw Invalid();
                }
            }

            versions[unit.Key] = ordered;
        }

        _versions = versions;
        _companies = all.Select(entry => entry.CompanyId).ToHashSet();
    }

    public IReadOnlyCollection<ApprovedUnitProfileEntry> Entries =>
        _versions.Values.SelectMany(unit => unit).ToArray();

    /// <summary>Whether any profile is approved for the company at all.</summary>
    public bool Serves(Guid companyId) => _companies.Contains(companyId);

    /// <summary>
    /// The version in force on <paramref name="effectiveOn"/>, or — when the date falls outside
    /// every window — the version nearest before it (or the first one), returned with its REAL
    /// window so the consumer's own date check rejects it. Null only when the company has no
    /// profile for the unit.
    /// </summary>
    public ApprovedUnitProfileEntry? Find(Guid companyId, Guid unitId, DateOnly effectiveOn)
    {
        if (!_versions.TryGetValue((companyId, unitId), out var ordered))
        {
            return null;
        }

        return ordered.LastOrDefault(entry => entry.EffectiveFrom <= effectiveOn) ?? ordered[0];
    }

    private static SettingsPrerequisiteException Invalid() => new("settings_profile_invalid");
}
