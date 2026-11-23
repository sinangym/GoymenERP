using GoymenERP.Modules.Manufacturing.Domain.Common;
using GoymenERP.Modules.Manufacturing.Domain.Lineage;

namespace GoymenERP.Manufacturing.UnitTests;

/// <summary>
/// URT-D63 (SINAN-CEVAP 15, 15.9): each input label of an output label carries a share in proportion to the kg consumed
/// of it. The share is an unrounded ratio (no rounding policy is chosen), lines are ordered by input label id so the
/// answer does not depend on the input order, and a missing, duplicate or zero-consumption input never yields a share.
/// </summary>
public sealed class ProductionLineageShareCalculatorTests
{
    private static readonly Guid InputA = Guid.Parse("a0000000-0000-0000-0000-00000000000a");
    private static readonly Guid InputB = Guid.Parse("b0000000-0000-0000-0000-00000000000b");
    private static readonly Guid InputC = Guid.Parse("c0000000-0000-0000-0000-00000000000c");

    private static ProductionLineageShareInput Input(Guid id, decimal kg) => new(id, new Kilograms(kg));

    [Fact]
    public void Each_input_label_carries_its_share_in_proportion_to_its_consumed_kg()
    {
        var result = ProductionLineageShareCalculatorV1.Calculate(
            [Input(InputA, 600m), Input(InputB, 300m), Input(InputC, 100m)]);

        Assert.Equal(ProductionLineageShareStatus.Calculated, result.Status);
        Assert.Equal(1000m, result.TotalConsumedKg);
        Assert.Null(result.PendingCode);
        Assert.Null(result.UnreadableCode);
        Assert.Equal(
            [(InputA, 600m, (decimal?)0.6m), (InputB, 300m, 0.3m), (InputC, 100m, 0.1m)],
            result.Lines.Select(line => (line.InputLabelId, line.ConsumedKg, line.Share)));
    }

    [Fact]
    public void The_share_is_an_unrounded_ratio_and_the_shares_of_an_output_add_up_to_one_within_decimal_precision()
    {
        var result = ProductionLineageShareCalculatorV1.Calculate(
            [Input(InputA, 1m), Input(InputB, 1m), Input(InputC, 1m)]);

        Assert.Equal(ProductionLineageShareStatus.Calculated, result.Status);
        Assert.All(result.Lines, line => Assert.Equal(1m / 3m, line.Share));
        Assert.Equal(1m, decimal.Round(result.Lines.Sum(line => line.Share!.Value), 20));
    }

    [Fact]
    public void A_single_input_label_carries_the_whole_output()
    {
        var result = ProductionLineageShareCalculatorV1.Calculate([Input(InputB, 12.345m)]);

        Assert.Equal(ProductionLineageShareStatus.Calculated, result.Status);
        var line = Assert.Single(result.Lines);
        Assert.Equal((InputB, 12.345m, (decimal?)1m), (line.InputLabelId, line.ConsumedKg, line.Share));
        Assert.Equal(12.345m, result.TotalConsumedKg);
    }

    [Fact]
    public void The_answer_does_not_depend_on_the_order_the_inputs_are_given_in()
    {
        var forward = ProductionLineageShareCalculatorV1.Calculate(
            [Input(InputA, 250.5m), Input(InputB, 749.5m), Input(InputC, 0.001m)]);
        var backward = ProductionLineageShareCalculatorV1.Calculate(
            [Input(InputC, 0.001m), Input(InputB, 749.5m), Input(InputA, 250.5m)]);

        Assert.Equal(forward.TotalConsumedKg, backward.TotalConsumedKg);
        Assert.Equal(forward.Lines, backward.Lines);
        Assert.Equal([InputA, InputB, InputC], forward.Lines.Select(line => line.InputLabelId));
    }

    [Fact]
    public void No_input_label_is_pending_and_carries_no_line_and_no_total()
    {
        var result = ProductionLineageShareCalculatorV1.Calculate([]);

        Assert.Equal(ProductionLineageShareStatus.Pending, result.Status);
        Assert.Equal(ProductionLineageShareCodesV1.NoInput, result.PendingCode);
        Assert.Null(result.UnreadableCode);
        Assert.Empty(result.Lines);
        Assert.Null(result.TotalConsumedKg);
    }

    [Fact]
    public void A_missing_list_a_missing_input_or_an_empty_label_id_is_unreadable()
    {
        Assert.Equal(ProductionLineageShareCodesV1.InputInvalid,
            ProductionLineageShareCalculatorV1.Calculate(null).UnreadableCode);
        Assert.Equal(ProductionLineageShareCodesV1.InputInvalid,
            ProductionLineageShareCalculatorV1.Calculate([Input(InputA, 1m), null!]).UnreadableCode);
        var emptyId = ProductionLineageShareCalculatorV1.Calculate([Input(Guid.Empty, 1m)]);

        Assert.Equal(ProductionLineageShareStatus.Unreadable, emptyId.Status);
        Assert.Equal(ProductionLineageShareCodesV1.InputInvalid, emptyId.UnreadableCode);
        Assert.Null(emptyId.PendingCode);
        Assert.Empty(emptyId.Lines);
        Assert.Null(emptyId.TotalConsumedKg);
    }

    [Fact]
    public void An_input_label_given_twice_is_unreadable_and_is_never_summed()
    {
        var result = ProductionLineageShareCalculatorV1.Calculate([Input(InputA, 10m), Input(InputA, 5m)]);

        Assert.Equal(ProductionLineageShareStatus.Unreadable, result.Status);
        Assert.Equal(ProductionLineageShareCodesV1.InputDuplicate, result.UnreadableCode);
        Assert.Empty(result.Lines);
    }

    [Fact]
    public void An_input_label_without_consumed_kg_is_unreadable_and_no_other_input_gets_its_share()
    {
        var result = ProductionLineageShareCalculatorV1.Calculate([Input(InputA, 10m), Input(InputB, 0m)]);

        Assert.Equal(ProductionLineageShareStatus.Unreadable, result.Status);
        Assert.Equal(ProductionLineageShareCodesV1.InputWithoutConsumption, result.UnreadableCode);
        Assert.Empty(result.Lines);
        Assert.Null(result.TotalConsumedKg);
    }

    [Fact]
    public void An_overflowing_total_is_unreadable_and_does_not_throw()
    {
        var result = ProductionLineageShareCalculatorV1.Calculate(
            [Input(InputA, decimal.MaxValue), Input(InputB, decimal.MaxValue)]);

        Assert.Equal(ProductionLineageShareStatus.Unreadable, result.Status);
        Assert.Equal(ProductionLineageShareCodesV1.ArithmeticOverflow, result.UnreadableCode);
        Assert.Empty(result.Lines);
    }
}
