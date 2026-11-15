using GoymenERP.Modules.Inventory.Application;
using GoymenERP.Modules.Manufacturing.Application;
using GoymenERP.Modules.Manufacturing.Domain.Common;
using GoymenERP.Modules.Manufacturing.Domain.Orders;

namespace GoymenERP.Manufacturing.UnitTests;

/// <summary>
/// URT-S16 (URT-D43, BS-5, BS-14): the output declaration carries the operator's label classification for a usable
/// remnant to the realization port. Only Standard or SecondQuality, only with UsableRemnant; anything else is refused
/// before any owner call or write. The choice joins the request hash only when it was sent.
/// </summary>
public sealed partial class ManufacturingCommandServiceTests
{
    [Theory]
    [InlineData(null, null)]
    [InlineData("SecondQuality", InventoryLabelClassificationV1.SecondQuality)]
    [InlineData("Standard", InventoryLabelClassificationV1.Standard)]
    public async Task A_usable_remnant_output_forwards_the_operator_label_classification_as_sent(
        string? sent, InventoryLabelClassificationV1? forwarded)
    {
        var fixture = new Fixture();
        var order = await fixture.ApprovedOrderAsync();
        order.IssueInput(order.Version, Guid.NewGuid(), Roll, new Kilograms(20m), At);

        var result = await fixture.Service.DeclareInventoryOutputAsync(order.Id,
            OutputLabelRequest(order.Version, "UsableRemnant", sent), "output-remnant-1", Permission,
            CancellationToken.None);

        Assert.Equal(ManufacturingApiStatus.Ok, result.Status);
        var command = Assert.Single(fixture.ReturnOutputs.Outputs);
        Assert.Equal((OutputClass.UsableRemnant, forwarded), (command.OutputClass, command.RemnantClassification));
    }

    [Fact]
    public async Task A_good_product_output_without_a_remnant_classification_forwards_none()
    {
        var fixture = new Fixture();
        var order = await fixture.ApprovedOrderAsync();
        order.IssueInput(order.Version, Guid.NewGuid(), Roll, new Kilograms(20m), At);

        var result = await fixture.Service.DeclareInventoryOutputAsync(order.Id,
            OutputLabelRequest(order.Version, "GoodProduct", null), "output-good-1", Permission,
            CancellationToken.None);

        Assert.Equal(ManufacturingApiStatus.Ok, result.Status);
        var command = Assert.Single(fixture.ReturnOutputs.Outputs);
        Assert.Equal((OutputClass.GoodProduct, (InventoryLabelClassificationV1?)null),
            (command.OutputClass, command.RemnantClassification));
    }

    [Theory]
    [InlineData("GoodProduct", "Standard")]
    [InlineData("SecondQuality", "SecondQuality")]
    [InlineData("Scrap", "SecondQuality")]
    [InlineData("UsableRemnant", "Scrap")]
    [InlineData("UsableRemnant", "standard")]
    [InlineData("UsableRemnant", "1")]
    [InlineData("UsableRemnant", "")]
    [InlineData("UsableRemnant", " Standard")]
    public async Task A_remnant_classification_outside_the_fixed_mapping_is_invalid_before_any_owner_call(
        string outputClass, string remnantClassification)
    {
        var fixture = new Fixture();
        var order = await fixture.ApprovedOrderAsync();

        var result = await fixture.Service.DeclareInventoryOutputAsync(order.Id,
            OutputLabelRequest(order.Version, outputClass, remnantClassification), "output-remnant-invalid",
            Permission, CancellationToken.None);

        Assert.Equal((ManufacturingApiStatus.Invalid, ManufacturingApiCodes.RequestInvalid, "remnantClassification"),
            (result.Status, result.Code, result.Field));
        Assert.Empty(fixture.ReturnOutputs.Outputs);
        Assert.Empty(fixture.Store.Writes);
    }

    [Fact]
    public async Task The_same_key_with_another_remnant_classification_is_an_idempotency_conflict()
    {
        var fixture = new Fixture();
        var order = await fixture.ApprovedOrderAsync();
        order.IssueInput(order.Version, Guid.NewGuid(), Roll, new Kilograms(20m), At);
        var first = OutputLabelRequest(order.Version, "UsableRemnant", null);

        var recorded = await fixture.Service.DeclareInventoryOutputAsync(order.Id, first, "output-remnant-key",
            Permission, CancellationToken.None);
        var replay = await fixture.Service.DeclareInventoryOutputAsync(order.Id, first, "output-remnant-key",
            Permission, CancellationToken.None);
        var changed = await fixture.Service.DeclareInventoryOutputAsync(order.Id,
            first with { RemnantClassification = "Standard" }, "output-remnant-key", Permission,
            CancellationToken.None);

        Assert.Equal((ManufacturingApiStatus.Ok, false), (recorded.Status, recorded.IsReplay));
        Assert.Equal((ManufacturingApiStatus.Ok, true), (replay.Status, replay.IsReplay));
        Assert.Equal((ManufacturingApiStatus.Conflict, ManufacturingApiCodes.IdempotencyConflict),
            (changed.Status, changed.Code));
        Assert.Single(fixture.ReturnOutputs.Outputs);
    }

    private static DeclareInventoryOutputRequestV1 OutputLabelRequest(
        long expectedVersion, string outputClass, string? remnantClassification) =>
        new(Guid.NewGuid(), expectedVersion, Roll, "8", outputClass, Product, "12", Guid.NewGuid(), Guid.NewGuid(),
            "SENTETIK-LBL-OUT", null, null, [], "Company", Company, "Company", Company, Location, "Free",
            Guid.NewGuid(), "5", At.AddMinutes(1), remnantClassification);
}
