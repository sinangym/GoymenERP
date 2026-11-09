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
/// SAT-D234 (Sinan, 2026-11-09) decides the content: the loss reason list starts with five standard reasons (Fiyat
/// yüksek, Termin uygun değil, Rakip seçildi, Müşteri vazgeçti, Diğer — "Diğer" requires an explanation, SAT-D25) and
/// the default quote validity is 15 days (SAT-D39: suggested on create, changeable, never mandatory). Each company with
/// an approved Sales policy source (GDC, GSP, GIN — SAT-D14) has its own version 1 of both, effective from the decision
/// date with no end. A served company without an entry would still answer
/// <see cref="SettingsPrerequisiteCodesV1.SalesPolicyNotFound"/>. Adding reasons from Settings (SAT-D234 (3)) needs a
/// persistent record and a maintenance surface; SAT-D209 defers it ("ertelendi: Ayarlar başlayınca"), so until then a
/// new reason is a new reviewed version here. The Turkish labels live in <see cref="ApprovedSalesQuoteLossReasonTexts"/>.
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

    private const string QuoteDecisions = "SAT-D234 (SINAN_KARARLARI_2026-11-09_SATIS_EK46.md); SAT-D14 (SINAN_KARARLARI_2026-10-26_SATIS_EK.md)";

    /// <summary>The SAT-D234 decision date: the first day every version 1 below is in force.</summary>
    private static readonly DateOnly DecisionDate = new(2026, 11, 9);

    /// <summary>
    /// GDC loss reason list, version 1: the five SAT-D234 reasons in the decided order. Profile id: UUIDv5 name
    /// "sales-quote-loss-reasons:10000000-0000-0000-0000-000000000001:v1"; reason ids: UUIDv5 name
    /// "sales-quote-loss-reason:10000000-0000-0000-0000-000000000001:&lt;code&gt;".
    /// </summary>
    public static readonly ApprovedSalesQuoteLossReasonListEntry GdcLossReasonsV1 = LossReasons(
        ApprovedUnitProfileCatalog.GdcCompanyId,
        Guid.Parse("ba9544f6-a1ab-52f2-accd-1ffa79cccdbc"),
        [
            Guid.Parse("b8831a89-d094-5265-99e8-46fba9b5dc01"), Guid.Parse("42d9f2e0-f053-5941-bd1c-c3e3cc3380bd"),
            Guid.Parse("caee8475-2475-5f13-a415-2faa48c85c8b"), Guid.Parse("f9801dc3-f9b7-53ce-b292-a448264aab74"),
            Guid.Parse("467a5707-70cf-5c7a-937e-216551c6c062"),
        ]);

    /// <summary>GSP loss reason list, version 1: as <see cref="GdcLossReasonsV1"/>, with its own ids (company …0002).</summary>
    public static readonly ApprovedSalesQuoteLossReasonListEntry GspLossReasonsV1 = LossReasons(
        ApprovedUnitProfileCatalog.GspCompanyId,
        Guid.Parse("1b63efc5-9786-51a7-bb35-f87dcae692b4"),
        [
            Guid.Parse("08910c05-c73e-557c-8a19-688ab4eea439"), Guid.Parse("a43e64e2-a468-5376-a5fe-7c66cd866453"),
            Guid.Parse("8d9698aa-f8cd-5ae8-a667-e7f7b4b1c564"), Guid.Parse("ed4a2b19-9010-5e03-a41b-51815291cfc8"),
            Guid.Parse("144df90d-6995-5df8-9171-c81001f822f4"),
        ]);

    /// <summary>GIN loss reason list, version 1: as <see cref="GdcLossReasonsV1"/>, with its own ids (company …0003).</summary>
    public static readonly ApprovedSalesQuoteLossReasonListEntry GinLossReasonsV1 = LossReasons(
        ApprovedUnitProfileCatalog.GinCompanyId,
        Guid.Parse("6faa722b-b59b-5bde-8af5-c690e8288d94"),
        [
            Guid.Parse("4454b3b2-7079-51f5-9368-f280a3316099"), Guid.Parse("1d7604a3-48b4-5b75-be96-a321ec1ed971"),
            Guid.Parse("a4768dd2-bb11-5cf8-a284-b526e4014a57"), Guid.Parse("0409df97-13e3-5898-bcee-31c1b078ff98"),
            Guid.Parse("00e7cff9-ee5a-54eb-aa90-0aaac1740d60"),
        ]);

    /// <summary>
    /// GDC default quote validity, version 1: 15 days (SAT-D234 (2), SAT-D39). Profile id: UUIDv5 name
    /// "sales-quote-default-validity:10000000-0000-0000-0000-000000000001:v1".
    /// </summary>
    public static readonly ApprovedSalesQuoteDefaultValidityEntry GdcDefaultValidityV1 = new(
        ApprovedUnitProfileCatalog.GdcCompanyId,
        ProfileId: Guid.Parse("eac8b8e7-53e9-52bf-9f3c-a89bd93d2dd5"),
        Version: 1,
        ValidityDays: 15,
        IsActive: true,
        EffectiveFrom: DecisionDate,
        EffectiveThrough: null,
        Source: QuoteDecisions + "; SAT-D39");

    /// <summary>GSP default quote validity, version 1: 15 days, as GDC's (company …0002).</summary>
    public static readonly ApprovedSalesQuoteDefaultValidityEntry GspDefaultValidityV1 = GdcDefaultValidityV1 with
    {
        CompanyId = ApprovedUnitProfileCatalog.GspCompanyId,
        ProfileId = Guid.Parse("5081c942-6eeb-563d-9c77-48cc1417e22d"),
    };

    /// <summary>GIN default quote validity, version 1: 15 days, as GDC's (company …0003).</summary>
    public static readonly ApprovedSalesQuoteDefaultValidityEntry GinDefaultValidityV1 = GdcDefaultValidityV1 with
    {
        CompanyId = ApprovedUnitProfileCatalog.GinCompanyId,
        ProfileId = Guid.Parse("a9a15bad-1a5b-5edd-ac0e-1d9028a3c50e"),
    };

    /// <summary>
    /// The reviewed catalog: the SAT-D14 companies are served, each with its SAT-D234 loss reason list and default validity.
    /// </summary>
    public static ApprovedSalesQuotePolicyCatalog Approved { get; } = new(
        [ApprovedUnitProfileCatalog.GdcCompanyId, ApprovedUnitProfileCatalog.GspCompanyId, ApprovedUnitProfileCatalog.GinCompanyId],
        [GdcLossReasonsV1, GspLossReasonsV1, GinLossReasonsV1],
        [GdcDefaultValidityV1, GspDefaultValidityV1, GinDefaultValidityV1]);

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

    /// <summary>The five SAT-D234 reasons in the decided order; only "Diğer" requires an explanation (SAT-D25).</summary>
    private static ApprovedSalesQuoteLossReasonListEntry LossReasons(Guid companyId, Guid profileId, Guid[] reasonIds) => new(
        companyId,
        profileId,
        Version: 1,
        Reasons:
        [
            new(reasonIds[0], "price_too_high", ApprovedSalesQuoteLossReasonTexts.PriceTooHigh, RequiresExplanation: false, IsActive: true),
            new(reasonIds[1], "delivery_not_suitable", ApprovedSalesQuoteLossReasonTexts.DeliveryNotSuitable, RequiresExplanation: false,
                IsActive: true),
            new(reasonIds[2], "competitor_chosen", ApprovedSalesQuoteLossReasonTexts.CompetitorChosen, RequiresExplanation: false,
                IsActive: true),
            new(reasonIds[3], "customer_withdrew", ApprovedSalesQuoteLossReasonTexts.CustomerWithdrew, RequiresExplanation: false,
                IsActive: true),
            new(reasonIds[4], "other", ApprovedSalesQuoteLossReasonTexts.Other, RequiresExplanation: true, IsActive: true),
        ],
        IsActive: true,
        EffectiveFrom: DecisionDate,
        EffectiveThrough: null,
        Source: QuoteDecisions + "; SAT-D25");

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
