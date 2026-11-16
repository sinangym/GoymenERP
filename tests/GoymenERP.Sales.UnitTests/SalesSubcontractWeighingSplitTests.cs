using GoymenERP.Modules.Sales.Domain;
using GoymenERP.Modules.Sales.Domain.Invoices;

namespace GoymenERP.Sales.UnitTests;

/// <summary>
/// SAT-D376/D377/D402 golden: a shared delivery weighing's net kg is split by theoretical kg (raw share first), every line but
/// the last is rounded to whole kg AwayFromZero, and the residue goes to the last line so the lines add up to the net kg rounded
/// AwayFromZero. Every value is SENTETIK.
/// </summary>
public sealed class SalesSubcontractWeighingSplitTests
{
    private static readonly Guid OrderA = Guid.NewGuid();
    private static readonly Guid OrderB = Guid.NewGuid();
    private static readonly Guid LineA1 = Guid.NewGuid();
    private static readonly Guid LineA2 = Guid.NewGuid();
    private static readonly Guid LineB1 = Guid.NewGuid();

    private static SalesSubcontractWeighingSplitLine Line(Guid order, Guid line, decimal? theoreticalKg) => new(order, line, theoreticalKg);

    private static decimal[] Billed(IReadOnlyList<SalesSubcontractWeighingSplitShare> shares) =>
        [.. shares.Select(share => share.BilledKg.Value)];

    public static TheoryData<decimal, decimal[], decimal[]> Golden() => new()
    {
        // net, theoretical kg per line (document order), billed whole kg per line.
        // 1000.4 × 1/3 = 333.4667 → 333 ; × 1/3 → 333 ; last = 1000 − 666 = 334.
        { 1000.400m, [100m, 100m, 100m], [333m, 333m, 334m] },
        // 1234.560 × 800/1200 = 823.04 → 823 ; last = 1235 − 823 = 412 (total rounded AwayFromZero from .560).
        { 1234.560m, [800m, 400m], [823m, 412m] },
        // Midpoint: 1001 × 1/2 = 500.5 → 501 (AwayFromZero, never banker's 500) ; last = 1001 − 501 = 500.
        { 1001.000m, [250m, 250m], [501m, 500m] },
        // Total midpoint: 999.5 → 1000 ; 999.5 × 0.25 = 249.875 → 250 ; last = 750.
        { 999.500m, [1m, 3m], [250m, 750m] },
        // Three decimals of theoretical kg: 2500.000 × 333.333/1000.000 = 833.3325 → 833 ; × 333.333 → 833 ; last 834.
        { 2500.000m, [333.333m, 333.333m, 333.334m], [833m, 833m, 834m] },
    };

    [Theory]
    [MemberData(nameof(Golden))]
    public void The_net_kg_is_shared_by_theoretical_kg_rounded_per_line_with_the_residue_on_the_last_line(decimal netKg,
        decimal[] theoretical, decimal[] expected)
    {
        var lines = theoretical.Select(kg => Line(OrderA, Guid.NewGuid(), kg)).ToList();

        var shares = SalesSubcontractWeighingSplit.Split(netKg, lines);

        Assert.Equal(expected, Billed(shares));
        Assert.Equal(decimal.Round(netKg, 0, MidpointRounding.AwayFromZero), shares.Sum(share => share.BilledKg.Value));
        Assert.Equal(lines.Select(line => line.SalesOrderLineId), shares.Select(share => share.SalesOrderLineId));
        Assert.All(shares, share => Assert.Equal(QuantityUnit.Kilogram, share.BilledKg.Unit));
    }

    [Fact]
    public void A_weighing_serving_several_orders_keeps_each_share_on_its_own_order_and_the_residue_on_the_weighings_last_line()
    {
        var shares = SalesSubcontractWeighingSplit.Split(1500.700m,
            [Line(OrderA, LineA1, 500m), Line(OrderB, LineB1, 500m), Line(OrderA, LineA2, 500m)]);

        // 1500.7 / 3 = 500.2333 → 500 twice ; last (order A's second line) = 1501 − 1000 = 501.
        Assert.Equal(new (Guid, Guid, decimal)[] { (OrderA, LineA1, 500m), (OrderB, LineB1, 500m), (OrderA, LineA2, 501m) },
            shares.Select(share => (share.SalesOrderId, share.SalesOrderLineId, share.BilledKg.Value)).ToArray());
    }

    [Fact]
    public void Instruction_lines_of_the_same_order_line_are_summed_at_its_first_appearance()
    {
        var shares = SalesSubcontractWeighingSplit.Split(900.000m,
            [Line(OrderA, LineA1, 200m), Line(OrderB, LineB1, 300m), Line(OrderA, LineA1, 100m)]);

        // A1 weighs 300 of 600: 450 ; B1 is last: 900 − 450 = 450.
        Assert.Equal(new (Guid, decimal)[] { (LineA1, 450m), (LineB1, 450m) },
            shares.Select(share => (share.SalesOrderLineId, share.BilledKg.Value)).ToArray());
    }

    [Fact]
    public void An_unshared_weighing_bills_its_one_line_the_net_kg_rounded_to_whole_kg_without_needing_theoretical_kg()
    {
        var shares = SalesSubcontractWeighingSplit.Split(1234.500m, [Line(OrderA, LineA1, null), Line(OrderA, LineA1, null)]);

        Assert.Equal([1235m], Billed(shares));
    }

    [Theory]
    [InlineData(null)]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(1.2345)]
    public void A_shared_weighing_line_without_a_readable_positive_theoretical_kg_stops_the_split_and_names_the_line(double? kg)
    {
        var refusal = Assert.Throws<SalesRuleException>(() => SalesSubcontractWeighingSplit.Split(1000m,
            [Line(OrderA, LineA1, 500m), Line(OrderB, LineB1, kg is { } value ? (decimal)value : null)]));

        Assert.Equal(SalesRuleCodes.SubcontractTheoreticalKgUnavailable, refusal.Code);
        Assert.Equal(LineB1, refusal.LineId);
    }

    [Fact]
    public void A_share_that_rounds_to_zero_is_refused_never_billed_as_zero()
    {
        // 10 × 1/1000 = 0.01 → 0 kg for the first line.
        var refusal = Assert.Throws<SalesRuleException>(() => SalesSubcontractWeighingSplit.Split(10m,
            [Line(OrderA, LineA1, 1m), Line(OrderB, LineB1, 999m)]));

        Assert.Equal(SalesRuleCodes.SubcontractShareNotPositive, refusal.Code);
        Assert.Equal(LineA1, refusal.LineId);
        Assert.Equal(SalesRuleCodes.SubcontractShareNotPositive, Assert.Throws<SalesRuleException>(() =>
            SalesSubcontractWeighingSplit.Split(0.400m, [Line(OrderA, LineA1, null)])).Code);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-5)]
    [InlineData(1.0001)]
    public void A_net_kg_that_is_not_a_positive_three_decimal_kilogram_value_is_unavailable(double netKg)
    {
        Assert.Equal(SalesRuleCodes.SubcontractNetKgUnavailable, Assert.Throws<SalesRuleException>(() =>
            SalesSubcontractWeighingSplit.Split((decimal)netKg, [Line(OrderA, LineA1, 1m)])).Code);
    }
}
