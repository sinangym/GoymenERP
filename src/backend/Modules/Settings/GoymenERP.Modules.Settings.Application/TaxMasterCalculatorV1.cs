using System.Globalization;
using GoymenERP.Modules.Settings.Application.Contracts.V1;

namespace GoymenERP.Modules.Settings.Application;

/// <summary>A tax category of the company (VAT class, withholding class or buyer category).</summary>
public sealed record TaxCategoryDataV1(Guid Id, string Kind, string Code, string Name);

/// <summary>
/// One immutable rate record. <c>Kind</c> <see cref="TaxRateKindsV1.Vat"/>: a VAT class's rate (<c>Applies</c> true,
/// <c>RatePercent</c> 0…100). <see cref="TaxRateKindsV1.Withholding"/>: a withholding class + buyer category, either
/// applying (<c>RatePercent</c> &gt; 0…100 and a <c>WithholdingCode</c>) or explicitly not applied. <c>IsWithdrawn</c>:
/// withdrawn before it took effect; such a record is ignored everywhere.
/// </summary>
public sealed record TaxRateRecordDataV1(
    Guid RecordId,
    string Kind,
    Guid ClassId,
    Guid? BuyerCategoryId,
    long RecordVersion,
    DateOnly EffectiveFrom,
    bool Applies,
    decimal? RatePercent,
    string? WithholdingCode,
    bool IsWithdrawn = false);

/// <summary>One immutable code list version (the whole list).</summary>
public sealed record TaxCodeListVersionDataV1(
    string Kind,
    long ListVersion,
    DateOnly EffectiveFrom,
    IReadOnlyList<TaxCodeListEntryViewV1> Entries,
    bool IsWithdrawn = false);

/// <summary>Everything the tax master of one company holds.</summary>
public sealed record TaxMasterDataV1(
    IReadOnlyDictionary<Guid, TaxCategoryDataV1> Categories,
    IReadOnlyList<TaxRateRecordDataV1> Records,
    IReadOnlyList<TaxCodeListVersionDataV1> CodeLists);

/// <summary>The VAT and withholding parts of a line's tax resolution (company/product identity added by the port).</summary>
public sealed record TaxLineResolutionPartsV1(
    string VatState,
    decimal? VatPercent,
    TaxRateRecordDataV1? VatRecord,
    string? VatReason,
    string WithholdingState,
    decimal? WithholdingPercent,
    string? WithholdingCode,
    TaxRateRecordDataV1? WithholdingRecord,
    string? WithholdingReason);

/// <summary>
/// The ONE deterministic calculator of the tax master (MOD-03; SAT-D73/D74/D82/D93), used by the Settings screen's read and by
/// the line tax port (<see cref="ISettingsLineTaxResolverV1"/>); no UI or report re-implements it.
/// <list type="bullet">
/// <item><b>Timeline</b> (the SAT-D78/D84 precedent of the discount limits): per series — a VAT class; a withholding class +
/// buyer category; a code list kind — the non-withdrawn record in force on a date is the one with the LATEST
/// <c>EffectiveFrom</c> on or before it, and of several on that date the highest version. Before the first record nothing is
/// in force. A later-dated record applies by itself from its date.</item>
/// <item><b>VAT</b> (SAT-D73/D82): the product's VAT class → its rate in force → <c>Resolved</c>; anything missing is
/// <c>Unresolved</c> with a reason — never 0 and never a default rate.</item>
/// <item><b>Withholding</b> (SAT-D74): no withholding class on the product → <c>NotApplicable</c>; otherwise the buyer
/// category is required and the record of (class, buyer category) in force answers <c>Resolved</c> (percent + code; the code
/// must be in the withholding code list in force on the same date) or <c>NotApplied</c> (an explicit "no withholding"
/// record); anything missing → <c>Unresolved</c>.</item>
/// <item><b>Amounts</b> are NOT computed here: the consumer passes <see cref="WithholdingShare"/> and the VAT percent to its
/// single amount calculator (Sales <c>SalesOrderTotalsCalculator</c>: line amount = quantity × the UNROUNDED net unit price,
/// rounded once, SAT-D76; VAT per line rounded; withholding = the ROUNDED line VAT × share, rounded, SAT-D75).</item>
/// </list>
/// </summary>
public static class TaxMasterCalculatorV1
{
    public const decimal MinimumPercent = 0m;
    public const decimal MaximumPercent = 100m;

    /// <summary>The record of one series in force on <paramref name="on"/> (null before its first record).</summary>
    public static TaxRateRecordDataV1? RateInForce(TaxMasterDataV1 data, string kind, Guid classId, Guid? buyerCategoryId,
        DateOnly on)
    {
        ArgumentNullException.ThrowIfNull(data);
        return Series(data, kind, classId, buyerCategoryId)
            .Where(record => record.EffectiveFrom <= on)
            .OrderBy(record => record.EffectiveFrom)
            .ThenBy(record => record.RecordVersion)
            .LastOrDefault();
    }

    /// <summary>
    /// The records of one series that start AFTER <paramref name="after"/>, earliest first; of several on one day only the
    /// highest version (the store withdraws a same-day superseded record anyway).
    /// </summary>
    public static IReadOnlyList<TaxRateRecordDataV1> ScheduledRates(TaxMasterDataV1 data, string kind, Guid classId,
        Guid? buyerCategoryId, DateOnly after)
    {
        ArgumentNullException.ThrowIfNull(data);
        return Series(data, kind, classId, buyerCategoryId)
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

    /// <summary>
    /// The VAT and withholding of a product (its classes as the Product owner answered them) for a buyer category on a date.
    /// <paramref name="vatClassId"/> null means the product is not classified (both parts <c>Unresolved</c>).
    /// </summary>
    public static TaxLineResolutionPartsV1 Resolve(TaxMasterDataV1 data, Guid? vatClassId, Guid? withholdingClassId,
        Guid? buyerCategoryId, DateOnly on)
    {
        ArgumentNullException.ThrowIfNull(data);
        if (vatClassId is not { } vatClass)
        {
            return Unclassified(TaxResolutionReasonsV1.ProductNotClassified);
        }

        string vatState = TaxResolutionStatesV1.Unresolved;
        decimal? vatPercent = null;
        TaxRateRecordDataV1? vatRecord = null;
        string? vatReason;
        if (!IsCategory(data, vatClass, TaxCategoryKindsV1.VatClass))
        {
            vatReason = TaxResolutionReasonsV1.VatClassUnknown;
        }
        else if (RateInForce(data, TaxRateKindsV1.Vat, vatClass, null, on) is { RatePercent: { } rate } record)
        {
            (vatState, vatPercent, vatRecord, vatReason) = (TaxResolutionStatesV1.Resolved, rate, record, null);
        }
        else
        {
            vatReason = TaxResolutionReasonsV1.VatRateMissing;
        }

        var (withholdingState, withholdingPercent, withholdingCode, withholdingRecord, withholdingReason) =
            ResolveWithholding(data, withholdingClassId, buyerCategoryId, on);
        return new TaxLineResolutionPartsV1(vatState, vatPercent, vatRecord, vatReason, withholdingState, withholdingPercent,
            withholdingCode, withholdingRecord, withholdingReason);
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
        if (string.IsNullOrEmpty(text) || text.Length > 3 + 1 + TaxMasterInputLimitsV1.PercentMaximumScale)
        {
            return false;
        }

        var dot = text.IndexOf('.', StringComparison.Ordinal);
        var integerPart = dot < 0 ? text : text[..dot];
        var fraction = dot < 0 ? string.Empty : text[(dot + 1)..];
        if (integerPart.Length is 0 or > 3 || !integerPart.All(char.IsAsciiDigit)
            || (integerPart.Length > 1 && integerPart[0] == '0')
            || (dot >= 0 && (fraction.Length is 0 || fraction.Length > TaxMasterInputLimitsV1.PercentMaximumScale
                || !fraction.All(char.IsAsciiDigit))))
        {
            return false;
        }

        if (!decimal.TryParse(text, NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out var parsed)
            || parsed < MinimumPercent || parsed > MaximumPercent || (!allowZero && parsed == MinimumPercent))
        {
            return false;
        }

        value = parsed;
        return true;
    }

    /// <summary>The canonical text of a percent (no trailing zeros, invariant culture).</summary>
    public static string Format(decimal value) => value.ToString("0.############################", CultureInfo.InvariantCulture);

    /// <summary>A category code: upper-case ASCII letters, digits, '_' and '-', starting with a letter or digit, 1…32.</summary>
    public static bool TryNormalizeCategoryCode(string? code, out string normalized) =>
        TryNormalizeCode(code, TaxMasterInputLimitsV1.CategoryCodeMaximumLength, allowSeparators: true, out normalized);

    /// <summary>A UBL-TR list code: upper-case ASCII letters and digits only, 1…16 (e.g. "601", "351").</summary>
    public static bool TryNormalizeListCode(string? code, out string normalized) =>
        TryNormalizeCode(code, TaxMasterInputLimitsV1.ListCodeMaximumLength, allowSeparators: false, out normalized);

    private static bool TryNormalizeCode(string? code, int maximumLength, bool allowSeparators, out string normalized)
    {
        normalized = code?.Trim().ToUpperInvariant() ?? string.Empty;
        var value = normalized;
        return value.Length > 0 && value.Length <= maximumLength
            && (char.IsAsciiLetterUpper(value[0]) || char.IsAsciiDigit(value[0]))
            && value.All(character => char.IsAsciiLetterUpper(character) || char.IsAsciiDigit(character)
                || (allowSeparators && character is '_' or '-'));
    }

    private static (string State, decimal? Percent, string? Code, TaxRateRecordDataV1? Record, string? Reason) ResolveWithholding(
        TaxMasterDataV1 data, Guid? withholdingClassId, Guid? buyerCategoryId, DateOnly on)
    {
        if (withholdingClassId is not { } withholdingClass)
        {
            return (TaxResolutionStatesV1.NotApplicable, null, null, null, null);
        }

        if (!IsCategory(data, withholdingClass, TaxCategoryKindsV1.WithholdingClass))
        {
            return Unresolved(TaxResolutionReasonsV1.WithholdingClassUnknown);
        }

        if (buyerCategoryId is not { } buyer)
        {
            return Unresolved(TaxResolutionReasonsV1.BuyerCategoryMissing);
        }

        if (!IsCategory(data, buyer, TaxCategoryKindsV1.BuyerCategory))
        {
            return Unresolved(TaxResolutionReasonsV1.BuyerCategoryUnknown);
        }

        var record = RateInForce(data, TaxRateKindsV1.Withholding, withholdingClass, buyer, on);
        if (record is null)
        {
            return Unresolved(TaxResolutionReasonsV1.WithholdingRateMissing);
        }

        if (!record.Applies)
        {
            return (TaxResolutionStatesV1.NotApplied, null, null, record, null);
        }

        // The code must still be listed on the same date: a list version withdrawn or replaced without it leaves the
        // withholding without a source (fail-closed), never a code the list no longer carries.
        var list = ListInForce(data, TaxCodeListKindsV1.Withholding, on);
        if (record.RatePercent is not { } percent || record.WithholdingCode is not { } code
            || list is null || !list.Entries.Any(entry => string.Equals(entry.Code, code, StringComparison.Ordinal)))
        {
            return Unresolved(TaxResolutionReasonsV1.WithholdingCodeNotInList);
        }

        return (TaxResolutionStatesV1.Resolved, percent, code, record, null);
    }

    private static (string, decimal?, string?, TaxRateRecordDataV1?, string?) Unresolved(string reason) =>
        (TaxResolutionStatesV1.Unresolved, null, null, null, reason);

    private static TaxLineResolutionPartsV1 Unclassified(string reason) =>
        new(TaxResolutionStatesV1.Unresolved, null, null, reason, TaxResolutionStatesV1.Unresolved, null, null, null, reason);

    private static bool IsCategory(TaxMasterDataV1 data, Guid id, string kind) =>
        data.Categories.TryGetValue(id, out var category) && category is not null && category.Kind == kind;

    private static IEnumerable<TaxRateRecordDataV1> Series(TaxMasterDataV1 data, string kind, Guid classId, Guid? buyerCategoryId) =>
        data.Records.Where(record => !record.IsWithdrawn && record.Kind == kind && record.ClassId == classId
                                     && record.BuyerCategoryId == buyerCategoryId);

    private static IEnumerable<TaxCodeListVersionDataV1> Lists(TaxMasterDataV1 data, string kind) =>
        data.CodeLists.Where(list => !list.IsWithdrawn && list.Kind == kind);
}
