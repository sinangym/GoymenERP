using GoymenERP.Modules.Inventory.Application;
using GoymenERP.Modules.Inventory.Domain;
using GoymenERP.Modules.Inventory.Domain.Movements;
using GoymenERP.Modules.Inventory.Domain.Stock;
using static GoymenERP.Inventory.UnitTests.InventoryFixture;

namespace GoymenERP.Inventory.UnitTests;

/// <summary>
/// F-009 D1 (URT-D28, URT-D31): the output-reversal contract, the new movement kind and the order-cancelled plan
/// release reason, without a database.
/// </summary>
public sealed class InventoryManufacturingOutputReversalContractTests
{
    private static readonly Guid Company = Guid.Parse("92000000-0000-4000-8000-000000000201");
    private static readonly DateTimeOffset OccurredAt = new(2026, 11, 15, 9, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Source_kind_command_name_and_movement_kind_are_stable()
    {
        Assert.Equal("manufacturing.output-reversal", InventoryManufacturingOutputReversalContractV1.OutputReversalSourceKind);
        Assert.Equal("inventory.manufacturing-output-reversal.stage.v1", InventoryManufacturingOutputReversalContractV1.CommandName);
        Assert.NotEqual(InventoryManufacturingRealizationContractV1.OutputSourceKind,
            InventoryManufacturingOutputReversalContractV1.OutputReversalSourceKind);
        Assert.Equal(10, (int)StockMovementKind.ManufacturingOutputReversal);
    }

    [Fact]
    public void A_valid_request_returns_its_exact_kilograms()
    {
        Assert.Equal(12.125m, InventoryManufacturingOutputReversalContractV1.Validate(Request(), Metadata(), Company).Value);
    }

    [Theory]
    [InlineData("company")]
    [InlineData("order")]
    [InlineData("output")]
    [InlineData("reversal")]
    [InlineData("label")]
    [InlineData("movement")]
    [InlineData("local-time")]
    [InlineData("reversal-is-movement")]
    [InlineData("reversal-is-output")]
    public void A_malformed_request_is_invalid(string fault)
    {
        var request = Request();
        var invalid = fault switch
        {
            "company" => request with { CompanyId = Guid.Empty },
            "order" => request with { ManufacturingOrderId = Guid.Empty },
            "output" => request with { OutputId = Guid.Empty },
            "reversal" => request with { ReversalId = Guid.Empty },
            "label" => request with { LabelId = Guid.Empty },
            "movement" => request with { MovementId = Guid.Empty },
            "local-time" => request with { OccurredAtUtc = new DateTimeOffset(2026, 11, 15, 12, 0, 0, TimeSpan.FromHours(3)) },
            "reversal-is-movement" => request with { MovementId = request.ReversalId },
            _ => request with { OutputId = request.ReversalId },
        };

        var error = Assert.Throws<InventoryManufacturingRealizationException>(() =>
            InventoryManufacturingOutputReversalContractV1.Validate(invalid, Metadata(), Company));
        Assert.Equal((400, InventoryManufacturingRealizationCodesV1.InvalidRequest), (error.Status, error.Code));
    }

    [Theory]
    [InlineData("0")]
    [InlineData("-1")]
    [InlineData("1.2345")]
    [InlineData("1,5")]
    public void A_quantity_that_is_not_exact_positive_kilograms_is_refused(string quantity)
    {
        var error = Assert.Throws<InventoryManufacturingRealizationException>(() =>
            InventoryManufacturingOutputReversalContractV1.Validate(Request() with { QuantityKg = quantity }, Metadata(),
                Company));
        Assert.Equal((400, InventoryManufacturingRealizationCodesV1.InvalidQuantity), (error.Status, error.Code));
    }

    [Fact]
    public void Another_company_is_not_found()
    {
        var error = Assert.Throws<InventoryManufacturingRealizationException>(() =>
            InventoryManufacturingOutputReversalContractV1.Validate(Request() with { CompanyId = Id(90) }, Metadata(),
                Company));
        Assert.Equal((404, InventoryManufacturingRealizationCodesV1.InvalidReference), (error.Status, error.Code));
    }

    [Fact]
    public void Reversal_refusals_are_defined_conflicts_only_in_the_reversal_contract()
    {
        foreach (var code in new[]
                 {
                     InventoryManufacturingOutputReversalCodesV1.ExceedsProduced,
                     InventoryManufacturingOutputReversalCodesV1.Shipped,
                     InventoryManufacturingOutputReversalCodesV1.IssuedToManufacturing,
                     InventoryManufacturingOutputReversalCodesV1.Transferred,
                     InventoryManufacturingOutputReversalCodesV1.Allocated,
                     InventoryManufacturingOutputReversalCodesV1.Blocked,
                     InventoryManufacturingOutputReversalCodesV1.NotFree,
                 })
        {
            Assert.True(InventoryManufacturingOutputReversalCodesV1.IsDefined(409, code), code);
            Assert.False(InventoryManufacturingOutputReversalCodesV1.IsDefined(400, code), code);
            // The V1 closed set does not grow: an older caller can never observe a reversal-only code.
            Assert.False(InventoryManufacturingRealizationCodesV1.IsDefined(409, code), code);
            Assert.Equal((409, code), Codes(InventoryManufacturingRealizationException.RefusedOutputReversal(409, code)));
        }

        Assert.True(InventoryManufacturingOutputReversalCodesV1.IsDefined(503,
            InventoryManufacturingRealizationCodesV1.Unavailable));
        Assert.Throws<InvalidOperationException>(() =>
            InventoryManufacturingRealizationException.RefusedOutputReversal(409, "inventory_unknown_code"));
        Assert.Throws<InvalidOperationException>(() => InventoryManufacturingRealizationException.Refused(409,
            InventoryManufacturingOutputReversalCodesV1.Shipped));
    }

    [Fact]
    public void A_reversal_movement_is_one_free_out_line()
    {
        var movement = Movement(StockMovementKind.ManufacturingOutputReversal, Out(Position(), 5m));

        Assert.Equal(StockMovementKind.ManufacturingOutputReversal, movement.Kind);
        Assert.Equal(StockDirection.Out, Assert.Single(movement.Lines).Direction);
    }

    [Theory]
    [InlineData("in-line")]
    [InlineData("two-out-lines")]
    [InlineData("out-and-in")]
    [InlineData("blocked")]
    public void An_inconsistent_reversal_movement_is_refused(string fault)
    {
        var free = Position();
        Action build = fault switch
        {
            "in-line" => () => Movement(StockMovementKind.ManufacturingOutputReversal, In(free, 5m)),
            "two-out-lines" => () => Movement(StockMovementKind.ManufacturingOutputReversal, Out(free, 2m),
                Out(Position(location: Warehouse2), 3m)),
            "out-and-in" => () => Movement(StockMovementKind.ManufacturingOutputReversal, Out(free, 5m),
                In(Position(location: Warehouse2), 5m)),
            _ => () => Movement(StockMovementKind.ManufacturingOutputReversal, Out(free.WithBlock(BlockState.Blocked), 5m)),
        };

        Assert.Equal(InventoryRuleCodes.InvalidMovement, Assert.Throws<InventoryRuleException>(build).Code);
    }

    [Fact]
    public void Order_cancellation_is_its_own_release_reason_code()
    {
        Assert.Equal(3, (int)InventoryManufacturingPlanReleaseReasonV1.OrderCancelled);
        Assert.Equal("manufacturing.order-cancelled", InventoryManufacturingPlanAllocationContractV1.ReleaseReasonCode(
            InventoryManufacturingPlanReleaseReasonV1.OrderCancelled));
        Assert.NotEqual(
            InventoryManufacturingPlanAllocationContractV1.ReleaseReasonCode(InventoryManufacturingPlanReleaseReasonV1.OrderClosed),
            InventoryManufacturingPlanAllocationContractV1.ReleaseReasonCode(InventoryManufacturingPlanReleaseReasonV1.OrderCancelled));
        InventoryManufacturingPlanAllocationContractV1.Validate(new InventoryManufacturingPlanReleaseRequestV1(
            Company, Id(3), "4", InventoryManufacturingPlanReleaseReasonV1.OrderCancelled, OccurredAt), Metadata(), Company);
    }

    private static (int, string) Codes(InventoryManufacturingRealizationException error) => (error.Status, error.Code);

    private static InventoryManufacturingOutputReversalRequestV1 Request() => new(
        Company, Id(10), Id(11), Id(12), "6", Id(13), "3", Id(14), "12.125", OccurredAt);

    private static InventoryCommandMetadata Metadata() => new(
        "manufacturing-user", "manufacturing.outputs.correct", ["MANUFACTURING-USER"],
        "corr-reversal-1", "cause-reversal-1", "reversal-key-1", "api");

    private static Guid Id(int value) => Guid.Parse($"92000000-0000-4000-8000-{value:D12}");
}
