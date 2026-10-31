using GoymenERP.Modules.Settings.Application.Contracts.V1;

namespace GoymenERP.Modules.Settings.Application;

/// <summary>One reviewed version of a company's e-Fatura scenario code list (SAT-D102).</summary>
public sealed record ApprovedEInvoiceScenarioCodeListEntry(
    Guid CompanyId,
    Guid ProfileId,
    long Version,
    IReadOnlyList<string> Codes,
    DateOnly EffectiveFrom,
    DateOnly? EffectiveThrough,
    string Source);

/// <summary>One reviewed version of a company's default e-Fatura scenario (SAT-D102).</summary>
public sealed record ApprovedEInvoiceScenarioDefaultEntry(
    Guid CompanyId,
    Guid ProfileId,
    long Version,
    string ScenarioCode,
    DateOnly EffectiveFrom,
    DateOnly? EffectiveThrough,
    string Source);

/// <summary>
/// The approved e-Fatura scenario policy (SAT-D102), as versioned, reviewed source data in the same shape and for the
/// same reasons as <see cref="ApprovedSalesPolicyCatalog"/>: nothing edits it at runtime, a new value is a new version
/// reviewed like code, and there is no migration, seed write or DbContext. When the policy becomes user-maintained, a
/// persistent provider replaces this class behind <see cref="ISettingsEInvoiceScenarioPolicyProviderV1"/>.
/// <para>
/// SAT-D120 (Sinan, 2026-10-31, EK17): the scenario CODE LIST starts with the standard GİB/UBL-TR set —
/// <c>TEMELFATURA</c>, <c>TICARIFATURA</c>, <c>EARSIVFATURA</c> (e-Arşiv) and <c>IHRACAT</c> — as a reviewed,
/// effective-dated version for each of GDC, GSP and GIN (the codes are the ASCII UBL-TR ProfileID tokens; the Turkish
/// display spelling is the UI's). The scenario DEFAULT stays EMPTY (SAT-D102: Sinan enters it), so until a default is
/// added every company answers "not found" for the default and an invoice draft with no customer override must ask
/// for the scenario (fail-closed). A customer override must be a code of this list.
/// </para>
/// <para>
/// A malformed catalog is refused as a whole with <c>settings_profile_invalid</c>: an empty identity, a blank source, a
/// non-positive version, a window that ends before it starts, a repeated version or overlapping windows within one
/// company, an empty / repeated / malformed code (<see cref="EInvoiceScenarioCodeFormatV1"/>), or a default whose code
/// the same company's list in force on the default's first day does not carry. Nothing is repaired or skipped.
/// </para>
/// </summary>
public sealed class ApprovedEInvoiceScenarioCatalog
{
    /// <summary>
    /// SAT-D120 standard GİB/UBL-TR scenario codes. ASCII ProfileID tokens, never the Turkish display spelling.
    /// </summary>
    public static readonly IReadOnlyList<string> StandardScenarioCodes =
        ["TEMELFATURA", "TICARIFATURA", "EARSIVFATURA", "IHRACAT"];

    private const string ScenarioListSource = "SAT-D120 (SINAN_KARARLARI_2026-10-31_SATIS_EK17.md); UBL-TR standard set";

    /// <summary>GDC's standard code list, version 1 (SAT-D120). Profile id: UUIDv5 "einvoice-scenario-codes:&lt;GDC&gt;:v1".</summary>
    public static readonly ApprovedEInvoiceScenarioCodeListEntry GdcScenarioCodesV1 = new(
        ApprovedUnitProfileCatalog.GdcCompanyId,
        ProfileId: Guid.Parse("3ea8202c-5f84-55e9-aa45-8b752087bcd9"),
        Version: 1,
        Codes: StandardScenarioCodes,
        EffectiveFrom: new DateOnly(2026, 10, 31),
        EffectiveThrough: null,
        Source: ScenarioListSource);

    /// <summary>GSP's standard code list, version 1 (SAT-D120).</summary>
    public static readonly ApprovedEInvoiceScenarioCodeListEntry GspScenarioCodesV1 = GdcScenarioCodesV1 with
    {
        CompanyId = ApprovedUnitProfileCatalog.GspCompanyId,
        ProfileId = Guid.Parse("9441682d-a31b-50f3-acfd-9c982399e0b3"),
    };

    /// <summary>GIN's standard code list, version 1 (SAT-D120).</summary>
    public static readonly ApprovedEInvoiceScenarioCodeListEntry GinScenarioCodesV1 = GdcScenarioCodesV1 with
    {
        CompanyId = ApprovedUnitProfileCatalog.GinCompanyId,
        ProfileId = Guid.Parse("d8c0c747-6fb4-525f-a363-dc5c1fc3e199"),
    };

    /// <summary>
    /// The reviewed catalog (SAT-D120): the standard code list for GDC, GSP and GIN; the default is deliberately empty
    /// for every company (SAT-D102 — "Boş kalsın, ben gireyim").
    /// </summary>
    public static ApprovedEInvoiceScenarioCatalog Approved { get; } = new(
        [GdcScenarioCodesV1, GspScenarioCodesV1, GinScenarioCodesV1], []);

    private readonly IReadOnlyDictionary<Guid, ApprovedEInvoiceScenarioCodeListEntry[]> _lists;
    private readonly IReadOnlyDictionary<Guid, ApprovedEInvoiceScenarioDefaultEntry[]> _defaults;

    /// <summary>Internal on purpose, as the other catalogs': only Settings' own tests build another one.</summary>
    internal ApprovedEInvoiceScenarioCatalog(
        IEnumerable<ApprovedEInvoiceScenarioCodeListEntry> codeLists,
        IEnumerable<ApprovedEInvoiceScenarioDefaultEntry> defaults)
    {
        ArgumentNullException.ThrowIfNull(codeLists);
        ArgumentNullException.ThrowIfNull(defaults);
        var lists = codeLists.ToArray();
        var defaultEntries = defaults.ToArray();
        if (lists.Any(entry => entry is null
                || !IsWellFormed(entry.CompanyId, entry.ProfileId, entry.Version,
                    entry.EffectiveFrom, entry.EffectiveThrough, entry.Source)
                || entry.Codes is null || entry.Codes.Count == 0
                || entry.Codes.Any(code => !EInvoiceScenarioCodeFormatV1.IsWellFormed(code))
                || entry.Codes.Distinct(StringComparer.Ordinal).Count() != entry.Codes.Count)
            || defaultEntries.Any(entry => entry is null
                || !IsWellFormed(entry.CompanyId, entry.ProfileId, entry.Version,
                    entry.EffectiveFrom, entry.EffectiveThrough, entry.Source)
                || !EInvoiceScenarioCodeFormatV1.IsWellFormed(entry.ScenarioCode)))
        {
            throw Invalid();
        }

        _lists = Versions(lists, entry => entry.CompanyId, entry => entry.Version,
            entry => entry.EffectiveFrom, entry => entry.EffectiveThrough);
        _defaults = Versions(defaultEntries, entry => entry.CompanyId, entry => entry.Version,
            entry => entry.EffectiveFrom, entry => entry.EffectiveThrough);

        foreach (var entry in defaultEntries)
        {
            if (CodeListFor(entry.CompanyId, entry.EffectiveFrom) is not { } list
                || !list.Codes.Contains(entry.ScenarioCode, StringComparer.Ordinal))
            {
                throw Invalid();
            }
        }
    }

    public IReadOnlyCollection<ApprovedEInvoiceScenarioCodeListEntry> CodeLists =>
        _lists.Values.SelectMany(versions => versions).ToArray();

    public IReadOnlyCollection<ApprovedEInvoiceScenarioDefaultEntry> Defaults =>
        _defaults.Values.SelectMany(versions => versions).ToArray();

    /// <summary>The list version whose window covers the date; null when none does (including an empty key).</summary>
    public ApprovedEInvoiceScenarioCodeListEntry? CodeListFor(Guid companyId, DateOnly effectiveOn) =>
        _lists.TryGetValue(companyId, out var ordered)
            ? ordered.SingleOrDefault(entry => Covers(entry.EffectiveFrom, entry.EffectiveThrough, effectiveOn))
            : null;

    /// <summary>The default version whose window covers the date; null when none does (including an empty key).</summary>
    public ApprovedEInvoiceScenarioDefaultEntry? DefaultFor(Guid companyId, DateOnly effectiveOn) =>
        _defaults.TryGetValue(companyId, out var ordered)
            ? ordered.SingleOrDefault(entry => Covers(entry.EffectiveFrom, entry.EffectiveThrough, effectiveOn))
            : null;

    private static bool Covers(DateOnly from, DateOnly? through, DateOnly on) =>
        from <= on && (through is not DateOnly end || on <= end);

    private static bool IsWellFormed(Guid companyId, Guid profileId, long version,
        DateOnly from, DateOnly? through, string? source) =>
        companyId != Guid.Empty && profileId != Guid.Empty && version > 0
        && (through is not DateOnly end || end >= from)
        && !string.IsNullOrWhiteSpace(source);

    private static IReadOnlyDictionary<TKey, TEntry[]> Versions<TKey, TEntry>(
        IEnumerable<TEntry> entries,
        Func<TEntry, TKey> key,
        Func<TEntry, long> version,
        Func<TEntry, DateOnly> from,
        Func<TEntry, DateOnly?> through)
        where TKey : notnull
    {
        var versions = new Dictionary<TKey, TEntry[]>();
        foreach (var family in entries.GroupBy(key))
        {
            var ordered = family.OrderBy(from).ToArray();
            if (ordered.Select(version).Distinct().Count() != ordered.Length)
            {
                throw Invalid();
            }

            for (var index = 1; index < ordered.Length; index++)
            {
                if (through(ordered[index - 1]) is not DateOnly previousEnd
                    || previousEnd >= from(ordered[index]))
                {
                    throw Invalid();
                }
            }

            versions[family.Key] = ordered;
        }

        return versions;
    }

    private static SettingsPrerequisiteException Invalid() => new("settings_profile_invalid");
}
