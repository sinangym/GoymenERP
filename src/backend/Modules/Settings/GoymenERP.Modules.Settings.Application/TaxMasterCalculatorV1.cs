using System.Globalization;
using GoymenERP.Modules.Settings.Application.Contracts.V1;

namespace GoymenERP.Modules.Settings.Application;

/// <summary>
/// One immutable default-tax record of an item type (SAT-D139): the reviewed catalog entry (<c>Source</c> <c>Catalog</c>,
/// version 0) or a Settings record (version ≥ 1). VAT applies with a percent, or not ("KDV yok") with an optional exemption
/// code; withholding (only when VAT applies) with a percent and an optional code, or not. <c>IsWithdrawn</c>: withdrawn
/// before it took effect; ignored everywhere.
/// </summary>
public sealed record TaxItemTypeDefaultDataV1(
    Guid RecordId,
    Guid CompanyId,
    string ItemType,
    string Source,
    long RecordVersion,
    DateOnly EffectiveFrom,
    bool VatApplies,
    decimal? VatPercent,
    string? ExemptionCode,
    bool WithholdingApplies,
    decimal? WithholdingPercent,
    string? WithholdingCode,
    bool IsWithdrawn = false);

/// <summary>One immutable code list version (the whole list).</summary>
public sealed record TaxCodeListVersionDataV1(
    string Kind,
    long ListVersion,
    DateOnly EffectiveFrom,
    IReadOnlyList<TaxCodeListEntryViewV1> Entries,
    bool IsWithdrawn = false);

/// <summary>One immutable withholding amount threshold record (VUK 232). <c>IsWithdrawn</c>: withdrawn before it took effect.</summary>
public sealed record TaxWithholdingThresholdDataV1(
    Guid RecordId,
    long RecordVersion,
    DateOnly EffectiveFrom,
    decimal Amount,
    string CurrencyCode,
    bool IsWithdrawn = false);

/// <summary>
/// SAT-D158: one immutable buyer situation list version (the whole list): the reviewed catalog (<c>Source</c> <c>Catalog</c>,
/// version 0) or a Settings version (≥ 1). <c>IsWithdrawn</c>: withdrawn before it took effect; ignored everywhere.
/// </summary>
public sealed record TaxBuyerSituationListVersionDataV1(
    long ListVersion,
    DateOnly EffectiveFrom,
    string Source,
    IReadOnlyList<TaxBuyerSituationEntryViewV1> Entries,
    bool IsWithdrawn = false);

/// <summary>
/// Everything the tax master of one company holds (the catalog entries included in <c>Defaults</c> and
/// <c>BuyerSituationLists</c>).
/// </summary>
public sealed record TaxMasterDataV1(
    IReadOnlyList<TaxItemTypeDefaultDataV1> Defaults,
    IReadOnlyList<TaxCodeListVersionDataV1> CodeLists,
    IReadOnlyList<TaxWithholdingThresholdDataV1> Thresholds,
    IReadOnlyList<TaxBuyerSituationListVersionDataV1> BuyerSituationLists);

/// <summary>The default tax of an item type on a date (company/product identity added by the port).</summary>
public sealed record TaxDefaultResolutionPartsV1(
    string State,
    string? Reason,
    TaxItemTypeDefaultDataV1? Record,
    string? VatState,
    decimal? VatPercent,
    string? ExemptionCodeState,
    string? ExemptionCode,
    string? ExemptionCodeReason,
    string? WithholdingState,
    decimal? WithholdingPercent,
    string? WithholdingCodeState,
    string? WithholdingCode,
    string? WithholdingCodeReason,
    string? BuyerAdjustment = null,
    TaxBuyerSituationListVersionDataV1? BuyerSituationList = null,
    TaxBuyerSituationEntryViewV1? BuyerSituation = null);

/// <summary>
/// The ONE deterministic calculator of the tax master (MOD-03; SAT-D93/D133/D135/D139), used by the Settings screen's read and
/// by the line tax port (<see cref="ISettingsLineTaxResolverV1"/>); no UI or report re-implements it.
/// <list type="bullet">
/// <item><b>Timeline</b> (the SAT-D78/D84 precedent of the discount limits): per series — an item type's default; a code list
/// kind; the threshold — the non-withdrawn record in force on a date is the one with the LATEST <c>EffectiveFrom</c> on or
/// before it, and of several on that date the highest version (a Settings record outranks the version-0 catalog entry on the
/// same day). Before the first record nothing is in force. A later-dated record applies by itself from its date.</item>
/// <item><b>Buyer situation</b> (SAT-D140/D147/D158): the entry of the buyer situation list in force on the date carries the
/// effect as data — withholding kept or removed (SAT-D74), VAT as the default's or KDV %0 under an exemption code the order
/// chose from the exemption list (SAT-D147, no withholding), and whether the order needs the buyer's document. An unknown or
/// withdrawn code is <c>Unresolved</c> (<c>buyer_situation_unknown</c>); no situation is hard-coded here.</item>
/// <item><b>Default</b> (SAT-D139): the item type's record in force → VAT (<c>Resolved</c> percent or <c>Exempt</c>) and
/// withholding (<c>Resolved</c> percent or <c>NotApplied</c>); an exemption / withholding code is checked against the list in
/// force on the same date and is otherwise <c>Unresolved</c> ("kaynak yok", the invoice waits). No item type or no record →
/// <c>Unresolved</c> — never 0, never a guessed default.</item>
/// <item><b>Amounts</b> are NOT computed here: the consumer passes the VAT percent (0 for <c>Exempt</c>) and
/// <see cref="WithholdingShare"/> to its single amount calculator (Sales <c>SalesOrderTotalsCalculator</c>: line amount =
/// quantity × the UNROUNDED net unit price, rounded once, SAT-D76; VAT per line rounded; withholding = the ROUNDED line VAT ×
/// share, rounded, SAT-D75).</item>
/// </list>
/// </summary>
public static class TaxMasterCalculatorV1
{
    public const decimal MinimumPercent = 0m;
    public const decimal MaximumPercent = 100m;

    /// <summary>The default of <paramref name="itemType"/> in force on <paramref name="on"/> (null before its first record).</summary>
    public static TaxItemTypeDefaultDataV1? DefaultInForce(TaxMasterDataV1 data, string itemType, DateOnly on)
    {
        ArgumentNullException.ThrowIfNull(data);
        return Defaults(data, itemType)
            .Where(record => record.EffectiveFrom <= on)
            .OrderBy(record => record.EffectiveFrom)
            .ThenBy(record => record.RecordVersion)
            .LastOrDefault();
    }

    /// <summary>The records of one item type that start AFTER <paramref name="after"/>, earliest first (one per day).</summary>
    public static IReadOnlyList<TaxItemTypeDefaultDataV1> ScheduledDefaults(TaxMasterDataV1 data, string itemType, DateOnly after)
    {
        ArgumentNullException.ThrowIfNull(data);
        return Defaults(data, itemType)
            .Where(record => record.EffectiveFrom > after)
            .GroupBy(record => record.EffectiveFrom)
            .Select(sameDay => sameDay.MaxBy(record => record.RecordVersion)!)
            .OrderBy(record => record.EffectiveFrom)
            .ToArray();
    }

    /// <summary>The code list version of <paramref name="kind"/> in force on <paramref name="on"/> (null before the first).</summary>
    public static TaxCodeListVersionDataV1? ListInForce(TaxMasterDataV1 data, string kind, DateOnly on)
    {
        ArgumentNullException.ThrowIfNull(data);
        return Lists(data, kind)
            .Where(list => list.EffectiveFrom <= on)
            .OrderBy(list => list.EffectiveFrom)
            .ThenBy(list => list.ListVersion)
            .LastOrDefault();
    }

    public static IReadOnlyList<TaxCodeListVersionDataV1> ScheduledLists(TaxMasterDataV1 data, string kind, DateOnly after)
    {
        ArgumentNullException.ThrowIfNull(data);
        return Lists(data, kind)
            .Where(list => list.EffectiveFrom > after)
            .GroupBy(list => list.EffectiveFrom)
            .Select(sameDay => sameDay.MaxBy(list => list.ListVersion)!)
            .OrderBy(list => list.EffectiveFrom)
            .ToArray();
    }

    /// <summary>The withholding amount threshold in force on <paramref name="on"/> (null before the first: "kaynak yok").</summary>
    public static TaxWithholdingThresholdDataV1? ThresholdInForce(TaxMasterDataV1 data, DateOnly on)
    {
        ArgumentNullException.ThrowIfNull(data);
        return Thresholds(data)
            .Where(record => record.EffectiveFrom <= on)
            .OrderBy(record => record.EffectiveFrom)
            .ThenBy(record => record.RecordVersion)
            .LastOrDefault();
    }

    public static IReadOnlyList<TaxWithholdingThresholdDataV1> ScheduledThresholds(TaxMasterDataV1 data, DateOnly after)
    {
        ArgumentNullException.ThrowIfNull(data);
        return Thresholds(data)
            .Where(record => record.EffectiveFrom > after)
            .GroupBy(record => record.EffectiveFrom)
            .Select(sameDay => sameDay.MaxBy(record => record.RecordVersion)!)
            .OrderBy(record => record.EffectiveFrom)
            .ToArray();
    }

    /// <summary>SAT-D158: the buyer situation list version in force on <paramref name="on"/> (null before the first).</summary>
    public static TaxBuyerSituationListVersionDataV1? BuyerSituationListInForce(TaxMasterDataV1 data, DateOnly on)
    {
        ArgumentNullException.ThrowIfNull(data);
        return SituationLists(data)
            .Where(list => list.EffectiveFrom <= on)
            .OrderBy(list => list.EffectiveFrom)
            .ThenBy(list => list.ListVersion)
            .LastOrDefault();
    }

    public static IReadOnlyList<TaxBuyerSituationListVersionDataV1> ScheduledBuyerSituationLists(TaxMasterDataV1 data,
        DateOnly after)
    {
        ArgumentNullException.ThrowIfNull(data);
        return SituationLists(data)
            .Where(list => list.EffectiveFrom > after)
            .GroupBy(list => list.EffectiveFrom)
            .Select(sameDay => sameDay.MaxBy(list => list.ListVersion)!)
            .OrderBy(list => list.EffectiveFrom)
            .ToArray();
    }

    /// <summary>
    /// A buyer situation code: ASCII letters and digits, starting with a letter, 1…64 (case kept; compared ordinally). Never
    /// trimmed into validity: surrounding whitespace is refused.
    /// </summary>
    public static bool IsWellFormedSituationCode(string? code) =>
        code is { Length: > 0 and <= TaxMasterInputLimitsV1.BuyerSituationCodeMaximumLength }
        && char.IsAsciiLetter(code[0]) && code.All(char.IsAsciiLetterOrDigit);

    /// <summary>
    /// The default tax of <paramref name="itemType"/> (null: the product has none) on <paramref name="on"/>, adjusted for the
    /// order's <paramref name="buyerSituation"/> by the entry of the buyer situation list in force (SAT-D140/D158) and, for a
    /// <see cref="TaxBuyerSituationVatModesV1.ZeroWithExemptionCode"/> situation, the <paramref name="exemptionCode"/> the order
    /// chose (SAT-D147; ignored for any other situation).
    /// </summary>
    public static TaxDefaultResolutionPartsV1 Resolve(TaxMasterDataV1 data, string? itemType, string buyerSituation, DateOnly on,
        string? exemptionCode = null)
    {
        ArgumentNullException.ThrowIfNull(data);
        var list = BuyerSituationListInForce(data, on);
        var situation = list?.Entries.SingleOrDefault(entry => string.Equals(entry.Code, buyerSituation, StringComparison.Ordinal));
        if (list is null || situation is null)
        {
            // SAT-D158: an unknown or withdrawn situation is "kaynak yok", never the normal default.
            return Unresolved(TaxResolutionReasonsV1.BuyerSituationUnknown);
        }

        var parts = ResolveDefault(data, itemType, on) with { BuyerSituationList = list, BuyerSituation = situation };
        if (parts.State != TaxResolutionStatesV1.Resolved)
        {
            return parts;
        }

        if (situation.VatMode == TaxBuyerSituationVatModesV1.ZeroWithExemptionCode)
        {
            // SAT-D147: KDV %0, the exemption code chosen from the list in force (else "kaynak yok", the invoice waits), and no
            // withholding (there is no VAT to withhold).
            var (codeState, code, codeReason) = Code(data, TaxCodeListKindsV1.Exemption, exemptionCode,
                TaxResolutionReasonsV1.ExemptionCodeMissing, on, TaxResolutionReasonsV1.CodeListMissing);
            return parts with
            {
                VatState = TaxResolutionStatesV1.ZeroRated,
                VatPercent = MinimumPercent,
                ExemptionCodeState = codeState,
                ExemptionCode = code,
                ExemptionCodeReason = codeReason,
                WithholdingState = TaxResolutionStatesV1.NotApplied,
                WithholdingPercent = null,
                WithholdingCodeState = TaxResolutionStatesV1.NotRequired,
                WithholdingCode = null,
                WithholdingCodeReason = null,
                BuyerAdjustment = TaxResolutionReasonsV1.BuyerZeroRatedWithExemptionCode,
            };
        }

        if (situation.VatMode != TaxBuyerSituationVatModesV1.Normal)
        {
            // A mode this calculator does not know (a newer list): fail closed.
            return Unresolved(TaxResolutionReasonsV1.BuyerSituationUnknown);
        }

        // SAT-D74: withholding only for a buyer subject to it; the VAT stays the default's.
        return situation.WithholdingApplies || parts.WithholdingState == TaxResolutionStatesV1.NotApplied
            ? parts
            : parts with
            {
                WithholdingState = TaxResolutionStatesV1.NotApplied,
                WithholdingPercent = null,
                WithholdingCodeState = TaxResolutionStatesV1.NotRequired,
                WithholdingCode = null,
                WithholdingCodeReason = null,
                BuyerAdjustment = TaxResolutionReasonsV1.BuyerNotSubjectToWithholding,
            };
    }

    private static TaxDefaultResolutionPartsV1 ResolveDefault(TaxMasterDataV1 data, string? itemType, DateOnly on)
    {
        if (itemType is null)
        {
            return Unresolved(TaxResolutionReasonsV1.ProductItemTypeMissing);
        }

        if (!TaxItemTypesV1.IsKnown(itemType))
        {
            return Unresolved(TaxResolutionReasonsV1.ItemTypeUnknown);
        }

        if (DefaultInForce(data, itemType, on) is not { } record)
        {
            return Unresolved(TaxResolutionReasonsV1.ItemTypeDefaultMissing);
        }

        // Fail closed: a zero VAT rate is never a resolved rate without its exemption code (the command and the CHECK refuse
        // it; a record that still carried it waits on the exemption code gate like "KDV yok").
        var (exemptionState, exemptionCode, exemptionReason) = record.VatApplies && record.VatPercent is > 0m
            ? (TaxResolutionStatesV1.NotRequired, (string?)null, (string?)null)
            : Code(data, TaxCodeListKindsV1.Exemption, record.ExemptionCode, TaxResolutionReasonsV1.ExemptionCodeMissing, on);
        var (withholdingCodeState, withholdingCode, withholdingReason) = record.WithholdingApplies
            ? Code(data, TaxCodeListKindsV1.Withholding, record.WithholdingCode, TaxResolutionReasonsV1.WithholdingCodeMissing, on)
            : (TaxResolutionStatesV1.NotRequired, (string?)null, (string?)null);
        return new TaxDefaultResolutionPartsV1(
            TaxResolutionStatesV1.Resolved,
            null,
            record,
            record.VatApplies ? TaxResolutionStatesV1.Resolved : TaxResolutionStatesV1.Exempt,
            record.VatApplies ? record.VatPercent : null,
            exemptionState,
            exemptionCode,
            exemptionReason,
            record.WithholdingApplies ? TaxResolutionStatesV1.Resolved : TaxResolutionStatesV1.NotApplied,
            record.WithholdingApplies ? record.WithholdingPercent : null,
            withholdingCodeState,
            withholdingCode,
            withholdingReason);
    }

    /// <summary>The share of the line VAT that is withheld: the percent / 100, exact (e.g. 50 → 0.5 for 5/10).</summary>
    public static decimal WithholdingShare(decimal withholdingPercent)
    {
        if (withholdingPercent is <= MinimumPercent or > MaximumPercent)
        {
            throw new ArgumentOutOfRangeException(nameof(withholdingPercent), "Tevkifat oranı 0'dan büyük ve en fazla 100 olmalıdır.");
        }

        return withholdingPercent / 100m;
    }

    /// <summary>
    /// A percent as lossless decimal text: digits, an optional '.' and 1…4 decimals, 0…100 (or &gt; 0 when
    /// <paramref name="allowZero"/> is false). No sign, exponent, group separator or whitespace; never rounded.
    /// </summary>
    public static bool TryParsePercent(string? text, bool allowZero, out decimal value)
    {
        value = 0m;
        if (!TryParseDecimalText(text, 3, out var parsed) || parsed < MinimumPercent || parsed > MaximumPercent
            || (!allowZero && parsed == MinimumPercent))
        {
            return false;
        }

        value = parsed;
        return true;
    }

    /// <summary>An amount as lossless decimal text above 0: 1…15 integer digits (no leading zero), optional 1…4 decimals.</summary>
    public static bool TryParseAmount(string? text, out decimal value)
    {
        value = 0m;
        if (!TryParseDecimalText(text, 15, out var parsed) || parsed <= 0m)
        {
            return false;
        }

        value = parsed;
        return true;
    }

    /// <summary>The canonical text of a percent or amount (no trailing zeros, invariant culture).</summary>
    public static string Format(decimal value) => value.ToString("0.############################", CultureInfo.InvariantCulture);

    /// <summary>A UBL-TR list code: upper-case ASCII letters and digits only, 1…16 (e.g. "601", "351").</summary>
    public static bool TryNormalizeListCode(string? code, out string normalized)
    {
        normalized = code?.Trim().ToUpperInvariant() ?? string.Empty;
        var value = normalized;
        return value.Length > 0 && value.Length <= TaxMasterInputLimitsV1.ListCodeMaximumLength
            && value.All(character => char.IsAsciiLetterUpper(character) || char.IsAsciiDigit(character));
    }

    /// <summary>An ISO 4217 alphabetic code: exactly three ASCII letters, upper-cased.</summary>
    public static bool TryNormalizeCurrencyCode(string? code, out string normalized)
    {
        normalized = code?.Trim().ToUpperInvariant() ?? string.Empty;
        return normalized.Length == 3 && normalized.All(char.IsAsciiLetterUpper);
    }

    /// <summary>Whether <paramref name="code"/> is in the <paramref name="kind"/> list in force on <paramref name="on"/>.</summary>
    public static bool IsListed(TaxMasterDataV1 data, string kind, string code, DateOnly on) =>
        ListInForce(data, kind, on)?.Entries.Any(entry => string.Equals(entry.Code, code, StringComparison.Ordinal)) ?? false;

    private static bool TryParseDecimalText(string? text, int maximumIntegerDigits, out decimal value)
    {
        value = 0m;
        if (string.IsNullOrEmpty(text) || text.Length > maximumIntegerDigits + 1 + TaxMasterInputLimitsV1.PercentMaximumScale)
        {
            return false;
        }

        var dot = text.IndexOf('.', StringComparison.Ordinal);
        var integerPart = dot < 0 ? text : text[..dot];
        var fraction = dot < 0 ? string.Empty : text[(dot + 1)..];
        if (integerPart.Length == 0 || integerPart.Length > maximumIntegerDigits || !integerPart.All(char.IsAsciiDigit)
            || (integerPart.Length > 1 && integerPart[0] == '0')
            || (dot >= 0 && (fraction.Length is 0 || fraction.Length > TaxMasterInputLimitsV1.PercentMaximumScale
                || !fraction.All(char.IsAsciiDigit))))
        {
            return false;
        }

        return decimal.TryParse(text, NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out value);
    }

    private static (string State, string? Code, string? Reason) Code(TaxMasterDataV1 data, string kind, string? code,
        string missingReason, DateOnly on, string? listMissingReason = null)
    {
        if (code is null)
        {
            return (TaxResolutionStatesV1.Unresolved, null, missingReason);
        }

        if (listMissingReason is not null && ListInForce(data, kind, on) is null)
        {
            return (TaxResolutionStatesV1.Unresolved, code, listMissingReason);
        }

        // The code must still be listed on the date: a list version withdrawn or replaced without it leaves the code without
        // a source (fail-closed), never a code the list no longer carries.
        return IsListed(data, kind, code, on)
            ? (TaxResolutionStatesV1.Resolved, code, null)
            : (TaxResolutionStatesV1.Unresolved, code, TaxResolutionReasonsV1.CodeNotInList);
    }

    private static TaxDefaultResolutionPartsV1 Unresolved(string reason) =>
        new(TaxResolutionStatesV1.Unresolved, reason, null, null, null, null, null, null, null, null, null, null, null);

    private static IEnumerable<TaxItemTypeDefaultDataV1> Defaults(TaxMasterDataV1 data, string itemType) =>
        data.Defaults.Where(record => !record.IsWithdrawn && record.ItemType == itemType);

    private static IEnumerable<TaxCodeListVersionDataV1> Lists(TaxMasterDataV1 data, string kind) =>
        data.CodeLists.Where(list => !list.IsWithdrawn && list.Kind == kind);

    private static IEnumerable<TaxWithholdingThresholdDataV1> Thresholds(TaxMasterDataV1 data) =>
        data.Thresholds.Where(record => !record.IsWithdrawn);

    private static IEnumerable<TaxBuyerSituationListVersionDataV1> SituationLists(TaxMasterDataV1 data) =>
        data.BuyerSituationLists.Where(list => !list.IsWithdrawn);
}
