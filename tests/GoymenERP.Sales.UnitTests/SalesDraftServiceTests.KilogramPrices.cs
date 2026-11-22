using GoymenERP.Modules.Sales.Application;
using GoymenERP.Modules.Sales.Application.Contracts;

namespace GoymenERP.Sales.UnitTests;

/// <summary>
/// SAT-D436 / SAT-D439 (AK-5): the order's kg price read is CLOSED until its golden is in main (FormulaGoldenPending, nothing else);
/// it needs the price read; the computed path is pinned to golden K01–K03 over a real piece order and its invoices.
/// </summary>
public sealed partial class SalesDraftServiceTests
{
    [Fact]
    public async Task SAT_D436_the_kg_price_read_is_closed_and_answers_formula_golden_pending_only()
    {
        Assert.False(SalesDraftService.KilogramPriceGoldenAccepted);
        var invoicing = await PieceInvoiceWorldAsync(40m, 495m, 500.16m);

        var read = await invoicing.World.Service().GetDraftKilogramPricesAsync(invoicing.Order.Draft.Id);

        Assert.Equal(new SalesOrderKilogramPricesResponse(SalesKilogramPriceStatesV1.FormulaGoldenPending, Currency), read);
    }

    [Fact]
    public async Task SAT_D436_the_kg_price_read_needs_the_price_read()
    {
        var invoicing = await PieceInvoiceWorldAsync(40m, 495m, 500.16m);
        invoicing.World.Authority.Denied.Add(PriceReadPermission);

        var refused = await Assert.ThrowsAsync<SalesDraftServiceException>(() =>
            invoicing.World.Service().GetDraftKilogramPricesAsync(invoicing.Order.Draft.Id));

        Assert.Equal(403, refused.Status);
    }

    [Fact]
    public async Task SAT_D436_golden_K01_to_K03_the_computed_order_read_and_realization()
    {
        // K02: 40 adet invoiced at 10 000,00 with 500,160 scale kg; K03: the other 60 adet at 15 000,00 with 748,900 kg.
        var invoicing = await PieceInvoiceWorldAsync(40m, 495m, 500.16m);
        var first = (await invoicing.Service().CreateInvoiceAsync(invoicing.Request(), Metadata("k-kgp-1"))).Invoice;
        var draft = Assert.Single(invoicing.World.Store.Drafts);
        var stored = invoicing.Invoices.Stored.Values.ToList();

        var afterFirst = SalesDraftService.KilogramPrices(draft, stored);
        var line = Assert.Single(afterFirst.Lines!);
        Assert.Equal(("1234.5", "Shown", "20.25"), (line.TheoreticalKg, line.TheoreticalPrice.State, line.TheoreticalPrice.Value)); // K01
        Assert.Equal(("40", "10000.00", "500.16", "19.99"), (line.InvoicedQuantity, line.InvoicedAmount, line.ActualKg, line.ActualPrice.Value)); // K02
        Assert.Equal(("6.36", "1.29"), (line.KgDifference, line.DeviationPercent)); // +6,360 kg, +1,28797…%
        Assert.Equal("20.25", afterFirst.DocumentTheoreticalPrice!.Value);
        Assert.Equal("19.99", afterFirst.DocumentActualPrice!.Value);
        Assert.NotNull(first);

        // K03: the same order's other 60 adet on a second note and invoice.
        var shipment = Guid.NewGuid();
        invoicing.Shipments.Answer = _ => new(SalesDispatchReadOutcomeV1.Found, Company,
        [
            new SalesInvoiceableShipmentV1(Guid.NewGuid(), "IRS-2026-000002", null, Approved, Party, Account, 1, shipment, Shipped,
                draft.Id, draft.Lines[0].Id, Product, PieceUnit, 60m, 740m),
        ], false, SalesInvoicedStateSourceV1.NoSource);
        invoicing.Weighings.Answer = note =>
        {
            var share = invoicing.Shipments.LastAnswer!.Shares.Single();
            var recorded = WeighingRecorded(note, theoretical: 740m, scale: 748.9m);
            return recorded with { Value = recorded.Value! with { KilogramAllocation = new(
                [new SalesDispatchKilogramLineV1(share.NoteLineNo, share.ShipmentId, share.ProductId, 740m, 748.9m)]) } };
        };
        var secondInvoice = (await invoicing.Service().CreateInvoiceAsync(new CreateSalesInvoiceRequest(Account,
            [new SalesInvoiceShareRequest(shipment)]), Metadata("k-kgp-2"))).Invoice;
        Assert.Equal(("60", "748.9"), (Assert.Single(secondInvoice.Lines).Quantity, secondInvoice.Lines[0].ActualKg));

        var full = SalesDraftService.KilogramPrices(draft, [.. invoicing.Invoices.Stored.Values]);
        var total = Assert.Single(full.Lines!);
        // K03: Σ 25 000,00 / (500,160 + 748,900) = 20,02 — the average of the invoices' figures (20,01) is never shown.
        Assert.Equal(("100", "25000.00", "1249.06", "20.02"), (total.InvoicedQuantity, total.InvoicedAmount, total.ActualKg, total.ActualPrice.Value));
        Assert.Equal(("14.56", "1.18"), (total.KgDifference, total.DeviationPercent));
        Assert.Equal("20.02", full.DocumentActualPrice!.Value);
    }

    [Fact]
    public async Task SAT_D436_an_invoice_with_a_piece_line_carries_a_pending_kg_price_block_and_a_kg_invoice_none()
    {
        var piece = await PieceInvoiceWorldAsync(40m, 495m, 500.16m);
        var kg = await InvoiceWorld.CreateAsync();

        var pieceInvoice = (await piece.Service().CreateInvoiceAsync(piece.Request(), Metadata("k-kgp-piece"))).Invoice;
        var kgInvoice = (await kg.Service().CreateInvoiceAsync(kg.Request(), Metadata("k-kgp-kg"))).Invoice;

        Assert.Equal(new SalesInvoiceKilogramPricesV1(SalesKilogramPriceStatesV1.FormulaGoldenPending), pieceInvoice.KilogramPrices);
        Assert.Null(kgInvoice.KilogramPrices);
    }
}
