using GoymenERP.Modules.Sales.Domain.RiskExceptions;

namespace GoymenERP.Sales.UnitTests;

/// <summary>
/// SAT-D285 (2), LOJ-D19: when the shipment that used an approved single-shipment exception is cancelled, the exception drops;
/// it is valid on no day afterwards (the Logistics gate then reads it as lapsed) and a new request and approval are needed. Only
/// an approved exception drops, and only by the one shipment that used it. Every identity is synthetic.
/// </summary>
public sealed class SalesShipmentRiskExceptionDropTests
{
    private static readonly Guid Company = Guid.Parse("11111111-1111-4111-8111-111111111111");
    private static readonly Guid Order = Guid.Parse("22222222-2222-4222-8222-222222222222");
    private static readonly Guid Instruction = Guid.Parse("e1e1e1e1-e1e1-4e1e-8e1e-e1e1e1e1e1e1");
    private static readonly Guid Shipment = Guid.Parse("a5a5a5a5-a5a5-4a5a-8a5a-a5a5a5a5a5a5");
    private static readonly Guid OtherShipment = Guid.Parse("b6b6b6b6-b6b6-4b6b-8b6b-b6b6b6b6b6b6");
    private static readonly DateTimeOffset T0 = new(2026, 11, 15, 7, 0, 0, TimeSpan.Zero);
    private static readonly DateOnly Day = new(2026, 11, 15);

    private static SalesShipmentRiskException Requested() =>
        SalesShipmentRiskException.Request(Guid.NewGuid(), Company, Order, 1, Instruction, "SENTETIK ödeme sözü", "synthetic-shipper", T0);

    private static SalesShipmentRiskException Approved()
    {
        var item = Requested();
        item.Approve(1, "synthetic-manager", T0, Day);
        return item;
    }

    [Fact]
    public void A_dropped_approval_is_valid_on_no_day_and_keeps_its_state_and_version()
    {
        var item = Approved();
        Assert.True(item.IsValidOn(Day));

        item.ApplyShipmentDropped(Shipment);

        Assert.False(item.IsValidOn(Day));
        Assert.Equal((SalesShipmentRiskExceptionState.Approved, 2L, (Guid?)Shipment),
            (item.State, item.Version, item.DroppedByShipmentId));
    }

    [Fact]
    public void The_same_drop_applied_twice_is_idempotent_but_another_shipment_is_refused()
    {
        var item = Approved();
        item.ApplyShipmentDropped(Shipment);

        item.ApplyShipmentDropped(Shipment);
        Assert.Throws<InvalidOperationException>(() => item.ApplyShipmentDropped(OtherShipment));

        Assert.Equal(Shipment, item.DroppedByShipmentId);
    }

    [Fact]
    public void Only_an_approved_exception_drops_and_an_empty_shipment_is_refused()
    {
        var requested = Requested();
        var rejected = Requested();
        rejected.Reject(1, "synthetic-manager", "SENTETIK ret", T0);

        Assert.Throws<InvalidOperationException>(() => requested.ApplyShipmentDropped(Shipment));
        Assert.Throws<InvalidOperationException>(() => rejected.ApplyShipmentDropped(Shipment));
        Assert.Throws<ArgumentException>(() => Approved().ApplyShipmentDropped(Guid.Empty));
        Assert.Null(requested.DroppedByShipmentId);
    }

    [Fact]
    public void An_approval_that_did_not_drop_stays_valid_on_its_business_date_only()
    {
        var item = Approved();

        Assert.Null(item.DroppedByShipmentId);
        Assert.True(item.IsValidOn(Day));
        Assert.False(item.IsValidOn(Day.AddDays(1)));
    }
}
