using GoymenERP.Modules.Inventory.Application;
using GoymenERP.Modules.Inventory.Domain;
using GoymenERP.Modules.Inventory.Domain.Balances;
using GoymenERP.Modules.Inventory.Domain.Movements;
using GoymenERP.Modules.Inventory.Domain.Quantities;
using GoymenERP.Modules.Inventory.Infrastructure.Persistence;
using static GoymenERP.Inventory.UnitTests.InventoryFixture;

namespace GoymenERP.Inventory.UnitTests;

/// <summary>
/// SAT-D318 (owner decision of 2026-11-13): an input issue refused for physical stock says which issue it was and
/// where. The pre-check refusals are proven against SQL in the participant's relational acceptance facts; the ledger
/// fallback cannot be reached through SQL behind that pre-check, so its mapping is proven here.
/// </summary>
public sealed class InventoryManufacturingStockShortfallTests
{
    private const string IssueSource = InventoryManufacturingRealizationContractV1.InputIssueSourceKind;
    private static readonly Guid Order = Guid.Parse("93000000-0000-4000-8000-000000000001");
    private static readonly Guid IssueLine = Guid.Parse("93000000-0000-4000-8000-000000000002");

    [Fact]
    public void The_shortfall_rides_only_on_the_physical_stock_refusal_and_leaves_its_status_and_code_unchanged()
    {
        var shortfall = Shortfall(requested: 5m, freePhysical: 4m, reserved: null);

        var refusal = InventoryManufacturingRealizationException.Refused(409,
            InventoryManufacturingRealizationCodesV1.PhysicalStockInsufficient, shortfall);

        Assert.Equal(409, refusal.Status);
        Assert.Equal(InventoryManufacturingRealizationCodesV1.PhysicalStockInsufficient, refusal.Code);
        Assert.Same(shortfall, refusal.StockShortfall);
        Assert.Null(InventoryManufacturingRealizationException.Refused(409,
            InventoryManufacturingRealizationCodesV1.PhysicalStockInsufficient).StockShortfall);
        Assert.Throws<InvalidOperationException>(() => InventoryManufacturingRealizationException.Refused(409,
            InventoryManufacturingRealizationCodesV1.AllocationInsufficientFreeStock, shortfall));
        Assert.Throws<InvalidOperationException>(() => InventoryManufacturingRealizationException.Refused(503,
            InventoryManufacturingRealizationCodesV1.PhysicalStockInsufficient, shortfall));
        Assert.Throws<ArgumentNullException>(() => InventoryManufacturingRealizationException.Refused(409,
            InventoryManufacturingRealizationCodesV1.PhysicalStockInsufficient, null!));
    }

    [Fact]
    public void A_shortfall_names_only_real_identities_and_a_request_its_usable_kg_cannot_cover()
    {
        // 10 kg on the bucket and 4 kg held by other demands leave 6 kg usable.
        Assert.Equal(6.001m, Shortfall(requested: 6.001m, freePhysical: 10m, reserved: 4m).RequestedKg.Value);
        Assert.Throws<ArgumentOutOfRangeException>(() => Shortfall(requested: 6m, freePhysical: 10m, reserved: 4m));
        Assert.Throws<ArgumentOutOfRangeException>(() => Shortfall(requested: 5m, freePhysical: 5m, reserved: null));
        // Reservations above the physical kg leave nothing usable, never a negative figure.
        Assert.Equal(7m, Shortfall(requested: 1m, freePhysical: 3m, reserved: 7m).ReservedKg!.Value.Value);
        Assert.Throws<ArgumentOutOfRangeException>(() => Shortfall(requested: 1m, freePhysical: 8m, reserved: 7m));

        Assert.Throws<ArgumentOutOfRangeException>(() => new InventoryManufacturingStockShortfallV1(
            StockMovementKind.Issue, IssueSource, Order, IssueLine, Label1, BandProduct, Warehouse1, Kilograms.Zero,
            Kilograms.Zero, null));
        Assert.Throws<ArgumentOutOfRangeException>(() => new InventoryManufacturingStockShortfallV1(
            (StockMovementKind)99, IssueSource, Order, IssueLine, Label1, BandProduct, Warehouse1, One, Kilograms.Zero,
            null));
        foreach (var sourceKind in new[] { "", " ", " " + IssueSource, new string('k', 65) })
        {
            Assert.Throws<ArgumentException>(() => new InventoryManufacturingStockShortfallV1(StockMovementKind.Issue,
                sourceKind, Order, IssueLine, Label1, BandProduct, Warehouse1, One, Kilograms.Zero, null));
        }

        Assert.Throws<ArgumentException>(() => new InventoryManufacturingStockShortfallV1(StockMovementKind.Issue,
            IssueSource, Guid.Empty, IssueLine, Label1, BandProduct, Warehouse1, One, Kilograms.Zero, null));
        Assert.Throws<ArgumentException>(() => new InventoryManufacturingStockShortfallV1(StockMovementKind.Issue,
            IssueSource, Order, Guid.Empty, Label1, BandProduct, Warehouse1, One, Kilograms.Zero, null));
        Assert.Throws<ArgumentException>(() => new InventoryManufacturingStockShortfallV1(StockMovementKind.Issue,
            IssueSource, Order, IssueLine, Guid.Empty, BandProduct, Warehouse1, One, Kilograms.Zero, null));
        Assert.Throws<ArgumentException>(() => new InventoryManufacturingStockShortfallV1(StockMovementKind.Issue,
            IssueSource, Order, IssueLine, Label1, Guid.Empty, Warehouse1, One, Kilograms.Zero, null));
        Assert.Throws<ArgumentException>(() => new InventoryManufacturingStockShortfallV1(StockMovementKind.Issue,
            IssueSource, Order, IssueLine, Label1, BandProduct, Guid.Empty, One, Kilograms.Zero, null));

        // A label that holds no stock anywhere has no place to name; a source without a line is still a source.
        var nowhere = new InventoryManufacturingStockShortfallV1(StockMovementKind.Issue, IssueSource, Order, null,
            Label1, BandProduct, null, One, Kilograms.Zero, null);
        Assert.Null(nowhere.LocationId);
        Assert.Null(nowhere.SourceLineId);
    }

    [Fact]
    public void The_available_kg_are_the_usable_kg_free_physical_less_other_demands_reservations_and_never_negative()
    {
        // SINAN-CEVAP (8) question 5 discloses this figure with the requested kg: what the line could have taken.
        var reservedPart = Shortfall(requested: 6.001m, freePhysical: 10m, reserved: 4m);
        Assert.Equal((6m, 10m, 4m), (reservedPart.AvailableKg.Value, reservedPart.FreePhysicalKg.Value,
            reservedPart.ReservedKg!.Value.Value));

        var overReserved = Shortfall(requested: 1m, freePhysical: 3m, reserved: 7m);
        Assert.Equal((0m, 3m), (overReserved.AvailableKg.Value, overReserved.FreePhysicalKg.Value));

        var noneCounted = Shortfall(requested: 5m, freePhysical: 4m, reserved: null);
        Assert.Equal((4m, 4m), (noneCounted.AvailableKg.Value, noneCounted.FreePhysicalKg.Value));

        var noneFound = Shortfall(requested: 5m, freePhysical: 4m, reserved: 0m);
        Assert.Equal(4m, noneFound.AvailableKg.Value);

        var empty = Shortfall(requested: 5m, freePhysical: 0m, reserved: null);
        Assert.Equal(0m, empty.AvailableKg.Value);
    }

    [Fact]
    public void The_ledger_fallback_carries_the_ledger_shortfall_of_the_input_issue_without_a_reservation()
    {
        var refusal = InventoryManufacturingRealizationParticipant.NegativeStockRefusal(LedgerShortfall(),
            InventoryManufacturingRealizationCodesV1.PhysicalStockInsufficient);

        Assert.Equal(409, refusal.Status);
        Assert.Equal(InventoryManufacturingRealizationCodesV1.PhysicalStockInsufficient, refusal.Code);
        var detail = Assert.IsType<InventoryManufacturingStockShortfallV1>(refusal.StockShortfall);
        Assert.Equal(StockMovementKind.Issue, detail.MovementKind);
        Assert.Equal(IssueSource, detail.SourceKind);
        Assert.Equal(Order, detail.SourceId);
        Assert.Equal(IssueLine, detail.SourceLineId);
        Assert.Equal(Label1, detail.LabelId);
        Assert.Equal(BandProduct, detail.ProductId);
        Assert.Equal(Warehouse1, detail.LocationId);
        Assert.Equal(100.5m, detail.RequestedKg.Value);
        Assert.Equal(100m, detail.AvailableKg.Value);
        Assert.Equal(100m, detail.FreePhysicalKg.Value);
        Assert.Null(detail.ReservedKg);
    }

    [Fact]
    public void Without_a_ledger_shortfall_or_for_another_refusal_the_ledger_fallback_stays_plain()
    {
        var withoutDetail = InventoryManufacturingRealizationParticipant.NegativeStockRefusal(null,
            InventoryManufacturingRealizationCodesV1.PhysicalStockInsufficient);
        var anotherRefusal = InventoryManufacturingRealizationParticipant.NegativeStockRefusal(LedgerShortfall(),
            InventoryManufacturingRealizationCodesV1.ReturnExceedsIssued);

        Assert.Equal(InventoryManufacturingRealizationCodesV1.PhysicalStockInsufficient, withoutDetail.Code);
        Assert.Null(withoutDetail.StockShortfall);
        Assert.Equal(409, anotherRefusal.Status);
        Assert.Equal(InventoryManufacturingRealizationCodesV1.ReturnExceedsIssued, anotherRefusal.Code);
        Assert.Null(anotherRefusal.StockShortfall);
    }

    private static Kilograms One => Kilograms.Positive(1m);

    private static InventoryManufacturingStockShortfallV1 Shortfall(decimal requested, decimal freePhysical,
        decimal? reserved) => new(StockMovementKind.Issue, IssueSource, Order, IssueLine, Label1, BandProduct,
        Warehouse1, Kilograms.Positive(requested), Kilograms.Of(freePhysical),
        reserved is { } held ? Kilograms.Of(held) : null);

    /// <summary>The ledger's own refusal of an input issue asking 100.5 kg of a 100 kg bucket.</summary>
    private static StockShortfall LedgerShortfall()
    {
        var ledger = new StockLedger();
        ledger.Apply(Receipt(Position(), 100m));
        var issue = new StockMovement(Guid.NewGuid(), CompanyA, StockMovementKind.Issue, At,
            new StockSourceReference(IssueSource, Order, IssueLine), [Out(Position(), 100.5m)]);
        return Assert.IsType<StockShortfall>(Assert.Throws<InventoryRuleException>(() => ledger.Apply(issue)).Shortfall);
    }
}
