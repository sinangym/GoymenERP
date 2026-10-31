using GoymenERP.Modules.Settings.Application.Contracts.V1;

namespace GoymenERP.Modules.Settings.Application;

/// <summary>One entry of a reviewed loss reason list version (SAT-D25).</summary>
public sealed record ApprovedSalesQuoteLossReasonEntry(
    Guid ReasonId,
    string Code,
    string Label,
    bool RequiresExplanation,
    bool IsActive);

/// <summary>One reviewed loss reason list version of a company (SAT-D25).</summary>
public sealed record ApprovedSalesQuoteLossReasonListEntry(
    Guid CompanyId,
    Guid ProfileId,
    long Version,
    IReadOnlyList<ApprovedSalesQuoteLossReasonEntry> Reasons,
    bool IsActive,
    DateOnly EffectiveFrom,
    DateOnly? EffectiveThrough,
    string Source);

/// <summary>One reviewed default quote validity version of a company (SAT-D39).</summary>
public sealed record ApprovedSalesQuoteDefaultValidityEntry(
    Guid CompanyId,
    Guid ProfileId,
    long Version,
    int ValidityDays,
    bool IsActive,
    DateOnly EffectiveFrom,
    DateOnly? EffectiveThrough,
    string Source);

/// <summary>
/// The approved Sales QUOTE policies (SAT-D25, SAT-D39) as versioned, reviewed source data — the shape and the
/// reasons of <see cref="ApprovedSalesPolicyCatalog"/>: nothing edits a policy at runtime, a new approval is a new
/// version reviewed like code, and there is no migration, seed write or DbContext. When the Sales policies become
/// user-maintained, a persistent provider replaces this class behind <see cref="ISettingsSalesQuotePolicyProviderV1"/>.
/// <para>
/// <b>Deliberately empty.</b> SAT-D25 makes the loss reason list a Settings record and SAT-D39 the default validity
/// day count a Settings record that Sinan enters (EK4 "açık kalanlar"); neither content is decided, so no version is
/// listed and nothing is seeded. The companies with an approved Sales policy source (GDC, GSP, GIN — SAT-D14) are
/// served and answer <see cref="SettingsPrerequisiteCodesV1.SalesPolicyNotFound"/> for both families: a loss is then
/// refused and no validity is suggested.
/// </para>
/// <para>
/// A malformed catalog is refused as a whole with <c>settings_profile_invalid</c>: an empty identity, a blank source,
/// a non-positive version, a window that ends before it starts, a repeated version or overlapping windows within one
/// company, a list without entries, an entry with an empty id or a blank code/label (or one longer than the stored
/// snapshot allows), a repeated reason id or code within a version, or a non-positive day count. Nothing is repaired.
/// </para>
/// <para>
/// Technical ids are UUIDv5 in namespace <c>eb68fb11-2146-5285-9cc5-6ce5f3981184</c> (the Sales policy namespace),
/// assigned once, never reused.
/// </para>
/// </summary>
public sealed class ApprovedSalesQuotePolicyCatalog
{
    /// <summary>The longest stable reason code a quote records (its snapshot column).</summary>
    public const int MaximumReasonCodeLength = 64;

    /// <summary>The longest reason label a quote records (its snapshot column).</summary>
    public const int MaximumReasonLabelLength = 200;

    /// <summary>
    /// The reviewed catalog: the SAT-D14 companies are served; no loss reason list and no default validity is approved
    /// (SAT-D25 / SAT-D39 values are Settings records not entered yet).
    /// </summary>
    public static ApprovedSalesQuotePolicyCatalog Approved { get; } = new(
        [ApprovedUnitProfileCatalog.GdcCompanyId, ApprovedUnitProfileCatalog.GspCompanyId, ApprovedUnitProfileCatalog.GinCompanyId],
        [],
        []);

    private readonly IReadOnlyDictionary<Guid, ApprovedSalesQuoteLossReasonListEntry[]> _lossReasons;
    private readonly IReadOnlyDictionary<Guid, ApprovedSalesQuoteDefaultValidityEntry[]> _validities;
    private readonly IReadOnlySet<Guid> _companies;

    /// <summary>Internal on purpose, as the other catalogs': only Settings' own tests build another one.</summary>
    internal ApprovedSalesQuotePolicyCatalog(
        IEnumerable<Guid> servedCompanies,
        IEnumerable<ApprovedSalesQuoteLossReasonListEntry> lossReasons,
        IEnumerable<ApprovedSalesQuoteDefaultValidityEntry> validities)
    {
        ArgumentNullException.ThrowIfNull(servedCompanies);
        ArgumentNullException.ThrowIfNull(lossReasons);
        ArgumentNullException.ThrowIfNull(validities);
        var companies = servedCompanies.ToArray();
        var lists = lossReasons.ToArray();
        var days = validities.ToArray();
        if (companies.Any(company => company == Guid.Empty)
            || lists.Any(entry => entry is null
                || !IsWellFormed(entry.CompanyId, entry.ProfileId, entry.Version, entry.EffectiveFrom, entry.EffectiveThrough, entry.Source)
                || !HasWellFormedReasons(entry.Reasons))
            || days.Any(entry => entry is null
                || !IsWellFormed(entry.CompanyId, entry.ProfileId, entry.Version, entry.EffectiveFrom, entry.EffectiveThrough, entry.Source)
                || entry.ValidityDays <= 0))
        {
            throw Invalid();
        }

        _lossReasons = Versions(lists, entry => entry.CompanyId, entry => entry.Version, entry => entry.EffectiveFrom,
            entry => entry.EffectiveThrough);
        _validities = Versions(days, entry => entry.CompanyId, entry => entry.Version, entry => entry.EffectiveFrom,
            entry => entry.EffectiveThrough);
        _companies = companies.Concat(lists.Select(entry => entry.CompanyId)).Concat(days.Select(entry => entry.CompanyId)).ToHashSet();
    }

    /// <summary>Whether the company has an approved Sales policy source at all.</summary>
    public bool Serves(Guid companyId) => _companies.Contains(companyId);

    /// <summary>
    /// The list version in force on the date, or — outside every window — the nearest earlier version (or the first one)
    /// with its REAL window, so the consumer's own check rejects it. Null when the company has no list.
    /// </summary>
    public ApprovedSalesQuoteLossReasonListEntry? LossReasonsFor(Guid companyId, DateOnly effectiveOn) =>
        _lossReasons.TryGetValue(companyId, out var ordered) ? InForce(ordered, effectiveOn, entry => entry.EffectiveFrom) : null;

    /// <summary>As <see cref="LossReasonsFor"/>, for the default validity; null when the company has none.</summary>
    public ApprovedSalesQuoteDefaultValidityEntry? DefaultValidityFor(Guid companyId, DateOnly effectiveOn) =>
        _validities.TryGetValue(companyId, out var ordered) ? InForce(ordered, effectiveOn, entry => entry.EffectiveFrom) : null;

    private static bool HasWellFormedReasons(IReadOnlyList<ApprovedSalesQuoteLossReasonEntry>? reasons)
    {
        if (reasons is null || reasons.Count == 0
            || reasons.Any(reason => reason is null || reason.ReasonId == Guid.Empty
                || string.IsNullOrWhiteSpace(reason.Code) || reason.Code.Length > MaximumReasonCodeLength
                || reason.Code != reason.Code.Trim()
                || string.IsNullOrWhiteSpace(reason.Label) || reason.Label.Length > MaximumReasonLabelLength
                || reason.Label != reason.Label.Trim()))
        {
            return false;
        }

        return reasons.Select(reason => reason.ReasonId).Distinct().Count() == reasons.Count
            && reasons.Select(reason => reason.Code).Distinct(StringComparer.Ordinal).Count() == reasons.Count;
    }

    private static bool IsWellFormed(Guid companyId, Guid profileId, long version, DateOnly from, DateOnly? through, string? source) =>
        companyId != Guid.Empty && profileId != Guid.Empty && version > 0
        && (through is not DateOnly end || end >= from)
        && !string.IsNullOrWhiteSpace(source);

    private static IReadOnlyDictionary<TKey, TEntry[]> Versions<TKey, TEntry>(IEnumerable<TEntry> entries, Func<TEntry, TKey> key,
        Func<TEntry, long> version, Func<TEntry, DateOnly> from, Func<TEntry, DateOnly?> through)
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
                if (through(ordered[index - 1]) is not DateOnly previousEnd || previousEnd >= from(ordered[index]))
                {
                    throw Invalid();
                }
            }

            versions[family.Key] = ordered;
        }

        return versions;
    }

    private static TEntry InForce<TEntry>(TEntry[] ordered, DateOnly effectiveOn, Func<TEntry, DateOnly> from)
        where TEntry : class =>
        ordered.LastOrDefault(entry => from(entry) <= effectiveOn) ?? ordered[0];

    private static SettingsPrerequisiteException Invalid() => new("settings_profile_invalid");
}
