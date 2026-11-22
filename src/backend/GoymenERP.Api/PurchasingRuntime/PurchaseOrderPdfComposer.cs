using System.Globalization;
using GoymenERP.DocumentGeneration.Pdf;
using GoymenERP.Modules.Product.Application.Contracts;
using GoymenERP.Modules.Purchasing.Application.Contracts;
using Microsoft.Extensions.Localization;

namespace GoymenERP.Api.PurchasingRuntime;

/// <summary>
/// WP13 (DALGA_PLANI_v2 1.14): lays the purchase order out for the PDF from the Purchasing owner's two reads — the authorized order
/// detail (with the supplier as Cari shows it) and the order totals (the shared commercial tax calculator's answer) — plus the Product
/// owner's code/name of each product and the Settings unit codes. It prints the answers verbatim and computes nothing: a price, line
/// amount, VAT or total is printed only as the owner answered it; a state the owner could not compute is printed as its reason, never
/// as 0. Prices and totals appear ONLY when both reads say <c>Visible</c>; otherwise no price column or total is drawn (never empty
/// cells). Only the general lines are printed (a technical line is a breakdown of its general line, not a separate commitment). Every
/// user text comes from <c>Resources/PurchasingRuntime.PurchaseOrderPdfComposer.resx</c>; a missing text is a server fault.
/// </summary>
public sealed class PurchaseOrderPdfComposer(IStringLocalizer<PurchaseOrderPdfComposer> texts)
{
    private const string GeneralKind = "General";
    private const string DateFormat = "dd.MM.yyyy";
    private const int MaximumFileNamePartyTextElements = 40;

    // Column weights of the line table: layout proportions only.
    private const double NarrowColumn = 0.7;
    private const double CodeColumn = 1.6;
    private const double NameColumn = 3.2;
    private const double NumberColumn = 1.4;
    private const double UnitColumn = 0.9;
    private const double DateColumn = 2.2;
    private const double PriceColumn = 1.9;

    public (DocumentPdfRequest Request, bool PricesVisible) Compose(Guid technicalId, PurchaseOrderReadDetailViewV1 detail,
        PurchaseOrderTotalsView totals, IReadOnlyDictionary<Guid, ProductPurchasingDocumentDisplayV1> products,
        IReadOnlyDictionary<Guid, string> unitCodes)
    {
        ArgumentNullException.ThrowIfNull(detail);
        ArgumentNullException.ThrowIfNull(totals);
        ArgumentNullException.ThrowIfNull(products);
        ArgumentNullException.ThrowIfNull(unitCodes);
        var order = detail.Order;
        if (totals.OrderId != order.Id || !string.Equals(totals.Version, order.Version, StringComparison.Ordinal))
        {
            throw new InvalidOperationException("The totals do not belong to the detail's version.");
        }

        var pricesVisible = string.Equals(detail.PriceDisclosure, PurchasingPriceDisclosureV1.Visible, StringComparison.Ordinal)
            && string.Equals(totals.PriceDisclosure, PurchasingPriceDisclosureV1.Visible, StringComparison.Ordinal);
        var currency = pricesVisible
            ? (detail.Currency ?? throw new InvalidOperationException("Visible prices need the order's currency.")).CurrencyCode
            : null;
        if (pricesVisible && totals.Currency is { } totalsCurrency
            && !string.Equals(totalsCurrency.CurrencyCode, currency, StringComparison.Ordinal))
        {
            throw new InvalidOperationException("The totals answer another currency than the detail.");
        }

        var general = detail.Lines.Where(line => string.Equals(line.Kind, GeneralKind, StringComparison.Ordinal))
            .OrderBy(line => line.LineNo).ToArray();
        var lineTotals = pricesVisible ? totals.Lines.ToDictionary(line => line.LineId) : null;
        var documentNo = order.DocumentNumber ?? throw new InvalidOperationException("A purchase order PDF needs the order's document number.");
        var blocks = new List<DocumentPdfBlock>
        {
            new DocumentPdfHeading(T("HeadingOrder")),
            new DocumentPdfFields(OrderFields(detail, documentNo, currency)),
            new DocumentPdfHeading(T("HeadingLines")),
        };
        if (general.Length == 0)
        {
            blocks.Add(new DocumentPdfParagraph(T("NoLines")));
        }
        else
        {
            blocks.Add(LineTable(general, products, unitCodes, lineTotals, currency));
        }

        blocks.Add(new DocumentPdfHeading(T("HeadingTotals")));
        if (pricesVisible)
        {
            blocks.Add(new DocumentPdfFields(TotalFields(totals, currency!)));
        }
        else
        {
            blocks.Add(new DocumentPdfParagraph(T("PricesNotShown")));
        }

        var draft = string.Equals(order.Status, "Draft", StringComparison.Ordinal);
        var request = new DocumentPdfRequest(
            technicalId,
            new PdfDocumentFileNameParts(
                pricesVisible ? T("DocumentType") : T("DocumentTypeWithoutPrices"),
                documentNo,
                FileNameParty(detail.Order.SupplierDisplay),
                order.EffectiveOn,
                checked(int.Parse(order.Version, NumberStyles.None, CultureInfo.InvariantCulture)),
                StatusText(order.Status)),
            Format("Title", documentNo),
            blocks,
            Format("Footer", documentNo, order.Version),
            T("PageLabel"),
            draft ? T("DraftWatermark") : null);
        return (request, pricesVisible);
    }

    private List<DocumentPdfField> OrderFields(PurchaseOrderReadDetailViewV1 detail, string documentNo, string? currency)
    {
        var order = detail.Order;
        var supplier = order.SupplierDisplay;
        var fields = new List<DocumentPdfField>
        {
            new(T("FieldDocumentNo"), documentNo),
            new(T("FieldStatus"), StatusText(order.Status)),
            new(T("FieldSupplier"), supplier.TradeName is { Length: > 0 } trade
                ? Format("SupplierWithTradeName", supplier.Code, supplier.LegalName, trade)
                : Format("Supplier", supplier.Code, supplier.LegalName)),
            new(T("FieldEffectiveOn"), order.EffectiveOn.ToString(DateFormat, CultureInfo.InvariantCulture)),
            new(T("FieldRevision"), order.Version),
        };
        if (currency is not null)
        {
            fields.Add(new(T("FieldCurrency"), currency));
        }

        if (detail.Cancellation is { } cancellation)
        {
            fields.Add(new(T("FieldCancellation"), Format("Cancellation", cancellation.CancelledBy,
                cancellation.CancelledAtUtc.ToString(DateFormat, CultureInfo.InvariantCulture), cancellation.Reason)));
        }

        return fields;
    }

    private DocumentPdfTable LineTable(IReadOnlyList<PurchaseOrderLineView> lines,
        IReadOnlyDictionary<Guid, ProductPurchasingDocumentDisplayV1> products, IReadOnlyDictionary<Guid, string> unitCodes,
        IReadOnlyDictionary<Guid, PurchaseOrderLineTotalView>? lineTotals, string? currency)
    {
        var columns = new List<DocumentPdfColumn>
        {
            new(T("ColumnLineNo"), NarrowColumn, DocumentPdfAlignment.End),
            new(T("ColumnProductCode"), CodeColumn),
            new(T("ColumnProductName"), NameColumn),
            new(T("ColumnQuantity"), NumberColumn, DocumentPdfAlignment.End),
            new(T("ColumnUnit"), UnitColumn),
            new(T("ColumnDelivery"), DateColumn),
        };
        if (lineTotals is not null)
        {
            columns.Add(new(T("ColumnUnitPrice"), PriceColumn, DocumentPdfAlignment.End));
            columns.Add(new(T("ColumnDiscount"), UnitColumn, DocumentPdfAlignment.End));
            columns.Add(new(T("ColumnNetAmount"), PriceColumn, DocumentPdfAlignment.End));
            columns.Add(new(T("ColumnVatAmount"), PriceColumn, DocumentPdfAlignment.End));
        }

        var rows = new List<IReadOnlyList<string>>(lines.Count);
        foreach (var line in lines)
        {
            if (!products.TryGetValue(line.ProductId, out var product) || product.Outcome != ProductPurchasingDocumentDisplayOutcomeV1.Available
                || product.Code is null || product.Name is null)
            {
                throw new InvalidOperationException("A printed line has no product display.");
            }

            var row = new List<string>
            {
                line.LineNo.ToString(CultureInfo.InvariantCulture),
                product.Code,
                product.Name,
                line.Ordered,
                Unit(unitCodes, line.UnitId),
                line.DeliverySchedule.Count == 0
                    ? T("NoDeliverySchedule")
                    : string.Join('\n', line.DeliverySchedule.OrderBy(slice => slice.SliceNo).Select(slice => Format("DeliverySlice",
                        slice.DeliveryDate.ToString(DateFormat, CultureInfo.InvariantCulture), slice.Quantity))),
            };
            if (lineTotals is not null)
            {
                if (!lineTotals.TryGetValue(line.LineId, out var total) || total.LineNo != line.LineNo)
                {
                    throw new InvalidOperationException("The totals do not answer a printed line.");
                }

                row.Add(line.Price is { } price
                    ? Format("UnitPrice", price.UnitPrice, currency!, Unit(unitCodes, price.PriceUnitId))
                    : T("PricePending"));
                row.Add(line.Price?.DiscountPercent is { } discount ? Format("Percent", discount) : string.Empty);
                row.Add(total.NetAmount is { } net ? Format("Amount", net, currency!) : StateText(total.State));
                row.Add(total.VatAmount is { } vat ? Format("Amount", vat, currency!) : StateText(total.VatState));
            }

            rows.Add(row);
        }

        return new DocumentPdfTable(columns, rows);
    }

    private List<DocumentPdfField> TotalFields(PurchaseOrderTotalsView totals, string currency) =>
    [
        new(T("TotalNet"), totals.NetAmount is { } net ? Format("Amount", net, currency) : StateText(totals.State)),
        new(T("TotalVat"), totals.VatAmount is { } vat ? Format("Amount", vat, currency) : StateText(totals.VatState)),
        new(T("TotalGross"), totals.GrossAmount is { } gross ? Format("Amount", gross, currency) : StateText(totals.VatState)),
        new(T("TotalWithheld"), totals.WithheldVatAmount is { } withheld ? Format("Amount", withheld, currency) : StateText(totals.WithholdingState)),
        new(T("TotalPayable"), totals.PayableAmount is { } payable ? Format("Amount", payable, currency) : StateText(totals.WithholdingState)),
    ];

    private string Unit(IReadOnlyDictionary<Guid, string> unitCodes, Guid unitId) =>
        unitCodes.TryGetValue(unitId, out var code) && !string.IsNullOrWhiteSpace(code)
            ? code
            : throw new InvalidOperationException("A printed unit has no code.");

    /// <summary>An owner state as its Turkish text; an unknown state is not printed (fail-closed).</summary>
    private string StateText(string state) => T("State" + state);

    private string StatusText(string status) => T("Status" + status);

    private static string FileNameParty(PurchasingSupplierDisplayV1 supplier)
    {
        var text = supplier.TradeName is { Length: > 0 } trade ? trade : supplier.LegalName;
        var elements = StringInfo.GetTextElementEnumerator(text);
        var builder = new System.Text.StringBuilder();
        var count = 0;
        while (elements.MoveNext() && count < MaximumFileNamePartyTextElements)
        {
            builder.Append(elements.GetTextElement());
            count++;
        }

        return builder.ToString();
    }

    private string Format(string key, params object[] values) => string.Format(CultureInfo.InvariantCulture, T(key), values);

    private string T(string key)
    {
        var text = texts[key];
        return text.ResourceNotFound || string.IsNullOrEmpty(text.Value)
            ? throw new InvalidOperationException($"The purchase order PDF text '{key}' is missing.")
            : text.Value;
    }
}
