using GoymenERP.Modules.Settings.Application.Contracts.V1;

namespace GoymenERP.Modules.Settings.Application;

/// <summary>One approved, immutable money-profile version, the operations it answers and the decision it comes from.</summary>
public sealed record ApprovedMoneyProfileEntry(
    Guid CompanyId,
    Guid CurrencyId,
    string CurrencyCode,
    IReadOnlySet<string> Operations,
    Guid ProfileId,
    long Version,
    int Scale,
    bool IsActive,
    DateOnly EffectiveFrom,
    DateOnly? EffectiveThrough,
    string Source);

/// <summary>
/// The approved money profiles, as versioned, reviewed source data (T02-b), in the same shape and
/// for the same reasons as <see cref="ApprovedUnitProfileCatalog"/>: nothing may edit a profile at
/// runtime, a new approval is a new version reviewed like code, and there is no migration, seed
/// write, DbContext or host change.
/// <para>
/// <b>Operations are the owners' keys, not Settings'.</b> A money profile answers only for the
/// operations listed on it: the consuming module's technical mapping (Business #254 5654539215),
/// published by its owner — Finance <c>finance.manual-bank-receipt.v1</c>, Sales
/// <c>sales.order-draft.v1</c>. Settings references neither module; an integration test pins these
/// literals to the owners' constants.
/// </para>
/// <para>
/// A malformed catalog is refused as a whole with <c>settings_profile_invalid</c>: an empty
/// identity, a blank currency code or source, no operation or a blank one, a negative scale, a
/// non-positive version, a window that ends before it starts, a repeated (company, currency,
/// version) or two versions of one currency whose windows overlap. Nothing is repaired or skipped.
/// </para>
/// </summary>
public sealed class ApprovedMoneyProfileCatalog
{
    /// <summary>
    /// GDC/TRY, version 1. Values and sources:
    /// <list type="bullet">
    /// <item>company GDC and currency TRY: Ö2 "ilk kabul profili GDC/TRY", Sinan's decisions of
    /// 2026-09-18 (#66 5722637822);</item>
    /// <item>scale 2: Ö2 "TL/USD/EUR nihai tutar 2" — the final amount. Ö2's "birim fiyat
    /// gerektiğinde 6 ondalığa kadar" has no field in <see cref="MoneyProfileV1"/> and is NOT
    /// published here; a priced write needs its own contract field (T28), not a guessed one;</item>
    /// <item>effective from 2026-09-18 with no end: Ö2, "profil geçerliliği açık onaydan itibaren
    /// sürümlü ve bitişsizse açıkça süresiz";</item>
    /// <item>operations: the Finance and Sales owners' published keys (see the class remarks);</item>
    /// <item>profile id and version: technical, the implementer's work (Ö2); UUIDv5 name
    /// "money-profile:10000000-0000-0000-0000-000000000001:TRY:v1" in the namespace documented on
    /// <see cref="SettingsCurrencyIdentitiesV1"/>.</item>
    /// </list>
    /// No rounding method, exchange rate or tax policy is implied: Ö2 leaves them open.
    /// </summary>
    public static readonly ApprovedMoneyProfileEntry GdcTurkishLiraV1 = new(
        ApprovedUnitProfileCatalog.GdcCompanyId,
        SettingsCurrencyIdentitiesV1.TurkishLira,
        CurrencyCode: "TRY",
        Operations: new HashSet<string>(StringComparer.Ordinal)
        {
            "finance.manual-bank-receipt.v1",
            "sales.order-draft.v1",
        },
        ProfileId: Guid.Parse("7f836492-a54f-512f-a8f4-5f556b3f0f9d"),
        Version: 1,
        Scale: 2,
        IsActive: true,
        EffectiveFrom: new DateOnly(2026, 9, 18),
        EffectiveThrough: null,
        Source: "Sinan Ö2 2026-09-18 (#66 5722637822); operation keys: Finance/Sales owners (Business #254 5654539215)");

    public static ApprovedMoneyProfileCatalog Approved { get; } = new([GdcTurkishLiraV1]);

    private readonly IReadOnlyDictionary<(Guid CompanyId, Guid CurrencyId), ApprovedMoneyProfileEntry[]> _versions;
    private readonly IReadOnlySet<Guid> _companies;
    private readonly IReadOnlySet<string> _operations;

    /// <summary>Internal on purpose, as the unit catalog's: only Settings' own tests build another one.</summary>
    internal ApprovedMoneyProfileCatalog(IEnumerable<ApprovedMoneyProfileEntry> entries)
    {
        ArgumentNullException.ThrowIfNull(entries);
        var all = entries.ToArray();
        if (all.Any(entry => entry is null
                || entry.CompanyId == Guid.Empty || entry.CurrencyId == Guid.Empty || entry.ProfileId == Guid.Empty
                || string.IsNullOrWhiteSpace(entry.CurrencyCode) || string.IsNullOrWhiteSpace(entry.Source)
                || entry.Operations is null || entry.Operations.Count == 0
                || entry.Operations.Any(string.IsNullOrWhiteSpace)
                || entry.Scale < 0 || entry.Version <= 0
                || entry.EffectiveThrough is DateOnly through && through < entry.EffectiveFrom))
        {
            throw Invalid();
        }

        var versions = new Dictionary<(Guid, Guid), ApprovedMoneyProfileEntry[]>();
        foreach (var currency in all.GroupBy(entry => (entry.CompanyId, entry.CurrencyId)))
        {
            var ordered = currency.OrderBy(entry => entry.EffectiveFrom).ToArray();
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

            versions[currency.Key] = ordered;
        }

        _versions = versions;
        _companies = all.Select(entry => entry.CompanyId).ToHashSet();
        _operations = all.SelectMany(entry => entry.Operations).ToHashSet(StringComparer.Ordinal);
    }

    public IReadOnlyCollection<ApprovedMoneyProfileEntry> Entries =>
        _versions.Values.SelectMany(currency => currency).ToArray();

    /// <summary>Whether any money profile is approved for the company at all.</summary>
    public bool Serves(Guid companyId) => _companies.Contains(companyId);

    /// <summary>Whether the operation key is one any approved profile answers for (exact, ordinal).</summary>
    public bool Approves(string? operation) => operation is not null && _operations.Contains(operation);

    /// <summary>
    /// The company's currencies that have an approved profile, one candidate each (any version),
    /// in catalog order. The display label is the approved currency code itself: no approved
    /// source names a longer label, so none is invented. Empty for a company with no profile.
    /// </summary>
    public IReadOnlyList<MoneyCurrencyCandidateV1> CurrenciesOf(Guid companyId) =>
        _versions
            .Where(currency => currency.Key.CompanyId == companyId)
            .Select(currency => currency.Value[^1])
            .Select(entry => new MoneyCurrencyCandidateV1(entry.CurrencyId, entry.CurrencyCode, entry.CurrencyCode))
            .ToArray();

    /// <summary>
    /// The version in force on <paramref name="effectiveOn"/>, or — outside every window — the
    /// nearest version before it (or the first one), returned with its REAL window so the
    /// consumer's own check rejects it. Null only when the company has no profile for the currency.
    /// </summary>
    // Not named after EF's primary-key lookup, for the same guard reason as the unit catalog.
    public ApprovedMoneyProfileEntry? VersionFor(Guid companyId, Guid currencyId, DateOnly effectiveOn)
    {
        if (!_versions.TryGetValue((companyId, currencyId), out var ordered))
        {
            return null;
        }

        return ordered.LastOrDefault(entry => entry.EffectiveFrom <= effectiveOn) ?? ordered[0];
    }

    private static SettingsPrerequisiteException Invalid() => new("settings_profile_invalid");
}
