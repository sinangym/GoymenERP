using GoymenERP.Modules.Logistics.Domain.WeighingDifferences;

namespace GoymenERP.Logistics.UnitTests;

public sealed class DispatchKilogramAllocationTests
{
    [Fact]
    public void Calculated_product_shares_preserve_the_total_and_put_rounding_residue_on_the_first_heaviest_line()
    {
        var product = Guid.NewGuid();
        var lines = new[] { new DispatchKilogramSourceLine(3, Guid.NewGuid(), product, 1m),
            new DispatchKilogramSourceLine(1, Guid.NewGuid(), product, 1m), new DispatchKilogramSourceLine(2, Guid.NewGuid(), product, 1m) };
        var result = DispatchKilogramAllocation.Calculate(lines, [new(product, 1m)]);
        Assert.Equal([1, 2, 3], result.Select(line => line.LineNo));
        Assert.Equal([0.334m, 0.333m, 0.333m], result.Select(line => line.Kilograms));
        Assert.Equal(1m, result.Sum(line => line.Kilograms));
    }

    [Fact]
    public void Products_do_not_offset_each_other_and_negative_difference_uses_the_same_source_ratio()
    {
        var first = Guid.NewGuid(); var second = Guid.NewGuid();
        var lines = new[] { new DispatchKilogramSourceLine(1, Guid.NewGuid(), first, 20m),
            new DispatchKilogramSourceLine(2, Guid.NewGuid(), first, 10m), new DispatchKilogramSourceLine(3, Guid.NewGuid(), second, 5m) };
        var result = DispatchKilogramAllocation.Calculate(lines, [new(first, 27m), new(second, 6m)]);
        Assert.Equal([18m, 9m, 6m], result.Select(line => line.Kilograms));
    }

    [Fact]
    public void Finalized_document_shares_are_copied_exactly_even_when_they_differ_from_the_theoretical_ratio()
    {
        var product = Guid.NewGuid();
        var lines = new[] { new DispatchKilogramSourceLine(1, Guid.NewGuid(), product, 10m), new DispatchKilogramSourceLine(2, Guid.NewGuid(), product, 10m) };
        var declared = new[] { new DispatchKilogramDeclaredLine(1, lines[0].ShipmentId, product, 9.123m),
            new DispatchKilogramDeclaredLine(2, lines[1].ShipmentId, product, 10.877m) };
        var result = DispatchKilogramAllocation.CopyFinalizedDocument(lines, [new(product, 20m)], declared);
        Assert.Equal([9.123m, 10.877m], result.Select(line => line.Kilograms));
        Assert.Throws<WeighingDifferenceRuleException>(() => DispatchKilogramAllocation.CopyFinalizedDocument(lines, [new(product, 20m)],
            [declared[0], declared[1] with { Kilograms = 10.876m }]));
        Assert.Throws<WeighingDifferenceRuleException>(() => DispatchKilogramAllocation.CopyFinalizedDocument(lines, [new(product, 20m)],
            [declared[0], declared[1] with { ShipmentId = Guid.NewGuid() }]));
    }

    [Fact]
    public void Missing_duplicate_or_zero_source_shares_are_not_turned_into_invoice_quantities()
    {
        var product = Guid.NewGuid(); var line = new DispatchKilogramSourceLine(1, Guid.NewGuid(), product, 10m);
        Assert.Throws<WeighingDifferenceRuleException>(() => DispatchKilogramAllocation.Calculate([line], []));
        Assert.Throws<WeighingDifferenceRuleException>(() => DispatchKilogramAllocation.Calculate([line, line], [new(product, 20m)]));
        Assert.Throws<WeighingDifferenceRuleException>(() => DispatchKilogramAllocation.Calculate([line with { TheoreticalKg = 0m }], [new(product, 1m)]));
        var small = DispatchKilogramAllocation.Calculate([line, line with { LineNo = 2, ShipmentId = Guid.NewGuid() }], [new(product, 0.001m)]);
        Assert.Equal([0m, 0.001m], small.Select(item => item.Kilograms));
        Assert.Equal(0m, Assert.Single(DispatchKilogramAllocation.Calculate([line], [new(product, 0m)])).Kilograms);
    }

    [Fact]
    public void Proportions_at_the_storage_boundary_do_not_overflow_or_lose_a_thousandth()
    {
        var product = Guid.NewGuid();
        var result = DispatchKilogramAllocation.Calculate([new(1, Guid.NewGuid(), product, 999999999999999.999m),
            new(2, Guid.NewGuid(), product, 999999999999999.999m), new(3, Guid.NewGuid(), product, 999999999999999.999m)],
            [new(product, 999999999999999.998m)]);
        Assert.Equal([333333333333333.332m, 333333333333333.333m, 333333333333333.333m], result.Select(line => line.Kilograms));
        Assert.Equal(999999999999999.998m, result.Sum(line => line.Kilograms));
    }
}
