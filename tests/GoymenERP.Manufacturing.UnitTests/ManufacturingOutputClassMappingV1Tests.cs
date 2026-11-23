using GoymenERP.Modules.Inventory.Application;
using GoymenERP.Modules.Manufacturing.Application;
using GoymenERP.Modules.Manufacturing.Domain.Orders;

namespace GoymenERP.Manufacturing.UnitTests;

/// <summary>
/// URT-S16 (URT-D43, BS-5, BS-14): the fixed output class to stock label mapping. Every company gets the same mapping;
/// a usable remnant keeps its label classification and carries the separate UsableRemnant production status.
/// </summary>
public sealed class ManufacturingOutputClassMappingV1Tests
{
    [Theory]
    [InlineData(OutputClass.GoodProduct, InventoryLabelClassificationV1.Standard)]
    [InlineData(OutputClass.SecondQuality, InventoryLabelClassificationV1.SecondQuality)]
    [InlineData(OutputClass.Scrap, InventoryLabelClassificationV1.Scrap)]
    public void A_stockable_output_class_opens_its_fixed_label_classification_with_no_production_status(
        OutputClass outputClass, InventoryLabelClassificationV1 expected)
    {
        Assert.True(ManufacturingOutputClassMappingV1.TryMap(outputClass, null, out var label));

        Assert.Equal(new ManufacturingOutputLabelV1(outputClass.ToString(), expected, null), label);
        Assert.True(InventoryManufacturingOutputClassificationV2.TryMap(label.InventoryOutputClass,
            out var inventoryClassification));
        Assert.Equal(expected, inventoryClassification);
    }

    [Theory]
    [InlineData(null, InventoryLabelClassificationV1.SecondQuality)]
    [InlineData(InventoryLabelClassificationV1.SecondQuality, InventoryLabelClassificationV1.SecondQuality)]
    [InlineData(InventoryLabelClassificationV1.Standard, InventoryLabelClassificationV1.Standard)]
    public void A_usable_remnant_keeps_the_chosen_classification_and_carries_the_usable_remnant_status(
        InventoryLabelClassificationV1? chosen, InventoryLabelClassificationV1 expected)
    {
        Assert.True(ManufacturingOutputClassMappingV1.TryMap(OutputClass.UsableRemnant, chosen, out var label));

        Assert.Equal(new ManufacturingOutputLabelV1(InventoryManufacturingOutputClassificationV2.UsableRemnant,
            expected, InventoryLabelProductionStatusV1.UsableRemnant), label);
        Assert.True(InventoryManufacturingOutputClassificationV2.TryResolveRemnant(chosen,
            out var inventoryClassification));
        Assert.Equal(expected, inventoryClassification);
    }

    [Fact]
    public void The_remnant_default_is_second_quality_and_equals_the_inventory_default()
    {
        Assert.Equal(InventoryLabelClassificationV1.SecondQuality,
            ManufacturingOutputClassMappingV1.DefaultRemnantClassification);
        Assert.Equal(InventoryManufacturingOutputClassificationV2.DefaultRemnantClassification,
            ManufacturingOutputClassMappingV1.DefaultRemnantClassification);
    }

    [Theory]
    [InlineData(OutputClass.ProcessLoss, null)]
    [InlineData((OutputClass)0, null)]
    [InlineData((OutputClass)99, null)]
    [InlineData(OutputClass.UsableRemnant, InventoryLabelClassificationV1.Scrap)]
    [InlineData(OutputClass.UsableRemnant, (InventoryLabelClassificationV1)99)]
    [InlineData(OutputClass.GoodProduct, InventoryLabelClassificationV1.Standard)]
    [InlineData(OutputClass.SecondQuality, InventoryLabelClassificationV1.SecondQuality)]
    [InlineData(OutputClass.Scrap, InventoryLabelClassificationV1.SecondQuality)]
    public void An_output_with_no_stock_label_or_a_misplaced_remnant_classification_is_refused(
        OutputClass outputClass, InventoryLabelClassificationV1? remnantClassification)
    {
        Assert.False(ManufacturingOutputClassMappingV1.TryMap(outputClass, remnantClassification, out var label));
        Assert.Null(label);
    }

    [Fact]
    public void Every_defined_output_class_except_process_loss_has_a_label_that_inventory_accepts()
    {
        foreach (var outputClass in Enum.GetValues<OutputClass>())
        {
            var mapped = ManufacturingOutputClassMappingV1.TryMap(outputClass, null, out var label);
            Assert.Equal(outputClass != OutputClass.ProcessLoss, mapped);
            if (label is null)
            {
                continue;
            }

            InventoryLabelClassificationV1 classification;
            var inventoryAccepts = label.ProductionStatus is null
                ? InventoryManufacturingOutputClassificationV2.TryMap(label.InventoryOutputClass, out classification)
                : InventoryManufacturingOutputClassificationV2.TryResolveRemnant(null, out classification);
            Assert.True(inventoryAccepts);
            Assert.Equal(label.Classification, classification);
        }
    }

    [Theory]
    [InlineData(null, true, null)]
    [InlineData("Standard", true, InventoryLabelClassificationV1.Standard)]
    [InlineData("SecondQuality", true, InventoryLabelClassificationV1.SecondQuality)]
    [InlineData("Scrap", false, null)]
    [InlineData("standard", false, null)]
    [InlineData("1", false, null)]
    [InlineData("", false, null)]
    [InlineData("Standard ", false, null)]
    public void The_remnant_classification_token_accepts_only_the_two_exact_names(
        string? token, bool accepted, InventoryLabelClassificationV1? expected)
    {
        Assert.Equal(accepted, ManufacturingOutputClassMappingV1.TryParseRemnantClassification(token,
            out var classification));
        Assert.Equal(expected, classification);
    }
}
