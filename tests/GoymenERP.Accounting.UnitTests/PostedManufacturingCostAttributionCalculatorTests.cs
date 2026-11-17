using GoymenERP.Modules.Accounting.Application;

namespace GoymenERP.Accounting.UnitTests;

public sealed class PostedManufacturingCostAttributionCalculatorTests
{
    private static readonly Guid Currency = new("c28a7b5a-26df-4584-bafe-21fb53fb899c");
    private static readonly DateTimeOffset At = new(2026, 11, 17, 10, 0, 0, TimeSpan.Zero);
    private static AccountingInventoryCostRatioV1 Ratio(decimal value) => AccountingInventoryCostAmountsV1.FromDecimal(value);
    private static PostedCostAttributionWeightV1 Share(decimal kg, int seconds = 0, long? sequence = null) =>
        new(Guid.NewGuid(), Guid.NewGuid(), 1, Ratio(kg), At.AddSeconds(seconds), sequence);

    [Theory]
    [InlineData(1)]
    [InlineData(-1)]
    public void One_posted_output_preserves_its_signed_total_and_assigns_residue_to_the_largest_physical_share(int sign)
    {
        var smaller = Share(1, 0); var larger = Share(2, 1);
        Assert.True(PostedManufacturingCostAttributionCalculatorV1.TryCalculate(100m * sign, Currency, 2,
            Ratio(3), [smaller, larger], out var value, out var reason));
        Assert.Null(reason); Assert.Equal(100m * sign, value!.Shares.Sum(x => x.RoundedSignedAmount));
        Assert.Equal(33.33m * sign, value.Shares.Single(x => x.Source == smaller).RoundedSignedAmount);
        Assert.Equal(66.67m * sign, value.Shares.Single(x => x.Source == larger).RoundedSignedAmount);
        Assert.Equal(new AccountingInventoryCostRatioV1((100 * sign).ToString(), "3"),
            value.Shares.Single(x => x.Source == smaller).ExactSignedAmount);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(-1)]
    public void Equal_largest_shares_use_the_earliest_actual_record_and_never_the_input_or_identifier_order(int sign)
    {
        var early = Share(1); var middle = Share(1, 1); var late = Share(1, 2);
        Assert.True(PostedManufacturingCostAttributionCalculatorV1.TryCalculate(100m * sign, Currency, 2,
            Ratio(3), [late, middle, early], out var value, out _));
        Assert.Equal(33.34m * sign, value!.Shares.Single(x => x.Source == early).RoundedSignedAmount);
        Assert.Equal(.01m * sign, value.Shares.Single(x => x.Source == early).SignedResidue);
        Assert.All(value.Shares.Where(x => x.Source != early), x => Assert.Equal(0m, x.SignedResidue));
    }

    [Fact]
    public void An_unproved_exact_timestamp_tie_blocks_only_a_nonzero_remainder()
    {
        var first = Share(1); var second = Share(1); var third = Share(1);
        Assert.False(PostedManufacturingCostAttributionCalculatorV1.TryCalculate(100, Currency, 2,
            Ratio(3), [first, second, third], out var unavailable, out var reason));
        Assert.Null(unavailable); Assert.Equal("attribution_chronology_unproven", reason);
        Assert.True(PostedManufacturingCostAttributionCalculatorV1.TryCalculate(99, Currency, 2,
            Ratio(3), [first, second, third], out var exact, out _));
        Assert.All(exact!.Shares, x => { Assert.Equal(33m, x.RoundedSignedAmount); Assert.Equal(0m, x.SignedResidue); });
        var proven = new[] { first with { ProvenOriginalSequence = 3 }, second with { ProvenOriginalSequence = 1 }, third with { ProvenOriginalSequence = 2 } };
        Assert.True(PostedManufacturingCostAttributionCalculatorV1.TryCalculate(100, Currency, 2, Ratio(3), proven, out var value, out _));
        Assert.Equal(.01m, value!.Shares.Single(x => x.Source.ShipmentId == second.ShipmentId).SignedResidue);
    }

    [Fact]
    public void Exact_fractional_lineage_weights_are_not_replaced_by_commercial_quantity_or_decimal_approximations()
    {
        var one = Share(1) with { NetQuantityKg = new("1", "3") };
        var two = Share(1, 1) with { NetQuantityKg = new("2", "3") };
        Assert.True(PostedManufacturingCostAttributionCalculatorV1.TryCalculate(1, Currency, 2, Ratio(1), [two, one], out var value, out _));
        Assert.Equal(.33m, value!.Shares.Single(x => x.Source == one).RoundedSignedAmount);
        Assert.Equal(.67m, value.Shares.Single(x => x.Source == two).RoundedSignedAmount);
        Assert.False(PostedManufacturingCostAttributionCalculatorV1.TryCalculate(1, Currency, 2, Ratio(2), [two, one], out _, out var reason));
        Assert.Equal("attribution_quantity_incomplete", reason);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(-1)]
    public void Excess_opposite_signed_residue_exhausts_each_share_at_zero_and_continues_in_proven_priority(int sign)
    {
        var weights = Enumerable.Range(0, 100).Select(index => Share(1, index)).ToArray();
        Assert.True(PostedManufacturingCostAttributionCalculatorV1.TryCalculate(.60m * sign, Currency, 2, Ratio(100), weights.Reverse().ToArray(), out var value, out var reason));
        Assert.Null(reason); Assert.Equal(.60m * sign, value!.Shares.Sum(x => x.RoundedSignedAmount));
        foreach (var first in weights.Take(40)) Assert.Equal(0m, value.Shares.Single(x => x.Source == first).RoundedSignedAmount);
        foreach (var rest in weights.Skip(40)) Assert.Equal(.01m * sign, value.Shares.Single(x => x.Source == rest).RoundedSignedAmount);
        Assert.All(value.Shares, x => Assert.True(x.RoundedSignedAmount == 0 || Math.Sign(x.RoundedSignedAmount) == sign));
    }

    [Fact]
    public void Unknown_order_blocks_a_partially_exhausted_tie_but_not_a_fully_exhausted_group()
    {
        var weights = Enumerable.Range(0, 100).Select(index => Share(1, index < 2 ? 0 : index)).ToArray();
        Assert.True(PostedManufacturingCostAttributionCalculatorV1.TryCalculate(.60m, Currency, 2, Ratio(100), weights, out var result, out _));
        Assert.All(result!.Shares.Where(x => x.Source.RecordedAtUtc == At), x => Assert.Equal(0m, x.RoundedSignedAmount));
        weights = Enumerable.Range(0, 100).Select(index => Share(1, index < 41 ? 0 : index)).ToArray();
        Assert.False(PostedManufacturingCostAttributionCalculatorV1.TryCalculate(.60m, Currency, 2, Ratio(100), weights, out _, out var reason));
        Assert.Equal("attribution_chronology_unproven", reason);
    }

    [Fact]
    public void Large_representable_amounts_keep_the_minor_unit_residue_without_decimal_sum_rounding()
    {
        var weights = new[] { Share(1), Share(1, 1), Share(1, 2) };
        foreach (var sign in new[] { 1m, -1m })
        {
            Assert.True(PostedManufacturingCostAttributionCalculatorV1.TryCalculate(1000000000000000000000000000m * sign,
                Currency, 2, Ratio(3), weights, out var value, out var reason));
            Assert.Null(reason);
            Assert.Equal(333333333333333333333333333.34m * sign,
                value!.Shares.Single(x => x.Source == weights[0]).RoundedSignedAmount);
            Assert.Equal(.01m * sign, value.Shares.Single(x => x.Source == weights[0]).SignedResidue);
            var exactTotal = value.Shares.Aggregate(Ratio(0), (sum, row) =>
                AccountingInventoryCostAmountsV1.Add(sum, Ratio(row.RoundedSignedAmount)));
            Assert.Equal(Ratio(1000000000000000000000000000m * sign), exactTotal);
        }
    }

    [Fact]
    public void Maximum_signed_decimal_uses_exact_minor_unit_sums_even_when_the_intermediate_rounded_sum_overflows_decimal()
    {
        var weights = new[] { Share(1), Share(1, 1) };
        foreach (var amount in new[] { decimal.MaxValue, decimal.MinValue })
        {
            Assert.True(PostedManufacturingCostAttributionCalculatorV1.TryCalculate(amount, Currency, 0, Ratio(2), weights, out var value, out _));
            Assert.Equal(amount > 0 ? -1m : 1m, value!.Shares.Single(x => x.Source == weights[0]).SignedResidue);
            Assert.Equal(Ratio(amount), value.Shares.Aggregate(Ratio(0), (sum, row) =>
                AccountingInventoryCostAmountsV1.Add(sum, Ratio(row.RoundedSignedAmount))));
        }
    }

    [Fact]
    public void Missing_duplicate_or_noncanonical_physical_proof_is_refused()
    {
        var share = Share(1);
        Assert.False(PostedManufacturingCostAttributionCalculatorV1.TryCalculate(1, Currency, 2, Ratio(2), [share, share], out _, out _));
        Assert.False(PostedManufacturingCostAttributionCalculatorV1.TryCalculate(1, Currency, 2, Ratio(1), [share with { NetQuantityKg = new("2", "2") }], out _, out _));
        Assert.False(PostedManufacturingCostAttributionCalculatorV1.TryCalculate(1, Currency, 2, Ratio(1), [share with { RecordedAtUtc = default }], out _, out _));
        Assert.False(PostedManufacturingCostAttributionCalculatorV1.TryCalculate(1, Currency, 2, Ratio(1), [share with { ProvenOriginalSequence = 0 }], out _, out _));
        Assert.False(PostedManufacturingCostAttributionCalculatorV1.TryCalculate(1, Currency, 2, Ratio(1), [], out _, out _));
    }
}
