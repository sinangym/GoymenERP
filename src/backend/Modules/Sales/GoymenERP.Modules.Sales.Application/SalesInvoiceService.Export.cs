using GoymenERP.Modules.Sales.Application.Contracts;
using GoymenERP.Modules.Sales.Domain;
using GoymenERP.Modules.Sales.Domain.Invoices;

namespace GoymenERP.Modules.Sales.Application;

/// <summary>
/// S5 API-34 — the sales invoice export (SAT-D236, SAT-D244, SAT-D248; DEC-SAL-006). The finalised invoices whose finalisation
/// falls on the Europe/Istanbul business days of the range (both included) are read in ONE bulk answer for the Accounting
/// workbook. A read: it writes nothing (no attempt, audit or file record — the order PDF precedent), opens no official connection
/// (DEC-INT-001, DEC-FIN-003) and never calls another module. <c>sales.invoices.export</c> is measured before anything is read and
/// after every awaited store read; the price read is measured before anything is read and once more at the end, after the last store
/// read: the file is prices, so without the price read the export is 403 (SAT-D238 (3): the current fail-closed rule stays until the price-withholding change lands; an export never
/// releases an amount the detail would withhold). More invoices than <see cref="SalesInvoiceApiV1.MaximumExportInvoices"/> is
/// 422 <see cref="SalesInvoiceProblemCodesV1.ExportTooLarge"/>: nothing is cut silently.
/// </summary>
public sealed partial class SalesInvoiceService
{
    private const string ExportPermission = SalesPermissionManifestV1.InvoicesExport;

    public async Task<SalesInvoiceExportV1> ExportInvoicesAsync(SalesInvoiceExportQuery query, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);
        var scope = await RequireAsync(ExportPermission, cancellationToken);
        await RequirePriceReadAsync(scope, cancellationToken);
        ValidateExport(query);
        var fromUtc = StartOfDayUtc(query.From);
        var toUtcExclusive = StartOfDayUtc(query.To.AddDays(1));

        var batch = await StoreAsync(scope,
            () => invoices.ListFinalizedInvoicesAsync(fromUtc, toUtcExclusive, SalesInvoiceApiV1.MaximumExportInvoices, cancellationToken),
            cancellationToken, ExportPermission);
        if (batch.HasMore)
        {
            throw new SalesDraftServiceException(422, SalesInvoiceProblemCodesV1.ExportTooLarge, [new("to", "range_too_large")]);
        }

        var orderIds = batch.Items.SelectMany(invoice => invoice.Lines.Select(line => line.Share.SalesOrderId)).Distinct().ToArray();
        var orderNos = orderIds.Length == 0
            ? new Dictionary<Guid, string>()
            : await StoreAsync(scope, () => invoices.FindOrderDocumentNumbersAsync(orderIds, cancellationToken), cancellationToken,
                ExportPermission);
        if (orderIds.Any(id => !orderNos.ContainsKey(id)))
        {
            // An invoice line names an order this company's store does not hold: a broken reference, never a blank cell.
            throw new SalesDraftServiceException(503, SalesDraftProblemCodesV1.ReferenceProviderInvalid);
        }

        var exported = batch.Items.Select(invoice => ExportInvoice(invoice, orderNos)).ToList();
        await RequirePriceReadAsync(scope, cancellationToken);
        return new SalesInvoiceExportV1(SalesInvoiceApiV1.ExportFormatXlsx, query.From, query.To, exported);
    }

    private static void ValidateExport(SalesInvoiceExportQuery query)
    {
        var errors = new List<SalesDraftFieldError>();
        // SAT-D236 (1) names Excel AND UBL-TR XML; only the workbook is served now. SAT-D278 (EK54) decided that both the
        // TEMELFATURA and TICARIFATURA profiles are supported, but the invoice type, the VKN/address source and the zip packaging
        // are still open and the XML is not implemented, so it is refused as an unknown format — never answered with a guessed document.
        if (!string.Equals(query.Format, SalesInvoiceApiV1.ExportFormatXlsx, StringComparison.Ordinal))
        {
            errors.Add(new("format", "validation_failed"));
        }

        if (query.From > query.To || query.To == DateOnly.MaxValue)
        {
            errors.Add(new("to", "validation_failed"));
        }
        else if (query.To.DayNumber - query.From.DayNumber + 1 > SalesInvoiceApiV1.MaximumExportRangeDays)
        {
            errors.Add(new("to", "range_too_long"));
        }

        if (errors.Count > 0)
        {
            throw new SalesDraftServiceException(400, "validation_failed", errors);
        }
    }

    /// <summary>SAT-D10: the start of the Europe/Istanbul day; without the zone nothing is guessed (503, never UTC).</summary>
    private static DateTimeOffset StartOfDayUtc(DateOnly day)
    {
        try
        {
            return SalesBusinessDay.StartOfDayUtc(day);
        }
        catch (SalesBusinessDayUnavailableException)
        {
            throw new SalesDraftServiceException(503, "service_unavailable");
        }
    }

    private static SalesInvoiceExportInvoiceV1 ExportInvoice(SalesInvoice invoice, IReadOnlyDictionary<Guid, string> orderNos)
    {
        var header = invoice.Header;
        var scale = header.AmountScale;
        var functionalScale = header.Rate?.FunctionalScale ?? 0;
        var finalization = invoice.Finalization
            ?? throw new SalesDraftServiceException(503, "service_unavailable");
        var lines = invoice.Lines.OrderBy(line => line.LineNo).ToList();
        return new SalesInvoiceExportInvoiceV1(invoice.Id, invoice.DocumentNo, invoice.Match?.OfficialNo, invoice.Match?.Ettn,
            Day(finalization.FinalizedAtUtc), header.BusinessDate, header.PartyId, header.CompanyAccountId, header.CurrencyId, scale,
            header.Rate is { } r ? new SalesInvoiceRateView(Decimal(r.Rate), r.RateDay, r.BulletinDate, r.FunctionalCurrencyId, r.FunctionalScale) : null,
            Amounts(invoice.Totals, scale),
            invoice.FunctionalTotals is { } totals ? Amounts(totals, functionalScale) : null,
            // SAT-D279 (EK55): the official document's own VAT and withholding (TL, SAT-D241) as the
            // match recorded them, beside the computed ones; empty without a recorded comparison, never copied from the invoice.
            NullableExactText(invoice.Match?.Taxes?.OfficialVatAmount),
            NullableExactText(invoice.Match?.Taxes?.OfficialWithholdingAmount),
            [.. finalization.Plan.OrderBy(item => item.SequenceNo).Select(item => new SalesInvoiceFinalInstalmentView(item.SequenceNo,
                item.DueDate, Text(item.Amount, scale), item.FunctionalAmount is { } functional ? Text(functional, functionalScale) : null,
                item.OpenItemId, item.InstallmentId))],
            // SAT-D246 / SAT-D248: every order the invoice is tied to, in line order, once each.
            [.. lines.Select(line => orderNos[line.Share.SalesOrderId]).Distinct(StringComparer.Ordinal)],
            [.. lines.Select(line => new SalesInvoiceExportLineV1(line.LineNo, line.Share.DispatchNoteNumber, line.Share.NoteLineNo,
                line.Share.ShipmentDate, line.Share.SalesOrderId, orderNos[line.Share.SalesOrderId], line.Share.ProductId,
                line.Share.UnitId, line.Share.QuantityText, Decimal(line.NetUnitPrice), line.Share.Commercial.DiscountPercent,
                line.Share.Commercial.DiscountAmount, line.Share.Commercial.SurchargePercent, line.Share.Commercial.SurchargeAmount,
                Decimal(line.VatPercent), line.WithholdingShare is { } share ? Decimal(share) : null, Amounts(line.Amounts, scale),
                line.Functional is { } f ? Amounts(f.Amounts, functionalScale) : null))]);
    }
}
