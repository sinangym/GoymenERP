using GoymenERP.Modules.Sales.Domain;
using GoymenERP.Modules.Sales.Domain.Drafts;

namespace GoymenERP.Sales.UnitTests;

/// <summary>
/// DEC-SAL-001 / API-08 realization calculator (SAT-D5, SAT-D15–D17) with the SAT-D5 starting ladder 1 / 2 / 3 % given as
/// test data (the production values come only from the Settings policy record). Every bound is inclusive and starts its band;
/// the arithmetic is exact decimal on the shared <see cref="Quantity"/> (kg ≤ 3 decimals, pieces whole) — no float, no
/// rounding, no division.
/// </summary>
public sealed class SalesRealizationCalculatorTests
{
    private static readonly SalesDeliveryTolerance Ladder = new(1m, 2m, 3m);

    private static Quantity Kg(decimal value) => Quantity.Of(value, QuantityUnit.Kilogram);

    private static Quantity Pieces(decimal value) => Quantity.Of(value, QuantityUnit.Piece);

    public static TheoryData<string, string, bool, string, string, string, string?> KilogramBoundaries => new()
    {
        // ordered, shipped, closed → state, direction, deviation, band
        { "1000", "1000", false, "Completed", "None", "0", "Within" },
        // Over-delivery is measured on every shipment; the warn band is "Tamamlandı" with its warning (SAT-D29).
        { "1000", "1009.999", false, "Completed", "Over", "9.999", "Within" },
        { "1000", "1010", false, "Completed", "Over", "10", "Warn" },
        { "1000", "1019.999", false, "Completed", "Over", "19.999", "Warn" },
        { "1000", "1010", true, "Completed", "Over", "10", "Warn" },
        // SAT-D30: the approval band awaits an over-delivery approval (none recorded); the block band is outside the tolerance.
        { "1000", "1020", false, "AwaitingApproval", "Over", "20", "Approval" },
        { "1000", "1029.999", false, "AwaitingApproval", "Over", "29.999", "Approval" },
        { "1000", "1030", false, "OutsideTolerance", "Over", "30", "Block" },
        { "1000", "1030", true, "OutsideTolerance", "Over", "30", "Block" },
        // Under-delivery within the tolerance is "Tamamlandı" (SAT-D5), open or closed (SAT-D17).
        { "1000", "990.001", false, "Completed", "Under", "9.999", "Within" },
        { "1000", "990.001", true, "Completed", "Under", "9.999", "Within" },
        // Open and outside the tolerance: the open remainder is not a shortfall (SAT-D16) — not banded.
        { "1000", "990", false, "Partial", "Under", "10", null },
        { "1000", "500", false, "Partial", "Under", "500", null },
        // Force-closed: the shortfall is measured and the rest is the closed remainder, never "Completed".
        // SAT-D31: a closed shortfall in the warn band is "Tamamlandı" with the warning (as SAT-D17's normal closing).
        { "1000", "990", true, "Completed", "Under", "10", "Warn" },
        { "1000", "980.001", true, "Completed", "Under", "19.999", "Warn" },
        { "1000", "980", true, "ClosedWithRemainder", "Under", "20", "Approval" },
        { "1000", "970.001", true, "ClosedWithRemainder", "Under", "29.999", "Approval" },
        { "1000", "970", true, "ClosedWithRemainder", "Under", "30", "Block" },
        { "1000", "0", true, "ClosedWithRemainder", "Under", "1000", "Block" },
        // Nothing shipped on an open line: not started, and the tolerance is not needed.
        { "1000", "0", false, "NotStarted", "Under", "1000", null },
        // Ö2: the smallest kilogram step decides exactly.
        { "0.1", "0.101", false, "Completed", "Over", "0.001", "Warn" },
        { "0.2", "0.201", false, "Completed", "Over", "0.001", "Within" },
    };

    [Theory]
    [MemberData(nameof(KilogramBoundaries))]
    public void Kilogram_lines_fall_into_the_band_their_bound_starts(string ordered, string shipped, bool closed, string state,
        string direction, string deviation, string? band)
    {
        var line = SalesRealizationCalculator.Line(Kg(decimal.Parse(ordered, System.Globalization.CultureInfo.InvariantCulture)),
            Kg(decimal.Parse(shipped, System.Globalization.CultureInfo.InvariantCulture)), closed, Ladder);

        Assert.Equal((state, direction, band), (line.State.ToString(), line.Direction.ToString(), line.Band?.ToString()));
        Assert.Equal(decimal.Parse(deviation, System.Globalization.CultureInfo.InvariantCulture), line.Deviation.Value);
        Assert.Equal(QuantityUnit.Kilogram, line.Deviation.Unit);
    }

    [Theory]
    [InlineData(100, 101, false, "Completed", "Warn")]
    [InlineData(100, 100, false, "Completed", "Within")]
    [InlineData(100, 99, false, "Partial", null)]
    [InlineData(100, 99, true, "Completed", "Warn")]
    [InlineData(100, 98, true, "ClosedWithRemainder", "Approval")]
    [InlineData(50, 51, false, "AwaitingApproval", "Approval")]
    [InlineData(101, 100, false, "Completed", "Within")]
    [InlineData(101, 102, false, "Completed", "Within")]
    [InlineData(33, 34, false, "OutsideTolerance", "Block")]
    [InlineData(34, 35, false, "AwaitingApproval", "Approval")]
    public void Piece_lines_are_whole_and_judged_with_the_same_ladder(int ordered, int shipped, bool closed, string state, string? band)
    {
        var line = SalesRealizationCalculator.Line(Pieces(ordered), Pieces(shipped), closed, Ladder);

        Assert.Equal((state, band), (line.State.ToString(), line.Band?.ToString()));
        Assert.Equal(QuantityUnit.Piece, line.Deviation.Unit);
    }

    [Fact]
    public void Only_a_completed_line_in_the_warn_band_carries_the_sat_d29_warning()
    {
        // Over in the warn band: completed WITH the warning.
        Assert.True(SalesRealizationCalculator.IsCompletedWithWarning(SalesRealizationCalculator.Line(Kg(1000m), Kg(1015m), false, Ladder)));
        // Within, approval band over, open under in the warn band: no warning.
        Assert.False(SalesRealizationCalculator.IsCompletedWithWarning(SalesRealizationCalculator.Line(Kg(1000m), Kg(1005m), false, Ladder)));
        Assert.False(SalesRealizationCalculator.IsCompletedWithWarning(SalesRealizationCalculator.Line(Kg(1000m), Kg(1020m), false, Ladder)));
        Assert.False(SalesRealizationCalculator.IsCompletedWithWarning(SalesRealizationCalculator.Line(Kg(1000m), Kg(985m), false, Ladder)));
        // SAT-D31: a closed shortfall in the warn band carries the same warning.
        Assert.True(SalesRealizationCalculator.IsCompletedWithWarning(SalesRealizationCalculator.Line(Kg(1000m), Kg(985m), true, Ladder)));
    }

    [Fact]
    public void An_approved_approval_band_over_delivery_is_completed_as_approved_and_an_approval_does_not_lift_the_block_band()
    {
        // SAT-D30: the approved variant the contract leaves room for ("Tamamlandı + onaylı fazla teslim").
        var approved = SalesRealizationCalculator.Line(Kg(1000m), Kg(1025m), false, Ladder, overDeliveryApproved: true);
        Assert.Equal((LineRealizationState.Completed, DeliveryToleranceBand.Approval, true, false),
            (approved.State, approved.Band, approved.ApprovedOverDelivery, SalesRealizationCalculator.IsCompletedWithWarning(approved)));
        // Not approved: awaiting; an approval never applies to the block band or to a line within the tolerance.
        Assert.Equal(LineRealizationState.AwaitingApproval, SalesRealizationCalculator.Line(Kg(1000m), Kg(1025m), false, Ladder).State);
        var blocked = SalesRealizationCalculator.Line(Kg(1000m), Kg(1030m), false, Ladder, overDeliveryApproved: true);
        Assert.Equal((LineRealizationState.OutsideTolerance, false), (blocked.State, blocked.ApprovedOverDelivery));
        Assert.False(SalesRealizationCalculator.Line(Kg(1000m), Kg(1005m), false, Ladder, overDeliveryApproved: true).ApprovedOverDelivery);
    }

    [Fact]
    public void Another_ladder_from_settings_moves_the_bands_and_nothing_is_a_code_constant()
    {
        var narrow = new SalesDeliveryTolerance(0.5m, 0.75m, 1.25m);

        Assert.Equal(DeliveryToleranceBand.Within, SalesRealizationCalculator.Band(Kg(1000m), Kg(4.999m), narrow));
        Assert.Equal(DeliveryToleranceBand.Warn, SalesRealizationCalculator.Band(Kg(1000m), Kg(5m), narrow));
        Assert.Equal(DeliveryToleranceBand.Approval, SalesRealizationCalculator.Band(Kg(1000m), Kg(7.5m), narrow));
        Assert.Equal(DeliveryToleranceBand.Block, SalesRealizationCalculator.Band(Kg(1000m), Kg(12.5m), narrow));
        // The same deviation is within the 1 / 2 / 3 % ladder.
        Assert.Equal(DeliveryToleranceBand.Within, SalesRealizationCalculator.Band(Kg(1000m), Kg(5m), Ladder));

        // A zero ladder: no deviation is no band, any deviation blocks.
        var zero = new SalesDeliveryTolerance(0m, 0m, 0m);
        Assert.Equal(LineRealizationState.Completed, SalesRealizationCalculator.Line(Kg(1000m), Kg(1000m), false, zero).State);
        Assert.Equal(DeliveryToleranceBand.Block, SalesRealizationCalculator.Line(Kg(1000m), Kg(1000.001m), false, zero).Band);
    }

    [Theory]
    [InlineData(-0.1, 1, 2)]
    [InlineData(2, 1, 3)]
    [InlineData(1, 3, 2)]
    [InlineData(1, 2, 100.001)]
    public void A_tolerance_that_is_not_a_ladder_is_refused(double warn, double approval, double block)
    {
        Assert.Throws<ArgumentException>(() => new SalesDeliveryTolerance((decimal)warn, (decimal)approval, (decimal)block));
    }

    [Fact]
    public void A_line_that_needs_the_tolerance_without_one_a_zero_order_another_unit_or_an_overflow_is_refused_not_guessed()
    {
        Assert.False(SalesRealizationCalculator.NeedsTolerance(Kg(0m), lineClosed: false));
        Assert.True(SalesRealizationCalculator.NeedsTolerance(Kg(0m), lineClosed: true));
        Assert.True(SalesRealizationCalculator.NeedsTolerance(Kg(0.001m), lineClosed: false));

        Assert.Throws<ArgumentNullException>(() => SalesRealizationCalculator.Line(Kg(10m), Kg(1m), false, null));
        Assert.Throws<ArgumentException>(() => SalesRealizationCalculator.Line(Kg(0m), Kg(0m), false, Ladder));
        Assert.Equal(SalesRuleCodes.UnitMismatch,
            Assert.Throws<SalesRuleException>(() => SalesRealizationCalculator.Line(Kg(10m), Pieces(10m), false, Ladder)).Code);
        var huge = decimal.MaxValue / 10m;
        Assert.Throws<OverflowException>(() => SalesRealizationCalculator.Line(Kg(decimal.Truncate(huge)), Kg(1m), false, Ladder));
    }

    [Theory]
    [InlineData("NotStarted", "NotStarted")]
    [InlineData("NotStarted,NotStarted", "NotStarted")]
    [InlineData("Completed,Completed", "Completed")]
    [InlineData("Completed,NotStarted", "Partial")]
    [InlineData("Partial,Completed", "Partial")]
    [InlineData("Completed,ClosedWithRemainder", "ClosedWithRemainder")]
    [InlineData("ClosedWithRemainder", "ClosedWithRemainder")]
    [InlineData("Completed,OutsideTolerance", "OutsideTolerance")]
    [InlineData("ClosedWithRemainder,OutsideTolerance,NotStarted", "OutsideTolerance")]
    [InlineData("Completed,AwaitingApproval", "AwaitingApproval")]
    [InlineData("AwaitingApproval,Partial", "AwaitingApproval")]
    [InlineData("AwaitingApproval,OutsideTolerance", "OutsideTolerance")]
    [InlineData("", "NotStarted")]
    public void The_axis_is_derived_from_every_line(string lines, string axis)
    {
        var states = lines.Length == 0
            ? []
            : lines.Split(',').Select(Enum.Parse<LineRealizationState>).ToArray();

        Assert.Equal(axis, SalesRealizationCalculator.Axis(states).ToString());
    }
}
