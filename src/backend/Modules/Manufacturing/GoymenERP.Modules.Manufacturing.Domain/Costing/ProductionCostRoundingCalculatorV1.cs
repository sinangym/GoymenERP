using GoymenERP.Modules.Manufacturing.Domain.Orders;

namespace GoymenERP.Modules.Manufacturing.Domain.Costing;

/// <summary>
/// The direction a half is rounded in when the recorded figures are rounded. URT-D54 fixes where the figures are rounded
/// and to how many decimals, not the direction: whether SAT-D11's "2 ondalik, AwayFromZero" of sales amounts applies to
/// this calculation was not asked (URT-D54 (3), NEEDS-SINAN, SINAN_KARARLARI_2026-11-14_URETIM_EK4 "Sinirlar"). The
/// direction is therefore an explicit input of the calculator with no default; the calculator never picks one.
/// </summary>
public enum ProductionCostRoundingDirectionV1
{
    /// <summary>A half is rounded away from zero (<see cref="MidpointRounding.AwayFromZero"/>), as SAT-D11 does.</summary>
    HalfAwayFromZero = 1,

    /// <summary>A half is rounded to the even digit (<see cref="MidpointRounding.ToEven"/>).</summary>
    HalfToEven = 2,
}

/// <summary>
/// How the rounding direction was read: <see cref="NotDecided"/> is a direction nobody has decided yet (the cost waits
/// and carries no figure), <see cref="Unreadable"/> one that could not be read.
/// </summary>
public enum ProductionCostRoundingPolicyStatusV1
{
    Value = 1,
    NotDecided = 2,
    Unreadable = 3,
}

/// <summary>
/// The rounding direction as its source answers it. <see cref="Direction"/> is set only for
/// <see cref="ProductionCostRoundingPolicyStatusV1.Value"/>; anything else, or an undefined value, is
/// <c>cost_rounding_policy_unreadable</c>.
/// </summary>
public sealed record ProductionCostRoundingPolicyInputV1(
    ProductionCostRoundingPolicyStatusV1 Status,
    ProductionCostRoundingDirectionV1? Direction)
{
    public static ProductionCostRoundingPolicyInputV1 NotDecided { get; } =
        new(ProductionCostRoundingPolicyStatusV1.NotDecided, null);
}

/// <summary>The rounding input: the order's distribution input and the rounding direction.</summary>
public sealed record ProductionCostRoundingInputV1(
    ProductionScrapCostDistributionInput Distribution,
    ProductionCostRoundingPolicyInputV1 Policy);

/// <summary>Stable codes of the rounding, in addition to the cost and distribution codes.</summary>
public static class ProductionCostRoundingCodesV1
{
    /// <summary>Pending: the rounding direction is not decided (URT-D54 (3)).</summary>
    public const string PolicyNotDecided = "cost_rounding_policy_not_decided";

    /// <summary>The rounding direction could not be read or is malformed.</summary>
    public const string PolicyUnreadable = "cost_rounding_policy_unreadable";

    /// <summary>A remainder is left and the order has no good product line with kg to carry it (URT-D54 (2)).</summary>
    public const string RemainderLineMissing = "rounding_remainder_line_missing";

    /// <summary>The good product line would be zero or below once it carries the remainder.</summary>
    public const string RemainderNotAbsorbed = "rounding_remainder_not_absorbed";
}

/// <summary>
/// One recorded line. <see cref="RecordedUnitCostPerKg"/> is the line's TL cost per kg at
/// <see cref="ProductionCostRoundingCalculatorV1.RecordedCostPerKgScale"/> decimals; <see cref="RecordedAmountTl"/> is the
/// line's TL at <see cref="ProductionCostRoundingCalculatorV1.RecordedAmountScale"/> decimals, the remainder included on
/// the line that carries it (<see cref="RemainderTl"/>, zero on every other line). Both are null on a scrap line, whose
/// <see cref="NotAllocatedCode"/> says why; <see cref="RecordedSeparateScrapExpenseTl"/> is that weighing's separate
/// scrap expense at the same scale, set only when scrap is out of the product cost (SAT-D316 (1)). A recorded amount is
/// not kg x the recorded unit cost: each is the full-precision figure rounded once (URT-D54 (1)).
/// </summary>
public sealed record ProductionCostRoundedLineV1(
    Guid OutputLabelId,
    OutputClass OutputClass,
    decimal Kilograms,
    decimal? RecordedUnitCostPerKg,
    decimal? RecordedAmountTl,
    decimal? RecordedSeparateScrapExpenseTl,
    decimal RemainderTl,
    string? AllocationBasis,
    string? NotAllocatedCode);

/// <summary>
/// The rounded result. A <see cref="ProductionReceiptCostStatus.Value"/> carries the full-precision distribution it was
/// built on, the direction used, the recorded cost per kg and net raw-material kg cost
/// (<see cref="ProductionCostRoundingCalculatorV1.RecordedCostPerKgScale"/> decimals), <see cref="InputTotalTl"/> (the
/// net input cost at <see cref="ProductionCostRoundingCalculatorV1.RecordedAmountScale"/> decimals: the TL that went
/// in), <see cref="RecordedWeighedScrapExpenseTl"/> (the sum of the recorded scrap expense lines), and
/// <see cref="DistributedTotalTl"/> (the recorded label amounts plus the recorded scrap expense), which always equals
/// <see cref="InputTotalTl"/> (URT-D54 (2)); <see cref="RemainderTl"/> is what rounding left and
/// <see cref="RemainderLabelId"/> the good product line that carries it (null when the remainder is zero). A pending or
/// unreadable result carries no line and no figure (never zero) and its code says why; <see cref="IssueReasons"/> are
/// the issue reasons of an <c>input_cost_zero</c> or <c>input_exchange_rate_missing</c> answer.
/// </summary>
public sealed record ProductionCostRoundedResultV1(
    ProductionReceiptCostStatus Status,
    ProductionScrapCostDistributionResult? Distribution,
    ProductionCostRoundingDirectionV1? Direction,
    decimal? RecordedCostPerKg,
    decimal? RecordedRawMaterialCostPerKg,
    decimal? InputTotalTl,
    decimal? RecordedWeighedScrapExpenseTl,
    decimal? DistributedTotalTl,
    decimal? RemainderTl,
    Guid? RemainderLabelId,
    IReadOnlyList<ProductionCostRoundedLineV1> Lines,
    string? PendingCode,
    string? UnreadableCode)
{
    public static ProductionCostRoundedResultV1 Pending(string code) =>
        new(ProductionReceiptCostStatus.Pending, null, null, null, null, null, null, null, null, null, [], code, null);

    public static ProductionCostRoundedResultV1 Unreadable(string code) =>
        new(ProductionReceiptCostStatus.Unreadable, null, null, null, null, null, null, null, null, null, [], null, code);

    public IReadOnlyList<ProductionReceiptCostIssueReasonV1> IssueReasons { get; init; } =
        Array.Empty<ProductionReceiptCostIssueReasonV1>();
}

/// <summary>
/// URT-D54 (SINAN_KARARLARI_2026-11-14_URETIM_EK4, #66 R9 9.5): the recorded figures of the production receipt cost.
/// <list type="bullet">
/// <item>The calculation runs at full precision (<see cref="ProductionReceiptCostCalculatorV1"/>,
/// <see cref="ProductionScrapCostDistributionCalculatorV1"/>); only the recorded figures are rounded, once: a kg cost to
/// <see cref="RecordedCostPerKgScale"/> decimals and a TL total to <see cref="RecordedAmountScale"/> decimals
/// (URT-D54 (1); not chosen: rounding to 2 decimals at every step, URT-D54 (3)).</item>
/// <item>The TL that went in is the net input cost rounded once; each label's amount is its full-precision allocated
/// amount (kg x the full-precision cost per kg) rounded once, and each scrap expense line its full-precision expense
/// rounded once. What is left (the TL that went in less the recorded label amounts and scrap expense lines) is written
/// on the good product line, so the TL that went in equals the TL distributed (URT-D54 (2)).</item>
/// <item>When the order has more than one good product label, the remainder goes to the one with the most kg, and of
/// those to the lowest label id (author reading: URT-D54 (2) says "saglam urun satiri" and does not say which of several;
/// the rule is deterministic and independent of the input order). A remainder with no good product line of more than
/// zero kg (for example an order whose produced kg are all second quality) is
/// <c>rounding_remainder_line_missing</c>, and a good product line that would be zero or below once it carries the
/// remainder is <c>rounding_remainder_not_absorbed</c>; neither is moved to another class silently. A remainder of zero
/// needs no line.</item>
/// <item>The direction of a half is an explicit input (<see cref="ProductionCostRoundingPolicyInputV1"/>): it is not
/// decided (URT-D54 (3), NEEDS-SINAN), and while it is not, the result is pending (<c>cost_rounding_policy_not_decided</c>)
/// and carries no figure.</item>
/// </list>
/// The recorded TL figures carry at most <see cref="RecordedAmountScale"/> decimals, so a consumer can hold them as Money
/// of the TL money profile (SAT-D265 (4)); Money refuses rather than rounds an amount over its scale, so a profile with
/// fewer decimals fails closed. Pending and unreadable answers of the distribution are carried through with their codes
/// and issue reasons; an open order is pending before the direction is read, an unreadable distribution or direction
/// wins over a pending one. An arithmetic overflow is <c>cost_arithmetic_overflow</c>. The calculator does not throw
/// for its input's content.
/// </summary>
public static class ProductionCostRoundingCalculatorV1
{
    public const int ContractVersion = 1;

    /// <summary>URT-D54 (1): "kg maliyeti 4 haneye".</summary>
    public const int RecordedCostPerKgScale = 4;

    /// <summary>URT-D54 (1): "toplam TL 2 haneye".</summary>
    public const int RecordedAmountScale = 2;

    public const string RemainderBasis = "rounding-remainder-on-largest-good-product-line";

    public static ProductionCostRoundedResultV1 Round(ProductionCostRoundingInputV1 input)
    {
        if (input?.Distribution is null || input.Policy is null)
        {
            return ProductionCostRoundedResultV1.Unreadable(ProductionReceiptCostCodesV1.InputInvalid);
        }

        var distribution = ProductionScrapCostDistributionCalculatorV1.Distribute(input.Distribution);
        if (distribution is
            { Status: ProductionReceiptCostStatus.Pending, PendingCode: ProductionReceiptCostCodesV1.OrderNotClosed })
        {
            return ProductionCostRoundedResultV1.Pending(ProductionReceiptCostCodesV1.OrderNotClosed);
        }

        if (distribution.Status is not (ProductionReceiptCostStatus.Value or ProductionReceiptCostStatus.Pending))
        {
            return ProductionCostRoundedResultV1.Unreadable(
                distribution.UnreadableCode ?? ProductionReceiptCostCodesV1.InputInvalid) with
            {
                IssueReasons = distribution.IssueReasons,
            };
        }

        MidpointRounding mode;
        switch (input.Policy)
        {
            case { Status: ProductionCostRoundingPolicyStatusV1.Value,
                Direction: ProductionCostRoundingDirectionV1.HalfAwayFromZero }:
                mode = MidpointRounding.AwayFromZero;
                break;
            case { Status: ProductionCostRoundingPolicyStatusV1.Value,
                Direction: ProductionCostRoundingDirectionV1.HalfToEven }:
                mode = MidpointRounding.ToEven;
                break;
            case { Status: ProductionCostRoundingPolicyStatusV1.NotDecided, Direction: null }:
                return ProductionCostRoundedResultV1.Pending(distribution.Status == ProductionReceiptCostStatus.Pending
                    ? distribution.PendingCode ?? ProductionReceiptCostCodesV1.InputInvalid
                    : ProductionCostRoundingCodesV1.PolicyNotDecided);
            default:
                return ProductionCostRoundedResultV1.Unreadable(ProductionCostRoundingCodesV1.PolicyUnreadable);
        }

        if (distribution.Status == ProductionReceiptCostStatus.Pending)
        {
            return ProductionCostRoundedResultV1.Pending(
                distribution.PendingCode ?? ProductionReceiptCostCodesV1.InputInvalid);
        }

        try
        {
            return Recorded(distribution, input.Policy.Direction!.Value, mode);
        }
        catch (OverflowException)
        {
            return ProductionCostRoundedResultV1.Unreadable(ProductionReceiptCostCodesV1.ArithmeticOverflow);
        }
    }

    private static ProductionCostRoundedResultV1 Recorded(ProductionScrapCostDistributionResult distribution,
        ProductionCostRoundingDirectionV1 direction, MidpointRounding mode)
    {
        if (distribution.Cost is not
            {
                CostPerKg: { } costPerKg, RawMaterialCostPerKg: { } rawMaterialCostPerKg, NetInputCost: { } netInputCost
            })
        {
            return ProductionCostRoundedResultV1.Unreadable(ProductionReceiptCostCodesV1.InputInvalid);
        }

        var inputTotal = decimal.Round(netInputCost, RecordedAmountScale, mode);
        var lines = new List<ProductionCostRoundedLineV1>(distribution.Lines.Count);
        var labelTotal = 0m;
        var scrapTotal = 0m;
        int? target = null;
        foreach (var line in distribution.Lines)
        {
            decimal? unitCost = line.UnitCostPerKg is { } unit ? decimal.Round(unit, RecordedCostPerKgScale, mode) : null;
            decimal? amount = line.AllocatedCost is { } allocated
                ? decimal.Round(allocated, RecordedAmountScale, mode)
                : null;
            decimal? scrapExpense = line.SeparateScrapExpense is { } expense
                ? decimal.Round(expense, RecordedAmountScale, mode)
                : null;
            labelTotal += amount ?? 0m;
            scrapTotal += scrapExpense ?? 0m;
            // Lines come ordered by class, then label id: the first good product line with the most kg wins a tie.
            if (line is { OutputClass: OutputClass.GoodProduct, AllocatedCost: not null, Kilograms: > 0m }
                && (target is null || line.Kilograms > lines[target.Value].Kilograms))
            {
                target = lines.Count;
            }

            lines.Add(new ProductionCostRoundedLineV1(line.OutputLabelId, line.OutputClass, line.Kilograms, unitCost,
                amount, scrapExpense, 0m, line.AllocationBasis, line.NotAllocatedCode));
        }

        var remainder = inputTotal - labelTotal - scrapTotal;
        Guid? remainderLabelId = null;
        if (remainder != 0m)
        {
            if (target is not { } index)
            {
                return ProductionCostRoundedResultV1.Unreadable(ProductionCostRoundingCodesV1.RemainderLineMissing);
            }

            var carrier = lines[index];
            var carried = carrier.RecordedAmountTl!.Value + remainder;
            if (carried <= 0m)
            {
                return ProductionCostRoundedResultV1.Unreadable(ProductionCostRoundingCodesV1.RemainderNotAbsorbed);
            }

            lines[index] = carrier with { RecordedAmountTl = carried, RemainderTl = remainder };
            remainderLabelId = carrier.OutputLabelId;
        }

        var distributed = 0m;
        foreach (var line in lines)
        {
            distributed += (line.RecordedAmountTl ?? 0m) + (line.RecordedSeparateScrapExpenseTl ?? 0m);
        }

        // URT-D54 (2): giren TL = dagitilan TL. True by construction; checked so a broken line can never be recorded.
        if (distributed != inputTotal)
        {
            return ProductionCostRoundedResultV1.Unreadable(ProductionReceiptCostCodesV1.InputInvalid);
        }

        return new ProductionCostRoundedResultV1(ProductionReceiptCostStatus.Value, distribution, direction,
            decimal.Round(costPerKg, RecordedCostPerKgScale, mode),
            decimal.Round(rawMaterialCostPerKg, RecordedCostPerKgScale, mode),
            inputTotal, scrapTotal, distributed, remainder, remainderLabelId, lines, null, null);
    }
}
