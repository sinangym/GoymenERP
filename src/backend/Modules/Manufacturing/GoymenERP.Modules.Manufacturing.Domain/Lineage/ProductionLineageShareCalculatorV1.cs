using GoymenERP.Modules.Manufacturing.Domain.Common;

namespace GoymenERP.Modules.Manufacturing.Domain.Lineage;

/// <summary>
/// One input label of an output label's lineage edge (URT-D62, URT-D63): the input label's identity in Inventory and
/// the kg of it consumed by the order the output was declared on.
/// </summary>
public sealed record ProductionLineageShareInput(Guid InputLabelId, Kilograms ConsumedKg);

/// <summary>The state of a lineage share answer.</summary>
public enum ProductionLineageShareStatus
{
    /// <summary>Every input carries its share; the shares are exact.</summary>
    Calculated = 1,

    /// <summary>There is no consumed kg to share yet; no line carries a share.</summary>
    Pending = 2,

    /// <summary>The input could not be read; no line and no share is given.</summary>
    Unreadable = 3,
}

/// <summary>Stable codes of the lineage share calculator.</summary>
public static class ProductionLineageShareCodesV1
{
    /// <summary>The output has no input label: there is nothing to share.</summary>
    public const string NoInput = "lineage_no_input";

    /// <summary>The input list or an input is missing, or an input label id is empty.</summary>
    public const string InputInvalid = "lineage_input_invalid";

    /// <summary>An input label is given twice for the same output.</summary>
    public const string InputDuplicate = "lineage_input_duplicate";

    /// <summary>An input label carries no consumed kg: without consumption there is no edge.</summary>
    public const string InputWithoutConsumption = "lineage_input_without_consumption";

    /// <summary>The arithmetic overflowed.</summary>
    public const string ArithmeticOverflow = "lineage_arithmetic_overflow";
}

/// <summary>
/// One input label's line: its consumed kg and its share of the output (consumed kg / total consumed kg of the output's
/// inputs). <see cref="Share"/> is an unrounded ratio in [0, 1]; it is null on a pending or unreadable answer.
/// </summary>
public sealed record ProductionLineageShareLine(Guid InputLabelId, decimal ConsumedKg, decimal? Share);

/// <summary>
/// The lineage share answer. A <see cref="ProductionLineageShareStatus.Calculated"/> answer carries one line per input,
/// ordered by input label id, and the total consumed kg the shares are taken of. A pending or unreadable answer carries
/// no line and no total (never zero), and its code says why.
/// </summary>
public sealed record ProductionLineageShareResult(
    ProductionLineageShareStatus Status,
    IReadOnlyList<ProductionLineageShareLine> Lines,
    decimal? TotalConsumedKg,
    string? PendingCode,
    string? UnreadableCode)
{
    public static ProductionLineageShareResult Pending(string code) =>
        new(ProductionLineageShareStatus.Pending, [], null, code, null);

    public static ProductionLineageShareResult Unreadable(string code) =>
        new(ProductionLineageShareStatus.Unreadable, [], null, null, code);
}

/// <summary>
/// URT-D63 (SINAN-CEVAP 15, 15.9): when one output label has more than one input label, each input label carries a share
/// of it in proportion to the kg consumed of it in that order. This is the single deterministic backend calculator of
/// that share: the store, the read port and any report or screen take the share from here and never compute it again.
/// <list type="bullet">
/// <item>Share of an input = its consumed kg / the sum of the consumed kg of all the output's inputs. The share is an
/// unrounded decimal ratio; how a share is rounded for display, and whether an output kg is attributed to each input
/// with a rounding remainder rule, is not decided here (no rounding policy is chosen).</item>
/// <item>No input at all is pending (<c>lineage_no_input</c>). A missing list or input or an empty label id
/// (<c>lineage_input_invalid</c>), a label given twice (<c>lineage_input_duplicate</c>) and an input with zero consumed
/// kg (<c>lineage_input_without_consumption</c>) are unreadable. An arithmetic overflow is
/// <c>lineage_arithmetic_overflow</c>.</item>
/// </list>
/// Lines are ordered by input label id, so the answer does not depend on the order the inputs are given in. The
/// calculator does not throw for its input's content.
/// </summary>
public static class ProductionLineageShareCalculatorV1
{
    public const int ContractVersion = 1;

    public static ProductionLineageShareResult Calculate(IReadOnlyList<ProductionLineageShareInput>? inputs)
    {
        if (inputs is null)
        {
            return ProductionLineageShareResult.Unreadable(ProductionLineageShareCodesV1.InputInvalid);
        }

        if (inputs.Count == 0)
        {
            return ProductionLineageShareResult.Pending(ProductionLineageShareCodesV1.NoInput);
        }

        var ids = new HashSet<Guid>();
        foreach (var input in inputs)
        {
            if (input is null || input.InputLabelId == Guid.Empty)
            {
                return ProductionLineageShareResult.Unreadable(ProductionLineageShareCodesV1.InputInvalid);
            }

            if (!ids.Add(input.InputLabelId))
            {
                return ProductionLineageShareResult.Unreadable(ProductionLineageShareCodesV1.InputDuplicate);
            }

            if (input.ConsumedKg.Value <= 0m)
            {
                return ProductionLineageShareResult.Unreadable(ProductionLineageShareCodesV1.InputWithoutConsumption);
            }
        }

        try
        {
            var total = 0m;
            foreach (var input in inputs)
            {
                total = checked(total + input.ConsumedKg.Value);
            }

            var lines = inputs
                .OrderBy(input => input.InputLabelId)
                .Select(input => new ProductionLineageShareLine(
                    input.InputLabelId, input.ConsumedKg.Value, input.ConsumedKg.Value / total))
                .ToArray();
            return new ProductionLineageShareResult(ProductionLineageShareStatus.Calculated, lines, total, null, null);
        }
        catch (OverflowException)
        {
            return ProductionLineageShareResult.Unreadable(ProductionLineageShareCodesV1.ArithmeticOverflow);
        }
    }
}
