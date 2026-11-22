using GoymenERP.Modules.Manufacturing.Domain.Common;

namespace GoymenERP.Modules.Manufacturing.Domain.Costing;

/// <summary>
/// The answer of one stamped issue cost as Inventory gives it (SAT-D305 (4)). That the cost of an issue is the
/// unit cost written on the issue movement at issue time is the reading of SAT-D317 (3) (author reading there,
/// revocable); Inventory gives the value and Manufacturing reads it through a versioned port.
/// </summary>
public enum IssueCostReadStatus
{
    Value = 1,
    Pending = 2,
    Unreadable = 3,
}

/// <summary>
/// One kg input issue of the order with the moving-average unit cost (TL per kg) Inventory stamped on the issue
/// movement at issue time. <see cref="UnitCostPerKg"/> is set only for <see cref="IssueCostReadStatus.Value"/>.
/// Every issue needs a readable cost, also one that is fully returned: its stamped cost values the return
/// (SAT-D317 (2)). A stamped cost of zero is not a cost: the order's cost is "hesaplanamadi"
/// (<c>input_cost_zero</c>), the result names the issue (<see cref="ProductionReceiptCostResult.IssueReasons"/>) and
/// the cost is read again once a cost is entered (URT-D58 (1), SINAN_KARARLARI_2026-11-14_URETIM_EK4; the option "accept
/// zero" was not chosen, URT-D58 (3)). <see cref="Currency"/> says in which currency the input entered stock and at
/// which rate its TL cost was converted (URT-D55, <see cref="IssueCostCurrencyInput"/>); there is no default.
/// </summary>
public sealed record IssueCostInput(Guid IssueMovementId, Kilograms Mass, IssueCostReadStatus Status,
    decimal? UnitCostPerKg, IssueCostCurrencyInput Currency);

/// <summary>
/// How the TL unit cost of one issue relates to a foreign currency (URT-D55 / #66 R9 9.6 and 9.10,
/// SINAN_KARARLARI_2026-11-14_URETIM_EK4).
/// <list type="bullet">
/// <item><see cref="FunctionalCurrency"/>: the input entered stock in TL; no currency line.</item>
/// <item><see cref="Converted"/>: the input entered stock in a foreign currency and its cost was converted to TL at the
/// rate recorded when it entered stock (goods receipt or purchase invoice, URT-D55 (2)); the currency amount and the
/// rate are shown in the report (<see cref="ProductionReceiptCostResult.CurrencyLines"/>).</item>
/// <item><see cref="RateMissing"/>: the input entered stock in a foreign currency and no rate was recorded; the cost is
/// "hesaplanamadi" (<c>input_exchange_rate_missing</c>, URT-D55 (2)).</item>
/// </list>
/// Not chosen: the rate of the day the order closes, and keeping the cost in the foreign currency (URT-D55 (3)).
/// </summary>
public enum IssueCostCurrencyStatus
{
    FunctionalCurrency = 1,
    Converted = 2,
    RateMissing = 3,

    /// <summary>The owner cannot prove the issue's entry currency; no functional-currency default is inferred.</summary>
    Unreadable = 4,
}

/// <summary>
/// The currency facts of one issue as the issue-cost source gives them (URT-D55). <see cref="CurrencyId"/> is the
/// Settings currency identity of the currency the input entered stock in; <see cref="ForeignUnitCostPerKg"/> is its cost
/// per kg in that currency; <see cref="ExchangeRate"/> is the TL per one unit of that currency recorded when it entered
/// stock. <see cref="IssueCostCurrencyStatus.FunctionalCurrency"/> carries none of the three;
/// <see cref="IssueCostCurrencyStatus.Converted"/> carries all three, each set (the id not empty, the amounts above zero);
/// <see cref="IssueCostCurrencyStatus.RateMissing"/> carries the currency id, no rate and no TL unit cost, and its foreign
/// cost when known. Anything else, or an undefined status, is <c>input_cost_unreadable</c>. The calculator values the
/// issue at its TL unit cost only; the foreign cost and the rate are shown, not recomputed or compared, because the TL
/// cost an issue carries is the stamped moving average (SAT-D317 (3)) and no tolerance between the two is decided. Which
/// receipt's rate a moving-average issue shows is the issue-cost source's contract, not this calculator's.
/// FNS-D73 (SINAN_KARARLARI_2026-11-15_DALGA2_EK12): the FX to TL conversion of a purchased input uses the purchase
/// order's rate (an invoice rate keeps SAT-D295/BS-1); the issue-cost source stamps that rate, Manufacturing never picks
/// a rate type or reads a rate table itself. A foreign-currency input with no order to take a rate from comes as
/// <see cref="IssueCostCurrencyStatus.RateMissing"/> and the cost stays "hesaplanamadi" (that case is NEEDS-SINAN).
/// An indivisible purchase-unit conversion may supply <see cref="ForeignUnitCostRatio"/> instead of a decimal foreign
/// unit cost. It is exact provenance only, never an approximate scalar or a substitute for the TL stamp.
/// </summary>
public sealed record IssueCostCurrencyInput(IssueCostCurrencyStatus Status, Guid? CurrencyId,
    decimal? ForeignUnitCostPerKg, decimal? ExchangeRate,
    [property: System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
    ProductionExactRatioV1? ForeignUnitCostRatio = null,
    [property: System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
    ProductionExactRatioV1? ExchangeRateRatio = null)
{
    public static IssueCostCurrencyInput FunctionalCurrency { get; } =
        new(IssueCostCurrencyStatus.FunctionalCurrency, null, null, null);
}

/// <summary>
/// One return of unused input, linked to the issue movement it reverses (SAT-D317 (2)): it is valued at that
/// issue's stamped cost, never at the moving average of the return time. A return without its issue is unreadable
/// (SAT-D317 (3)). SAT-D303 / SAT-D306 (customer and purchase returns) are other rules and are not applied here.
/// </summary>
public sealed record ReturnCostInput(Guid ReturnMovementId, Guid IssueMovementId, Kilograms Mass);

/// <summary>The scrap-into-cost company setting as Settings answers it (SAT-D305 (2)).</summary>
public enum ScrapCostSettingStatus
{
    On = 1,
    Off = 2,
    Unreadable = 3,
}

/// <summary>
/// How the scrap price of one weighing's month was read: <see cref="Missing"/> is a price that was not entered for
/// that month, <see cref="Unreadable"/> one that could not be read.
/// </summary>
public enum ScrapPriceReadStatus
{
    Value = 1,
    Missing = 2,
    Unreadable = 3,
}

/// <summary>
/// The scrap price in TL per kg that values one scrap weighing (SAT-D316 (1) with scrap out of cost; URT-D75 (1) with
/// scrap in cost, where that value is taken off the product cost too and the scrap enters stock at it). It is the
/// monthly scrap price entered by hand (SAT-D316 (3)-(4)) of the month the scrap was weighed in: each weighing takes
/// its own month's price, so an order that spans two months values each weighing at its own month's price
/// (URT-D47 / #66 6864631364 (15)). The caller reads that month's price for each weighing; the calculator takes it as
/// an explicit input and never picks a month or a value. A weighing with scrap kg whose month has no price entered
/// (<see cref="Missing"/>) leaves the cost pending until the price is entered (URT-D47 / #66 6864631364 (15), the
/// chosen option: "fiyat girilene kadar bekler"); a price that cannot be read, or a malformed one, gives no amount
/// (SAT-D316 (6)), and so is a price of zero or below (URT-D74 (2)). A weighing of zero kg with no price entered is
/// computed with no value for it (SAT-D326 (4) / #66 6857496471 (4)), in both settings. A price above the order's net raw-material kg cost stops the calculation (SAT-D326 (3) /
/// #66 6857496471 (3), URT-D44 / #66 6864631364 (1), see <see cref="ProductionReceiptCostCalculatorV1"/>). Who enters
/// the price is not decided (SINAN_KARARLARI_2026-11-13 "Sinirlar"). <see cref="TlPerKg"/> is set only for
/// <see cref="ScrapPriceReadStatus.Value"/>.
/// </summary>
public sealed record ScrapPriceInput(ScrapPriceReadStatus Status, decimal? TlPerKg)
{
    public static ScrapPriceInput Missing { get; } = new(ScrapPriceReadStatus.Missing, null);
}

/// <summary>
/// One weighing of the order's scrap: the scrap output's id, its weighed kg and the scrap price of the month it was
/// weighed in (URT-D47 / #66 6864631364 (15), <see cref="ScrapPriceInput"/>). The order's weighed scrap is the sum of
/// its weighings; an output id given twice is <c>mass_unbalanced</c>, as a movement given twice is.
/// </summary>
public sealed record ScrapWeighingInput(Guid ScrapOutputId, Kilograms Mass, ScrapPriceInput Price);

/// <summary>
/// Which inputs not tracked in kg the order consumed; the calculator costs kg inputs only (URT-D45 /
/// #66 6864631364 (13), the chosen option: "Urun maliyeti yalniz kg girdilerden hesaplanir").
/// <list type="bullet">
/// <item><see cref="None"/>: kg inputs only.</item>
/// <item><see cref="PaintLitres"/>: the Paint process's paint, tracked in litres (Master PNT-006, URT-D9,
/// URT-D41 (3)). Its cost is not loaded on the product in v1 and the result says so
/// (<see cref="ProductionReceiptCostResult.PaintCostNote"/>) instead of leaving it out silently
/// (SAT-D327 / #66 6857496471 (5)).</item>
/// <item><see cref="Other"/>: any other input consumed in pieces, litres or metres instead of kg. Like the paint, its
/// cost is not loaded on the product in v1 and the result says so
/// (<see cref="ProductionReceiptCostResult.NonKgInputCostNote"/>, URT-D45 / #66 6864631364 (13)).</item>
/// <item><see cref="PaintLitresAndOther"/>: both; the result carries both notes.</item>
/// </list>
/// There is no default: an undefined value is <c>cost_input_invalid</c>. An input consumed in pieces, litres or metres
/// is never one of the kg issues, so it is outside the mass check of this calculator too (URT-D50 (1),
/// SINAN_KARARLARI_2026-11-14_URETIM_EK4); the kg equivalent of an output in pieces or metres (URT-D50 (2), URT-D51) is
/// the closing slice's work.
/// </summary>
public enum NonKgInputUse
{
    None = 1,
    PaintLitres = 2,
    Other = 3,
    PaintLitresAndOther = 4,
}

/// <summary>
/// The order's facts the production receipt cost needs: its stock outputs by class, its scrap weighings, the declared
/// process loss, the inputs not tracked in kg and whether it is closed. <see cref="UsableRemnant"/> is the usable
/// remnant returned to stock (for example a narrow strip or a short piece): it returns like an unused input at the
/// net raw-material kg cost, and its kg and value are taken out of both the divisor and the cost
/// (URT-D46 / #66 6864631364 (14) with scrap out of cost; #66 6874860782, SINAN-CEVAP (10), with scrap in cost); a
/// caller gives a remnant here, never as second quality, or it would take a product's cost.
/// <see cref="DeclaredProcessLoss"/> is the order's declared process loss (the <c>ProcessLoss</c> output class): fire
/// is exactly the weighed scrap plus the declared process loss (#66 5917438293 item 3A). Consumed kg that no output,
/// remnant, weighed scrap or declared process loss accounts for is the unexplained difference; it never becomes process
/// loss or scrap ("aciklanamayan fark otomatik fire olmaz", URT-D5). WIP and measurement correction, which
/// DEC-PRDCT-006 reconciles apart, are not inputs here: which side of the out / in comparison they belong on is not
/// decided (NEEDS-SINAN, SINAN_KARARLARI_2026-11-13); a caller gives neither as any of the classes here.
/// <see cref="ClosedWithOutputAboveInput"/> is true for an order closed with output above input where the close
/// measures it (on a parent roll, or on the order total for a process without a main roll input; URT-D41 (1),
/// URT-D42 (1), URT-D49), which only a close stored before URT-D41 can be: such an order's cost is unreadable with
/// <c>output_exceeds_input</c>, "hesaplanamadı — çıkan giren kg'dan fazla" (URT-D52 (1), SINAN_KARARLARI_2026-11-14
/// URETIM_EK4). The order totals here cannot show a roll that is above input while another is short, so the caller
/// sets it from the order (<see cref="LegacyClosureCostV1.WithClosureMeasure"/>); false by default.
/// </summary>
public sealed record ProductionReceiptCostInput(
    bool OrderClosed,
    IReadOnlyList<IssueCostInput> Issues,
    IReadOnlyList<ReturnCostInput> Returns,
    Kilograms GoodProduct,
    Kilograms SecondQuality,
    Kilograms UsableRemnant,
    IReadOnlyList<ScrapWeighingInput> ScrapWeighings,
    Kilograms DeclaredProcessLoss,
    ScrapCostSettingStatus ScrapSetting,
    NonKgInputUse NonKgInput,
    bool ClosedWithOutputAboveInput = false);

public enum ProductionReceiptCostStatus
{
    Value = 1,
    Pending = 2,
    Unreadable = 3,
}

public static class ProductionReceiptCostCodesV1
{
    public const string InputCostUnreadable = "input_cost_unreadable";
    public const string ScrapSettingUnreadable = "scrap_setting_unreadable";
    public const string MassUnbalanced = "mass_unbalanced";
    public const string ScrapPriceUnreadable = "scrap_price_unreadable";
    public const string ScrapPriceAboveRawMaterialCost = "scrap_price_above_raw_material_cost";
    public const string ProductCostNotPositive = "product_cost_not_positive";
    public const string InputInvalid = "cost_input_invalid";
    public const string ArithmeticOverflow = "cost_arithmetic_overflow";

    // URT-D58 / URT-D55: the issue has no usable cost yet; the result names the issues and is read again once the cost
    // or the rate is entered.
    public const string InputCostZero = "input_cost_zero";
    public const string InputExchangeRateMissing = "input_exchange_rate_missing";

    /// <summary>
    /// URT-D52 (1): the order was closed with output above input on a parent roll (or on its total for a process
    /// without a main roll input) under the rule before URT-D41; its cost shows "hesaplanamadı — çıkan giren kg'dan
    /// fazla" (<see cref="LegacyClosureCostTextsV1.OutputExceedsInput"/>).
    /// </summary>
    public const string OutputExceedsInput = "output_exceeds_input";

    // Why a result is pending; not unreadable codes.
    public const string OrderNotClosed = "order_not_closed";
    public const string ScrapPriceMissing = "scrap_price_missing";

    // The notes on a value; not unreadable codes.
    public const string PaintCostNotIncluded = "paint_cost_not_included";
    public const string NonKgInputCostNotIncluded = "non_kg_input_cost_not_included";

    public const string BasisWithScrap = "production-input-average-plus-scrap";
    public const string BasisWithoutScrap = "production-input-average-less-scrap-value";
}

/// <summary>
/// A note on a value: a stable code and its Turkish user text from <see cref="ProductionReceiptCostTextsV1"/>.
/// </summary>
public sealed record ProductionReceiptCostNoteV1(string Code, string Text);

/// <summary>
/// The result: a <see cref="ProductionReceiptCostStatus.Value"/> carries the same TL-per-kg cost for the good
/// product and the second quality output, always above zero (SAT-D326 (2) / #66 6857496471 (2));
/// <see cref="RawMaterialCostPerKg"/>: the order's net raw-material kg cost (net input cost / consumed kg,
/// URT-D44 / #66 6864631364 (1)), which is also the TL per kg a usable remnant enters stock at (URT-D46 /
/// #66 6864631364 (14)); <see cref="UsableRemnantValue"/>: the TL the usable remnant takes out of the cost (remnant kg
/// x net raw-material kg cost, zero when there is no remnant); <see cref="WeighedScrapExpense"/>: the TL of the weighed
/// scrap line of the separate scrap / period expense (the sum of each weighing's kg x its month's price; zero when
/// scrap is in the product cost, where the same value is taken off the product cost but enters the scrap stock
/// instead of an expense (URT-D75 (1), (3)), or when there is no weighed scrap); and <see cref="ProcessLossKg"/>: the declared
/// process loss in kg, with <see cref="ProcessLossRatio"/> = declared process loss kg / consumed kg (consumed =
/// issued - returned), a fraction (0.02 = 2 %), not a percent. The process loss carries no amount: its value is zero
/// and its cost stays on the product (SAT-D316 (2)). The kg and the ratio are display facts only and no money depends
/// on them; that a report shows them as a separate line and that the ratio's divisor is the consumed kg are author
/// readings, not decisions (#66 6826603722 (b) and the report sentence of 6826763903 (c) were not asked,
/// SINAN_KARARLARI_2026-11-13 "Sinirlar"). <see cref="UnexplainedKg"/> is the consumed kg no output, remnant, weighed
/// scrap or declared process loss accounts for (never negative: more out than in is <c>mass_unbalanced</c>, URT-D40);
/// it is outside the scrap / process loss lines and never fire (URT-D5), and like the process loss it adds nothing to
/// the expense, so the cost per kg is the same whichever of the two a kg is in. <see cref="PaintCostNote"/>
/// (<c>paint_cost_not_included</c>, "boya maliyeti dahil değil", SAT-D327 / #66 6857496471 (5)) and
/// <see cref="NonKgInputCostNote"/> (<c>non_kg_input_cost_not_included</c>, "kg dışı girdi maliyeti dahil değil",
/// URT-D45 / #66 6864631364 (13)) are set on a value only, when the order consumed such an input whose cost is not in
/// the cost per kg; null otherwise. <see cref="PendingCode"/> says why a result is pending: <c>order_not_closed</c>
/// (SAT-D317 (1)) or <c>scrap_price_missing</c> (URT-D47 / #66 6864631364 (15)). A pending or unreadable result carries
/// no amount, kg, ratio or note (never zero). <see cref="NetInputCost"/> is the TL that went in (net input cost, before
/// the remnant and the scrap expense are taken off): the rounding of URT-D54 distributes exactly it
/// (<see cref="ProductionCostRoundingCalculatorV1"/>). <see cref="CurrencyLines"/> are the converted issues of a value,
/// in issue movement id order (URT-D55 (2), display only). <see cref="IssueReasons"/> name the issues of an
/// <c>input_cost_zero</c> or <c>input_exchange_rate_missing</c> result (URT-D58 (1), URT-D55 (2)), in issue movement id
/// order; they are empty on any other result. Equality compares the two lists element by element, so the same input in
/// another order gives an equal result.
/// </summary>
public sealed record ProductionReceiptCostResult(
    ProductionReceiptCostStatus Status,
    decimal? CostPerKg,
    decimal? RawMaterialCostPerKg,
    decimal? UsableRemnantValue,
    decimal? WeighedScrapExpense,
    decimal? ProcessLossKg,
    decimal? ProcessLossRatio,
    decimal? UnexplainedKg,
    string? CostBasis,
    ProductionReceiptCostNoteV1? PaintCostNote,
    ProductionReceiptCostNoteV1? NonKgInputCostNote,
    string? PendingCode,
    string? UnreadableCode)
{
    public static ProductionReceiptCostResult Pending(string code) =>
        new(ProductionReceiptCostStatus.Pending, null, null, null, null, null, null, null, null, null, null, code,
            null);

    public static ProductionReceiptCostResult Unreadable(string code) =>
        new(ProductionReceiptCostStatus.Unreadable, null, null, null, null, null, null, null, null, null, null, null,
            code);

    public decimal? NetInputCost { get; init; }

    public IReadOnlyList<ProductionReceiptCostCurrencyLineV1> CurrencyLines { get; init; } =
        Array.Empty<ProductionReceiptCostCurrencyLineV1>();

    public IReadOnlyList<ProductionReceiptCostIssueReasonV1> IssueReasons { get; init; } =
        Array.Empty<ProductionReceiptCostIssueReasonV1>();

    /// <summary>
    /// WP11 (DALGA_PLANI_v2 1.12, "bu sayıyı açıkla"): the intermediate values of a value, captured by the calculator in the
    /// same run that computed them; null on a pending or unreadable result. Display only: never serialized into stored
    /// evidence, never compared by <see cref="Equals(ProductionReceiptCostResult?)"/> (it is a function of the same input).
    /// </summary>
    [System.Text.Json.Serialization.JsonIgnore]
    public ProductionReceiptCostTraceV1? Trace { get; init; }

    public bool Equals(ProductionReceiptCostResult? other) =>
        other is not null
        && Status == other.Status
        && CostPerKg == other.CostPerKg
        && RawMaterialCostPerKg == other.RawMaterialCostPerKg
        && UsableRemnantValue == other.UsableRemnantValue
        && WeighedScrapExpense == other.WeighedScrapExpense
        && ProcessLossKg == other.ProcessLossKg
        && ProcessLossRatio == other.ProcessLossRatio
        && UnexplainedKg == other.UnexplainedKg
        && CostBasis == other.CostBasis
        && Equals(PaintCostNote, other.PaintCostNote)
        && Equals(NonKgInputCostNote, other.NonKgInputCostNote)
        && PendingCode == other.PendingCode
        && UnreadableCode == other.UnreadableCode
        && NetInputCost == other.NetInputCost
        && CurrencyLines.SequenceEqual(other.CurrencyLines)
        && IssueReasons.SequenceEqual(other.IssueReasons);

    public override int GetHashCode()
    {
        var hash = new HashCode();
        hash.Add(Status);
        hash.Add(CostPerKg);
        hash.Add(RawMaterialCostPerKg);
        hash.Add(UsableRemnantValue);
        hash.Add(WeighedScrapExpense);
        hash.Add(ProcessLossKg);
        hash.Add(ProcessLossRatio);
        hash.Add(UnexplainedKg);
        hash.Add(CostBasis);
        hash.Add(PaintCostNote);
        hash.Add(NonKgInputCostNote);
        hash.Add(PendingCode);
        hash.Add(UnreadableCode);
        hash.Add(NetInputCost);
        hash.Add(CurrencyLines.Count);
        hash.Add(IssueReasons.Count);
        return hash.ToHashCode();
    }
}

/// <summary>
/// One converted issue of a value (URT-D55 (2)): the currency it entered stock in, its cost per kg in that currency, the
/// rate recorded when it entered stock, the TL cost per kg the calculator used and the kg the order kept (issued -
/// returned). <see cref="ForeignAmount"/> = kept kg x foreign cost per kg, unrounded: a display figure, never posted;
/// how a report rounds a foreign amount is not decided here.
/// Ratio-only entry currency carries null scalar fields and the exact unit/kept-amount ratios instead.
/// </summary>
public sealed record ProductionReceiptCostCurrencyLineV1(
    Guid IssueMovementId,
    Guid CurrencyId,
    decimal? ForeignUnitCostPerKg,
    decimal? ExchangeRate,
    decimal TlUnitCostPerKg,
    decimal KeptKilograms,
    decimal? ForeignAmount,
    ProductionExactRatioV1? ForeignUnitCostRatio = null,
    ProductionExactRatioV1? ForeignAmountRatio = null,
    [property: System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
    ProductionExactRatioV1? ExchangeRateRatio = null);

/// <summary>
/// WP11: one kg issue as the calculator valued it: issued and returned kg, the kg the order kept, the stamped TL cost per kg
/// and the amount it added to the net input cost (kept kg x TL cost per kg).
/// </summary>
public sealed record ProductionReceiptCostIssueTraceV1(Guid IssueMovementId, decimal IssuedKg, decimal ReturnedKg,
    decimal KeptKg, decimal TlUnitCostPerKg, decimal AmountTl);

/// <summary>WP11: one scrap weighing as the calculator valued it (kg x its month's TL price; no price and no value at zero kg).</summary>
public sealed record ProductionReceiptCostScrapTraceV1(Guid ScrapOutputId, decimal Kilograms, decimal? TlPerKg, decimal ValueTl);

/// <summary>
/// WP11: the calculator's intermediate values for a value, in the order of its formula:
/// net input cost = Σ issue amounts; consumed kg = Σ kept kg; net raw-material kg cost = net / consumed; usable remnant value =
/// remnant kg x net raw-material kg cost; weighed scrap value = Σ weighing values; produced kg = good + second quality;
/// cost numerator = net − remnant value − scrap value; cost per kg = numerator / produced kg (unrounded).
/// </summary>
public sealed record ProductionReceiptCostTraceV1(
    IReadOnlyList<ProductionReceiptCostIssueTraceV1> Issues,
    decimal ConsumedKg,
    decimal NetInputCostTl,
    decimal RawMaterialCostPerKgTl,
    decimal UsableRemnantKg,
    decimal UsableRemnantValueTl,
    IReadOnlyList<ProductionReceiptCostScrapTraceV1> ScrapWeighings,
    decimal WeighedScrapValueTl,
    decimal GoodProductKg,
    decimal SecondQualityKg,
    decimal ProducedKg,
    decimal CostNumeratorTl,
    decimal CostPerKgTl);

/// <summary>
/// One issue that keeps the order's cost from being computed and why: <c>input_cost_zero</c> (URT-D58 (1)) or
/// <c>input_exchange_rate_missing</c> (URT-D55 (2)).
/// </summary>
public sealed record ProductionReceiptCostIssueReasonV1(Guid IssueMovementId, string Code);

/// <summary>
/// SAT-D305 (4), SAT-D307, SAT-D316, SAT-D317, URT-D40 and Sinan's answers of #66 6857496471 and #66 6864631364
/// (SINAN_KARARLARI_2026-11-13_EK1: SAT-D326, SAT-D327, URT-D44 to URT-D47): the single deterministic production
/// receipt cost of v1.
/// <list type="bullet">
/// <item>net input cost = sum(issued kg x stamped issue cost) - sum(returned kg x the linked issue's stamped cost);
/// consumed kg = issued kg - returned kg; net raw-material kg cost = net input cost / consumed kg
/// (URT-D44 / #66 6864631364 (1), the chosen option "Net": the same basis as the product cost; golden: 500 kg x 40 TL
/// and 500 kg x 50 TL issued, 400 kg of the 50 TL lot returned, (20000 + 5000) / 600 = 41.67 TL per kg, so a 43 TL
/// scrap price stops);</item>
/// <item>a usable remnant returns to stock like an unused input at the net raw-material kg cost: usable remnant value
/// = remnant kg x net raw-material kg cost is taken out of the cost, and the remnant kg are not in the divisor
/// (URT-D46 / #66 6864631364 (14), the chosen option "Iade gibi stoga donsun"; golden: 1000 kg x 40 TL, 900 kg good,
/// 50 kg remnant, 50 kg weighed scrap x 10 TL, (40000 - 2000 - 500) / 900 = 41.67 TL per kg). With scrap in cost the
/// remnant returns the same way and the scrap share is borne by the product only (#66 6874860782, SINAN-CEVAP (10),
/// URT-D59 (1)-(2));</item>
/// <item>scrap in cost (default, SAT-D305 (2)): cost per kg = (net - usable remnant value - weighed scrap value) /
/// (good product kg + second quality kg), the same for both classes (SAT-D307 (1)-(2)); the weighed scrap value is
/// the sum of each weighing's kg x the scrap price of the month it was weighed in, and the scrap enters stock at that
/// value (URT-D75 (1), (3), URT-D65). Golden (URT-D75 (4)): 1000 kg x 40 TL = 40000 TL; 900 kg good, 50 kg remnant,
/// 50 kg scrap at 10 TL/kg: remnant 2000 TL, scrap 500 TL, 37500 / 900 = 41.666... TL per kg; this replaces the
/// 42.22 TL example of URT-D59 (3), which did not take the scrap value off (URT-D75 (5)). The process loss and any
/// unexplained kg have no value and raise the cost of what was produced. The decision text names the good product kg
/// as the divisor; its golden has no second quality, and the divisor keeps SAT-D307's good + second quality kg
/// (author reading; whether second quality leaves this divisor is NEEDS-SINAN). The missing, unreadable and too high
/// price rules below apply here as they do with scrap out of cost: a weighing with scrap kg whose month has no price
/// leaves the cost pending (URT-D65);</item>
/// <item>scrap out of cost (SAT-D316, which sets the amount of SAT-D308 (1)-(2) and partly replaces them): each scrap
/// weighing is valued at the scrap price of the month it was weighed in (URT-D47 / #66 6864631364 (15)), that amount is
/// taken off the cost and reported apart, and the rest is loaded on the product (SAT-D316 (1)): weighed scrap expense =
/// sum(weighing kg x its month's scrap price), cost per kg = (net - usable remnant value - weighed scrap expense) /
/// (good product kg + second quality kg). The divisor is Sinan's answer SAT-D326 (1) / #66 6857496471 (1)
/// ("Saglam + 2. kalite kg"), whose example the option text counted as the golden example: 1000 kg x 40 TL, 900 kg good
/// + 50 kg second quality, 50 kg weighed scrap x 10 TL = 500 TL expense, 39500 / 950 = 41.578947... TL per kg. The
/// process loss has value zero, so its cost stays on the product (SAT-D316 (2)). A weighing with scrap kg whose month
/// has no price entered leaves the cost pending (<c>scrap_price_missing</c>, never zero) until the price is entered
/// (URT-D47 / #66 6864631364 (15)); a price that cannot be read or is malformed is unreadable (SAT-D316 (6)); a
/// weighing of zero kg with no price entered, or no weighing at all, is computed with no expense for it (SAT-D326 (4) /
/// #66 6857496471 (4)). The expense is only computed here; no expense record is enabled (SAT-D308 (5));</item>
/// <item>a scrap price above the net raw-material kg cost stops the calculation so the price is checked
/// (SAT-D326 (3) / #66 6857496471 (3), "Hesap dursun, fiyat kontrol edilsin"; URT-D44 / #66 6864631364 (1)); a price
/// equal to it computes. The check runs on each weighing's price in both settings, also on a weighing of zero kg
/// (author reading). A price of zero or below is unreadable in both settings (URT-D74 (2)): the month's price must be
/// entered above zero (URT-D74 (1)) and the calculator never finalises a cost on a zero price. Because no weighing's price is above the net raw-material kg cost, the remnant
/// and scrap values together never exceed the net input cost;</item>
/// <item>a cost per kg of zero or below is unreadable, never a value (SAT-D326 (2) / #66 6857496471 (2); "zero or
/// below" is the text of the option chosen in 6857438754 "2-a", and the note under (2), not Sinan's words, aligns it
/// with SAT-D297);</item>
/// <item>kg inputs only: the cost of the Paint process's paint, tracked in litres, and of any other input consumed in
/// pieces, litres or metres is not loaded on the product in v1, and the result carries a note for each
/// (SAT-D327 / #66 6857496471 (5), URT-D45 / #66 6864631364 (13), <see cref="NonKgInputUse"/>);</item>
/// <item>no labour or overhead in v1. Issue costs and the scrap prices are TL per kg: an input that entered stock in a
/// foreign currency comes with its TL cost converted at the rate recorded when it entered stock, and the currency, its
/// cost and the rate are shown on a currency line (URT-D55 (2), <see cref="IssueCostCurrencyInput"/>); an input with no
/// rate recorded is "hesaplanamadi" (<c>input_exchange_rate_missing</c>). Nothing is rounded here: the calculation runs
/// at full precision and the recorded figures are rounded once, by <see cref="ProductionCostRoundingCalculatorV1"/>
/// (URT-D54 (1)-(2)).</item>
/// <item>an issue whose stamped cost is zero makes the order's cost "hesaplanamadi" (<c>input_cost_zero</c>); the
/// result names every such issue and the cost is read again once a cost is entered (URT-D58 (1)). The calculator keeps
/// no state: every read computes from the costs the source gives at that time, so the entered cost is what the next
/// read uses.</item>
/// </list>
/// The cost is calculated for closed orders only; an open order gives pending (<c>order_not_closed</c>) and no amount
/// (SAT-D317 (1)). In a closed order an issue cost that is still pending is unreadable, not pending. More out than in
/// gives no amount even inside the closing tolerance (URT-D40 (1)-(2), URT-D41 (1)): "out" is good product + second
/// quality + usable remnant + weighed scrap + declared process loss and "in" is the consumed kg (issued - returned),
/// the reading of URT-D40 (3). The rule is measured per input coil at order closing (URT-D42 (1)), which is the
/// closing slice's work; this calculator has the order totals only and refuses when the totals alone already break
/// the rule. The short side (the tolerance and the approval of URT-D6, URT-D41 (2), URT-D42 (2)) is the closing
/// step's too: the calculator takes a closed order as having passed it and puts no limit on the unexplained kg (author
/// reading). Any issue cost that is not a value or is below zero, malformed currency facts of an issue, an unknown
/// scrap setting, a return without its issue or over
/// its issue, a movement id used twice (also as both an issue and a return), a scrap output id used twice, outputs,
/// remnant, weighed scrap and declared process loss over the consumed kg, a zero divisor, an unreadable or malformed
/// scrap price, a scrap price above the net raw-material kg cost, a cost per kg of zero or below, a missing input,
/// list or element or an undefined <see cref="NonKgInputUse"/> (<c>cost_input_invalid</c>) or an arithmetic overflow
/// (<c>cost_arithmetic_overflow</c>) gives <see cref="ProductionReceiptCostStatus.Unreadable"/>, and so does an order
/// the caller marks as closed with output above input (<c>output_exceeds_input</c>, URT-D52 (1)); an unreadable answer
/// wins over a missing scrap price, since entering the price would not make it readable. For the same reason any
/// other unreadable input, a return, a scrap output or the mass checks wins over a zero cost or a missing rate; the
/// scrap price checks are made only once every issue has a cost, since they compare with the net raw-material kg cost.
/// A missing rate is reported before a zero cost (author reading); the issue reasons list both. The calculator does not
/// throw for its input's content. Sums run in movement id and scrap output id order and the price checks are
/// evaluated over all weighings before one answer is chosen, so the result does not depend on the order the
/// movements and weighings are given in.
/// </summary>
public static class ProductionReceiptCostCalculatorV1
{
    public const int ContractVersion = 1;

    public static ProductionReceiptCostResult Calculate(ProductionReceiptCostInput input)
    {
        if (input is null)
        {
            return ProductionReceiptCostResult.Unreadable(ProductionReceiptCostCodesV1.InputInvalid);
        }

        if (!input.OrderClosed)
        {
            return ProductionReceiptCostResult.Pending(ProductionReceiptCostCodesV1.OrderNotClosed);
        }

        if (input.Issues is null || input.Returns is null || input.ScrapWeighings is null
            || input.NonKgInput is not (NonKgInputUse.None or NonKgInputUse.PaintLitres or NonKgInputUse.Other
                or NonKgInputUse.PaintLitresAndOther))
        {
            return ProductionReceiptCostResult.Unreadable(ProductionReceiptCostCodesV1.InputInvalid);
        }

        // URT-D52 (1): a close stored before URT-D41 with output above input gives no amount; the order is not reopened
        // or changed (URT-D42 (3)).
        if (input.ClosedWithOutputAboveInput)
        {
            return ProductionReceiptCostResult.Unreadable(ProductionReceiptCostCodesV1.OutputExceedsInput);
        }

        try
        {
            return Closed(input);
        }
        catch (OverflowException)
        {
            return ProductionReceiptCostResult.Unreadable(ProductionReceiptCostCodesV1.ArithmeticOverflow);
        }
    }

    private static ProductionReceiptCostResult Closed(ProductionReceiptCostInput input)
    {
        if (input.Issues.Count == 0)
        {
            return ProductionReceiptCostResult.Unreadable(ProductionReceiptCostCodesV1.MassUnbalanced);
        }

        // A rate-missing issue has no TL cost; its entry carries zero and is never summed, because a non-empty reason
        // list returns before any sum.
        var issues = new SortedDictionary<Guid, (decimal Mass, decimal Cost, IssueCostCurrencyInput Currency)>();
        var reasons = new SortedDictionary<Guid, string>();
        foreach (var issue in input.Issues)
        {
            if (issue is null || issue.Currency is null)
            {
                return ProductionReceiptCostResult.Unreadable(ProductionReceiptCostCodesV1.InputInvalid);
            }

            if (issue.Status != IssueCostReadStatus.Value)
            {
                return ProductionReceiptCostResult.Unreadable(ProductionReceiptCostCodesV1.InputCostUnreadable);
            }

            decimal cost;
            string? reason = null;
            switch (issue.Currency.Status)
            {
                case IssueCostCurrencyStatus.RateMissing when IsRateMissing(issue):
                    cost = 0m;
                    reason = ProductionReceiptCostCodesV1.InputExchangeRateMissing;
                    break;
                case IssueCostCurrencyStatus.FunctionalCurrency or IssueCostCurrencyStatus.Converted
                    when IsWellFormed(issue.Currency) && issue.UnitCostPerKg is { } stamped && stamped >= 0m:
                    cost = stamped;
                    // URT-D58 (1): zero is not a cost; the order's cost waits for one to be entered.
                    reason = stamped == 0m ? ProductionReceiptCostCodesV1.InputCostZero : null;
                    break;
                default:
                    return ProductionReceiptCostResult.Unreadable(ProductionReceiptCostCodesV1.InputCostUnreadable);
            }

            if (!issues.TryAdd(issue.IssueMovementId, (issue.Mass.Value, cost, issue.Currency)))
            {
                return ProductionReceiptCostResult.Unreadable(ProductionReceiptCostCodesV1.MassUnbalanced);
            }

            if (reason is not null)
            {
                reasons.Add(issue.IssueMovementId, reason);
            }
        }

        var returned = new Dictionary<Guid, decimal>();
        var returnIds = new HashSet<Guid>();
        foreach (var item in input.Returns)
        {
            if (item is null)
            {
                return ProductionReceiptCostResult.Unreadable(ProductionReceiptCostCodesV1.InputInvalid);
            }

            if (issues.ContainsKey(item.ReturnMovementId) || !returnIds.Add(item.ReturnMovementId)
                || !issues.TryGetValue(item.IssueMovementId, out var issue))
            {
                return ProductionReceiptCostResult.Unreadable(ProductionReceiptCostCodesV1.MassUnbalanced);
            }

            var total = returned.GetValueOrDefault(item.IssueMovementId) + item.Mass.Value;
            if (total > issue.Mass)
            {
                return ProductionReceiptCostResult.Unreadable(ProductionReceiptCostCodesV1.MassUnbalanced);
            }

            returned[item.IssueMovementId] = total;
        }

        // SortedDictionary enumerates by scrap output id: the sums are the same whatever order the input lists.
        var weighings = new SortedDictionary<Guid, ScrapWeighingInput>();
        foreach (var weighing in input.ScrapWeighings)
        {
            if (weighing is null)
            {
                return ProductionReceiptCostResult.Unreadable(ProductionReceiptCostCodesV1.InputInvalid);
            }

            if (!weighings.TryAdd(weighing.ScrapOutputId, weighing))
            {
                return ProductionReceiptCostResult.Unreadable(ProductionReceiptCostCodesV1.MassUnbalanced);
            }
        }

        if (input.ScrapSetting is not (ScrapCostSettingStatus.On or ScrapCostSettingStatus.Off))
        {
            return ProductionReceiptCostResult.Unreadable(ProductionReceiptCostCodesV1.ScrapSettingUnreadable);
        }

        // SortedDictionary enumerates by issue movement id: the sums are the same whatever order the input lists.
        var consumed = 0m;
        foreach (var (id, issue) in issues)
        {
            consumed += issue.Mass - returned.GetValueOrDefault(id);
        }

        var weighedScrap = 0m;
        foreach (var weighing in weighings.Values)
        {
            weighedScrap += weighing.Mass.Value;
        }

        var produced = input.GoodProduct.Value + input.SecondQuality.Value;
        var remnant = input.UsableRemnant.Value;
        var processLoss = input.DeclaredProcessLoss.Value;
        // URT-D40 (1)-(3): what came out cannot be more than what went in; no tolerance on this side (URT-D41 (1)).
        var unexplained = consumed - produced - remnant - weighedScrap - processLoss;
        if (produced <= 0m || unexplained < 0m)
        {
            return ProductionReceiptCostResult.Unreadable(ProductionReceiptCostCodesV1.MassUnbalanced);
        }

        // URT-D55 (2) and URT-D58 (1): an issue without a rate or with a zero cost leaves the order's cost
        // "hesaplanamadi"; the result names every such issue, and a later read with the cost or rate entered computes.
        if (reasons.Count > 0)
        {
            var code = reasons.ContainsValue(ProductionReceiptCostCodesV1.InputExchangeRateMissing)
                ? ProductionReceiptCostCodesV1.InputExchangeRateMissing
                : ProductionReceiptCostCodesV1.InputCostZero;
            return ProductionReceiptCostResult.Unreadable(code) with
            {
                IssueReasons = reasons.Select(static pair => new ProductionReceiptCostIssueReasonV1(pair.Key, pair.Value))
                    .ToArray(),
            };
        }

        var netCost = 0m;
        var currencyLines = new List<ProductionReceiptCostCurrencyLineV1>();
        var issueTrace = new List<ProductionReceiptCostIssueTraceV1>(issues.Count);
        foreach (var (id, issue) in issues)
        {
            var kept = issue.Mass - returned.GetValueOrDefault(id);
            var issueAmount = kept * issue.Cost;
            netCost += issueAmount;
            issueTrace.Add(new(id, issue.Mass, returned.GetValueOrDefault(id), kept, issue.Cost, issueAmount));
            if (issue.Currency is { Status: IssueCostCurrencyStatus.Converted, CurrencyId: { } currencyId } currency)
            {
                currencyLines.Add(new(id, currencyId, currency.ForeignUnitCostPerKg, currency.ExchangeRate, issue.Cost, kept,
                    currency.ForeignUnitCostPerKg is { } foreign ? kept * foreign : null,
                    currency.ForeignUnitCostRatio, currency.ForeignUnitCostRatio?.Multiply(kept), currency.ExchangeRateRatio));
            }
        }

        // consumed >= produced > 0 here. URT-D44 / #66 6864631364 (1): the net raw-material kg cost, after returns.
        var rawMaterialCostPerKg = netCost / consumed;
        // URT-D46 / #66 6864631364 (14) and, with scrap in cost, #66 6874860782 (SINAN-CEVAP (10)): the remnant returns
        // at the net raw-material kg cost and leaves the divisor.
        var remnantValue = remnant * rawMaterialCostPerKg;
        var processLossRatio = processLoss / consumed;
        var amounts = new Amounts(netCost, rawMaterialCostPerKg, remnantValue, processLoss, processLossRatio, unexplained,
            currencyLines.ToArray());

        // URT-D75 (1) with scrap in cost and SAT-D316 (1) with scrap out of cost: each weighing is valued at its own
        // month's price and that value is taken off the product cost in both settings; only where it goes differs (the
        // scrap stock with scrap in cost, URT-D75 (3) / URT-D65; the separate expense with scrap out of cost).
        var scrapValue = 0m;
        var scrapTrace = new List<ProductionReceiptCostScrapTraceV1>(weighings.Count);
        var priceUnreadable = false;
        var priceAboveRawMaterialCost = false;
        var priceMissing = false;
        foreach (var weighing in weighings.Values)
        {
            var mass = weighing.Mass.Value;
            switch (weighing.Price)
            {
                // SAT-D326 (4) / #66 6857496471 (4): no scrap kg and no price entered adds nothing.
                case { Status: ScrapPriceReadStatus.Missing, TlPerKg: null } when mass == 0m:
                    scrapTrace.Add(new(weighing.ScrapOutputId, mass, null, 0m));
                    break;
                // URT-D47 / #66 6864631364 (15), URT-D65: the weighing's month has no price yet; the cost waits for it.
                case { Status: ScrapPriceReadStatus.Missing, TlPerKg: null }:
                    priceMissing = true;
                    break;
                // URT-D74 (2): only a price above zero values a weighing; zero or below is unreadable, never a value.
                // SAT-D326 (3) / #66 6857496471 (3), URT-D44 / #66 6864631364 (1): above the net raw-material kg cost
                // stops the calculation.
                case { Status: ScrapPriceReadStatus.Value, TlPerKg: { } price } when price > 0m:
                    priceAboveRawMaterialCost |= price > rawMaterialCostPerKg;
                    var weighingValue = mass * price;
                    scrapValue += weighingValue;
                    scrapTrace.Add(new(weighing.ScrapOutputId, mass, price, weighingValue));
                    break;
                default:
                    priceUnreadable = true;
                    break;
            }
        }

        if (priceUnreadable)
        {
            return ProductionReceiptCostResult.Unreadable(ProductionReceiptCostCodesV1.ScrapPriceUnreadable);
        }

        if (priceAboveRawMaterialCost)
        {
            return ProductionReceiptCostResult.Unreadable(ProductionReceiptCostCodesV1.ScrapPriceAboveRawMaterialCost);
        }

        if (priceMissing)
        {
            return ProductionReceiptCostResult.Pending(ProductionReceiptCostCodesV1.ScrapPriceMissing);
        }

        // URT-D75 (1), (4): (net - usable remnant value - weighed scrap value) / produced kg; golden 1000 kg x 40 TL,
        // 900 kg good, 50 kg remnant, 50 kg scrap x 10 TL: (40000 - 2000 - 500) / 900 = 41.666... TL per kg. The scrap
        // value is no separate expense with scrap in cost, so the expense line stays zero there.
        var numerator = netCost - remnantValue - scrapValue;
        var costPerKg = numerator / produced;
        var trace = new ProductionReceiptCostTraceV1(issueTrace.ToArray(), consumed, netCost, rawMaterialCostPerKg, remnant,
            remnantValue, scrapTrace.ToArray(), scrapValue, input.GoodProduct.Value, input.SecondQuality.Value, produced,
            numerator, costPerKg);
        var value = input.ScrapSetting == ScrapCostSettingStatus.On
            ? Value(input, costPerKg, 0m, amounts, ProductionReceiptCostCodesV1.BasisWithScrap)
            : Value(input, costPerKg, scrapValue, amounts, ProductionReceiptCostCodesV1.BasisWithoutScrap);
        return value.Status == ProductionReceiptCostStatus.Value ? value with { Trace = trace } : value;
    }

    // SAT-D326 (2) / #66 6857496471 (2): a cost per kg of zero or below (also one that underflows to zero in decimal)
    // is unreadable, never a value.
    private static ProductionReceiptCostResult Value(ProductionReceiptCostInput input, decimal costPerKg,
        decimal scrapExpense, Amounts amounts, string basis) =>
        costPerKg <= 0m
            ? ProductionReceiptCostResult.Unreadable(ProductionReceiptCostCodesV1.ProductCostNotPositive)
            : new ProductionReceiptCostResult(ProductionReceiptCostStatus.Value, costPerKg,
                amounts.RawMaterialCostPerKg, amounts.RemnantValue, scrapExpense, amounts.ProcessLoss,
                amounts.ProcessLossRatio, amounts.Unexplained, basis,
                input.NonKgInput is NonKgInputUse.PaintLitres or NonKgInputUse.PaintLitresAndOther
                    ? new ProductionReceiptCostNoteV1(ProductionReceiptCostCodesV1.PaintCostNotIncluded,
                        ProductionReceiptCostTextsV1.PaintCostNotIncluded)
                    : null,
                input.NonKgInput is NonKgInputUse.Other or NonKgInputUse.PaintLitresAndOther
                    ? new ProductionReceiptCostNoteV1(ProductionReceiptCostCodesV1.NonKgInputCostNotIncluded,
                        ProductionReceiptCostTextsV1.NonKgInputCostNotIncluded)
                    : null,
                null,
                null)
            {
                NetInputCost = amounts.NetCost,
                CurrencyLines = amounts.CurrencyLines,
            };

    private static bool IsWellFormed(IssueCostCurrencyInput currency) => currency switch
    {
        { Status: IssueCostCurrencyStatus.FunctionalCurrency, CurrencyId: null, ForeignUnitCostPerKg: null,
            ExchangeRate: null, ForeignUnitCostRatio: null, ExchangeRateRatio: null } => true,
        { Status: IssueCostCurrencyStatus.Converted, CurrencyId: { } id } => id != Guid.Empty
            && (currency.ForeignUnitCostPerKg is > 0m && currency.ForeignUnitCostRatio is null
                || currency.ForeignUnitCostPerKg is null && currency.ForeignUnitCostRatio?.IsValid() == true)
            && (currency.ExchangeRate is > 0m && (currency.ExchangeRateRatio is null || currency.ExchangeRateRatio.IsValid() && currency.ExchangeRateRatio.Represents(currency.ExchangeRate.Value))
                || currency.ExchangeRate is null && currency.ExchangeRateRatio?.IsValid() == true),
        _ => false,
    };

    // URT-D55 (2): no rate recorded, so no TL cost either; the foreign cost is shown when known.
    private static bool IsRateMissing(IssueCostInput issue) =>
        issue is
        {
            UnitCostPerKg: null,
            Currency: { Status: IssueCostCurrencyStatus.RateMissing, CurrencyId: { } id, ExchangeRate: null, ExchangeRateRatio: null } currency
        }
        && id != Guid.Empty
        && (currency.ForeignUnitCostRatio is null && currency.ForeignUnitCostPerKg is null or > 0m
            || currency.ForeignUnitCostPerKg is null && currency.ForeignUnitCostRatio?.IsValid() == true);

    private readonly record struct Amounts(decimal NetCost, decimal RawMaterialCostPerKg, decimal RemnantValue,
        decimal ProcessLoss, decimal ProcessLossRatio, decimal Unexplained,
        IReadOnlyList<ProductionReceiptCostCurrencyLineV1> CurrencyLines);
}
