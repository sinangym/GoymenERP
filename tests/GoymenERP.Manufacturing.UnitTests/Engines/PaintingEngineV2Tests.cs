using GoymenERP.Modules.Manufacturing.Domain.Engines;
using GoymenERP.Modules.Manufacturing.Domain.Engines.Painting;
using GoymenERP.Modules.Manufacturing.Domain.Plans;

namespace GoymenERP.Manufacturing.UnitTests.Engines;

public sealed class PaintingEngineV2Tests
{
    [Fact]
    public void Two_ordered_layers_and_cleaning_match_the_D_M016_golden_without_double_loss()
    {
        var firstId = Guid.NewGuid();
        var secondId = Guid.NewGuid();
        var spec = Plan([
            Layer(firstId, 50m, 0.50m, 0.80m),
            Layer(secondId, 25m, 0.50m, 1.00m),
        ], cleaningLitres: 2m);

        var result = PaintingEngineV2.Calculate(spec);

        Assert.Equal(EngineOutcome.Computed, result.Outcome);
        Assert.Equal(PaintingEngineV2.FormulaVersion, result.Value!.FormulaVersion);
        Assert.Equal([firstId, secondId], result.Value.OrderedLayers.Select(layer => layer.LayerId));
        Assert.Equal([5m, 2.5m], result.Value.OrderedLayers.Select(layer => layer.DryFilmLitres));
        Assert.Equal([12.5m, 5m], result.Value.OrderedLayers.Select(layer => layer.TheoreticalWetLitres));
        Assert.Equal(7.5m, result.Value.TotalDryFilmLitres);
        Assert.Equal(17.5m, result.Value.TotalTheoreticalWetPaintLitres);
        Assert.Equal(2m, result.Value.PlannedCleaningLitres);
        Assert.Equal(19.5m, result.Value.TotalPlannedLitres);
        Assert.Null(result.Value.TotalTheoreticalPaintKilograms);
    }

    [Fact]
    public void A_source_density_enables_only_that_layers_theoretical_kg_and_all_densities_enable_the_total()
    {
        var density = new PaintingDensitySnapshotV1(Guid.NewGuid(), "density-v3", 1.2m);
        var both = PaintingEngineV2.Calculate(Plan([
            Layer(Guid.NewGuid(), 50m, 0.5m, 0.8m, density),
            Layer(Guid.NewGuid(), 25m, 0.5m, 1m, density),
        ], 2m)).Value!;

        Assert.Equal([15m, 6m], both.OrderedLayers.Select(layer => layer.TheoreticalPaintKilograms));
        Assert.Equal(21m, both.TotalTheoreticalPaintKilograms);

        var partial = PaintingEngineV2.Calculate(Plan([
            Layer(Guid.NewGuid(), 50m, 0.5m, 0.8m, density),
            Layer(Guid.NewGuid(), 25m, 0.5m, 1m),
        ], 2m)).Value!;

        Assert.Equal(15m, partial.OrderedLayers[0].TheoreticalPaintKilograms);
        Assert.Null(partial.OrderedLayers[1].TheoreticalPaintKilograms);
        Assert.Null(partial.TotalTheoreticalPaintKilograms);
    }

    [Fact]
    public void Missing_sources_and_formula_inputs_are_unavailable_and_never_defaulted()
    {
        var layer = new PaintingLayerSnapshotV1(
            Guid.NewGuid(), null, null, null, null, null, null);

        var result = PaintingEngineV2.Calculate(Plan([layer], cleaningLitres: null));

        Assert.Equal(EngineOutcome.Unavailable, result.Outcome);
        Assert.Equal([
            "orderedLayers[0].material",
            "orderedLayers[0].colour",
            "orderedLayers[0].surface",
            "orderedLayers[0].dryFilmMicrons",
            "orderedLayers[0].volumetricSolidsRatio",
            "orderedLayers[0].transferEfficiency",
            PaintingEngineV2.CleaningParameter,
        ], result.MissingParameters);
    }

    [Fact]
    public void An_empty_layer_list_is_unavailable_and_input_collection_is_defensively_copied()
    {
        Assert.Equal([PaintingEngineV2.LayersParameter],
            PaintingEngineV2.Calculate(Plan([], 0m)).MissingParameters);

        var layers = new List<PaintingLayerSnapshotV1> { Layer(Guid.NewGuid(), 50m, 0.5m, 0.8m) };
        var spec = Plan(layers, 0m);
        layers.Clear();

        Assert.Single(spec.OrderedLayers);
        Assert.Equal(EngineOutcome.Computed, PaintingEngineV2.Calculate(spec).Outcome);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-0.1)]
    public void Ratios_must_be_positive(decimal ratio)
    {
        var result = PaintingEngineV2.Calculate(Plan([
            Layer(Guid.NewGuid(), 50m, ratio, 0.8m),
        ], 0m));

        Assert.Equal((EngineCodes.ValueNotPositive, "orderedLayers[0].volumetricSolidsRatio"), Refusal(result));
    }

    [Theory]
    [InlineData(1.0001)]
    [InlineData(55)]
    public void Ratios_above_one_are_refused_not_reinterpreted_as_percentages(decimal ratio)
    {
        var solids = PaintingEngineV2.Calculate(Plan([
            Layer(Guid.NewGuid(), 50m, ratio, 0.8m),
        ], 0m));
        var efficiency = PaintingEngineV2.Calculate(Plan([
            Layer(Guid.NewGuid(), 50m, 0.5m, ratio),
        ], 0m));

        Assert.Equal((EngineCodes.RatioAboveOne, "orderedLayers[0].volumetricSolidsRatio"), Refusal(solids));
        Assert.Equal((EngineCodes.RatioAboveOne, "orderedLayers[0].transferEfficiency"), Refusal(efficiency));
    }

    [Fact]
    public void Negative_cleaning_is_refused_and_zero_must_be_explicit()
    {
        var negative = PaintingEngineV2.Calculate(Plan([
            Layer(Guid.NewGuid(), 50m, 0.5m, 0.8m),
        ], -1m));
        var missing = PaintingEngineV2.Calculate(Plan([
            Layer(Guid.NewGuid(), 50m, 0.5m, 0.8m),
        ], null));

        Assert.Equal((EngineCodes.ValueNegative, PaintingEngineV2.CleaningParameter), Refusal(negative));
        Assert.Equal([PaintingEngineV2.CleaningParameter], missing.MissingParameters);
    }

    [Fact]
    public void Density_must_be_positive_and_is_never_substituted()
    {
        var density = new PaintingDensitySnapshotV1(Guid.NewGuid(), "density-v1", 0m);

        var result = PaintingEngineV2.Calculate(Plan([
            Layer(Guid.NewGuid(), 50m, 0.5m, 0.8m, density),
        ], 0m));

        Assert.Equal((EngineCodes.ValueNotPositive, "orderedLayers[0].density.kilogramsPerLitre"), Refusal(result));
    }

    [Fact]
    public void Non_terminating_or_overflowing_answers_are_refused_without_rounding_or_throwing()
    {
        var nonTerminating = PaintingEngineV2.Calculate(Plan([
            Layer(Guid.NewGuid(), 1m, 1m, 0.3m),
        ], 0m));
        var overflow = PaintingEngineV2.Calculate(Plan([
            LayerWithArea(Guid.NewGuid(), decimal.MaxValue, decimal.MaxValue, 1m, 1m),
        ], 0m));

        Assert.Equal((PaintingEngineV2.ArithmeticNotRepresentableCode,
            "orderedLayers[0].theoreticalWetLitres"), Refusal(nonTerminating));
        Assert.Equal(PaintingEngineV2.ArithmeticNotRepresentableCode, overflow.RefusalCode);
    }

    [Fact]
    public void Duplicate_layer_identity_is_rejected_but_source_order_is_never_sorted_or_renumbered()
    {
        var repeatedId = Guid.NewGuid();
        Assert.Throws<ArgumentException>(() => Plan([
            Layer(repeatedId, 50m, 0.5m, 0.8m),
            Layer(repeatedId, 25m, 0.5m, 1m),
        ], 0m));

        var first = Guid.NewGuid();
        var second = Guid.NewGuid();
        var output = PaintingEngineV2.Calculate(Plan([
            Layer(second, 25m, 0.5m, 1m),
            Layer(first, 50m, 0.5m, 0.8m),
        ], 0m)).Value!;

        Assert.Equal([second, first], output.OrderedLayers.Select(layer => layer.LayerId));
    }

    [Fact]
    public void Version_2_has_no_scrap_factor_or_actual_realization_contract()
    {
        var inputProperties = typeof(PaintingPlanSpecV1).GetProperties()
            .Concat(typeof(PaintingLayerSnapshotV1).GetProperties())
            .Select(property => property.Name)
            .ToArray();
        var outputProperties = typeof(PaintingPlanCalculationV2).GetProperties()
            .Concat(typeof(PaintingLayerCalculationV2).GetProperties())
            .Select(property => property.Name)
            .ToArray();

        Assert.DoesNotContain(inputProperties, name => name.Contains("Scrap", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(outputProperties, name => name.Contains("Actual", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(outputProperties, name => name.Contains("Return", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(outputProperties, name => name.Contains("Purchased", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Legacy_GDC_PNT_1_formula_and_golden_remain_unchanged()
    {
        var legacy = PaintingEngineV1.Paint(new PaintLitresInputV1(480m, 80m, 0.55m, 1.10m));

        Assert.Equal("GDC-PNT/1 (master v3 §28.7)", PaintingEngineV1.FormulaVersion);
        Assert.Equal(76.8m, legacy.Value!.TheoreticalLitres);
    }

    private static PaintingPlanSpecV1 Plan(
        IEnumerable<PaintingLayerSnapshotV1> layers,
        decimal? cleaningLitres) =>
        new(Guid.Parse("42dbbb34-b25c-43fc-9c38-8cb4cf9050d6"), "recipe-v7", layers, cleaningLitres);

    private static PaintingLayerSnapshotV1 Layer(
        Guid id,
        decimal filmMicrons,
        decimal solids,
        decimal efficiency,
        PaintingDensitySnapshotV1? density = null) =>
        LayerWithArea(id, 100m, filmMicrons, solids, efficiency, density);

    private static PaintingLayerSnapshotV1 LayerWithArea(
        Guid id,
        decimal areaSquareMetres,
        decimal filmMicrons,
        decimal solids,
        decimal efficiency,
        PaintingDensitySnapshotV1? density = null) =>
        new(
            id,
            new PaintingMaterialReferenceSnapshotV1(Guid.NewGuid(), "material-v2"),
            new PaintingColourReferenceSnapshotV1(Guid.NewGuid(), "colour-v4"),
            new PaintingSurfaceSnapshotV1(Guid.NewGuid(), "surface-v3", "outer", areaSquareMetres),
            filmMicrons,
            solids,
            efficiency,
            density);

    private static (string?, string?) Refusal(EngineResult<PaintingPlanCalculationV2> result) =>
        (result.RefusalCode, result.RefusedParameter);
}
