using GoymenERP.Modules.Product.Application.Contracts;
using GoymenERP.Modules.Settings.Application;
using GoymenERP.Modules.Sales.Application.Contracts;
using GoymenERP.Modules.Settings.Application.Contracts.V1;

namespace GoymenERP.Sales.UnitTests;

/// <summary>
/// SAT-D434 / SAT-D435 (AK-4): a piece line is invoiced as pieces × piece price; the theoretical kg (pieces × the kg/unit pinned at
/// approval) and the scale kg the weighing difference carried to the share are information only. Values from golden K02 / K04
/// (ADET_KG_FORMUL_GOLDEN_V1: 100 adet × 250,00 TL, k = 12,345 kg/adet). Every identity is SENTETIK.
/// </summary>
public sealed partial class SalesDraftServiceTests
{
    private static async Task<InvoiceWorld> PieceInvoiceWorldAsync(decimal pieces, decimal shippedKg, decimal scaleKg,
        bool pinFigure = true, string? surchargePercent = null)
    {
        var world = new World
        {
            // SAT-D473: without a figure the owner READ the card and says it gives none; the approval goes on unpinned.
            KilogramsPerUnit = pinFigure
                ? new FakeKilogramsPerUnit { Figure = 12.345m }
                : new FakeKilogramsPerUnit { Outcome = ProductTheoreticalKilogramsPerUnitOutcomeV1.NoSource,
                    Code = ProductTheoreticalKilogramsPerUnitCodesV1.NoSource },
            LineTaxes = new FakeLineTaxes
            {
                Defaults =
                [
                    new TaxItemTypeDefaultDataV1(Guid.NewGuid(), Company, TaxItemTypesV1.Steel, TaxDefaultSourcesV1.Record, 1, Day, true,
                        20m, null, false, null, null),
                ],
            },
        };
        world.Settings.PieceUnits.Add(PieceUnit);
        var priced = Priced(discount: null);
        var request = priced with
        {
            Lines = [priced.Lines[0] with { OrderUnitId = PieceUnit, Quantity = "100", RequestedDeliverySlices = null,
                UnitPrice = "250.00", PriceUnitId = PieceUnit }],
        };
        var created = (await world.Service().CreateDraftAsync(request, Metadata("k-piece-invoice-create"))).Detail;
        if (surchargePercent is not null)
        {
            created = (await world.Service().ReviseCommercialAsync(created.Draft.Id, Revision(created.Draft.Version,
                new SalesDraftLineCommercialV1(created.Lines[0].LineId, "250.00", PieceUnit, SurchargePercent: surchargePercent)),
                Metadata("k-piece-invoice-surcharge"))).Detail;
        }

        var version = await ApproveDiscountChecksAsync(world, created.Draft.Id, created.Draft.Version, created.Lines[0].LineId);
        var submitted = await world.Service().SubmitDraftAsync(created.Draft.Id, Command(version), Metadata("k-piece-invoice-submit"));
        var approved = (await world.Service().ApproveDraftAsync(created.Draft.Id, Command(submitted.Detail.Draft.Version),
            Metadata("k-piece-invoice-approve"))).Detail;
        await world.Service().RecordCustomerAcceptanceAsync(approved.Draft.Id, Command(approved.Draft.Version), Metadata("k-piece-invoice-accept"));
        Assert.Equal(pinFigure ? "12.345" : null, approved.Lines[0].TheoreticalKilogramsPerUnit);

        var invoicing = InvoiceWorld.Around(world, approved);
        invoicing.Shipments.Answer = _ => new(SalesDispatchReadOutcomeV1.Found, Company,
        [
            new SalesInvoiceableShipmentV1(Guid.NewGuid(), "IRS-2026-000001", null, Approved, Party, Account, 1, invoicing.ShipmentId, Shipped,
                approved.Draft.Id, approved.Lines[0].LineId, Product, PieceUnit, pieces, shippedKg),
        ], false, SalesInvoicedStateSourceV1.NoSource);
        invoicing.Weighings.Answer = note =>
        {
            var lines = invoicing.Shipments.LastAnswer!.Shares.Where(share => share.DispatchNoteId == note)
                .Select(share => new SalesDispatchKilogramLineV1(share.NoteLineNo, share.ShipmentId, share.ProductId, share.Kilograms!.Value,
                    scaleKg)).ToArray();
            var recorded = WeighingRecorded(note, theoretical: shippedKg, scale: scaleKg);
            return recorded with { Value = recorded.Value! with { KilogramAllocation = new(lines) } };
        };
        return invoicing;
    }

    [Fact]
    public async Task SAT_D434_a_piece_line_is_invoiced_in_pieces_with_theoretical_and_scale_kg_as_information()
    {
        // Golden K02: 40 adet faturalandı, teorik 40 × 12,345 = 493,800 kg, kantar 500,160 kg; tutar 40 × 250,00 = 10 000,00.
        var invoicing = await PieceInvoiceWorldAsync(40m, 495m, 500.16m);

        var invoice = (await invoicing.Service().CreateInvoiceAsync(invoicing.Request(), Metadata("k-piece-invoice"))).Invoice;

        var line = Assert.Single(invoice.Lines);
        Assert.Equal(("40", PieceUnit, "250"), (line.Quantity, line.UnitId, line.NetUnitPrice));
        Assert.Equal("10000.00", line.Amounts.NetAmount);
        Assert.Equal(("493.8", "500.16"), (line.TheoreticalKg, line.ActualKg));
        var stored = Assert.Single(Assert.Single(invoicing.Invoices.Stored).Value.Lines);
        Assert.Equal((40m, 493.8m, 500.16m), (stored.Share.Quantity, stored.Share.TheoreticalKilograms, stored.Share.ActualKilograms));
    }

    [Fact]
    public async Task SAT_D435_the_scale_kg_never_changes_the_piece_line_amount()
    {
        // Golden K04: 100 adet tek faturada, kantar 1 209,810 kg (−%2); tutar 25 000,00 sabit.
        var invoicing = await PieceInvoiceWorldAsync(100m, 1230m, 1209.81m);

        var line = Assert.Single((await invoicing.Service().CreateInvoiceAsync(invoicing.Request(), Metadata("k-piece-k04"))).Invoice.Lines);

        Assert.Equal(("100", "25000.00"), (line.Quantity, line.Amounts.NetAmount));
        Assert.Equal(("1234.5", "1209.81"), (line.TheoreticalKg, line.ActualKg));
    }

    [Fact]
    public async Task SAT_D434_a_kg_line_carries_no_kilogram_information()
    {
        var invoicing = await InvoiceWorld.CreateAsync();

        var line = Assert.Single((await invoicing.Service().CreateInvoiceAsync(invoicing.Request(), Metadata("k-kg-info"))).Invoice.Lines);

        Assert.Null(line.TheoreticalKg);
        Assert.Null(line.ActualKg);
    }

    [Fact]
    public async Task SAT_D473_a_piece_line_approved_without_a_figure_is_invoiced_in_pieces_with_its_scale_kg_and_no_theoretical_kg()
    {
        // The card gave no kg/unit at approval: 40 adet × 250,00 = 10 000,00 still; kantar 500,160 kg travels; teorik kg boş, sıfır değil.
        var invoicing = await PieceInvoiceWorldAsync(40m, 495m, 500.16m, pinFigure: false);

        var invoice = (await invoicing.Service().CreateInvoiceAsync(invoicing.Request(), Metadata("k-piece-no-figure"))).Invoice;

        var line = Assert.Single(invoice.Lines);
        Assert.Equal(("40", "10000.00"), (line.Quantity, line.Amounts.NetAmount));
        Assert.Equal(((string?)null, "500.16"), (line.TheoreticalKg, line.ActualKg));
        var stored = Assert.Single(Assert.Single(invoicing.Invoices.Stored).Value.Lines);
        Assert.Equal((40m, (decimal?)null, (decimal?)500.16m), (stored.Share.Quantity, stored.Share.TheoreticalKilograms, stored.Share.ActualKilograms));
        // Golden M11/K13: the theoretical kg price "cannot be calculated"; the actual kg price is the scale kg's.
        var prices = invoice.KilogramPrices!;
        Assert.Equal("Computed", prices.State);
        var linePrices = Assert.Single(prices.Lines!);
        Assert.Equal(("TheoreticalKgUnavailable", (string?)null), (linePrices.TheoreticalPrice.State, linePrices.TheoreticalPrice.Value));
        Assert.Equal(("Shown", "19.99"), (linePrices.ActualPrice.State, linePrices.ActualPrice.Value));
        Assert.Equal(("DocumentNotAllKilogramLines", (string?)null), (prices.DocumentTheoreticalPrice!.State, prices.DocumentTheoreticalPrice.Value));

        // The order's kg price read: the theoretical kg and price stay unavailable (never 0).
        var order = GoymenERP.Modules.Sales.Application.SalesDraftService.KilogramPrices(Assert.Single(invoicing.World.Store.Drafts),
            [Assert.Single(invoicing.Invoices.Stored).Value]);
        var orderLine = Assert.Single(order.Lines!);
        Assert.Null(orderLine.TheoreticalKg);
        Assert.Equal(("TheoreticalKgUnavailable", (string?)null), (orderLine.TheoreticalPrice.State, orderLine.TheoreticalPrice.Value));
    }

    [Fact]
    public async Task AK4_a_piece_share_whose_weighing_allocation_names_other_kilograms_waits()
    {
        var invoicing = await PieceInvoiceWorldAsync(40m, 495m, 500.16m);
        invoicing.Weighings.Answer = note =>
        {
            var share = invoicing.Shipments.LastAnswer!.Shares.Single();
            // The allocation's theoretical kg must be the share's physical kg (495), never its piece count (40).
            var lines = new[] { new SalesDispatchKilogramLineV1(share.NoteLineNo, share.ShipmentId, share.ProductId, 40m, 40m) };
            var recorded = WeighingRecorded(note, theoretical: 40m, scale: 40m);
            return recorded with { Value = recorded.Value! with { KilogramAllocation = new(lines) } };
        };

        var refused = await Assert.ThrowsAsync<SalesDraftServiceException>(() =>
            invoicing.Service().CreateInvoiceAsync(invoicing.Request(), Metadata("k-piece-weighing")));

        Assert.Equal([new SalesDraftFieldError("shares[0]", SalesInvoiceReasonCodesV1.WeighingDifferenceUnavailable)], refused.Errors);
    }
}
