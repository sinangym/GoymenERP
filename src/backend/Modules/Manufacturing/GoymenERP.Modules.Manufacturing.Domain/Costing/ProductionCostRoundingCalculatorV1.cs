using GoymenERP.Modules.Manufacturing.Domain.Orders;

namespace GoymenERP.Modules.Manufacturing.Domain.Costing;

/// <summary>Stable codes of the rounding, in addition to the cost and distribution codes.</summary>
public static class ProductionCostRoundingCodesV1
{
    /// <summary>
    /// Pending: a remainder is left and the order has no good product line with kg to carry it (URT-D80: "Emirde iyi
    /// urun etiketi yoksa maliyet 'bekliyor' kalir"). The remainder is not written on another class.
    /// </summary>
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
/// not kg x the recorded unit cost: it is the unrounded kg cost x kg rounded once (URT-D79), never recomputed from the
/// recorded 4-decimal kg cost.
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
/// built on, the recorded cost per kg and net raw-material kg cost
/// (<see cref="ProductionCostRoundingCalculatorV1.RecordedCostPerKgScale"/> decimals), <see cref="InputTotalTl"/> (the
/// net input cost at <see cref="ProductionCostRoundingCalculatorV1.RecordedAmountScale"/> decimals: the TL that went
/// in), <see cref="RecordedWeighedScrapExpenseTl"/> (the sum of the recorded scrap expense lines), and
/// <see cref="DistributedTotalTl"/> (the sum of the recorded label amounts and the recorded scrap expense, not rounded
/// again), which always equals <see cref="InputTotalTl"/> (URT-D79, URT-D80); <see cref="RemainderTl"/> is what rounding
/// left and <see cref="RemainderLabelId"/> the good product line that carries it (null when the remainder is zero). A
/// pending or unreadable result carries no line and no figure (never zero) and its code says why;
/// <see cref="IssueReasons"/> are the issue reasons of an <c>input_cost_zero</c> or <c>input_exchange_rate_missing</c>
/// answer.
/// </summary>
public sealed record ProductionCostRoundedResultV1(
    ProductionReceiptCostStatus Status,
    ProductionScrapCostDistributionResult? Distribution,
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
        new(ProductionReceiptCostStatus.Pending, null, null, null, null, null, null, null, null, [], code, null);

    public static ProductionCostRoundedResultV1 Unreadable(string code) =>
        new(ProductionReceiptCostStatus.Unreadable, null, null, null, null, null, null, null, null, [], null, code);

    public IReadOnlyList<ProductionReceiptCostIssueReasonV1> IssueReasons { get; init; } =
        Array.Empty<ProductionReceiptCostIssueReasonV1>();
}

/// <summary>
/// The recorded figures of the production receipt cost: URT-D54 (SINAN_KARARLARI_2026-11-14_URETIM_EK4) as completed by
/// URT-D78…URT-D80 (SINAN_KARARLARI_2026-11-15_DALGA2_EK10, SINAN-CEVAP 31).
/// <list type="bullet">
/// <item>The calculation runs at full precision (<see cref="ProductionReceiptCostCalculatorV1"/>,
/// <see cref="ProductionScrapCostDistributionCalculatorV1"/>); only the recorded figures are rounded, once: a kg cost to
/// <see cref="RecordedCostPerKgScale"/> decimals and a TL amount to <see cref="RecordedAmountScale"/> decimals (URT-D79;
/// not chosen: 6 decimals, or rounding at every step).</item>
/// <item>The TL that went in is the net input cost rounded once. Each label's amount is the unrounded kg cost x that
/// label's kg rounded once, never the recorded 4-decimal kg cost x kg; each scrap expense line is its full-precision
/// expense rounded once. The order's total is the sum of the recorded lines and is not rounded again (URT-D79).</item>
/// <item>What is left (the TL that went in less the recorded label amounts and scrap expense lines) is written on the
/// good product line with the most kg; of equal kg, on the one with the smallest label number
/// (<see cref="CompareLabelNumbers"/>; URT-D80, the tie rule an implementation detail of the decision). So the TL that
/// went in equals the TL distributed. A remainder with no good product line of more than zero kg (for example an order
/// whose produced kg are all second quality) leaves the cost pending (<c>rounding_remainder_line_missing</c>, URT-D80);
/// it is never written on second quality, remnant or scrap. A good product line that would be zero or below once it
/// carries the remainder is <c>rounding_remainder_not_absorbed</c>. A remainder of zero needs no line.</item>
/// <item>A half is rounded away from zero (0.125 → 0.13; −0.125 → −0.13), the one rounding rule of the system
/// (URT-D78: SAT-D11 and the 21 October 2026 rule "Bankaci yuvarlama kullanilmaz", the rule of
/// <c>Money.Round</c>). It is fixed: there is no company setting and no policy source for it, and the cost never
/// waits for a rounding rule.</item>
/// </list>
/// The recorded TL figures carry at most <see cref="RecordedAmountScale"/> decimals, so a consumer can hold them as Money
/// of the TL money profile (SAT-D265 (4)); Money refuses rather than rounds an amount over its scale, so a profile with
/// fewer decimals fails closed. Pending and unreadable answers of the distribution are carried through with their codes
/// and issue reasons. An arithmetic overflow is <c>cost_arithmetic_overflow</c>. The calculator does not throw for its
/// input's content.
/// </summary>
public static class ProductionCostRoundingCalculatorV1
{
    public const int ContractVersion = 1;

    /// <summary>URT-D79: "kg maliyeti kayitta 4 ondalik saklanir" (SAT-D265, URT-D54 (1)).</summary>
    public const int RecordedCostPerKgScale = 4;

    /// <summary>URT-D79: the label (line) amount is rounded once to 2 decimals.</summary>
    public const int RecordedAmountScale = 2;

    /// <summary>
    /// URT-D78: a half is rounded away from zero, the same single rule as SAT-D11 and <c>Money.Round</c>. Not a setting.
    /// </summary>
    public const MidpointRounding HalfRounding = MidpointRounding.AwayFromZero;

    public const string RemainderBasis = "rounding-remainder-on-largest-good-product-line";

    public static ProductionCostRoundedResultV1 Round(ProductionScrapCostDistributionInput input)
    {
        if (input is null)
        {
            return ProductionCostRoundedResultV1.Unreadable(ProductionReceiptCostCodesV1.InputInvalid);
        }

        var distribution = ProductionScrapCostDistributionCalculatorV1.Distribute(input);
        switch (distribution.Status)
        {
            case ProductionReceiptCostStatus.Value:
                break;
            case ProductionReceiptCostStatus.Pending:
                return ProductionCostRoundedResultV1.Pending(
                    distribution.PendingCode ?? ProductionReceiptCostCodesV1.InputInvalid);
            default:
                return ProductionCostRoundedResultV1.Unreadable(
                    distribution.UnreadableCode ?? ProductionReceiptCostCodesV1.InputInvalid) with
                {
                    IssueReasons = distribution.IssueReasons,
                };
        }

        try
        {
            return Recorded(distribution, input.Labels);
        }
        catch (OverflowException)
        {
            return ProductionCostRoundedResultV1.Unreadable(ProductionReceiptCostCodesV1.ArithmeticOverflow);
        }
    }

    /// <summary>
    /// The order of two label numbers for the URT-D80 tie (implementation detail, reversible): the shorter number first,
    /// then ordinal. For numbers that share a prefix (ETK-9, ETK-10) this is their numeric order; for any two numbers it
    /// is deterministic and independent of culture.
    /// </summary>
    public static int CompareLabelNumbers(string left, string right)
    {
        ArgumentNullException.ThrowIfNull(left);
        ArgumentNullException.ThrowIfNull(right);
        var byLength = left.Length.CompareTo(right.Length);
        return byLength != 0 ? byLength : string.CompareOrdinal(left, right);
    }

    private static ProductionCostRoundedResultV1 Recorded(ProductionScrapCostDistributionResult distribution,
        IReadOnlyList<ProductionScrapCostLabelInput> labels)
    {
        if (distribution.Cost is not
            {
                CostPerKg: { } costPerKg, RawMaterialCostPerKg: { } rawMaterialCostPerKg, NetInputCost: { } netInputCost
            })
        {
            return ProductionCostRoundedResultV1.Unreadable(ProductionReceiptCostCodesV1.InputInvalid);
        }

        // The distribution has accepted every label: none is null, ids and label numbers are unique and canonical.
        var labelNumbers = new Dictionary<Guid, string>(labels.Count);
        foreach (var label in labels)
        {
            labelNumbers.Add(label.OutputLabelId, label.LabelNumber);
        }

        var inputTotal = decimal.Round(netInputCost, RecordedAmountScale, HalfRounding);
        var lines = new List<ProductionCostRoundedLineV1>(distribution.Lines.Count);
        var labelTotal = 0m;
        var scrapTotal = 0m;
        int? target = null;
        foreach (var line in distribution.Lines)
        {
            decimal? unitCost = line.UnitCostPerKg is { } unit
                ? decimal.Round(unit, RecordedCostPerKgScale, HalfRounding)
                : null;
            decimal? amount = line.AllocatedCost is { } allocated
                ? decimal.Round(allocated, RecordedAmountScale, HalfRounding)
                : null;
            decimal? scrapExpense = line.SeparateScrapExpense is { } expense
                ? decimal.Round(expense, RecordedAmountScale, HalfRounding)
                : null;
            labelTotal += amount ?? 0m;
            scrapTotal += scrapExpense ?? 0m;
            if (line is { OutputClass: OutputClass.GoodProduct, AllocatedCost: not null, Kilograms: > 0m }
                && (target is null || CarriesBefore(line, lines[target.Value], labelNumbers)))
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
                return ProductionCostRoundedResultV1.Pending(ProductionCostRoundingCodesV1.RemainderLineMissing);
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

        // URT-D79: the total is the sum of the recorded lines, not rounded again.
        var distributed = 0m;
        foreach (var line in lines)
        {
            distributed += (line.RecordedAmountTl ?? 0m) + (line.RecordedSeparateScrapExpenseTl ?? 0m);
        }

        // URT-D79/URT-D80: giren TL = dagitilan TL. True by construction; checked so a broken line can never be recorded.
        if (distributed != inputTotal)
        {
            return ProductionCostRoundedResultV1.Unreadable(ProductionReceiptCostCodesV1.InputInvalid);
        }

        return new ProductionCostRoundedResultV1(ProductionReceiptCostStatus.Value, distribution,
            decimal.Round(costPerKg, RecordedCostPerKgScale, HalfRounding),
            decimal.Round(rawMaterialCostPerKg, RecordedCostPerKgScale, HalfRounding),
            inputTotal, scrapTotal, distributed, remainder, remainderLabelId, lines, null, null);
    }

    /// <summary>
    /// URT-D80: whether <paramref name="candidate"/> carries the remainder before <paramref name="current"/>: more kg,
    /// or equal kg and a smaller label number. Label numbers are unique within the order, so the order of the lines
    /// never decides.
    /// </summary>
    private static bool CarriesBefore(ProductionScrapCostDistributionLine candidate, ProductionCostRoundedLineV1 current,
        IReadOnlyDictionary<Guid, string> labelNumbers)
    {
        if (candidate.Kilograms != current.Kilograms)
        {
            return candidate.Kilograms > current.Kilograms;
        }

        return CompareLabelNumbers(labelNumbers[candidate.OutputLabelId], labelNumbers[current.OutputLabelId]) < 0;
    }
}
