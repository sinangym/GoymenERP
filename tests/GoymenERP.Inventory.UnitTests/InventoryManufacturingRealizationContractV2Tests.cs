using GoymenERP.Modules.Inventory.Application;
using GoymenERP.Modules.Inventory.Domain.Stock;
using GoymenERP.Modules.Inventory.Infrastructure.Persistence;

namespace GoymenERP.Inventory.UnitTests;

/// <summary>
/// B-03 (API-22/27, DEC-PRDCT-007, URT-D14): the V2 output declaration maps the Manufacturing output class to an
/// Inventory label classification and refuses a class without a stockable label before any read or write.
/// </summary>
public sealed class InventoryManufacturingRealizationContractV2Tests
{
    private static readonly Guid Company = Guid.Parse("92000000-0000-4000-8000-000000000001");

    [Theory]
    [InlineData("GoodProduct", InventoryLabelClassificationV1.Standard)]
    [InlineData("SecondQuality", InventoryLabelClassificationV1.SecondQuality)]
    [InlineData("Scrap", InventoryLabelClassificationV1.Scrap)]
    public void A_stockable_output_class_maps_to_exactly_one_label_classification(
        string outputClass, InventoryLabelClassificationV1 expected)
    {
        Assert.True(InventoryManufacturingOutputClassificationV2.TryMap(outputClass, out var mapped));
        Assert.Equal(expected, mapped);

        var (quantity, classification) = InventoryManufacturingRealizationContractV2.Validate(
            Output() with { OutputClass = outputClass }, Metadata(), Company);
        Assert.Equal(8.5m, quantity.Value);
        Assert.Equal(expected, classification);
    }

    [Theory]
    [InlineData("ProcessLoss")]
    [InlineData("UsableRemnant")]
    [InlineData("COIL")]
    [InlineData("goodproduct")]
    [InlineData("Standard")]
    public void A_class_without_a_stockable_label_is_refused_with_the_closed_v2_code(string outputClass)
    {
        Assert.False(InventoryManufacturingOutputClassificationV2.TryMap(outputClass, out _));
        var error = Assert.Throws<InventoryManufacturingRealizationException>(() =>
            InventoryManufacturingRealizationContractV2.Validate(
                Output() with { OutputClass = outputClass }, Metadata(), Company));
        Assert.Equal((400, InventoryManufacturingRealizationCodesV2.OutputClassNotStockable),
            (error.Status, error.Code));
    }

    [Fact]
    public void The_v1_validation_runs_first_so_a_bad_quantity_is_refused_before_the_class_is_read()
    {
        var error = Assert.Throws<InventoryManufacturingRealizationException>(() =>
            InventoryManufacturingRealizationContractV2.Validate(
                Output() with { OutputClass = "UsableRemnant", QuantityKg = "0" }, Metadata(), Company));
        Assert.Equal(InventoryManufacturingRealizationCodesV1.InvalidQuantity, error.Code);
    }

    [Theory]
    [InlineData("")]
    [InlineData("GoodProduct ")]
    public void A_non_canonical_class_token_keeps_the_v1_invalid_request_refusal(string outputClass)
    {
        Assert.False(InventoryManufacturingOutputClassificationV2.TryMap(outputClass, out _));
        var error = Assert.Throws<InventoryManufacturingRealizationException>(() =>
            InventoryManufacturingRealizationContractV2.Validate(
                Output() with { OutputClass = outputClass }, Metadata(), Company));
        Assert.Equal((400, InventoryManufacturingRealizationCodesV1.InvalidRequest), (error.Status, error.Code));
    }

    [Fact]
    public void The_new_code_is_only_in_the_v2_closed_set_and_the_v1_set_is_unchanged()
    {
        Assert.Equal("inventory_manufacturing_output_class_not_stockable",
            InventoryManufacturingRealizationCodesV2.OutputClassNotStockable);
        Assert.True(InventoryManufacturingRealizationCodesV2.IsDefined(400,
            InventoryManufacturingRealizationCodesV2.OutputClassNotStockable));
        Assert.False(InventoryManufacturingRealizationCodesV2.IsDefined(409,
            InventoryManufacturingRealizationCodesV2.OutputClassNotStockable));
        Assert.False(InventoryManufacturingRealizationCodesV1.IsDefined(400,
            InventoryManufacturingRealizationCodesV2.OutputClassNotStockable));
        Assert.True(InventoryManufacturingRealizationCodesV2.IsDefined(409,
            InventoryManufacturingRealizationCodesV1.ParentInputNotIssued));
        Assert.False(InventoryManufacturingRealizationCodesV2.IsDefined(422, "invented"));

        Assert.Throws<InvalidOperationException>(() => InventoryManufacturingRealizationException.Refused(400,
            InventoryManufacturingRealizationCodesV2.OutputClassNotStockable));
        Assert.Equal(InventoryManufacturingRealizationCodesV2.OutputClassNotStockable,
            InventoryManufacturingRealizationException.RefusedV2(400,
                InventoryManufacturingRealizationCodesV2.OutputClassNotStockable).Code);
        Assert.Throws<InvalidOperationException>(() =>
            InventoryManufacturingRealizationException.RefusedV2(409, "invented"));
    }

    [Fact]
    public void The_v2_participant_keeps_v1_issue_and_return_shapes_and_has_its_own_output_command()
    {
        var type = typeof(IInventoryManufacturingRealizationParticipantV2);
        Assert.Contains(typeof(IAsyncDisposable), type.GetInterfaces());
        Assert.Equal(typeof(InventoryManufacturingInputIssueResultV1),
            type.GetMethod(nameof(IInventoryManufacturingRealizationParticipantV2.StageInputIssueAsync))!
                .ReturnType.GetGenericArguments().Single());
        Assert.Equal(typeof(InventoryManufacturingInputReturnResultV1),
            type.GetMethod(nameof(IInventoryManufacturingRealizationParticipantV2.StageInputReturnAsync))!
                .ReturnType.GetGenericArguments().Single());
        Assert.Equal(typeof(InventoryManufacturingOutputResultV2),
            type.GetMethod(nameof(IInventoryManufacturingRealizationParticipantV2.StageOutputAsync))!
                .ReturnType.GetGenericArguments().Single());
        Assert.NotEqual(InventoryManufacturingRealizationContractV1.OutputCommandName,
            InventoryManufacturingRealizationContractV2.OutputCommandName);

        var implementation = typeof(InventoryManufacturingRealizationParticipant);
        Assert.Contains(typeof(IInventoryManufacturingRealizationParticipantV1), implementation.GetInterfaces());
        Assert.Contains(type, implementation.GetInterfaces());
    }

    [Fact]
    public void The_v2_request_carries_every_v1_field_unchanged()
    {
        var v2 = Output() with { OutputClass = "Scrap" };
        var v1 = v2.ToV1();
        Assert.Equal(typeof(InventoryManufacturingOutputRequestV1).GetProperties().Length,
            typeof(InventoryManufacturingOutputRequestV2).GetProperties().Length);
        foreach (var property in typeof(InventoryManufacturingOutputRequestV1).GetProperties())
        {
            Assert.Equal(property.GetValue(v1),
                typeof(InventoryManufacturingOutputRequestV2).GetProperty(property.Name)!.GetValue(v2));
        }
    }

    private static InventoryManufacturingOutputRequestV2 Output() => new(
        Company, Id(2), Id(8), "9", Id(4), "13", "GoodProduct", Id(9), "product-v1",
        Id(10), Id(11), "MFG-OUTPUT-001", "HEAT-1", "COIL-1", ["MTC"],
        StockPartyKind.Company, Company, StockPartyKind.Company, Company, Id(12), BlockState.Free,
        Id(13), "8.5", new DateTimeOffset(2026, 9, 24, 15, 32, 0, TimeSpan.Zero));

    private static InventoryCommandMetadata Metadata() => new(
        "manufacturing-user", "manufacturing.realization.write", ["MANUFACTURING-USER"],
        "corr-manufacturing-1", "cause-manufacturing-1", "manufacturing-key-1", "api");

    private static Guid Id(int suffix) => Guid.Parse($"92000000-0000-4000-8000-{suffix:000000000000}");
}
