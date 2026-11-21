using GoymenERP.Modules.Logistics.Domain.WeighingDifferences;

namespace GoymenERP.Logistics.UnitTests;

/// <summary>
/// STK-S24 single backend calculator of the sales weighing difference (SAT-D319..D325, SAT-D340..D344, WGT-002). The rate and
/// band values here are synthetic test inputs passed as Settings values; the calculator holds none of them. SENTETIK / NOT_ENV.
/// </summary>
public sealed class WeighingDifferenceCalculatorTests
{
    private static readonly Guid ProductA = Guid.Parse("24000000-0000-4000-8000-00000000000a");
    private static readonly Guid ProductB = Guid.Parse("24000000-0000-4000-8000-00000000000b");
    private static readonly Guid Shipment1 = Guid.Parse("24000000-0000-4000-8000-000000000001");
    private static readonly Guid Shipment2 = Guid.Parse("24000000-0000-4000-8000-000000000002");
    private static readonly Guid Shipment3 = Guid.Parse("24000000-0000-4000-8000-000000000003");
    private const decimal Rate = 0.5m;

    [Fact]
    public void Exactly_the_rate_on_the_dispatch_note_total_requires_approval()
    {
        // SAT-D341: 5 kg excess on a 1000 kg note is exactly 0.5 %: approval.
        var evaluation = Evaluate([Line(1, ProductA, 1000m)], [Scale(ProductA, 1005m, 0m)], Policy(Rate));

        Assert.Equal(WeighingDifferenceDecision.ApprovalRequired, evaluation.Decision);
        Assert.Equal(5m, evaluation.TotalExcessKg.Value);
        Assert.Equal(5m, evaluation.DifferenceKg);
        Assert.Equal(1, evaluation.Products.Single().TargetLineNo);
        Assert.Equal(Rate, evaluation.ApprovalRatePercent);
    }

    [Fact]
    public void An_excess_just_below_the_rate_is_completed_without_approval()
    {
        // SAT-D319 (4)-(5): 4.999 kg on 1000 kg is below 0.5 %; the comparison is exact, nothing is rounded up.
        var evaluation = Evaluate([Line(1, ProductA, 1000m)], [Scale(ProductA, 1004.999m, 0m)], Policy(Rate));

        Assert.Equal(WeighingDifferenceDecision.WithinRate, evaluation.Decision);
        Assert.Equal(4.999m, evaluation.TotalExcessKg.Value);
    }

    [Fact]
    public void An_excess_above_the_rate_requires_approval()
    {
        var evaluation = Evaluate([Line(1, ProductA, 1000m)], [Scale(ProductA, 1008m, 0m)], Policy(Rate));

        Assert.Equal(WeighingDifferenceDecision.ApprovalRequired, evaluation.Decision);
        Assert.Equal(8m, evaluation.TotalExcessKg.Value);
        Assert.Equal(0.8m, WeighingDifferenceCalculator.ExcessRatioPercent(evaluation.TotalExcessKg.Value,
            evaluation.TheoreticalKg.Value));
    }

    [Fact]
    public void Sufficient_physical_stock_leaves_no_excess_and_no_approval()
    {
        // SAT-D322 (1), SAT-D342: the kg above the note is covered by the physical stock the system still holds.
        var evaluation = Evaluate([Line(1, ProductA, 1000m)], [Scale(ProductA, 1008m, 8m)], Policy(Rate));

        var product = evaluation.Products.Single();
        Assert.Equal(WeighingDifferenceDecision.NoExcess, evaluation.Decision);
        Assert.Equal((8m, 8m, 0m), (product.DifferenceKg, product.PhysicallyCoveredKg.Value, product.ExcessKg.Value));
        Assert.Null(product.TargetLineNo);
        Assert.True(evaluation.TotalExcessKg.IsZero);
    }

    [Fact]
    public void Only_the_part_physical_stock_does_not_cover_is_excess()
    {
        var evaluation = Evaluate([Line(1, ProductA, 1000m)], [Scale(ProductA, 1008m, 3m)], Policy(Rate));

        var product = evaluation.Products.Single();
        Assert.Equal((3m, 5m), (product.PhysicallyCoveredKg.Value, product.ExcessKg.Value));
        Assert.Equal(WeighingDifferenceDecision.ApprovalRequired, evaluation.Decision);
    }

    [Fact]
    public void The_reverse_direction_never_creates_an_excess()
    {
        // SAT-D320 (3): the scale below the note yields no record, whatever the rate; the signed difference is still kept.
        var evaluation = Evaluate([Line(1, ProductA, 1000m)], [Scale(ProductA, 990m, 0m)], Policy(Rate));

        Assert.Equal(WeighingDifferenceDecision.NoExcess, evaluation.Decision);
        Assert.Equal(-10m, evaluation.DifferenceKg);
        Assert.Equal((-10m, 0m, 0m),
            (evaluation.Products.Single().DifferenceKg, evaluation.Products.Single().PhysicallyCoveredKg.Value,
                evaluation.Products.Single().ExcessKg.Value));
    }

    [Fact]
    public void Products_never_offset_each_other()
    {
        // SAT-D343: product A is 10 kg over, product B 10 kg under; the note total balances but A's excess still counts.
        var evaluation = Evaluate(
            [Line(1, ProductA, 500m), Line(2, ProductB, 500m, Shipment2)],
            [Scale(ProductA, 510m, 0m), Scale(ProductB, 490m, 0m)],
            Policy(Rate, Bands()));

        Assert.Equal(0m, evaluation.DifferenceKg);
        Assert.Equal(WeighingDifferenceBandLevel.None, evaluation.BandLevel);
        Assert.Equal(10m, evaluation.TotalExcessKg.Value);
        Assert.Equal(WeighingDifferenceDecision.ApprovalRequired, evaluation.Decision);
        Assert.Equal([ProductA, ProductB], evaluation.Products.Select(product => product.ProductId).ToArray());
    }

    [Fact]
    public void The_excess_is_measured_on_the_dispatch_note_total_not_on_the_product()
    {
        // SAT-D320 (2), SAT-D321 (4): 4 kg on product B (100 kg, 4 %) is 0.4 % of the 1000 kg note: below the rate.
        var evaluation = Evaluate(
            [Line(1, ProductA, 900m), Line(2, ProductB, 100m, Shipment2)],
            [Scale(ProductA, 900m, 0m), Scale(ProductB, 104m, 0m)],
            Policy(Rate));

        Assert.Equal(1000m, evaluation.TheoreticalKg.Value);
        Assert.Equal(4m, evaluation.TotalExcessKg.Value);
        Assert.Equal(WeighingDifferenceDecision.WithinRate, evaluation.Decision);
    }

    [Fact]
    public void The_target_line_is_the_products_heaviest_line_and_the_first_on_a_tie()
    {
        // SAT-D322 (3)-(4).
        var evaluation = Evaluate(
            [Line(1, ProductA, 300m), Line(2, ProductA, 500m, Shipment2), Line(3, ProductA, 500m, Shipment3)],
            [Scale(ProductA, 1310m, 0m)],
            Policy(Rate));

        var product = evaluation.Products.Single();
        Assert.Equal(1300m, product.NoteKg.Value);
        Assert.Equal(10m, product.ExcessKg.Value);
        Assert.Equal(2, product.TargetLineNo);
    }

    [Fact]
    public void An_unreadable_rate_never_completes_an_excess_without_approval()
    {
        var withExcess = Evaluate([Line(1, ProductA, 1000m)], [Scale(ProductA, 1000.001m, 0m)], WeighingDifferencePolicy.Unreadable);
        var withoutExcess = Evaluate([Line(1, ProductA, 1000m)], [Scale(ProductA, 1008m, 8m)], WeighingDifferencePolicy.Unreadable);

        Assert.Equal(WeighingDifferenceDecision.ApprovalRequired, withExcess.Decision);
        Assert.Null(withExcess.ApprovalRatePercent);
        Assert.Equal(WeighingDifferenceDecision.NoExcess, withoutExcess.Decision);
    }

    [Fact]
    public void Unreadable_bands_leave_the_band_level_not_evaluated()
    {
        var evaluation = Evaluate([Line(1, ProductA, 1000m)], [Scale(ProductA, 1500m, 500m)], Policy(Rate));

        Assert.Equal(WeighingDifferenceBandLevel.NotEvaluated, evaluation.BandLevel);
        Assert.Null(evaluation.Bands);
    }

    [Theory]
    [InlineData(1009.999, WeighingDifferenceBandLevel.None)]
    [InlineData(1010.0, WeighingDifferenceBandLevel.Warning)]
    [InlineData(1019.999, WeighingDifferenceBandLevel.Warning)]
    [InlineData(1020.0, WeighingDifferenceBandLevel.Approval)]
    [InlineData(1030.0, WeighingDifferenceBandLevel.Block)]
    [InlineData(970.0, WeighingDifferenceBandLevel.Block)]
    [InlineData(985.0, WeighingDifferenceBandLevel.Warning)]
    public void Bands_compare_the_absolute_difference_with_the_note_total_at_or_above(double scaleKg,
        WeighingDifferenceBandLevel expected)
    {
        // SAT-D324: the bands are record and report only; they never change the excess decision.
        var scale = decimal.Round((decimal)scaleKg, 3);
        var evaluation = Evaluate([Line(1, ProductA, 1000m)], [Scale(ProductA, scale, 1000m)], Policy(Rate, Bands()));

        Assert.Equal(expected, evaluation.BandLevel);
        Assert.Equal(WeighingDifferenceDecision.NoExcess, evaluation.Decision);
    }

    [Fact]
    public void A_line_that_is_not_in_kilograms_is_refused()
    {
        var line = new WeighingDifferenceNoteLine(1, Shipment1, ProductA, Quantity.Of(10m, QuantityUnit.Piece));

        var refusal = Assert.Throws<WeighingDifferenceRuleException>(() =>
            WeighingDifferenceCalculator.Evaluate([line], [Scale(ProductA, 10m, 0m)], Policy(Rate)));

        Assert.Equal(WeighingDifferenceRuleCodes.UnitNotKilogram, refusal.Code);
    }

    [Fact]
    public void Every_note_product_needs_exactly_one_scale_measurement()
    {
        var missing = Assert.Throws<WeighingDifferenceRuleException>(() => Evaluate(
            [Line(1, ProductA, 500m), Line(2, ProductB, 500m, Shipment2)], [Scale(ProductA, 500m, 0m)], Policy(Rate)));
        var unexpected = Assert.Throws<WeighingDifferenceRuleException>(() => Evaluate(
            [Line(1, ProductA, 500m)], [Scale(ProductA, 500m, 0m), Scale(ProductB, 1m, 0m)], Policy(Rate)));
        var duplicate = Assert.Throws<WeighingDifferenceRuleException>(() => Evaluate(
            [Line(1, ProductA, 500m)], [Scale(ProductA, 500m, 0m), Scale(ProductA, 501m, 0m)], Policy(Rate)));

        Assert.Equal(WeighingDifferenceRuleCodes.MeasurementMissing, missing.Code);
        Assert.Equal(WeighingDifferenceRuleCodes.MeasurementUnexpected, unexpected.Code);
        Assert.Equal(WeighingDifferenceRuleCodes.MeasurementDuplicate, duplicate.Code);
    }

    [Fact]
    public void Settings_values_out_of_range_or_out_of_order_are_refused_by_the_policy()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new WeighingDifferencePolicy(0m, null));
        Assert.Throws<ArgumentOutOfRangeException>(() => new WeighingDifferencePolicy(WeighingDifferenceCalculator.PercentScale + 1m, null));
        Assert.Throws<ArgumentOutOfRangeException>(() => new WeighingDifferenceBands(2m, 2m, 3m));
        Assert.Throws<ArgumentOutOfRangeException>(() => new WeighingDifferenceBands(0m, 2m, 3m));
        Assert.Throws<ArgumentOutOfRangeException>(() => new WeighingDifferenceBands(1m, 3m, 2m));
    }

    internal static WeighingDifferenceEvaluation Evaluate(IReadOnlyCollection<WeighingDifferenceNoteLine> lines,
        IReadOnlyCollection<WeighingDifferenceProductMeasurement> measurements, WeighingDifferencePolicy policy) =>
        WeighingDifferenceCalculator.Evaluate(lines, measurements, policy);

    internal static WeighingDifferenceNoteLine Line(int lineNo, Guid productId, decimal kg, Guid? shipmentId = null) =>
        new(lineNo, shipmentId ?? Shipment1, productId, Quantity.Of(kg, QuantityUnit.Kilogram));

    internal static WeighingDifferenceProductMeasurement Scale(Guid productId, decimal scaleKg, decimal physicalKg) =>
        new(productId, Quantity.Of(scaleKg, QuantityUnit.Kilogram), Quantity.Of(physicalKg, QuantityUnit.Kilogram));

    internal static WeighingDifferencePolicy Policy(decimal? rate, WeighingDifferenceBands? bands = null) => new(rate, bands);

    private static WeighingDifferenceBands Bands() => new(1m, 2m, 3m);
}
