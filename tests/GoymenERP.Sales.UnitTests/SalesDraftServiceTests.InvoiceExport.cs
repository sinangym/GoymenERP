using System.Globalization;
using GoymenERP.Modules.Sales.Application.Contracts;

namespace GoymenERP.Sales.UnitTests;

// S5 API-34: the sales invoice export read (SAT-D236, SAT-D238, SAT-D244, SAT-D246, SAT-D248). Every value is SENTETIK.
public sealed partial class SalesDraftServiceTests
{
    private const string InvoiceExport = SalesPermissionManifestV1.InvoicesExport;
    // The fake store stamps the finalisation with its own clock (UnixEpoch = 1970-01-01 02:00 Europe/Istanbul).
    private static readonly DateOnly FinalizedDay = new(1970, 1, 1);

    private static SalesInvoiceExportQuery ExportQuery(DateOnly? from = null, DateOnly? to = null, string? format = "xlsx") =>
        new(format, from ?? FinalizedDay, to ?? FinalizedDay);

    /// <summary>An invoice of <paramref name="orders"/> approved orders (one shipment share each), finalised by a third actor.</summary>
    private static async Task<(InvoiceWorld Invoicing, Guid Id, IReadOnlyList<SalesOrderDraftDetailView> Orders)> FinalizedInvoiceAsync(
        int orders = 1)
    {
        var invoicing = await InvoiceWorld.CreateAsync();
        var all = new List<SalesOrderDraftDetailView> { invoicing.Order };
        var shares = new List<(SalesOrderDraftDetailView Order, Guid ShipmentId)> { (invoicing.Order, invoicing.ShipmentId) };
        for (var index = 1; index < orders; index++)
        {
            var order = await PricedApprovedAsync(invoicing.World, $"k-export-order-{index}");
            await invoicing.World.Service().RecordCustomerAcceptanceAsync(order.Draft.Id, Command(order.Draft.Version),
                Metadata($"k-export-accept-{index}"));
            all.Add(order);
            shares.Add((order, Guid.NewGuid()));
        }

        invoicing.Shipments.Answer = _ => new(SalesDispatchReadOutcomeV1.Found, Company,
        [
            .. shares.Select((share, index) => new SalesInvoiceableShipmentV1(Guid.NewGuid(), $"IRS-2026-00000{index + 1}", null, Approved,
                Party, Account, 1, share.ShipmentId, Shipped, share.Order.Draft.Id, share.Order.Lines[0].LineId, Product, KgUnit, 1.25m)),
        ], false, SalesInvoicedStateSourceV1.NoSource);
        var created = (await invoicing.Service().CreateInvoiceAsync(
            new CreateSalesInvoiceRequest(Account, [.. shares.Select(share => new SalesInvoiceShareRequest(share.ShipmentId))]),
            Metadata("k-export-create"))).Invoice;
        await invoicing.Service().SubmitInvoiceAsync(created.Id, Command("1"), Metadata("k-export-submit"));
        invoicing.World.User.Id = "synthetic-invoice-approver";
        await invoicing.Service().ApproveInvoiceAsync(created.Id, Command("2"), Metadata("k-export-approve"));
        invoicing.World.User.Id = "synthetic-invoice-matcher";
        // SAT-D241 (merged from main): the official VAT and withholding TL totals are those of this invoice, so a multi-order
        // invoice's larger VAT is no difference either.
        invoicing.Official.Has(decimal.Parse(created.FunctionalTotals!.GrossAmount, CultureInfo.InvariantCulture),
            vat: decimal.Parse(created.FunctionalTotals.VatAmount, CultureInfo.InvariantCulture),
            withholding: decimal.Parse(created.FunctionalTotals.WithheldVatAmount, CultureInfo.InvariantCulture));
        await invoicing.Service().MatchInvoiceAsync(created.Id, MatchRequest(), Metadata("k-export-match"));
        invoicing.World.User.Id = "synthetic-invoice-finalizer";
        await invoicing.Service().FinalizeInvoiceAsync(created.Id, Command("3"), Metadata("k-export-final"));
        foreach (var order in all)
        {
            invoicing.Invoices.OrderNumbers[order.Draft.Id] = order.Draft.DocumentNo;
        }

        invoicing.World.Authority.Measured.Clear();
        return (invoicing, created.Id, all);
    }

    [Fact]
    public async Task A_finalized_invoice_is_exported_with_its_lines_amounts_due_plan_and_order_number()
    {
        var (invoicing, id, orders) = await FinalizedInvoiceAsync();
        var detail = await invoicing.Service().GetInvoiceAsync(id);
        invoicing.World.Authority.Measured.Clear();

        var export = await invoicing.Service().ExportInvoicesAsync(ExportQuery());

        Assert.Equal(("xlsx", FinalizedDay, FinalizedDay), (export.Format, export.From, export.To));
        var invoice = Assert.Single(export.Invoices);
        Assert.Equal((id, detail.DocumentNo, "GIB2026000000001", FinalizedDay), (invoice.Id, invoice.DocumentNo, invoice.OfficialNo,
            invoice.FinalizedOn));
        Assert.Equal((Party, Account, detail.CurrencyId, detail.AmountScale), (invoice.PartyId, invoice.CompanyAccountId, invoice.CurrencyId,
            invoice.AmountScale));
        Assert.Equal(detail.Totals, invoice.Totals);
        Assert.Equal(detail.FunctionalTotals, invoice.FunctionalTotals);
        // Issue #66 comment 6771235622 / EK55 (pending): the official document's VAT and withholding are the recorded match's
        // (SAT-D241), answered beside the computed totals.
        Assert.NotNull(detail.Match!.OfficialVatAmount);
        Assert.NotNull(detail.Match.OfficialWithholdingAmount);
        Assert.Equal((detail.Match.OfficialVatAmount, detail.Match.OfficialWithholdingAmount),
            (invoice.OfficialVatFunctionalAmount, invoice.OfficialWithholdingFunctionalAmount));
        Assert.Equal(detail.Finalization!.Instalments, invoice.Instalments);
        Assert.Equal([orders[0].Draft.DocumentNo], invoice.SalesOrderNos);
        var line = Assert.Single(invoice.Lines);
        Assert.Equal((1, "IRS-2026-000001", orders[0].Draft.Id, orders[0].Draft.DocumentNo, Product, KgUnit),
            (line.LineNo, line.DispatchNoteNumber, line.SalesOrderId, line.SalesOrderNo, line.ProductId, line.UnitId));
        Assert.Equal(detail.Lines[0].Amounts, line.Amounts);
        // The export name, then the price read, before the read; both again after it (SAT-D238 fail-closed).
        Assert.Equal([InvoiceExport, InvoicePriceRead], invoicing.World.Authority.Measured.Take(2));
        Assert.Equal(InvoicePriceRead, invoicing.World.Authority.Measured[^1]);
    }

    [Fact]
    public async Task A_multi_order_invoice_carries_every_order_number_once_in_line_order_SAT_D246()
    {
        var (invoicing, _, orders) = await FinalizedInvoiceAsync(orders: 2);

        var invoice = Assert.Single((await invoicing.Service().ExportInvoicesAsync(ExportQuery())).Invoices);

        Assert.Equal([orders[0].Draft.DocumentNo, orders[1].Draft.DocumentNo], invoice.SalesOrderNos);
        Assert.Equal([orders[0].Draft.DocumentNo, orders[1].Draft.DocumentNo], invoice.Lines.Select(line => line.SalesOrderNo));
    }

    [Fact]
    public async Task Only_finalized_invoices_whose_finalization_day_is_in_the_range_are_exported()
    {
        var (invoicing, _, _) = await FinalizedInvoiceAsync();
        // A draft invoice of the same day is never exported.
        var draftShipment = Guid.NewGuid();
        invoicing.Shipments.Answer = _ => new(SalesDispatchReadOutcomeV1.Found, Company,
        [
            new SalesInvoiceableShipmentV1(Guid.NewGuid(), "IRS-2026-000009", null, Approved, Party, Account, 1, draftShipment, Shipped,
                invoicing.Order.Draft.Id, invoicing.Order.Lines[0].LineId, Product, KgUnit, 0.25m),
        ], false, SalesInvoicedStateSourceV1.NoSource);
        await invoicing.Service().CreateInvoiceAsync(new CreateSalesInvoiceRequest(Account, [new SalesInvoiceShareRequest(draftShipment)]),
            Metadata("k-export-draft"));
        Assert.Equal(2, invoicing.Invoices.Stored.Count);

        Assert.Single((await invoicing.Service().ExportInvoicesAsync(ExportQuery())).Invoices);
        Assert.Single((await invoicing.Service().ExportInvoicesAsync(ExportQuery(FinalizedDay.AddDays(-30), FinalizedDay))).Invoices);
        Assert.Empty((await invoicing.Service().ExportInvoicesAsync(ExportQuery(FinalizedDay.AddDays(1), FinalizedDay.AddDays(2)))).Invoices);
        Assert.Empty((await invoicing.Service().ExportInvoicesAsync(ExportQuery(FinalizedDay.AddDays(-2), FinalizedDay.AddDays(-1)))).Invoices);
    }

    [Fact]
    public async Task The_export_needs_its_own_name_and_the_price_read_before_anything_is_read_and_again_at_the_end_SAT_D238()
    {
        var (invoicing, _, _) = await FinalizedInvoiceAsync();
        invoicing.World.Authority.Denied.Add(InvoiceExport);
        Assert.Equal(403, (await Assert.ThrowsAsync<SalesDraftServiceException>(() =>
            invoicing.Service().ExportInvoicesAsync(ExportQuery()))).Status);

        invoicing.World.Authority.Denied.Clear();
        invoicing.World.Authority.Denied.Add(InvoicePriceRead);
        Assert.Equal(403, (await Assert.ThrowsAsync<SalesDraftServiceException>(() =>
            invoicing.Service().ExportInvoicesAsync(ExportQuery()))).Status);
        Assert.Equal(0, invoicing.Invoices.ExportReads);

        // The invoice read alone is not the export: the export name is its own.
        invoicing.World.Authority.Denied.Clear();
        invoicing.World.Authority.Denied.Add(SalesPermissionManifestV1.InvoicesRead);
        Assert.Single((await invoicing.Service().ExportInvoicesAsync(ExportQuery())).Invoices);

        // A price read lost after the store read stops the answer before any amount leaves.
        invoicing.World.Authority.Denied.Clear();
        invoicing.World.Authority.LostLater = (InvoicePriceRead, invoicing.World.Authority.Calls + 2);
        var reads = invoicing.Invoices.ExportReads;
        Assert.Equal(403, (await Assert.ThrowsAsync<SalesDraftServiceException>(() =>
            invoicing.Service().ExportInvoicesAsync(ExportQuery()))).Status);
        Assert.Equal(reads + 1, invoicing.Invoices.ExportReads);

        invoicing.World.Authority.LostLater = null;
        invoicing.World.User.Authenticated = false;
        Assert.Equal((401, "unauthenticated"), Status(await Assert.ThrowsAsync<SalesDraftServiceException>(() =>
            invoicing.Service().ExportInvoicesAsync(ExportQuery()))));
    }

    [Theory]
    [InlineData("ubl-tr", 0, 0, "format", "validation_failed")]
    [InlineData(null, 0, 0, "format", "validation_failed")]
    [InlineData("XLSX", 0, 0, "format", "validation_failed")]
    [InlineData("xlsx", 1, 0, "to", "validation_failed")]
    [InlineData("xlsx", 0, 366, "to", "range_too_long")]
    public async Task An_unknown_format_or_a_bad_range_is_400_before_any_read(string? format, int fromOffset, int toOffset, string field,
        string reason)
    {
        var invoicing = await InvoiceWorld.CreateAsync();

        var refused = await Assert.ThrowsAsync<SalesDraftServiceException>(() => invoicing.Service().ExportInvoicesAsync(
            ExportQuery(FinalizedDay.AddDays(fromOffset), FinalizedDay.AddDays(toOffset), format)));

        Assert.Equal((400, "validation_failed"), Status(refused));
        Assert.Equal([new SalesDraftFieldError(field, reason)], refused.Errors);
        Assert.Equal(0, invoicing.Invoices.ExportReads);
    }

    [Fact]
    public async Task A_full_year_range_is_read_and_more_invoices_than_the_limit_is_422_never_a_cut_file()
    {
        var (invoicing, _, _) = await FinalizedInvoiceAsync();
        Assert.Single((await invoicing.Service().ExportInvoicesAsync(ExportQuery(FinalizedDay.AddDays(-365), FinalizedDay))).Invoices);

        invoicing.Invoices.ExportHasMore = true;
        var refused = await Assert.ThrowsAsync<SalesDraftServiceException>(() => invoicing.Service().ExportInvoicesAsync(ExportQuery()));
        Assert.Equal((422, SalesInvoiceProblemCodesV1.ExportTooLarge), Status(refused));
        Assert.Equal([new SalesDraftFieldError("to", "range_too_large")], refused.Errors);
    }

    [Fact]
    public async Task An_order_number_the_store_does_not_hold_is_503_never_a_blank_cell()
    {
        var (invoicing, _, _) = await FinalizedInvoiceAsync();
        invoicing.Invoices.OrderNumbers.Clear();

        Assert.Equal((503, SalesDraftProblemCodesV1.ReferenceProviderInvalid), Status(await Assert.ThrowsAsync<SalesDraftServiceException>(() =>
            invoicing.Service().ExportInvoicesAsync(ExportQuery()))));
    }
}
