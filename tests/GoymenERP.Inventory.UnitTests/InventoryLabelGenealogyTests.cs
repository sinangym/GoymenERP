using GoymenERP.Modules.Inventory.Domain;
using GoymenERP.Modules.Inventory.Domain.Labels;
using GoymenERP.Modules.Inventory.Domain.Quantities;
using GoymenERP.Modules.Inventory.Domain.Stock;

namespace GoymenERP.Inventory.UnitTests;

public sealed partial class InventoryLabelGenealogyTests
{
    private static readonly Guid CompanyId = Guid.Parse("10000000-0000-0000-0000-000000000001");
    private static readonly Guid ProductId = Guid.Parse("20000000-0000-0000-0000-000000000001");
    private static readonly Guid LotId = Guid.Parse("30000000-0000-0000-0000-000000000001");
    private static readonly Guid OwnerId = Guid.Parse("40000000-0000-0000-0000-000000000001");
    private static readonly Guid LocationId = Guid.Parse("50000000-0000-0000-0000-000000000001");

    [Fact]
    public void Split_requires_one_whole_source_and_conserves_mass_into_new_labels()
    {
        var source = Source("SRC", 10m, version: 7);
        var first = Output("OUT-1", 4m);
        var second = Output("OUT-2", 6m);

        var transformation = Transform(InventoryLabelTransformationKind.Split, [source], [first, second]);

        Assert.Equal(Kilograms.Positive(10m), transformation.Sources[0].Quantity);
        Assert.Equal(2, transformation.Outputs.Count);
    }

    [Fact]
    public void Merge_requires_multiple_sources_and_one_new_label()
    {
        var transformation = Transform(
            InventoryLabelTransformationKind.Merge,
            [Source("SRC-1", 4m), Source("SRC-2", 6m)],
            [Output("OUT", 10m)]);

        Assert.Equal(2, transformation.Sources.Count);
        Assert.Single(transformation.Outputs);
    }

    [Fact]
    public void A_split_or_merge_can_never_be_recorded_with_the_manufacturing_kind()
    {
        Assert.Equal(3, (int)InventoryLabelTransformationKind.Manufacturing);
        AssertInvalid(() => Transform(InventoryLabelTransformationKind.Manufacturing,
            [Source("SRC-1", 4m), Source("SRC-2", 6m)], [Output("OUT", 10m)]));
        AssertInvalid(() => Transform(InventoryLabelTransformationKind.Manufacturing,
            [Source("SRC", 10m)], [Output("OUT-1", 4m), Output("OUT-2", 6m)]));
    }

    [Theory]
    [InlineData(InventoryLabelTransformationKind.Split, 2, 1)]
    [InlineData(InventoryLabelTransformationKind.Merge, 1, 2)]
    public void Shape_that_is_not_split_or_merge_is_refused(
        InventoryLabelTransformationKind kind,
        int sourceCount,
        int outputCount)
    {
        var sources = Enumerable.Range(1, sourceCount).Select(index => Source($"SRC-{index}", 10m / sourceCount)).ToArray();
        var outputs = Enumerable.Range(1, outputCount).Select(index => Output($"OUT-{index}", 10m / outputCount)).ToArray();

        var error = Assert.Throws<InventoryRuleException>(() => Transform(kind, sources, outputs));

        Assert.Equal(InventoryRuleCodes.InvalidLabelTransformation, error.Code);
    }

    [Fact]
    public void Transformation_refuses_mass_creation_or_loss()
    {
        var error = Assert.Throws<InventoryRuleException>(() => Transform(
            InventoryLabelTransformationKind.Split,
            [Source("SRC", 10m)],
            [Output("OUT-1", 4m), Output("OUT-2", 5m)]));

        Assert.Equal(InventoryRuleCodes.InvalidLabelTransformation, error.Code);
    }

    [Fact]
    public void Transformation_refuses_cross_lot_or_cross_position_mixing()
    {
        var otherLot = Source("OTHER-LOT", 5m, lotId: Guid.NewGuid());
        var otherLocation = Source("OTHER-LOCATION", 5m, locationId: Guid.NewGuid());

        AssertInvalid(() => Transform(InventoryLabelTransformationKind.Merge,
            [Source("SRC", 5m), otherLot], [Output("OUT", 10m)]));
        AssertInvalid(() => Transform(InventoryLabelTransformationKind.Merge,
            [Source("SRC", 5m), otherLocation], [Output("OUT", 10m)]));
    }

    [Fact]
    public void Transformation_refuses_reusing_a_source_identity_as_an_output()
    {
        var source = Source("SRC", 10m);
        var duplicate = Output("OUT-1", 4m, source.Label.Id);

        AssertInvalid(() => Transform(InventoryLabelTransformationKind.Split,
            [source], [duplicate, Output("OUT-2", 6m)]));
    }

    [Fact]
    public void Output_cannot_copy_print_or_document_history()
    {
        var output = Output("OUT-1", 4m);
        output.Label.Print(DateTimeOffset.Parse("2026-09-24T10:00:00Z"), "actor");

        AssertInvalid(() => Transform(InventoryLabelTransformationKind.Split,
            [Source("SRC", 10m)], [output, Output("OUT-2", 6m)]));
    }

    [Fact]
    public void Transformation_requires_exact_positive_source_versions()
    {
        AssertInvalid(() => Transform(InventoryLabelTransformationKind.Split,
            [Source("SRC", 10m, version: 0)], [Output("OUT-1", 4m), Output("OUT-2", 6m)]));
    }

    [Fact]
    public void Split_of_a_usable_remnant_gives_every_child_the_usable_remnant_production_status()
    {
        var source = Remnant(Source("SRC", 10m));

        var transformation = Transform(InventoryLabelTransformationKind.Split, [source],
            [Remnant(Output("OUT-1", 4m)), Remnant(Output("OUT-2", 6m))]);

        Assert.Equal(LabelProductionStatus.UsableRemnant, transformation.OutputProductionStatus);
        Assert.All(transformation.Outputs,
            output => Assert.Equal(LabelProductionStatus.UsableRemnant, output.Label.ProductionStatus));
    }

    [Fact]
    public void Split_of_a_usable_remnant_refuses_a_child_without_the_inherited_production_status()
    {
        AssertInvalid(() => Transform(InventoryLabelTransformationKind.Split, [Remnant(Source("SRC", 10m))],
            [Remnant(Output("OUT-1", 4m)), Output("OUT-2", 6m)]));
        AssertInvalid(() => Transform(InventoryLabelTransformationKind.Split, [Remnant(Source("SRC", 10m))],
            [Output("OUT-1", 4m), Output("OUT-2", 6m)]));
    }

    [Fact]
    public void Split_of_a_label_without_production_status_keeps_the_children_without_one()
    {
        var transformation = Transform(InventoryLabelTransformationKind.Split, [Source("SRC", 10m)],
            [Output("OUT-1", 4m), Output("OUT-2", 6m)]);

        Assert.Null(transformation.OutputProductionStatus);
        Assert.All(transformation.Outputs, output => Assert.Null(output.Label.ProductionStatus));
        AssertInvalid(() => Transform(InventoryLabelTransformationKind.Split, [Source("SRC", 10m)],
            [Remnant(Output("OUT-1", 4m)), Output("OUT-2", 6m)]));
    }

    [Fact]
    public void Merge_result_is_a_usable_remnant_only_when_every_source_is_one()
    {
        var transformation = Transform(InventoryLabelTransformationKind.Merge,
            [Remnant(Source("SRC-1", 4m)), Remnant(Source("SRC-2", 6m))], [Remnant(Output("OUT", 10m))]);

        Assert.Equal(LabelProductionStatus.UsableRemnant, transformation.OutputProductionStatus);
        Assert.Equal(LabelProductionStatus.UsableRemnant, Assert.Single(transformation.Outputs).Label.ProductionStatus);
        AssertInvalid(() => Transform(InventoryLabelTransformationKind.Merge,
            [Remnant(Source("SRC-1", 4m)), Remnant(Source("SRC-2", 6m))], [Output("OUT", 10m)]));
    }

    [Fact]
    public void Merge_of_mixed_sources_leaves_the_result_without_production_status()
    {
        var transformation = Transform(InventoryLabelTransformationKind.Merge,
            [Remnant(Source("SRC-1", 4m)), Source("SRC-2", 6m)], [Output("OUT", 10m)]);

        Assert.Null(transformation.OutputProductionStatus);
        Assert.Null(Assert.Single(transformation.Outputs).Label.ProductionStatus);
        AssertInvalid(() => Transform(InventoryLabelTransformationKind.Merge,
            [Remnant(Source("SRC-1", 4m)), Source("SRC-2", 6m)], [Remnant(Output("OUT", 10m))]));
    }

    [Theory]
    [InlineData(new[] { true }, true)]
    [InlineData(new[] { false }, false)]
    [InlineData(new[] { true, true }, true)]
    [InlineData(new[] { true, true, true }, true)]
    [InlineData(new[] { true, false }, false)]
    [InlineData(new[] { false, true, true }, false)]
    [InlineData(new[] { false, false }, false)]
    public void Inherited_production_status_is_usable_remnant_only_when_every_source_is_one(
        bool[] remnantSources,
        bool expectedRemnant)
    {
        var labels = remnantSources.Select((remnant, index) =>
        {
            var label = Label(Guid.NewGuid(), $"SRC-{index}", LotId);
            if (remnant)
            {
                label.MarkProductionStatus(LabelProductionStatus.UsableRemnant);
            }

            return label;
        }).ToArray();

        Assert.Equal(expectedRemnant ? (LabelProductionStatus?)LabelProductionStatus.UsableRemnant : null,
            InventoryLabelTransformation.InheritedProductionStatus(labels));
    }

    [Fact]
    public void Inherited_production_status_ignores_the_classification_and_needs_a_source()
    {
        var secondQuality = Label(Guid.NewGuid(), "SRC-SECOND", LotId);
        secondQuality.Classify(LabelClassification.SecondQuality);
        var remnant = Label(Guid.NewGuid(), "SRC-REMNANT", LotId);
        remnant.Classify(LabelClassification.Standard);
        remnant.MarkProductionStatus(LabelProductionStatus.UsableRemnant);

        Assert.Null(InventoryLabelTransformation.InheritedProductionStatus([secondQuality]));
        Assert.Equal(LabelProductionStatus.UsableRemnant,
            InventoryLabelTransformation.InheritedProductionStatus([remnant]));
        Assert.Equal(LabelClassification.Standard, remnant.Classification);
        AssertInvalid(() => InventoryLabelTransformation.InheritedProductionStatus([]));
    }

    private static InventoryLabelTransformationSource Remnant(InventoryLabelTransformationSource source)
    {
        source.Label.MarkProductionStatus(LabelProductionStatus.UsableRemnant);
        return source;
    }

    private static InventoryLabelTransformationOutput Remnant(InventoryLabelTransformationOutput output)
    {
        output.Label.MarkProductionStatus(LabelProductionStatus.UsableRemnant);
        return output;
    }

    private static InventoryLabelTransformation Transform(
        InventoryLabelTransformationKind kind,
        IReadOnlyList<InventoryLabelTransformationSource> sources,
        IReadOnlyList<InventoryLabelTransformationOutput> outputs) =>
        new(Guid.NewGuid(), CompanyId, kind, DateTimeOffset.Parse("2026-09-24T10:00:00Z"), sources, outputs, "Paketleme");

    private static InventoryLabelTransformationSource Source(
        string code,
        decimal quantity,
        long version = 1,
        Guid? lotId = null,
        Guid? locationId = null)
    {
        var label = Label(Guid.NewGuid(), code, lotId ?? LotId);
        return new(label, Position(label, locationId ?? LocationId), Kilograms.Positive(quantity), version);
    }

    private static InventoryLabelTransformationOutput Output(string code, decimal quantity, Guid? id = null)
    {
        var label = Label(id ?? Guid.NewGuid(), code, LotId);
        return new(label, Position(label, LocationId), Kilograms.Positive(quantity));
    }

    private static MaterialLabel Label(Guid id, string code, Guid lotId) =>
        new(id, CompanyId, code, ProductId, lotId, ["MTC"], "HEAT-1", "COIL-1");

    private static StockPosition Position(MaterialLabel label, Guid locationId) =>
        new(CompanyId, new StockOwner(StockPartyKind.Company, OwnerId),
            new StockCustodian(StockPartyKind.Company, CompanyId), locationId, label, BlockState.Free);

    private static void AssertInvalid(Action action)
    {
        var error = Assert.Throws<InventoryRuleException>(action);
        Assert.Equal(InventoryRuleCodes.InvalidLabelTransformation, error.Code);
    }
}
