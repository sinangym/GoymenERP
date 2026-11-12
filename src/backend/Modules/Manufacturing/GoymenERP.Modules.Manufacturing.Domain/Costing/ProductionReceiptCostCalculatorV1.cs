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

/// <summary>
/// The order's facts the production receipt cost needs: its stock outputs by class and whether it is closed.
/// <see cref="UsableRemnant"/> is carried so a remnant keeps the answer fail-closed: how a remnant shares the
/// input cost is not decided.
/// </summary>
public sealed record ProductionReceiptCostInput(
    bool OrderClosed,
    IReadOnlyList<IssueCostInput> Issues,
    IReadOnlyList<ReturnCostInput> Returns,
    Kilograms GoodProduct,
    Kilograms SecondQuality,
    Kilograms UsableRemnant,
    ScrapCostSettingStatus ScrapSetting);

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

    public const string BasisWithScrap = "production-input-average-plus-scrap";
    public const string BasisWithoutScrap = "production-input-average";
}

/// <summary>
/// The result: a <see cref="ProductionReceiptCostStatus.Value"/> carries the same TL-per-kg cost for the good
/// product and the second quality output, and <see cref="ScrapPeriodExpense"/>: the TL of scrap and process loss
/// reported as a separate scrap / period expense (zero when scrap is in the product cost). Anything else carries no
/// amount (never zero).
/// </summary>
public sealed record ProductionReceiptCostResult(ProductionReceiptCostStatus Status, decimal? CostPerKg,
    decimal? ScrapPeriodExpense, string? CostBasis, string? UnreadableCode)
{
    public static ProductionReceiptCostResult Pending { get; } = new(ProductionReceiptCostStatus.Pending, null, null,
        null, null);

    public static ProductionReceiptCostResult Unreadable(string code) =>
        new(ProductionReceiptCostStatus.Unreadable, null, null, null, code);
}

/// <summary>
/// SAT-D305 (4) and Sinan #66 (6816723313, item 4): the single deterministic production receipt cost of v1.
/// <list type="bullet">
/// <item>net input cost = sum(issued kg x stamped issue cost) - sum(returned kg x the linked issue's stamped cost);</item>
/// <item>scrap in cost (default, SAT-D305 (2)): cost per kg = net / (good product kg + second quality kg), the
/// same for both classes (SAT-D307 (1)-(2)), so scrap and process loss raise the cost of what was produced;</item>
/// <item>scrap out of cost (SAT-D308 answering the SAT-D307 (3) note, Sinan #66 6818566934 item 4): scrap is not loaded on the product, so
/// cost per kg = net / consumed kg (issued - returned) and the scrap and process loss share,
/// net - cost per kg x produced kg, is reported as a separate scrap / period expense;</item>
/// <item>no labour or overhead in v1. The quotient is not rounded here: the precision and rounding rule are the
/// Inventory average-cost calculator's (EK59 / SAT-D305), fixed in Inventory's K3 PR and applied there.</item>
/// </list>
/// The order must be closed (no provisional cost, Stok answer 2). Any issue cost that is not a value, an unknown
/// scrap setting, a usable remnant, a return without its issue or over its issue, or a zero divisor gives
/// <see cref="ProductionReceiptCostStatus.Unreadable"/>.
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

        if (input.Issues.Count == 0)
        {
            return ProductionReceiptCostResult.Unreadable(ProductionReceiptCostCodesV1.MassUnbalanced);
        }

        var issues = new Dictionary<Guid, (decimal Mass, decimal Cost)>();
        foreach (var issue in input.Issues)
        {
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
            if (!returnIds.Add(item.ReturnMovementId) || !issues.TryGetValue(item.IssueMovementId, out var issue))
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

        var netCost = 0m;
        var consumed = 0m;
        foreach (var (id, issue) in issues)
        {
            var kept = issue.Mass - returned.GetValueOrDefault(id);
            netCost += kept * issue.Cost;
            consumed += kept;
        }

        var produced = input.GoodProduct.Value + input.SecondQuality.Value;
        if (produced <= 0m || produced > consumed)
        {
            return ProductionReceiptCostResult.Unreadable(ProductionReceiptCostCodesV1.MassUnbalanced);
        }

        if (input.ScrapSetting == ScrapCostSettingStatus.On)
        {
            return new ProductionReceiptCostResult(ProductionReceiptCostStatus.Value, netCost / produced, 0m,
                ProductionReceiptCostCodesV1.BasisWithScrap, null);
        }

        // The expense is taken from the exact shares, net x (consumed - produced) / consumed, so the product's
        // part and the expense add up to the net cost without a quotient residue being multiplied back.
        return new ProductionReceiptCostResult(ProductionReceiptCostStatus.Value, netCost / consumed,
            netCost * (consumed - produced) / consumed, ProductionReceiptCostCodesV1.BasisWithoutScrap, null);
    }
}
