using GoymenERP.Modules.Settings.Application.Contracts.V1;

namespace GoymenERP.Modules.Settings.Application;

/// <summary>One reviewed Sales delivery tolerance version (SAT-D5).</summary>
public sealed record ApprovedSalesDeliveryToleranceEntry(
    Guid CompanyId,
    Guid ProfileId,
    long Version,
    decimal WarnPercent,
    decimal ApprovalPercent,
    decimal BlockPercent,
    bool IsActive,
    DateOnly EffectiveFrom,
    DateOnly? EffectiveThrough,
    string Source);

/// <summary>One reviewed Sales discount limit version of one limit group (SAT-D2).</summary>
public sealed record ApprovedSalesDiscountLimitEntry(
    Guid CompanyId,
    Guid LimitGroupId,
    Guid ProfileId,
    long Version,
    decimal MaximumDiscountPercent,
    bool IsActive,
    DateOnly EffectiveFrom,
    DateOnly? EffectiveThrough,
    string Source);

/// <summary>One reviewed Sales order-approval amount threshold version of one currency (SAT-D6).</summary>
public sealed record ApprovedSalesApprovalAmountThresholdEntry(
    Guid CompanyId,
    Guid CurrencyId,
    Guid ProfileId,
    long Version,
    bool IsEnabled,
    string? ThresholdAmountText,
    bool IsActive,
    DateOnly EffectiveFrom,
    DateOnly? EffectiveThrough,
    string Source);

/// <summary>
/// The approved Sales policies (MOD-03 slice 1), as versioned, reviewed source data, in the same
/// shape and for the same reasons as <see cref="ApprovedMoneyProfileCatalog"/>: nothing may edit a
/// policy at runtime, a new approval is a new version reviewed like code, and there is no
/// migration, seed write or DbContext. When the policies become user-maintained, a persistent
/// provider replaces this class behind <see cref="ISettingsSalesPolicyProviderV1"/>.
/// <para>
/// A company is served when it has any approved Sales policy; a served company's family without an
/// entry for the identity asked about answers <see cref="SettingsPrerequisiteCodesV1.SalesPolicyNotFound"/>.
/// </para>
/// <para>
/// A malformed catalog is refused as a whole with <c>settings_profile_invalid</c>: an empty
/// identity, a blank source, a non-positive version, a window that ends before it starts, a
/// repeated version or overlapping windows within one family key, a tolerance outside
/// 0 ≤ warn ≤ approval ≤ block ≤ 100, a discount limit outside 0..100, an enabled threshold without a
/// lossless positive amount in canonical form (no leading zero) or a disabled threshold carrying one. Nothing is repaired or skipped.
/// </para>
/// <para>
/// Technical ids are UUIDv5 in namespace <c>eb68fb11-2146-5285-9cc5-6ce5f3981184</c>
/// (= UUIDv5(URL, "https://github.com/sinangym/GoymenERP/settings/sales-policies")); assigned once,
/// never reused.
/// </para>
/// </summary>
public sealed class ApprovedSalesPolicyCatalog
{
    /// <summary>GSP — the permanent company identity pinned by the Organization
    /// <c>AddCompanyFiscalRuntimeSeed</c> migration.</summary>
    public static readonly Guid GspCompanyId = Guid.Parse("10000000-0000-0000-0000-000000000002");

    /// <summary>GIN — the permanent company identity pinned by the Organization
    /// <c>AddCompanyFiscalRuntimeSeed</c> migration.</summary>
    public static readonly Guid GinCompanyId = Guid.Parse("10000000-0000-0000-0000-000000000003");

    private const string SalesExtraDecisions = "SAT-D14 (SINAN_KARARLARI_2026-10-26_SATIS_EK.md)";

    /// <summary>
    /// GDC delivery tolerance, version 1: warn 1 %, approval 2 %, block 3 %. SAT-D5 (Sinan,
    /// 2026-10-25) carries OPEN-004's stock starting values over to Sales; effective from the
    /// decision date with no end. Profile id: UUIDv5 name
    /// "sales-delivery-tolerance:10000000-0000-0000-0000-000000000001:v1". Per-product/operation
    /// overrides (also SAT-D5) are not in this slice.
    /// </summary>
    public static readonly ApprovedSalesDeliveryToleranceEntry GdcDeliveryToleranceV1 = new(
        ApprovedUnitProfileCatalog.GdcCompanyId,
        ProfileId: Guid.Parse("c088f8d5-26c4-5ed9-ae6f-f47e73a26b11"),
        Version: 1,
        WarnPercent: 1m,
        ApprovalPercent: 2m,
        BlockPercent: 3m,
        IsActive: true,
        EffectiveFrom: new DateOnly(2026, 10, 25),
        EffectiveThrough: null,
        Source: "SAT-D5 (SINAN_KARARLARI_2026-10-25_SATIS.md); OPEN-004 sales part");

    /// <summary>
    /// GDC/TRY order-approval amount threshold, version 1: defined as a policy key but disabled, so
    /// no amount is set. SAT-D6 (Sinan, 2026-10-25): a single approver (Müdür) for now; OPEN-009
    /// stays partly open. Profile id: UUIDv5 name
    /// "sales-approval-amount-threshold:10000000-0000-0000-0000-000000000001:TRY:v1".
    /// </summary>
    public static readonly ApprovedSalesApprovalAmountThresholdEntry GdcTurkishLiraApprovalThresholdV1 = new(
        ApprovedUnitProfileCatalog.GdcCompanyId,
        SettingsCurrencyIdentitiesV1.TurkishLira,
        ProfileId: Guid.Parse("5c0ec7f4-2116-59c6-82fc-9c2a737a4342"),
        Version: 1,
        IsEnabled: false,
        ThresholdAmountText: null,
        IsActive: true,
        EffectiveFrom: new DateOnly(2026, 10, 25),
        EffectiveThrough: null,
        Source: "SAT-D6 (SINAN_KARARLARI_2026-10-25_SATIS.md); OPEN-009 KISMEN");

    /// <summary>
    /// GSP delivery tolerance, version 1: the same 1 / 2 / 3 % as GDC. SAT-D14 (Sinan, 2026-10-26):
    /// GSP and GIN run on GDC's Sales policies; effective from that decision's date with no end.
    /// Profile id: UUIDv5 name "sales-delivery-tolerance:10000000-0000-0000-0000-000000000002:v1".
    /// </summary>
    public static readonly ApprovedSalesDeliveryToleranceEntry GspDeliveryToleranceV1 = GdcDeliveryToleranceV1 with
    {
        CompanyId = GspCompanyId,
        ProfileId = Guid.Parse("e5ba2cba-1f1e-5ba0-b801-7b66d21190af"),
        EffectiveFrom = new DateOnly(2026, 10, 26),
        Source = SalesExtraDecisions + "; values SAT-D5",
    };

    /// <summary>
    /// GIN delivery tolerance, version 1: as <see cref="GspDeliveryToleranceV1"/> (SAT-D14). Profile id:
    /// UUIDv5 name "sales-delivery-tolerance:10000000-0000-0000-0000-000000000003:v1".
    /// </summary>
    public static readonly ApprovedSalesDeliveryToleranceEntry GinDeliveryToleranceV1 = GdcDeliveryToleranceV1 with
    {
        CompanyId = GinCompanyId,
        ProfileId = Guid.Parse("c5fd5cfa-b37f-5464-a920-152eb96c8c84"),
        EffectiveFrom = new DateOnly(2026, 10, 26),
        Source = SalesExtraDecisions + "; values SAT-D5",
    };

    /// <summary>
    /// GSP/TRY order-approval amount threshold, version 1: disabled, no amount, as GDC's (SAT-D14,
    /// SAT-D6). Profile id: UUIDv5 name
    /// "sales-approval-amount-threshold:10000000-0000-0000-0000-000000000002:TRY:v1".
    /// </summary>
    public static readonly ApprovedSalesApprovalAmountThresholdEntry GspTurkishLiraApprovalThresholdV1 =
        GdcTurkishLiraApprovalThresholdV1 with
        {
            CompanyId = GspCompanyId,
            ProfileId = Guid.Parse("320e06e3-0408-51f1-92e6-f8905e7b674f"),
            EffectiveFrom = new DateOnly(2026, 10, 26),
            Source = SalesExtraDecisions + "; values SAT-D6; OPEN-009 KISMEN",
        };

    /// <summary>
    /// GIN/TRY order-approval amount threshold, version 1: as <see cref="GspTurkishLiraApprovalThresholdV1"/>.
    /// Profile id: UUIDv5 name "sales-approval-amount-threshold:10000000-0000-0000-0000-000000000003:TRY:v1".
    /// </summary>
    public static readonly ApprovedSalesApprovalAmountThresholdEntry GinTurkishLiraApprovalThresholdV1 =
        GdcTurkishLiraApprovalThresholdV1 with
        {
            CompanyId = GinCompanyId,
            ProfileId = Guid.Parse("3cd01981-8511-5dc1-b21b-6c632425d915"),
            EffectiveFrom = new DateOnly(2026, 10, 26),
            Source = SalesExtraDecisions + "; values SAT-D6; OPEN-009 KISMEN",
        };

    /// <summary>
    /// The reviewed catalog: GDC, GSP and GIN (SAT-D14). Discount limits are deliberately empty: no
    /// limit value is approved, and SAT-D2 sends every discount of a group without a limit to approval.
    /// </summary>
    public static ApprovedSalesPolicyCatalog Approved { get; } = new(
        [GdcDeliveryToleranceV1, GspDeliveryToleranceV1, GinDeliveryToleranceV1],
        [],
        [GdcTurkishLiraApprovalThresholdV1, GspTurkishLiraApprovalThresholdV1, GinTurkishLiraApprovalThresholdV1]);

    private const decimal MaximumPercent = 100m;

    private readonly IReadOnlyDictionary<Guid, ApprovedSalesDeliveryToleranceEntry[]> _tolerances;
    private readonly IReadOnlyDictionary<(Guid CompanyId, Guid LimitGroupId), ApprovedSalesDiscountLimitEntry[]> _discounts;
    private readonly IReadOnlyDictionary<(Guid CompanyId, Guid CurrencyId), ApprovedSalesApprovalAmountThresholdEntry[]> _thresholds;
    private readonly IReadOnlySet<Guid> _companies;

    /// <summary>Internal on purpose, as the other catalogs': only Settings' own tests build another one.</summary>
    internal ApprovedSalesPolicyCatalog(
        IEnumerable<ApprovedSalesDeliveryToleranceEntry> tolerances,
        IEnumerable<ApprovedSalesDiscountLimitEntry> discounts,
        IEnumerable<ApprovedSalesApprovalAmountThresholdEntry> thresholds)
    {
        ArgumentNullException.ThrowIfNull(tolerances);
        ArgumentNullException.ThrowIfNull(discounts);
        ArgumentNullException.ThrowIfNull(thresholds);
        var tolerance = tolerances.ToArray();
        var discount = discounts.ToArray();
        var threshold = thresholds.ToArray();

        if (tolerance.Any(entry => entry is null
                || !IsWellFormed(entry.CompanyId, entry.ProfileId, entry.Version,
                    entry.EffectiveFrom, entry.EffectiveThrough, entry.Source)
                || entry.WarnPercent < 0m
                || entry.WarnPercent > entry.ApprovalPercent
                || entry.ApprovalPercent > entry.BlockPercent
                || entry.BlockPercent > MaximumPercent)
            || discount.Any(entry => entry is null
                || !IsWellFormed(entry.CompanyId, entry.ProfileId, entry.Version,
                    entry.EffectiveFrom, entry.EffectiveThrough, entry.Source)
                || entry.LimitGroupId == Guid.Empty
                || entry.MaximumDiscountPercent < 0m || entry.MaximumDiscountPercent > MaximumPercent)
            || threshold.Any(entry => entry is null
                || !IsWellFormed(entry.CompanyId, entry.ProfileId, entry.Version,
                    entry.EffectiveFrom, entry.EffectiveThrough, entry.Source)
                || entry.CurrencyId == Guid.Empty
                || !HasConsistentAmount(entry)))
        {
            throw Invalid();
        }

        _tolerances = Versions(tolerance, entry => entry.CompanyId,
            entry => entry.Version, entry => entry.EffectiveFrom, entry => entry.EffectiveThrough);
        _discounts = Versions(discount, entry => (entry.CompanyId, entry.LimitGroupId),
            entry => entry.Version, entry => entry.EffectiveFrom, entry => entry.EffectiveThrough);
        _thresholds = Versions(threshold, entry => (entry.CompanyId, entry.CurrencyId),
            entry => entry.Version, entry => entry.EffectiveFrom, entry => entry.EffectiveThrough);
        _companies = tolerance.Select(entry => entry.CompanyId)
            .Concat(discount.Select(entry => entry.CompanyId))
            .Concat(threshold.Select(entry => entry.CompanyId))
            .ToHashSet();
    }

    public IReadOnlyCollection<ApprovedSalesDeliveryToleranceEntry> DeliveryTolerances =>
        _tolerances.Values.SelectMany(versions => versions).ToArray();

    public IReadOnlyCollection<ApprovedSalesDiscountLimitEntry> DiscountLimits =>
        _discounts.Values.SelectMany(versions => versions).ToArray();

    public IReadOnlyCollection<ApprovedSalesApprovalAmountThresholdEntry> ApprovalAmountThresholds =>
        _thresholds.Values.SelectMany(versions => versions).ToArray();

    /// <summary>Whether any Sales policy is approved for the company at all.</summary>
    public bool Serves(Guid companyId) => _companies.Contains(companyId);

    /// <summary>
    /// The version in force on the date, or — outside every window — the nearest earlier version
    /// (or the first one) with its REAL window, so the consumer's own check rejects it. Null only
    /// when the company has no delivery tolerance.
    /// </summary>
    public ApprovedSalesDeliveryToleranceEntry? DeliveryToleranceFor(Guid companyId, DateOnly effectiveOn) =>
        _tolerances.TryGetValue(companyId, out var ordered) ? InForce(ordered, effectiveOn, e => e.EffectiveFrom) : null;

    /// <summary>As <see cref="DeliveryToleranceFor"/>, for one limit group; null when it has no limit.</summary>
    public ApprovedSalesDiscountLimitEntry? DiscountLimitFor(Guid companyId, Guid limitGroupId, DateOnly effectiveOn) =>
        _discounts.TryGetValue((companyId, limitGroupId), out var ordered)
            ? InForce(ordered, effectiveOn, e => e.EffectiveFrom)
            : null;

    /// <summary>As <see cref="DeliveryToleranceFor"/>, for one currency; null when it has no threshold.</summary>
    public ApprovedSalesApprovalAmountThresholdEntry? ApprovalAmountThresholdFor(
        Guid companyId, Guid currencyId, DateOnly effectiveOn) =>
        _thresholds.TryGetValue((companyId, currencyId), out var ordered)
            ? InForce(ordered, effectiveOn, e => e.EffectiveFrom)
            : null;

    private static bool IsWellFormed(Guid companyId, Guid profileId, long version,
        DateOnly from, DateOnly? through, string? source) =>
        companyId != Guid.Empty && profileId != Guid.Empty && version > 0
        && (through is not DateOnly end || end >= from)
        && !string.IsNullOrWhiteSpace(source);

    private static bool HasConsistentAmount(ApprovedSalesApprovalAmountThresholdEntry entry)
    {
        if (!entry.IsEnabled)
        {
            return entry.ThresholdAmountText is null;
        }

        var text = entry.ThresholdAmountText;
        // Canonical form: no leading zero before the integer digits ("0100" is refused, "0.50" is not),
        // so one amount has exactly one spelling in the reviewed catalog.
        if (text is { Length: > 1 } && text[0] == '0' && text[1] != '.')
        {
            return false;
        }

        try
        {
            // Lexical check only (lossless positive decimal). The text's own length is passed as the
            // scale because it bounds any fractional length, so no scale is enforced here: the scale
            // belongs to the currency's money profile and is checked by the consumer.
            ExactPositiveDecimalV1.Parse(text, text?.Length ?? 0);
            return true;
        }
        catch (SettingsPrerequisiteException)
        {
            return false;
        }
    }

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

    private static TEntry InForce<TEntry>(TEntry[] ordered, DateOnly effectiveOn, Func<TEntry, DateOnly> from)
        where TEntry : class =>
        ordered.LastOrDefault(entry => from(entry) <= effectiveOn) ?? ordered[0];

    private static SettingsPrerequisiteException Invalid() => new("settings_profile_invalid");
}
