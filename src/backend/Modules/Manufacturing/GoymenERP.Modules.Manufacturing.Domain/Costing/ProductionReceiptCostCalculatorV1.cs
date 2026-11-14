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
/// (SAT-D317 (2)). A stamped cost of zero is not taken as a cost: how a zero-cost issue reads in the production cost
/// (alignment with SAT-D297, where a zero unit cost is "cost unknown") is left to the port contract and is not
/// decided (SINAN_KARARLARI_2026-11-13 "Sinirlar"), so until then zero gives <c>input_cost_unreadable</c> and no
/// amount.
/// </summary>
public sealed record IssueCostInput(Guid IssueMovementId, Kilograms Mass, IssueCostReadStatus Status,
    decimal? UnitCostPerKg);

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
/// The scrap price in TL per kg that values one scrap weighing when scrap is out of cost (SAT-D316 (1)). It is the
/// monthly scrap price entered by hand (SAT-D316 (3)-(4)) of the month the scrap was weighed in: each weighing takes
/// its own month's price, so an order that spans two months values each weighing at its own month's price
/// (URT-D47 / #66 6864631364 (15)). The caller reads that month's price for each weighing; the calculator takes it as
/// an explicit input and never picks a month or a value. A weighing with scrap kg whose month has no price entered
/// (<see cref="Missing"/>) leaves the cost pending until the price is entered (URT-D47 / #66 6864631364 (15), the
/// chosen option: "fiyat girilene kadar bekler"); a price that cannot be read, or a malformed one, gives no amount
/// (SAT-D316 (6)). A weighing of zero kg with no price entered is computed with no expense (SAT-D326 (4) /
/// #66 6857496471 (4)). A price above the order's net raw-material kg cost stops the calculation (SAT-D326 (3) /
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
/// There is no default: an undefined value is <c>cost_input_invalid</c>.
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
    NonKgInputUse NonKgInput);

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
/// scrap is in the product cost or when there is no weighed scrap); and <see cref="ProcessLossKg"/>: the declared
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
/// no amount, kg, ratio or note (never zero).
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
}

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
/// answering the remnant share of URT-D46 (6); example: the same order, remnant 2000 TL, (40000 - 2000) / 900 =
/// 42.22 TL per kg);</item>
/// <item>scrap in cost (default, SAT-D305 (2)): cost per kg = (net - usable remnant value) / (good product kg + second
/// quality kg), the same for both classes (SAT-D307 (1)-(2)), so scrap, process loss and any unexplained kg raise the
/// cost of what was produced;</item>
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
/// equal to it computes. The check runs on each weighing's price where prices are used (scrap out of cost), also on a
/// weighing of zero kg (author reading). Because no weighing's price is above the net raw-material kg cost, the remnant
/// and scrap values together never exceed the net input cost;</item>
/// <item>a cost per kg of zero or below is unreadable, never a value (SAT-D326 (2) / #66 6857496471 (2); "zero or
/// below" is the text of the option chosen in 6857438754 "2-a", and the note under (2), not Sinan's words, aligns it
/// with SAT-D297);</item>
/// <item>kg inputs only: the cost of the Paint process's paint, tracked in litres, and of any other input consumed in
/// pieces, litres or metres is not loaded on the product in v1, and the result carries a note for each
/// (SAT-D327 / #66 6857496471 (5), URT-D45 / #66 6864631364 (13), <see cref="NonKgInputUse"/>);</item>
/// <item>no labour or overhead in v1. The calculator carries no currency: issue costs and the scrap prices are taken
/// as TL per kg as given (for a purchase in a foreign currency, the TL value of SAT-D295 (2) / BS-1); the port
/// contract guarantees that and the calculator does not check it. Nothing is rounded here: SAT-D265 sets the moving
/// average's internal precision at 4 decimals (2) and keeps the existing Money rounding, half away from zero (4);
/// where they are applied to the cost per kg, the remnant value and the scrap expense, and where a rounding remainder
/// goes, are the port contract's to fix (SINAN_KARARLARI_2026-11-13 "Sinirlar").</item>
/// </list>
/// The cost is calculated for closed orders only; an open order gives pending (<c>order_not_closed</c>) and no amount
/// (SAT-D317 (1)). In a closed order an issue cost that is still pending is unreadable, not pending. More out than in
/// gives no amount even inside the closing tolerance (URT-D40 (1)-(2), URT-D41 (1)): "out" is good product + second
/// quality + usable remnant + weighed scrap + declared process loss and "in" is the consumed kg (issued - returned),
/// the reading of URT-D40 (3). The rule is measured per input coil at order closing (URT-D42 (1)), which is the
/// closing slice's work; this calculator has the order totals only and refuses when the totals alone already break
/// the rule. The short side (the tolerance and the approval of URT-D6, URT-D41 (2), URT-D42 (2)) is the closing
/// step's too: the calculator takes a closed order as having passed it and puts no limit on the unexplained kg (author
/// reading). Any issue cost that is not a positive value, an unknown scrap setting, a return without its issue or over
/// its issue, a movement id used twice (also as both an issue and a return), a scrap output id used twice, outputs,
/// remnant, weighed scrap and declared process loss over the consumed kg, a zero divisor, an unreadable or malformed
/// scrap price, a scrap price above the net raw-material kg cost, a cost per kg of zero or below, a missing input,
/// list or element or an undefined <see cref="NonKgInputUse"/> (<c>cost_input_invalid</c>) or an arithmetic overflow
/// (<c>cost_arithmetic_overflow</c>) gives <see cref="ProductionReceiptCostStatus.Unreadable"/>; an unreadable answer
/// wins over a missing scrap price, since entering the price would not make it readable. The calculator does not
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

        var issues = new SortedDictionary<Guid, (decimal Mass, decimal Cost)>();
        foreach (var issue in input.Issues)
        {
            if (issue is null)
            {
                return ProductionReceiptCostResult.Unreadable(ProductionReceiptCostCodesV1.InputInvalid);
            }

            if (issue.Status != IssueCostReadStatus.Value || issue.UnitCostPerKg is not { } cost || cost <= 0m)
            {
                return ProductionReceiptCostResult.Unreadable(ProductionReceiptCostCodesV1.InputCostUnreadable);
            }

            if (!issues.TryAdd(issue.IssueMovementId, (issue.Mass.Value, cost)))
            {
                return ProductionReceiptCostResult.Unreadable(ProductionReceiptCostCodesV1.MassUnbalanced);
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
        var netCost = 0m;
        var consumed = 0m;
        foreach (var (id, issue) in issues)
        {
            var kept = issue.Mass - returned.GetValueOrDefault(id);
            netCost += kept * issue.Cost;
            consumed += kept;
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

        // consumed >= produced > 0 here. URT-D44 / #66 6864631364 (1): the net raw-material kg cost, after returns.
        var rawMaterialCostPerKg = netCost / consumed;
        // URT-D46 / #66 6864631364 (14) and, with scrap in cost, #66 6874860782 (SINAN-CEVAP (10)): the remnant returns
        // at the net raw-material kg cost and leaves the divisor.
        var remnantValue = remnant * rawMaterialCostPerKg;
        var processLossRatio = processLoss / consumed;
        var amounts = new Amounts(rawMaterialCostPerKg, remnantValue, processLoss, processLossRatio, unexplained);
        if (input.ScrapSetting == ScrapCostSettingStatus.On)
        {
            return Value(input, (netCost - remnantValue) / produced, 0m, amounts,
                ProductionReceiptCostCodesV1.BasisWithScrap);
        }

        var scrapExpense = 0m;
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
                    break;
                // URT-D47 / #66 6864631364 (15): the weighing's month has no price yet; the cost waits for it.
                case { Status: ScrapPriceReadStatus.Missing, TlPerKg: null }:
                    priceMissing = true;
                    break;
                // SAT-D326 (3) / #66 6857496471 (3), URT-D44 / #66 6864631364 (1): above the net raw-material kg cost
                // stops the calculation.
                case { Status: ScrapPriceReadStatus.Value, TlPerKg: { } price } when price >= 0m:
                    priceAboveRawMaterialCost |= price > rawMaterialCostPerKg;
                    scrapExpense += mass * price;
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

        return Value(input, (netCost - remnantValue - scrapExpense) / produced, scrapExpense, amounts,
            ProductionReceiptCostCodesV1.BasisWithoutScrap);
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
                null);

    private readonly record struct Amounts(decimal RawMaterialCostPerKg, decimal RemnantValue, decimal ProcessLoss,
        decimal ProcessLossRatio, decimal Unexplained);
}
