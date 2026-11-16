using GoymenERP.Modules.Sales.Domain;
using GoymenERP.Modules.Sales.Domain.Invoices;

namespace GoymenERP.Sales.UnitTests;

/// <summary>
/// KLN-D89 (SINAN-CEVAP 34.1): the subcontract invoice kg ratio is measured in each line's weighbridge net kg over their sum;
/// declared and shipment-share kilograms never enter, and a line without a weighing of its own stops the calculation with an
/// explicit code (fail closed). Every value is SENTETIK.
/// </summary>
public sealed class SalesSubcontractNetKgBasisTests
{
    private static readonly Guid LineA = Guid.NewGuid();
    private static readonly Guid LineB = Guid.NewGuid();
    private static readonly Guid WeighingA = Guid.NewGuid();
    private static readonly Guid WeighingB = Guid.NewGuid();

    private static SalesSubcontractWeighingFacts Weighed(Guid weighingId, decimal netKg, params Guid[] servedLines) =>
        new(weighingId, true, netKg, servedLines);

    private static string CodeOf(Action action) => Assert.Throws<SalesRuleException>(action).Code;

    [Fact]
    public void The_ratio_denominator_is_the_sum_of_the_lines_weighbridge_net_kg()
    {
        var basis = SalesSubcontractNetKgBasis.Of(
            [(LineA, WeighingA), (LineB, WeighingB)],
            [Weighed(WeighingA, 1200.5m, LineA), Weighed(WeighingB, 799.5m, LineB)]);

        Assert.Equal(2000m, basis.Denominator.Value);
        Assert.Equal(QuantityUnit.Kilogram, basis.Denominator.Unit);
        Assert.Equal(new (Guid, Guid, decimal)[] { (LineA, WeighingA, 1200.5m), (LineB, WeighingB, 799.5m) },
            basis.Shares.Select(share => (share.OrderLineId, share.WeighingId, share.NetKg.Value)).ToArray());
        Assert.Equal(0.60025m, basis.RatioOf(LineA));
        Assert.Equal(0.39975m, basis.RatioOf(LineB));
        Assert.Equal(1m, basis.RatioOf(LineA) + basis.RatioOf(LineB));
    }

    [Fact]
    public void A_single_weighed_line_takes_the_whole_net_kg()
    {
        var basis = SalesSubcontractNetKgBasis.Of([(LineA, WeighingA)], [Weighed(WeighingA, 987.654m, LineA)]);

        Assert.Equal(987.654m, basis.Denominator.Value);
        Assert.Equal(1m, basis.RatioOf(LineA));
    }

    [Fact]
    public void A_line_without_a_weighing_stops_the_calculation_with_its_code_and_names_the_line()
    {
        var refusal = Assert.Throws<SalesRuleException>(() => SalesSubcontractNetKgBasis.Of(
            [(LineA, WeighingA), (LineB, null)],
            [Weighed(WeighingA, 1000m, LineA)]));

        Assert.Equal(SalesRuleCodes.SubcontractLineNotWeighed, refusal.Code);
        Assert.Equal(LineB, refusal.LineId);
        // A weighing id the owner did not answer is the same "no weighing", never a zero kilogram.
        Assert.Equal(SalesRuleCodes.SubcontractLineNotWeighed, CodeOf(() => SalesSubcontractNetKgBasis.Of(
            [(LineA, WeighingB)], [Weighed(WeighingA, 1000m, LineA)])));
    }

    [Fact]
    public void A_weighing_shared_by_several_order_lines_gives_no_line_its_own_net_kg_and_the_calculation_stops()
    {
        // KLN-D89 forbids the declared and the LOJ-D12 shipment-share kilograms as the ratio basis; one weighing over two lines
        // has no per-line net kg, so the basis refuses instead of splitting it.
        var refusal = Assert.Throws<SalesRuleException>(() => SalesSubcontractNetKgBasis.Of(
            [(LineA, WeighingA), (LineB, WeighingA)],
            [Weighed(WeighingA, 1000m, LineA, LineB)]));

        Assert.Equal(SalesRuleCodes.SubcontractLineNotWeighed, refusal.Code);
        Assert.Equal(LineA, refusal.LineId);
        // A weighing that serves another order line is not this line's weighing either.
        Assert.Equal(SalesRuleCodes.SubcontractLineNotWeighed, CodeOf(() => SalesSubcontractNetKgBasis.Of(
            [(LineA, WeighingA)], [Weighed(WeighingA, 1000m, LineB)])));
    }

    [Theory]
    [InlineData(false, "1000")]
    [InlineData(true, null)]
    [InlineData(true, "0")]
    [InlineData(true, "-5")]
    [InlineData(true, "10.0005")]
    public void An_unanswered_or_unusable_net_kg_refuses_with_its_own_code(bool isAvailable, string? netKgText)
    {
        decimal? netKg = netKgText is null ? null : decimal.Parse(netKgText, System.Globalization.CultureInfo.InvariantCulture);

        var refusal = Assert.Throws<SalesRuleException>(() => SalesSubcontractNetKgBasis.Of(
            [(LineA, WeighingA)], [new SalesSubcontractWeighingFacts(WeighingA, isAvailable, netKg, [LineA])]));

        Assert.Equal(SalesRuleCodes.SubcontractNetKgUnavailable, refusal.Code);
        Assert.Equal(LineA, refusal.LineId);
    }

    [Fact]
    public void Repeated_lines_or_weighings_and_an_empty_invoice_are_programming_errors()
    {
        Assert.Throws<ArgumentException>(() => SalesSubcontractNetKgBasis.Of([], [Weighed(WeighingA, 1m, LineA)]));
        Assert.Throws<ArgumentException>(() => SalesSubcontractNetKgBasis.Of(
            [(LineA, WeighingA), (LineA, WeighingA)], [Weighed(WeighingA, 1m, LineA)]));
        Assert.Throws<ArgumentException>(() => SalesSubcontractNetKgBasis.Of(
            [(LineA, WeighingA)], [Weighed(WeighingA, 1m, LineA), Weighed(WeighingA, 2m, LineA)]));
        var basis = SalesSubcontractNetKgBasis.Of([(LineA, WeighingA)], [Weighed(WeighingA, 1m, LineA)]);
        Assert.Throws<ArgumentException>(() => basis.RatioOf(LineB));
    }
}
