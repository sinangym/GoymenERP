using GoymenERP.Modules.Accounting.Application;

namespace GoymenERP.Accounting.UnitTests;

public sealed class ManufacturingAttributionShipmentEvidenceTests
{
    [Fact]
    public void A_return_preserves_the_original_gross_quantity_and_proves_only_its_exact_net_root_share()
    {
        var shipment = Shipment();
        var returned = Returned(shipment, 2m);
        Assert.True(ManufacturingAttributionShipmentEvidenceV1.IsValid(returned));
        Assert.Equal(3m, returned.PhysicalQuantityKg);
        Assert.Equal(new AccountingInventoryCostRatioV1("1", "3"), returned.NetRootQuantityKg);
        Assert.False(ManufacturingAttributionShipmentEvidenceV1.IsValid(returned with { NetRootQuantityKg = new("1", "1") }));
    }

    [Fact]
    public void A_full_return_is_proved_zero_not_missing_cost_or_negative_stock()
    {
        var full = Returned(Shipment(), 3m);
        Assert.True(ManufacturingAttributionShipmentEvidenceV1.IsValid(full));
        Assert.Equal(new AccountingInventoryCostRatioV1("0", "1"), full.NetRootQuantityKg);
        Assert.False(ManufacturingAttributionShipmentEvidenceV1.IsValid(Returned(Shipment(), 4m)));
    }

    [Fact]
    public void An_unproved_legacy_return_or_other_issues_return_cannot_reduce_a_share()
    {
        var value = Returned(Shipment(), 2m);
        Assert.False(ManufacturingAttributionShipmentEvidenceV1.IsValid(value with { Returns = null }));
        Assert.False(ManufacturingAttributionShipmentEvidenceV1.IsValid(value with { HasReturns = false }));
        Assert.False(ManufacturingAttributionShipmentEvidenceV1.IsValid(value with
        { Returns = [value.Returns![0] with { SourceIssueMovementId = Guid.NewGuid() }] }));
        Assert.False(ManufacturingAttributionShipmentEvidenceV1.IsValid(value with { Returns = [value.Returns![0], value.Returns[0]] }));
        Assert.True(ManufacturingAttributionShipmentEvidenceV1.IsValid(Shipment()));
    }

    [Fact]
    public void Exact_large_return_sums_do_not_hide_an_excess_in_decimal_rounding()
    {
        var shipment = Shipment() with { PhysicalQuantityKg = 1000000000000000000000000000m,
            RootShare = new("1", "1"), NetRootQuantityKg = new("0", "1"), HasReturns = true };
        ManufacturingAttributionReturnV1 Return(decimal quantity) => new(Guid.NewGuid(), Guid.NewGuid(), 1,
            shipment.MovementId, quantity, shipment.RecordedAtUtc.AddSeconds(1));
        // decimal.Sum would erase the last cent; exact mass conservation must reject it.
        shipment = shipment with { Returns = [Return(shipment.PhysicalQuantityKg), Return(.01m)] };
        Assert.False(ManufacturingAttributionShipmentEvidenceV1.IsValid(shipment));
    }

    private static ManufacturingAttributionShipmentV1 Shipment() => new(Guid.NewGuid(), Guid.NewGuid(), 1, Guid.NewGuid(),
        Guid.NewGuid(), Guid.NewGuid(), 3m, new("1", "3"), new("1", "1"),
        new DateTimeOffset(2026, 11, 17, 10, 0, 0, TimeSpan.Zero), null, false, "{\"synthetic\":true}");

    private static ManufacturingAttributionShipmentV1 Returned(ManufacturingAttributionShipmentV1 source, decimal quantity) => source with
    {
        HasReturns = true,
        Returns = [new(Guid.NewGuid(), Guid.NewGuid(), 1, source.MovementId, quantity, source.RecordedAtUtc.AddSeconds(1))],
        NetRootQuantityKg = AccountingInventoryCostAmountsV1.Multiply(AccountingInventoryCostAmountsV1.FromDecimal(source.PhysicalQuantityKg - quantity), source.RootShare),
    };
}
