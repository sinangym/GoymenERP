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
/// (SAT-D317 (2)) and it counts in the raw-material kg cost (see <see cref="ProductionReceiptCostCalculatorV1"/>).
/// A stamped cost of zero is not taken as a cost: how a zero-cost issue reads in the production cost (alignment
/// with SAT-D297, where a zero unit cost is "cost unknown") is left to the port contract and is not decided
/// (SINAN_KARARLARI_2026-11-13 "Sinirlar"), so until then zero gives <c>input_cost_unreadable</c> and no amount.
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
/// How the scrap price (TL per kg of weighed scrap) was read for the order: <see cref="Missing"/> is a price that
/// was not entered for the period, <see cref="Unreadable"/> one that could not be read.
/// </summary>
public enum ScrapPriceReadStatus
{
    Value = 1,
    Missing = 2,
    Unreadable = 3,
}

/// <summary>
/// The scrap price in TL per kg that values the weighed scrap when scrap is out of cost (SAT-D316 (1)). It is the
/// period's scrap price entered by hand (SAT-D316 (3)-(4)); the calculator takes it as an explicit input and never
/// picks a value. With weighed scrap to value, a missing or unreadable price gives no amount (SAT-D316 (6)); with
/// no weighed scrap and no price entered (<see cref="Missing"/>) the cost is computed (#66 6857496471 (4)). A price
/// above the order's raw-material kg cost stops the calculation (#66 6857496471 (3), see
/// <see cref="ProductionReceiptCostCalculatorV1"/>). Which period's price the caller reads (the order's or the
/// receipt's) and who enters it are not decided (SINAN_KARARLARI_2026-11-13 "Sinirlar"); the calculator chooses
/// neither. <see cref="TlPerKg"/> is set only for <see cref="ScrapPriceReadStatus.Value"/>.
/// </summary>
public sealed record ScrapPriceInput(ScrapPriceReadStatus Status, decimal? TlPerKg)
{
    public static ScrapPriceInput Missing { get; } = new(ScrapPriceReadStatus.Missing, null);
}

/// <summary>
/// Whether the order consumed an input that is not tracked in kg; the calculator costs kg inputs only.
/// <list type="bullet">
/// <item><see cref="None"/>: kg inputs only.</item>
/// <item><see cref="PaintLitres"/>: the Paint process's paint, tracked in litres (Master PNT-006, URT-D9,
/// URT-D41 (3)). Its cost is not loaded on the product in v1 and the result says so
/// (<c>non_kg_input_cost_not_included</c>) instead of leaving it out silently (#66 6857496471 (5)).</item>
/// <item><see cref="Other"/>: any other input that is not tracked in kg, also together with paint. How it is
/// costed is not decided (processes other than Paint whose input is not in kg, SINAN_KARARLARI_2026-11-13
/// "Sinirlar"), so the result is unreadable (<c>non_kg_input_unpriced</c>) and carries no amount.</item>
/// </list>
/// There is no default: an undefined value is <c>cost_input_invalid</c>.
/// </summary>
public enum NonKgInputUse
{
    None = 1,
    PaintLitres = 2,
    Other = 3,
}

/// <summary>
/// The order's facts the production receipt cost needs: its stock outputs by class, the weighed scrap, the declared
/// process loss, the inputs not tracked in kg and whether it is closed. <see cref="UsableRemnant"/> is carried so a
/// remnant keeps the answer fail-closed: how a remnant shares the input cost is not decided (OC-3); a caller must
/// give a remnant here, never as second quality, or it would take a product's cost. <see cref="DeclaredProcessLoss"/>
/// is the order's declared process loss (the <c>ProcessLoss</c> output class): fire is exactly the weighed scrap plus
/// the declared process loss (#66 5917438293 item 3A). Consumed kg that no output, weighed scrap or declared process
/// loss accounts for is the unexplained difference; it never becomes process loss or scrap ("aciklanamayan fark
/// otomatik fire olmaz", URT-D5). WIP and measurement correction, which DEC-PRDCT-006 reconciles apart, are not
/// inputs here: which side of the out / in comparison they belong on is not decided (NEEDS-SINAN,
/// SINAN_KARARLARI_2026-11-13); a caller gives neither as any of the classes here.
/// </summary>
public sealed record ProductionReceiptCostInput(
    bool OrderClosed,
    IReadOnlyList<IssueCostInput> Issues,
    IReadOnlyList<ReturnCostInput> Returns,
    Kilograms GoodProduct,
    Kilograms SecondQuality,
    Kilograms UsableRemnant,
    Kilograms WeighedScrap,
    Kilograms DeclaredProcessLoss,
    ScrapCostSettingStatus ScrapSetting,
    ScrapPriceInput ScrapPrice,
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
    public const string UsableRemnantUnpriced = "usable_remnant_unpriced";
    public const string NonKgInputUnpriced = "non_kg_input_unpriced";
    public const string ScrapPriceUnreadable = "scrap_price_unreadable";
    public const string ScrapPriceAboveRawMaterialCost = "scrap_price_above_raw_material_cost";
    public const string ScrapValueExceedsInputCost = "scrap_value_exceeds_input_cost";
    public const string ProductCostNotPositive = "product_cost_not_positive";
    public const string InputInvalid = "cost_input_invalid";
    public const string ArithmeticOverflow = "cost_arithmetic_overflow";

    // A marker on a value, not an unreadable code.
    public const string NonKgInputCostNotIncluded = "non_kg_input_cost_not_included";

    public const string BasisWithScrap = "production-input-average-plus-scrap";
    public const string BasisWithoutScrap = "production-input-average-less-scrap-value";
}

/// <summary>
/// The result: a <see cref="ProductionReceiptCostStatus.Value"/> carries the same TL-per-kg cost for the good
/// product and the second quality output, always above zero (#66 6857496471 (2)), <see cref="WeighedScrapExpense"/>:
/// the TL of the weighed scrap line of the separate scrap / period expense (zero when scrap is in the product cost
/// or when there is no weighed scrap), and <see cref="ProcessLossKg"/>: the declared process loss in kg, with
/// <see cref="ProcessLossRatio"/> = declared process loss kg / consumed kg (consumed = issued - returned), a fraction
/// (0.02 = 2 %), not a percent. The process loss carries no amount: its value is zero and its cost stays on the
/// product (SAT-D316 (2)). The kg and the ratio are display facts only and no money depends on them; that a report
/// shows them as a separate line and that the ratio's divisor is the consumed kg are author readings, not decisions
/// (#66 6826603722 (b) and the report sentence of 6826763903 (c) were not asked, SINAN_KARARLARI_2026-11-13
/// "Sinirlar"). <see cref="UnexplainedKg"/> is the consumed kg no output, weighed scrap or declared process loss
/// accounts for (never negative: more out than in is <c>mass_unbalanced</c>, URT-D40); it is outside the scrap /
/// process loss lines and never fire (URT-D5), and like the process loss it adds nothing to the expense, so the cost
/// per kg is the same whichever of the two a kg is in. <see cref="CostExclusion"/> is set on a value only:
/// <c>non_kg_input_cost_not_included</c> when the order consumed the Paint process's paint, whose cost is not in
/// the cost per kg (#66 6857496471 (5)); null otherwise. A pending or unreadable result carries no amount, kg, ratio
/// or marker (never zero).
/// </summary>
public sealed record ProductionReceiptCostResult(ProductionReceiptCostStatus Status, decimal? CostPerKg,
    decimal? WeighedScrapExpense, decimal? ProcessLossKg, decimal? ProcessLossRatio, decimal? UnexplainedKg,
    string? CostBasis, string? CostExclusion, string? UnreadableCode)
{
    public static ProductionReceiptCostResult Pending { get; } = new(ProductionReceiptCostStatus.Pending, null, null,
        null, null, null, null, null, null);

    public static ProductionReceiptCostResult Unreadable(string code) =>
        new(ProductionReceiptCostStatus.Unreadable, null, null, null, null, null, null, null, code);
}

/// <summary>
/// SAT-D305 (4), SAT-D307, SAT-D316, SAT-D317, URT-D40 and Sinan's answers of #66 6857496471: the single
/// deterministic production receipt cost of v1.
/// <list type="bullet">
/// <item>net input cost = sum(issued kg x stamped issue cost) - sum(returned kg x the linked issue's stamped cost);</item>
/// <item>scrap in cost (default, SAT-D305 (2)): cost per kg = net / (good product kg + second quality kg), the
/// same for both classes (SAT-D307 (1)-(2)), so scrap, process loss and any unexplained kg raise the cost of what
/// was produced;</item>
/// <item>scrap out of cost (SAT-D316, which sets the amount of SAT-D308 (1)-(2) and partly replaces them): the
/// weighed scrap is valued at the scrap price, that amount is taken off the cost and reported apart, and the rest
/// of the net cost is loaded on the product (SAT-D316 (1)): weighed scrap expense = weighed scrap kg x scrap
/// price, cost per kg = (net - weighed scrap expense) / (good product kg + second quality kg). The divisor is
/// Sinan's answer #66 6857496471 (1) ("Saglam + 2. kalite kg"), whose example the option text counted as the
/// golden example: 1000 kg x 40 TL, 900 kg good + 50 kg second quality, 50 kg weighed scrap x 10 TL = 500 TL
/// expense, 39500 / 950 = 41.578947... TL per kg. The process loss has value zero, so its cost stays on the
/// product (SAT-D316 (2)). The scrap price is the period's hand-entered TL per kg (SAT-D316 (3)); with weighed
/// scrap, a missing or unreadable scrap price is unreadable (SAT-D316 (6)); with no weighed scrap and no price
/// entered the cost is computed with a zero expense (#66 6857496471 (4)). The expense is only computed here; no
/// expense record is enabled (SAT-D308 (5));</item>
/// <item>a scrap price above the order's raw-material kg cost stops the calculation so the price is checked
/// (#66 6857496471 (3), "Hesap dursun, fiyat kontrol edilsin"); a price equal to it computes. The raw-material kg
/// cost is the weighted issue unit cost of the order's kg inputs, sum(issued kg x stamped issue cost) /
/// sum(issued kg), before returns. That definition is this code's reading, not Sinan's words, and is to be
/// confirmed; the reading written under that answer (net cost / consumed kg) gives the same value when nothing is
/// returned. The check runs where the price is used (scrap out of cost), also when a price is given with no weighed
/// scrap (author reading);</item>
/// <item>a cost per kg of zero or below is unreadable, never a value (#66 6857496471 (2), the same logic as
/// SAT-D297);</item>
/// <item>kg inputs only: the cost of the Paint process's paint, tracked in litres, is not loaded on the product in
/// v1 and the result is marked (#66 6857496471 (5), <see cref="NonKgInputUse"/>); any other input not tracked in
/// kg is unreadable;</item>
/// <item>no labour or overhead in v1. The calculator carries no currency: issue costs and the scrap price are taken
/// as TL per kg as given (for a purchase in a foreign currency, the TL value of SAT-D295 (2) / BS-1); the port
/// contract guarantees that and the calculator does not check it. Nothing is rounded here: the internal precision
/// and the rounding mode are SAT-D265 (2)-(4) (4 decimals, half away from zero, once); where they are applied to
/// the cost per kg and to the scrap expense, and where a rounding remainder goes, are the port contract's to fix
/// (SINAN_KARARLARI_2026-11-13 "Sinirlar").</item>
/// </list>
/// The cost is calculated for closed orders only; an open order gives pending and no amount (SAT-D317 (1)). In a
/// closed order an issue cost that is still pending is unreadable, not pending. More out than in gives no amount
/// even inside the closing tolerance (URT-D40 (1)-(2), URT-D41 (1)): "out" is good product + second quality +
/// weighed scrap + declared process loss and "in" is the consumed kg (issued - returned), the reading of
/// URT-D40 (3). The rule is measured per input coil at order closing (URT-D42 (1)), which is the closing slice's
/// work; this calculator has the order totals only and refuses when the totals alone already break the rule. The
/// short side (the tolerance and the approval of URT-D6, URT-D41 (2), URT-D42 (2)) is the closing step's too: the
/// calculator takes a closed order as having passed it and puts no limit on the unexplained kg (author reading).
/// Any issue cost that is not a positive value, an unknown scrap setting, a usable remnant, an input not tracked in
/// kg other than paint, a return without its issue or over its issue, a movement id used twice (also as both an
/// issue and a return), outputs, weighed scrap and declared process loss over the consumed kg, a zero divisor, a
/// scrap price above the raw-material kg cost, a scrap value over the net cost, a cost per kg of zero or below, a
/// missing input, list or element or an undefined <see cref="NonKgInputUse"/> (<c>cost_input_invalid</c>) or an
/// arithmetic overflow (<c>cost_arithmetic_overflow</c>) gives <see cref="ProductionReceiptCostStatus.Unreadable"/>;
/// the calculator does not throw for its input's content. Sums run in <see cref="IssueCostInput.IssueMovementId"/>
/// order, so the result does not depend on the order the movements are given in.
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
            return ProductionReceiptCostResult.Pending;
        }

        if (input.Issues is null || input.Returns is null
            || input.NonKgInput is not (NonKgInputUse.None or NonKgInputUse.PaintLitres or NonKgInputUse.Other))
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

        if (input.ScrapSetting is not (ScrapCostSettingStatus.On or ScrapCostSettingStatus.Off))
        {
            return ProductionReceiptCostResult.Unreadable(ProductionReceiptCostCodesV1.ScrapSettingUnreadable);
        }

        if (input.UsableRemnant.Value > 0m)
        {
            return ProductionReceiptCostResult.Unreadable(ProductionReceiptCostCodesV1.UsableRemnantUnpriced);
        }

        if (input.NonKgInput == NonKgInputUse.Other)
        {
            return ProductionReceiptCostResult.Unreadable(ProductionReceiptCostCodesV1.NonKgInputUnpriced);
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

        var produced = input.GoodProduct.Value + input.SecondQuality.Value;
        var weighedScrap = input.WeighedScrap.Value;
        var processLoss = input.DeclaredProcessLoss.Value;
        // URT-D40 (1)-(3): what came out cannot be more than what went in; no tolerance on this side (URT-D41 (1)).
        var unexplained = consumed - produced - weighedScrap - processLoss;
        if (produced <= 0m || unexplained < 0m)
        {
            return ProductionReceiptCostResult.Unreadable(ProductionReceiptCostCodesV1.MassUnbalanced);
        }

        var processLossRatio = processLoss / consumed;
        if (input.ScrapSetting == ScrapCostSettingStatus.On)
        {
            return Value(input, netCost / produced, 0m, processLoss, processLossRatio, unexplained,
                ProductionReceiptCostCodesV1.BasisWithScrap);
        }

        var scrapExpense = 0m;
        // #66 6857496471 (4): no weighed scrap and no scrap price entered for the period computes with no expense.
        if (weighedScrap > 0m || input.ScrapPrice is not { Status: ScrapPriceReadStatus.Missing, TlPerKg: null })
        {
            if (input.ScrapPrice is not { Status: ScrapPriceReadStatus.Value, TlPerKg: { } price } || price < 0m)
            {
                return ProductionReceiptCostResult.Unreadable(ProductionReceiptCostCodesV1.ScrapPriceUnreadable);
            }

            // #66 6857496471 (3): a scrap price above the raw-material kg cost stops the calculation.
            if (price > RawMaterialCostPerKg(issues))
            {
                return ProductionReceiptCostResult.Unreadable(
                    ProductionReceiptCostCodesV1.ScrapPriceAboveRawMaterialCost);
            }

            scrapExpense = weighedScrap * price;
            if (scrapExpense > netCost)
            {
                return ProductionReceiptCostResult.Unreadable(ProductionReceiptCostCodesV1.ScrapValueExceedsInputCost);
            }
        }

        return Value(input, (netCost - scrapExpense) / produced, scrapExpense, processLoss, processLossRatio,
            unexplained, ProductionReceiptCostCodesV1.BasisWithoutScrap);
    }

    // #66 6857496471 (2): a cost per kg of zero or below (also one that rounds to zero) is unreadable, never a value.
    private static ProductionReceiptCostResult Value(ProductionReceiptCostInput input, decimal costPerKg,
        decimal scrapExpense, decimal processLoss, decimal processLossRatio, decimal unexplained, string basis) =>
        costPerKg <= 0m
            ? ProductionReceiptCostResult.Unreadable(ProductionReceiptCostCodesV1.ProductCostNotPositive)
            : new ProductionReceiptCostResult(ProductionReceiptCostStatus.Value, costPerKg, scrapExpense, processLoss,
                processLossRatio, unexplained, basis,
                input.NonKgInput == NonKgInputUse.PaintLitres
                    ? ProductionReceiptCostCodesV1.NonKgInputCostNotIncluded
                    : null,
                null);

    // The weighted issue unit cost of the order's kg inputs before returns (author reading of "emirde kullanilan
    // hammaddenin kg maliyeti", #66 6857496471 (3), to be confirmed). The mass check guarantees issued kg > 0.
    private static decimal RawMaterialCostPerKg(SortedDictionary<Guid, (decimal Mass, decimal Cost)> issues)
    {
        var issuedCost = 0m;
        var issuedKg = 0m;
        foreach (var issue in issues.Values)
        {
            issuedCost += issue.Mass * issue.Cost;
            issuedKg += issue.Mass;
        }

        return issuedCost / issuedKg;
    }
}
