using GoymenERP.Modules.Inventory.Application;
using GoymenERP.Modules.Inventory.Domain.Balances;
using GoymenERP.Modules.Inventory.Domain.Labels;
using GoymenERP.Modules.Inventory.Domain.Lots;
using GoymenERP.Modules.Inventory.Domain.Quantities;
using GoymenERP.Modules.Inventory.Domain.Stock;
using static GoymenERP.Inventory.UnitTests.InventoryFixture;

namespace GoymenERP.Inventory.UnitTests;

/// <summary>T07(a) read shapes: exact kilograms as text, deterministic order, no actor identities. Synthetic data.</summary>
public sealed class InventoryReadModelsTests
{
    [Fact]
    public void A_label_view_carries_the_piece_its_document_status_and_history_without_actor_identities()
    {
        var label = new MaterialLabel(Label1, CompanyA, CodeOf(Label1), BandProduct, Lot1,
            requiredDocuments: ["mtc", "coa"], heatNumber: "SYN-HEAT", coilNumber: "SYN-COIL");
        label.Print(At, "synthetic-printer");
        label.Reprint(At.AddHours(1), "synthetic-printer", "SENTETIK yıpranma");
        label.AttachDocument("mtc", At.AddHours(2), "synthetic-clerk");

        var view = InventoryReadModelsV1.LabelView(label, new MaterialLot(Lot1, CompanyA, BandProduct, heatNumber: "SYN-LOT-HEAT"));

        Assert.Equal((Label1, CompanyA, CodeOf(Label1), BandProduct, Lot1), (view.LabelId, view.CompanyId, view.Code, view.ProductId, view.LotId));
        Assert.Equal(("SYN-HEAT", "SYN-COIL"), (view.HeatNumber, view.CoilNumber));
        Assert.Equal(LabelDocumentStatus.PendingDocuments.ToString(), view.DocumentStatus);
        Assert.Equal(["coa", "mtc"], view.RequiredDocuments);
        Assert.Equal([new InventoryLabelDocumentV1("mtc", At.AddHours(2))], view.Documents);
        Assert.Equal([new InventoryLabelPrintV1(1, At, null), new InventoryLabelPrintV1(2, At.AddHours(1), "SENTETIK yıpranma")], view.Prints);
        // The lot's own values, never the label's (Business #66 5734060341, Q1): its unknown coil stays null.
        Assert.Equal(new InventoryLotV1(Lot1, "SYN-LOT-HEAT", null), view.Lot);
        // Who printed or attached is audit's, behind audit's own permission.
        var text = System.Text.Json.JsonSerializer.Serialize(view);
        Assert.DoesNotContain("synthetic-printer", text, StringComparison.Ordinal);
        Assert.DoesNotContain("synthetic-clerk", text, StringComparison.Ordinal);
    }

    [Fact]
    public void A_label_view_needs_the_labels_own_lot()
    {
        var label = Label();

        Assert.Throws<ArgumentException>(() => InventoryReadModelsV1.LabelView(label,
            new MaterialLot(Guid.Parse("30000000-0000-0000-0000-000000000002"), CompanyA, BandProduct)));
        Assert.Throws<ArgumentException>(() => InventoryReadModelsV1.LabelView(label, new MaterialLot(Lot1, CompanyB, BandProduct)));
    }

    [Fact]
    public void Balances_are_exact_text_totals_by_block_and_a_deterministic_order()
    {
        var free = Position();
        var blocked = Position(block: BlockState.Blocked);
        var balances = new[]
        {
            new StockBalance(blocked, Kilograms.Of(100.500m)),
            new StockBalance(free, Kilograms.Of(899.625m)),
        };

        var view = InventoryReadModelsV1.Balances(Label1, balances);

        Assert.Equal(("1000.125", "899.625", "100.5"), (view.PhysicalKg, view.FreeKg, view.BlockedKg));
        Assert.Equal(["Free", "Blocked"], view.Positions.Select(position => position.Block));
        var first = view.Positions[0];
        Assert.Equal(("Company", CompanyA, "Company", CompanyA, Warehouse1, BandProduct, "899.625"),
            (first.OwnerKind, first.OwnerId, first.CustodianKind, first.CustodianId, first.LocationId, first.ProductId, first.QuantityKg));
        // The same input in another order answers the same.
        Assert.Equal(view.Positions, InventoryReadModelsV1.Balances(Label1, [.. balances.Reverse()]).Positions);
    }

    [Fact]
    public void A_label_without_stock_has_no_positions_and_zero_totals_and_another_labels_stock_is_refused()
    {
        var empty = InventoryReadModelsV1.Balances(Label1, []);
        Assert.Empty(empty.Positions);
        Assert.Equal(("0", "0", "0"), (empty.PhysicalKg, empty.FreeKg, empty.BlockedKg));

        Assert.Throws<ArgumentException>(() => InventoryReadModelsV1.Balances(Label2,
            [new StockBalance(Position(), Kilograms.Of(1m))]));
    }
}
