using GoymenERP.Modules.Manufacturing.Domain.Common;

namespace GoymenERP.Modules.Manufacturing.Domain.Costing;

/// <summary>
/// The answer of one stamped issue cost as Inventory gives it (SAT-D305 (4), Stok answers in Sinan #66 6823091345).
/// </summary>
public enum IssueCostReadStatus
{
    Value = 1,
    Pending = 2,
    Unreadable = 3,
}

/// <summary>
/// One input issue of the order with the moving-average unit cost (TL per kg) Inventory stamped on the issue
/// movement at issue time. <see cref="UnitCostPerKg"/> is set only for <see cref="IssueCostReadStatus.Value"/>.
/// </summary>
public sealed record IssueCostInput(Guid IssueMovementId, Kilograms Mass, IssueCostReadStatus Status,
    decimal? UnitCostPerKg);

/// <summary>
/// One return of unused input, linked to the issue movement it reverses (Stok answer 3): it is valued at that
/// issue's stamped cost, never at a new average.
/// </summary>
public sealed record ReturnCostInput(Guid ReturnMovementId, Guid IssueMovementId, Kilograms Mass);

/// <summary>The scrap-into-cost company setting as Settings answers it (SAT-D305 (2)).</summary>
public enum ScrapCostSettingStatus
{
    On = 1,
    Off = 2,
    Unreadable = 3,
}

/// <summary>How the scrap price (TL per kg of weighed scrap) was read for the order.</summary>
public enum ScrapPriceReadStatus
{
    Value = 1,
    Missing = 2,
    Unreadable = 3,
}

/// <summary>
/// The scrap (sale / recycling) price in TL per kg that values the weighed scrap when scrap is out of cost
/// (Sinan #66 6826603722 (a)). It is the period's (monthly) scrap price entered by hand in TL per kg; a closed
/// period's price does not change, and a missing price is unreadable (Sinan #66 6826763903 (d)). The calculator
/// takes it as an explicit input and never picks a value: the caller reads the price of the receipt's period.
/// <see cref="TlPerKg"/> is set only for <see cref="ScrapPriceReadStatus.Value"/>.
/// </summary>
public sealed record ScrapPriceInput(ScrapPriceReadStatus Status, decimal? TlPerKg)
{
    public static ScrapPriceInput Missing { get; } = new(ScrapPriceReadStatus.Missing, null);
}

/// <summary>
/// The order's facts the production receipt cost needs: its stock outputs by class, the weighed scrap, the declared
/// process loss and whether it is closed. <see cref="UsableRemnant"/> is carried so a remnant keeps the answer
/// fail-closed: how a remnant shares the input cost is not decided (OC-3). <see cref="DeclaredProcessLoss"/> is the
/// order's declared process loss (the <c>ProcessLoss</c> output class): fire is exactly the weighed scrap plus the
/// declared process loss (#66 5917438293 item 3A). Consumed kg that no output, weighed scrap or declared process
/// loss accounts for is the unexplained difference; it never becomes process loss or scrap ("aciklanamayan fark
/// otomatik fire olmaz", URT-D5).
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
    ScrapPriceInput ScrapPrice);

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
    public const string ScrapPriceUnreadable = "scrap_price_unreadable";
    public const string ScrapValueExceedsInputCost = "scrap_value_exceeds_input_cost";
    public const string InputInvalid = "cost_input_invalid";
    public const string ArithmeticOverflow = "cost_arithmetic_overflow";

    public const string BasisWithScrap = "production-input-average-plus-scrap";
    public const string BasisWithoutScrap = "production-input-average-less-scrap-value";
}

/// <summary>
/// The result: a <see cref="ProductionReceiptCostStatus.Value"/> carries the same TL-per-kg cost for the good
/// product and the second quality output, <see cref="WeighedScrapExpense"/>: the TL of the weighed scrap line of the
/// separate scrap / period expense (zero when scrap is in the product cost), and <see cref="ProcessLossKg"/>: the
/// declared process loss in kg, the report's second line (Sinan #66 6826603722 (b)), with
/// <see cref="ProcessLossRatio"/> = declared process loss kg / consumed kg (consumed = issued - returned), a
/// fraction (0.02 = 2 %), not a percent. The process loss line carries kg and ratio only: its value is zero and its
/// cost stays on the good product (Sinan #66 6826763903 (c)). <see cref="UnexplainedKg"/> is the consumed kg no
/// output, weighed scrap or declared process loss accounts for (never negative: more declared than consumed is
/// <c>mass_unbalanced</c>); it is outside the scrap / process loss lines and never fire (URT-D5), and like the
/// process loss it adds nothing to the expense, so the cost per kg is the same whichever of the two a kg is in.
/// A pending or unreadable result carries no amount, kg or ratio (never zero).
/// </summary>
public sealed record ProductionReceiptCostResult(ProductionReceiptCostStatus Status, decimal? CostPerKg,
    decimal? WeighedScrapExpense, decimal? ProcessLossKg, decimal? ProcessLossRatio, decimal? UnexplainedKg,
    string? CostBasis, string? UnreadableCode)
{
    public static ProductionReceiptCostResult Pending { get; } = new(ProductionReceiptCostStatus.Pending, null, null,
        null, null, null, null, null);

    public static ProductionReceiptCostResult Unreadable(string code) =>
        new(ProductionReceiptCostStatus.Unreadable, null, null, null, null, null, null, code);
}

/// <summary>
/// SAT-D305 (4) and Sinan #66 (6816723313, item 4): the single deterministic production receipt cost of v1.
/// <list type="bullet">
/// <item>net input cost = sum(issued kg x stamped issue cost) - sum(returned kg x the linked issue's stamped cost);</item>
/// <item>scrap in cost (default, SAT-D305 (2)): cost per kg = net / (good product kg + second quality kg), the
/// same for both classes (SAT-D307 (1)-(2)), so scrap, process loss and any unexplained kg raise the cost of what
/// was produced;</item>
/// <item>scrap out of cost (SAT-D308 answering the SAT-D307 (3) note, Sinan #66 6818566934 item 4): scrap is not
/// loaded on the product. The weighed scrap is valued at the scrap price and the rest of the net cost is loaded on
/// the product (Sinan #66 6826603722 (a)): weighed scrap expense = weighed scrap kg x scrap price, cost per kg =
/// (net - weighed scrap expense) / (good product kg + second quality kg). The declared process loss is a separate
/// report line of the same expense (6826603722 (b)) with kg and ratio only: its value is zero, so its cost stays
/// on the product (6826763903 (c)). The scrap price is the period's hand-entered TL per kg (6826763903 (d)); a
/// missing or unreadable scrap price is unreadable;</item>
/// <item>no labour or overhead in v1. The quotient is not rounded here: the precision and rounding rule are the
/// Inventory average-cost calculator's (EK59 / SAT-D305), fixed in Inventory's K3 PR and applied there.</item>
/// </list>
/// The order must be closed (no provisional cost, Stok answer 2). Any issue cost that is not a value, an unknown
/// scrap setting, a usable remnant, a return without its issue or over its issue, a movement id used twice (also
/// as both an issue and a return), outputs, weighed scrap and declared process loss over the consumed kg, a zero
/// divisor, a missing list or element (<c>cost_input_invalid</c>) or an arithmetic overflow
/// (<c>cost_arithmetic_overflow</c>) gives <see cref="ProductionReceiptCostStatus.Unreadable"/>; the calculator
/// does not throw for its input's content. Sums run in <see cref="IssueCostInput.IssueMovementId"/> order, so the
/// result does not depend on the order the movements are given in.
/// </summary>
public static class ProductionReceiptCostCalculatorV1
{
    public const int ContractVersion = 1;

    public static ProductionReceiptCostResult Calculate(ProductionReceiptCostInput input)
    {
        ArgumentNullException.ThrowIfNull(input);
        if (!input.OrderClosed)
        {
            return ProductionReceiptCostResult.Pending;
        }

        if (input.Issues is null || input.Returns is null)
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

            if (issue.Status != IssueCostReadStatus.Value || issue.UnitCostPerKg is not { } cost || cost < 0m)
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
        var unexplained = consumed - produced - weighedScrap - processLoss;
        if (produced <= 0m || unexplained < 0m)
        {
            return ProductionReceiptCostResult.Unreadable(ProductionReceiptCostCodesV1.MassUnbalanced);
        }

        var processLossRatio = processLoss / consumed;
        if (input.ScrapSetting == ScrapCostSettingStatus.On)
        {
            return new ProductionReceiptCostResult(ProductionReceiptCostStatus.Value, netCost / produced, 0m,
                processLoss, processLossRatio, unexplained, ProductionReceiptCostCodesV1.BasisWithScrap, null);
        }

        if (input.ScrapPrice is not { Status: ScrapPriceReadStatus.Value, TlPerKg: { } price } || price < 0m)
        {
            return ProductionReceiptCostResult.Unreadable(ProductionReceiptCostCodesV1.ScrapPriceUnreadable);
        }

        var scrapExpense = weighedScrap * price;
        if (scrapExpense > netCost)
        {
            return ProductionReceiptCostResult.Unreadable(ProductionReceiptCostCodesV1.ScrapValueExceedsInputCost);
        }

        return new ProductionReceiptCostResult(ProductionReceiptCostStatus.Value, (netCost - scrapExpense) / produced,
            scrapExpense, processLoss, processLossRatio, unexplained, ProductionReceiptCostCodesV1.BasisWithoutScrap,
            null);
    }
}
