using GoymenERP.Modules.Logistics.Domain.Quantities;
using GoymenERP.Modules.Logistics.Domain.Returns;

namespace GoymenERP.Logistics.UnitTests;

public sealed class CustomerReturnTests
{
    private static readonly Guid Company = Guid.Parse("11111111-1111-4111-8111-111111111111");
    private static readonly Guid Unit = Guid.Parse("22222222-2222-4222-8222-222222222222");
    private static readonly DateTimeOffset At = new(2026, 10, 3, 12, 0, 0, TimeSpan.FromHours(3));

    [Fact]
    public void Request_preserves_the_exact_delivery_chain_and_has_no_inventory_effect()
    {
        var item = Pending(Quantity.Positive(250m, QuantityUnit.Kilogram), Quantity.Positive(400m, QuantityUnit.Kilogram));

        Assert.Equal((Company, CustomerReturnStatus.Pending, 1L, "alice"),
            (item.CompanyId, item.Status, item.Version, item.RequestedBy));
        Assert.Null(item.InventoryMovementId);
        Assert.Null(item.OriginalLabelId);
        Assert.Null(item.OriginalShipmentMovementId);
    }

    [Fact]
    public void Request_never_exceeds_its_source_delivery()
    {
        var error = Assert.Throws<CustomerReturnRuleException>(() =>
            Pending(Quantity.Positive(401m, QuantityUnit.Kilogram), Quantity.Positive(400m, QuantityUnit.Kilogram)));

        Assert.Equal(CustomerReturnRuleCodesV1.QuantityExceedsDelivery, error.Code);
    }

    [Fact]
    public void Preparer_cannot_decide_and_a_stale_decision_changes_nothing()
    {
        var item = Pending(Quantity.Positive(100m, QuantityUnit.Kilogram), Quantity.Positive(400m, QuantityUnit.Kilogram));
        var own = Assert.Throws<CustomerReturnRuleException>(() => item.RequireDecision(1, "alice"));
        Assert.Equal(CustomerReturnRuleCodesV1.OwnDecision, own.Code);
        Assert.Throws<CustomerReturnVersionConflictException>(() => item.RequireDecision(2, "bob"));
        Assert.Equal((CustomerReturnStatus.Pending, 1L), (item.Status, item.Version));
    }

    [Fact]
    public void Different_actor_approval_keeps_source_history_and_records_only_the_compensating_identity()
    {
        var item = Pending(Quantity.Positive(100m, QuantityUnit.Kilogram), Quantity.Positive(400m, QuantityUnit.Kilogram));
        var movement = Guid.NewGuid();
        var label = Guid.NewGuid();
        var sourceMovement = Guid.NewGuid();

        item.Approve(1, "bob", movement, label, sourceMovement, At.AddMinutes(5), "accepted");

        Assert.Equal((CustomerReturnStatus.Approved, 2L, "bob", movement, label, sourceMovement),
            (item.Status, item.Version, item.DecidedBy, item.InventoryMovementId, item.OriginalLabelId,
                item.OriginalShipmentMovementId));
        var terminal = Assert.Throws<CustomerReturnRuleException>(() =>
            item.Reject(2, "carol", At.AddMinutes(6), "late"));
        Assert.Equal(CustomerReturnRuleCodesV1.NotPending, terminal.Code);
    }

    private static CustomerReturn Pending(Quantity quantity, Quantity delivered) => CustomerReturn.Request(
        Guid.NewGuid(), Company, Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(),
        Guid.NewGuid(), Unit, quantity, delivered, "customer returned goods", "alice", At, At.AddMinutes(1));
}
