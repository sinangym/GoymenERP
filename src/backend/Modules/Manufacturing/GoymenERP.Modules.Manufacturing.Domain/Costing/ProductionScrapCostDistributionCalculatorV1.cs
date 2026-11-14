using GoymenERP.Modules.Manufacturing.Domain.Common;
using GoymenERP.Modules.Manufacturing.Domain.Orders;

namespace GoymenERP.Modules.Manufacturing.Domain.Costing;

/// <summary>
/// One physical output label of the order that carries a share of the production receipt cost: a good product, second
/// quality or usable remnant label (the output id is the label's identity in Inventory, <see cref="OutputDeclaration"/>).
/// Scrap labels are not listed here: they are the order's scrap weighings
/// (<see cref="ScrapWeighingInput.ScrapOutputId"/>). Process loss has no label (plan v2 §7.3).
/// </summary>
public sealed record ProductionScrapCostLabelInput(Guid OutputLabelId, OutputClass OutputClass, Kilograms Mass);

/// <summary>
/// The distribution input: the order's production receipt cost input (<see cref="ProductionReceiptCostInput"/>) and its
/// output labels. The labels of each class must add up to exactly that class's kg in the cost input.
/// </summary>
public sealed record ProductionScrapCostDistributionInput(
    ProductionReceiptCostInput Cost,
    IReadOnlyList<ProductionScrapCostLabelInput> Labels);

/// <summary>
/// Stable codes of the distribution, in addition to <see cref="ProductionReceiptCostCodesV1"/>.
/// </summary>
public static class ProductionScrapCostDistributionCodesV1
{
    /// <summary>A label is missing, names an undefined class, or names scrap or process loss.</summary>
    public const string LabelInvalid = "cost_label_invalid";

    /// <summary>A label id is used twice, also as a scrap output id.</summary>
    public const string LabelDuplicate = "cost_label_duplicate";

    /// <summary>The labels of a class do not add up to that class's kg in the cost input.</summary>
    public const string LabelMassMismatch = "cost_label_mass_mismatch";

    /// <summary>
    /// A scrap label's stock cost: URT-D8 has the scrap enter stock with a label, and no decision fixes the cost that
    /// label carries, with scrap in the product cost or out of it. The line carries no amount until it is decided.
    /// </summary>
    public const string ScrapLabelCostNotDecided = "scrap_label_cost_not_decided";

    public const string AllocatedAtProductCost = "allocated-at-product-cost-per-kg";
    public const string AllocatedAtRawMaterialCost = "allocated-at-net-raw-material-cost-per-kg";
}

/// <summary>
/// One label's line. <see cref="UnitCostPerKg"/> and <see cref="AllocatedCost"/> (kg x unit cost) are TL and
/// unrounded; both are null on a scrap line (<see cref="NotAllocatedCode"/> says why).
/// <see cref="SeparateScrapExpense"/> is set on a scrap line only when scrap is out of the product cost: that weighing's
/// kg x its month's scrap price, its share of <see cref="ProductionReceiptCostResult.WeighedScrapExpense"/>
/// (SAT-D316 (1), URT-D47). It is the separate scrap / period expense line, not the label's stock cost.
/// </summary>
public sealed record ProductionScrapCostDistributionLine(
    Guid OutputLabelId,
    OutputClass OutputClass,
    decimal Kilograms,
    decimal? UnitCostPerKg,
    decimal? AllocatedCost,
    decimal? SeparateScrapExpense,
    string? AllocationBasis,
    string? NotAllocatedCode);

/// <summary>
/// The distribution result. A <see cref="ProductionReceiptCostStatus.Value"/> carries the cost result it was built on
/// and one line per label and scrap weighing; <see cref="AllocatedTotal"/> is the exact sum of the allocated lines
/// (unrounded). A pending or unreadable result carries no line and no amount (never zero), and its code says why.
/// </summary>
public sealed record ProductionScrapCostDistributionResult(
    ProductionReceiptCostStatus Status,
    ProductionReceiptCostResult? Cost,
    IReadOnlyList<ProductionScrapCostDistributionLine> Lines,
    decimal? AllocatedTotal,
    string? PendingCode,
    string? UnreadableCode)
{
    public static ProductionScrapCostDistributionResult Pending(string code) =>
        new(ProductionReceiptCostStatus.Pending, null, [], null, code, null);

    public static ProductionScrapCostDistributionResult Unreadable(string code) =>
        new(ProductionReceiptCostStatus.Unreadable, null, [], null, null, code);
}

/// <summary>
/// SAT-D307 scrap cost distribution over the order's output labels (12 November answer item 4), on the single production
/// receipt cost of <see cref="ProductionReceiptCostCalculatorV1"/>; it computes no cost per kg of its own.
/// <list type="bullet">
/// <item>Good product and second quality labels carry the same cost per kg (SAT-D307 (1)-(2), SAT-D326 (1)): allocated =
/// label kg x <see cref="ProductionReceiptCostResult.CostPerKg"/>. With scrap in cost the scrap cost is borne by these
/// two classes only (SAT-D307 (3)); with scrap out of cost the weighed scrap at its month's price is taken off first
/// and reported apart (SAT-D316 (1)). Process loss has value zero and stays on the product (SAT-D316 (2)).</item>
/// <item>A usable remnant label returns at the net raw-material kg cost (URT-D44, URT-D46 / #66 6864631364 (14), with
/// scrap in cost #66 6874860782): allocated = label kg x
/// <see cref="ProductionReceiptCostResult.RawMaterialCostPerKg"/>; the remnant lines add up to
/// <see cref="ProductionReceiptCostResult.UsableRemnantValue"/>.</item>
/// <item>A scrap weighing gives a line with no allocated amount (<c>scrap_label_cost_not_decided</c>, URT-D8): the stock
/// cost of a scrap label is not decided. With scrap out of cost the line shows its share of the separate scrap
/// expense.</item>
/// </list>
/// The amounts are TL, as the cost calculator's are, and nothing is rounded here: where SAT-D265's Money rounding (half
/// away from zero) is applied to a label's amount and where a rounding remainder goes are the port contract's rounding
/// work (URT-S14), not this calculator's. Pending and unreadable answers of the cost calculator are carried through with
/// their codes. An open order is pending before the labels are checked (its labels are not final); otherwise a missing
/// label list or label, a label of class scrap, process loss or an undefined class (<c>cost_label_invalid</c>), a label
/// id used twice or also used as a scrap output id (<c>cost_label_duplicate</c>), or labels whose kg do not add up to
/// their class's kg (<c>cost_label_mass_mismatch</c>) is unreadable, and that wins over a missing scrap price. An
/// arithmetic overflow is <c>cost_arithmetic_overflow</c>. Lines are ordered by class, then label id, so the result does
/// not depend on the order the labels and weighings are given in. The calculator does not throw for its input's
/// content.
/// </summary>
public static class ProductionScrapCostDistributionCalculatorV1
{
    public const int ContractVersion = 1;

    public static ProductionScrapCostDistributionResult Distribute(ProductionScrapCostDistributionInput input)
    {
        if (input?.Cost is null)
        {
            return ProductionScrapCostDistributionResult.Unreadable(ProductionReceiptCostCodesV1.InputInvalid);
        }

        var cost = ProductionReceiptCostCalculatorV1.Calculate(input.Cost);
        if (cost is
            { Status: ProductionReceiptCostStatus.Pending, PendingCode: ProductionReceiptCostCodesV1.OrderNotClosed })
        {
            return ProductionScrapCostDistributionResult.Pending(ProductionReceiptCostCodesV1.OrderNotClosed);
        }

        if (input.Labels is null || input.Cost.ScrapWeighings is null)
        {
            return ProductionScrapCostDistributionResult.Unreadable(ProductionReceiptCostCodesV1.InputInvalid);
        }

        try
        {
            var labelCode = CheckLabels(input.Cost, input.Labels);
            if (labelCode is not null)
            {
                return ProductionScrapCostDistributionResult.Unreadable(labelCode);
            }

            return cost.Status switch
            {
                ProductionReceiptCostStatus.Value => Lines(input, cost),
                ProductionReceiptCostStatus.Pending => ProductionScrapCostDistributionResult.Pending(
                    cost.PendingCode ?? ProductionReceiptCostCodesV1.InputInvalid),
                _ => ProductionScrapCostDistributionResult.Unreadable(
                    cost.UnreadableCode ?? ProductionReceiptCostCodesV1.InputInvalid),
            };
        }
        catch (OverflowException)
        {
            return ProductionScrapCostDistributionResult.Unreadable(ProductionReceiptCostCodesV1.ArithmeticOverflow);
        }
    }

    private static string? CheckLabels(ProductionReceiptCostInput cost, IReadOnlyList<ProductionScrapCostLabelInput> labels)
    {
        var ids = new HashSet<Guid>();
        foreach (var weighing in cost.ScrapWeighings)
        {
            if (weighing is null)
            {
                return ProductionReceiptCostCodesV1.InputInvalid;
            }

            if (!ids.Add(weighing.ScrapOutputId))
            {
                return ProductionScrapCostDistributionCodesV1.LabelDuplicate;
            }
        }

        var good = 0m;
        var second = 0m;
        var remnant = 0m;
        foreach (var label in labels)
        {
            if (label is null
                || label.OutputClass is not (OutputClass.GoodProduct or OutputClass.SecondQuality
                    or OutputClass.UsableRemnant))
            {
                return ProductionScrapCostDistributionCodesV1.LabelInvalid;
            }

            if (!ids.Add(label.OutputLabelId))
            {
                return ProductionScrapCostDistributionCodesV1.LabelDuplicate;
            }

            switch (label.OutputClass)
            {
                case OutputClass.GoodProduct:
                    good += label.Mass.Value;
                    break;
                case OutputClass.SecondQuality:
                    second += label.Mass.Value;
                    break;
                default:
                    remnant += label.Mass.Value;
                    break;
            }
        }

        return good == cost.GoodProduct.Value && second == cost.SecondQuality.Value
            && remnant == cost.UsableRemnant.Value
                ? null
                : ProductionScrapCostDistributionCodesV1.LabelMassMismatch;
    }

    private static ProductionScrapCostDistributionResult Lines(
        ProductionScrapCostDistributionInput input, ProductionReceiptCostResult cost)
    {
        if (cost.CostPerKg is not { } costPerKg || cost.RawMaterialCostPerKg is not { } rawMaterialCostPerKg)
        {
            return ProductionScrapCostDistributionResult.Unreadable(ProductionReceiptCostCodesV1.InputInvalid);
        }

        var scrapOut = input.Cost.ScrapSetting == ScrapCostSettingStatus.Off;
        var lines = new List<ProductionScrapCostDistributionLine>(input.Labels.Count + input.Cost.ScrapWeighings.Count);
        foreach (var label in input.Labels)
        {
            var remnant = label.OutputClass == OutputClass.UsableRemnant;
            var unitCost = remnant ? rawMaterialCostPerKg : costPerKg;
            var allocated = label.Mass.Value * unitCost;
            lines.Add(new ProductionScrapCostDistributionLine(label.OutputLabelId, label.OutputClass, label.Mass.Value,
                unitCost, allocated, null,
                remnant
                    ? ProductionScrapCostDistributionCodesV1.AllocatedAtRawMaterialCost
                    : ProductionScrapCostDistributionCodesV1.AllocatedAtProductCost,
                null));
        }

        foreach (var weighing in input.Cost.ScrapWeighings)
        {
            // The cost calculator has already accepted every price it used (value, not above the raw-material kg cost),
            // and a zero-kg weighing with no price adds nothing (SAT-D326 (4)).
            decimal? expense = scrapOut
                ? weighing.Price.TlPerKg is { } price ? weighing.Mass.Value * price : 0m
                : null;
            lines.Add(new ProductionScrapCostDistributionLine(weighing.ScrapOutputId, OutputClass.Scrap,
                weighing.Mass.Value, null, null, expense, null,
                ProductionScrapCostDistributionCodesV1.ScrapLabelCostNotDecided));
        }

        lines.Sort(static (left, right) =>
        {
            var byClass = left.OutputClass.CompareTo(right.OutputClass);
            return byClass != 0 ? byClass : left.OutputLabelId.CompareTo(right.OutputLabelId);
        });

        // Summed after sorting: unrounded decimal addition is not associative at its last digit.
        var total = 0m;
        foreach (var line in lines)
        {
            total += line.AllocatedCost ?? 0m;
        }

        return new ProductionScrapCostDistributionResult(ProductionReceiptCostStatus.Value, cost, lines, total, null,
            null);
    }
}
