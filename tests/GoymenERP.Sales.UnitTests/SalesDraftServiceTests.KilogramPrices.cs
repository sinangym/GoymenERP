using GoymenERP.Modules.Sales.Application;
using GoymenERP.Modules.Sales.Application.Contracts;
using GoymenERP.Modules.Sales.Domain.Invoices;

namespace GoymenERP.Sales.UnitTests;

/// <summary>
/// SAT-D436 / SAT-D439 (AK-5): the order's kg price read is OPEN (golden ADET_KG_FORMUL_GOLDEN_V1 §1 in this train); the closed
/// path is pinned through the named switch parameter (FormulaGoldenPending, nothing else). It needs the price read; the computed
/// path is pinned to golden K01–K03 over a real piece order and its invoices. Only finalised invoices enter the realization
/// (<see cref="SalesDraftService.EntersKilogramPriceRealization"/>, NEEDS-SINAN in #1189).
/// </summary>
public sealed partial class SalesDraftServiceTests
{
    [Fact]
    public async Task SAT_D436_closed_the_kg_price_read_answers_formula_golden_pending_only()
    {
        var invoicing = await PieceInvoiceWorldAsync(40m, 495m, 500.16m);

        var read = await invoicing.World.Service().GetDraftKilogramPricesAsync(invoicing.Order.Draft.Id, kilogramPriceOpen: false);

        Assert.Equal(new SalesOrderKilogramPricesResponse(SalesKilogramPriceStatesV1.FormulaGoldenPending, Currency), read);
    }

    [Fact]
    public async Task SAT_D436_open_the_kg_price_read_of_an_order_without_a_finalised_invoice_is_golden_K01()
    {
        Assert.True(SalesDraftService.KilogramPriceGoldenAccepted);
        var invoicing = await PieceInvoiceWorldAsync(40m, 495m, 500.16m);
        // A draft invoice exists, but it is not an invoiced amount yet: the realization stays empty (TK6).
        await invoicing.Service().CreateInvoiceAsync(invoicing.Request(), Metadata("k-kgp-open"));
        invoicing.World.InvoiceRecords = invoicing.Invoices;

        var read = await invoicing.World.Service().GetDraftKilogramPricesAsync(invoicing.Order.Draft.Id);

        Assert.Equal((SalesKilogramPriceStatesV1.Computed, Currency), (read.State, read.CurrencyId));
        var line = Assert.Single(read.Lines!);
        Assert.Equal(("1234.5", "Shown", "20.25"), (line.TheoreticalKg, line.TheoreticalPrice.State, line.TheoreticalPrice.Value)); // K01
        Assert.Equal(("0", null, null, "AmountUnavailable", null),
            (line.InvoicedQuantity, line.InvoicedAmount, line.ActualKg, line.ActualPrice.State, line.ActualPrice.Value));
        Assert.Equal(("Shown", "20.25"), (read.DocumentTheoreticalPrice!.State, read.DocumentTheoreticalPrice.Value));
        Assert.Equal(("WeighedKgUnavailable", null), (read.DocumentActualPrice!.State, read.DocumentActualPrice.Value));
    }

    [Theory]
    [InlineData(SalesInvoiceState.Draft, false)]
    [InlineData(SalesInvoiceState.PendingApproval, false)]
    [InlineData(SalesInvoiceState.Approved, false)]
    [InlineData(SalesInvoiceState.Rejected, false)]
    [InlineData(SalesInvoiceState.Cancelled, false)]
    [InlineData(SalesInvoiceState.Finalized, true)]
    [InlineData(SalesInvoiceState.OfficiallyCancelled, false)]
    public async Task SAT_D436_only_a_finalised_invoice_enters_the_order_realization(SalesInvoiceState state, bool enters)
    {
        // NEEDS-SINAN (#1189): "faturalanan" is read as finalised only. This theory pins the one rule point for every state.
        var invoicing = await PieceInvoiceWorldAsync(40m, 495m, 500.16m);
        await invoicing.Service().CreateInvoiceAsync(invoicing.Request(), Metadata("k-kgp-state"));
        var draft = Assert.Single(invoicing.World.Store.Drafts);
        var invoice = InState(Assert.Single(invoicing.Invoices.Stored.Values), state);

        Assert.Equal(enters, SalesDraftService.EntersKilogramPriceRealization(invoice));
        var line = Assert.Single(SalesDraftService.KilogramPrices(draft, [invoice]).Lines!);
        Assert.Equal(enters ? ("40", "10000.00", "19.99") : ("0", null, null),
            (line.InvoicedQuantity, line.InvoicedAmount, line.ActualPrice.Value));
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
        var stored = invoicing.Invoices.Stored.Values.Select(Finalized).ToList();

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

        var full = SalesDraftService.KilogramPrices(draft, [.. invoicing.Invoices.Stored.Values.Select(Finalized)]);
        var total = Assert.Single(full.Lines!);
        // K03: Σ 25 000,00 / (500,160 + 748,900) = 20,02 — the average of the invoices' figures (20,01) is never shown.
        Assert.Equal(("100", "25000.00", "1249.06", "20.02"), (total.InvoicedQuantity, total.InvoicedAmount, total.ActualKg, total.ActualPrice.Value));
        Assert.Equal(("14.56", "1.18"), (total.KgDifference, total.DeviationPercent));
        Assert.Equal("20.02", full.DocumentActualPrice!.Value);
    }

    [Fact]
    public async Task SAT_D436_an_invoice_with_a_piece_line_carries_its_golden_K02_kg_prices_and_a_kg_invoice_none()
    {
        var piece = await PieceInvoiceWorldAsync(40m, 495m, 500.16m);
        var kg = await InvoiceWorld.CreateAsync();

        var pieceInvoice = (await piece.Service().CreateInvoiceAsync(piece.Request(), Metadata("k-kgp-piece"))).Invoice;
        var kgInvoice = (await kg.Service().CreateInvoiceAsync(kg.Request(), Metadata("k-kgp-kg"))).Invoice;

        // K02: the invoice line 40 adet × 250,00 = 10 000,00 over 493,800 theoretical and 500,160 scale kg. The document's own
        // figure is shown on the document whatever its state (SAT-D434: information on the invoice).
        var block = pieceInvoice.KilogramPrices!;
        Assert.Equal(SalesKilogramPriceStatesV1.Computed, block.State);
        var line = Assert.Single(block.Lines!);
        Assert.Equal(("Shown", "20.25", "Shown", "19.99"),
            (line.TheoreticalPrice.State, line.TheoreticalPrice.Value, line.ActualPrice.State, line.ActualPrice.Value));
        Assert.Equal(("20.25", "19.99"), (block.DocumentTheoreticalPrice!.Value, block.DocumentActualPrice!.Value));
        Assert.Null(kgInvoice.KilogramPrices);
    }

    [Fact]
    public async Task SAT_D436_closed_an_invoice_with_a_piece_line_carries_a_pending_kg_price_block_and_a_kg_invoice_none()
    {
        var piece = await PieceInvoiceWorldAsync(40m, 495m, 500.16m);
        var kg = await InvoiceWorld.CreateAsync();
        await piece.Service().CreateInvoiceAsync(piece.Request(), Metadata("k-kgp-piece-closed"));
        await kg.Service().CreateInvoiceAsync(kg.Request(), Metadata("k-kgp-kg-closed"));

        Assert.Equal(new SalesInvoiceKilogramPricesV1(SalesKilogramPriceStatesV1.FormulaGoldenPending),
            SalesInvoiceService.KilogramPrices(Assert.Single(piece.Invoices.Stored.Values), kilogramPriceOpen: false));
        Assert.Null(SalesInvoiceService.KilogramPrices(Assert.Single(kg.Invoices.Stored.Values), kilogramPriceOpen: false));
    }

    private static SalesInvoice Finalized(SalesInvoice invoice) => InState(invoice, SalesInvoiceState.Finalized);

    /// <summary>The same stored invoice facts restored in another lifecycle state (with the decision facts that state needs).</summary>
    private static SalesInvoice InState(SalesInvoice invoice, SalesInvoiceState state)
    {
        const string checker = "synthetic-invoice-checker";
        var at = invoice.CreatedAtUtc.AddMinutes(5);
        var facts = state switch
        {
            SalesInvoiceState.Draft => SalesInvoiceDecisionFacts.None,
            SalesInvoiceState.PendingApproval => new SalesInvoiceDecisionFacts(invoice.CreatedBy, at, null, null, null, null, null, null),
            SalesInvoiceState.Cancelled => new SalesInvoiceDecisionFacts(null, null, null, null, null, invoice.CreatedBy, at, null),
            SalesInvoiceState.OfficiallyCancelled => new SalesInvoiceDecisionFacts(invoice.CreatedBy, at, checker, at, null,
                checker, at, "SENTETIK resmi iptal"),
            _ => new SalesInvoiceDecisionFacts(invoice.CreatedBy, at, checker, at, null, null, null, null),
        };
        return SalesInvoice.Restore(invoice.Id, invoice.CompanyId, invoice.DocumentNo, invoice.Header, state, invoice.Version,
            invoice.CreatedBy, invoice.CreatedAtUtc, invoice.Lines, invoice.Instalments, facts);
    }
}
