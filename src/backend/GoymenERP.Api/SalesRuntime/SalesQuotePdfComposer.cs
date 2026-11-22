using System.Globalization;
using GoymenERP.DocumentGeneration.Pdf;
using GoymenERP.Modules.Sales.Application.Contracts;
using Microsoft.Extensions.Localization;

namespace GoymenERP.Api.SalesRuntime;

/// <summary>
/// WP13 (DALGA_PLANI_v2 1.14): lays the sales quote (API-22, <c>sales.quotes</c>) out for the PDF from the Sales quote read, the Cari
/// owner's party display and, only when prices are printed, the Settings currency code of the quote's money profile. It prints the
/// answers verbatim and computes nothing: the quoted unit price and discount are the quote's own texts; no line amount or total is
/// printed (a quote has no totals calculator; the order computes them after the conversion). Prices appear ONLY when every line says
/// <see cref="SalesPriceDisclosure.Visible"/>; otherwise no price column or currency is drawn. A lost or converted quote carries its
/// state as a large watermark. Every user text comes from <c>Resources/SalesRuntime.SalesQuotePdfComposer.resx</c>.
/// </summary>
public sealed class SalesQuotePdfComposer(IStringLocalizer<SalesQuotePdfComposer> texts)
{
    private const string DateFormat = "dd.MM.yyyy";
    private const string RevisionPrefix = "Rev.";
    private const int MaximumFileNamePartyTextElements = 40;

    private const double NarrowColumn = 0.7;
    private const double CodeColumn = 1.6;
    private const double NameColumn = 3.2;
    private const double NumberColumn = 1.4;
    private const double UnitColumn = 0.9;
    private const double DateColumn = 1.6;
    private const double PriceColumn = 1.9;
    private const double NoteColumn = 2.4;

    public (DocumentPdfRequest Request, bool PricesVisible) Compose(Guid technicalId, SalesQuoteDetailView detail,
        SalesPartyDisplayV1 party, string? currencyCode, IReadOnlyDictionary<Guid, string> unitCodes)
    {
        ArgumentNullException.ThrowIfNull(detail);
        ArgumentNullException.ThrowIfNull(party);
        ArgumentNullException.ThrowIfNull(unitCodes);
        var quote = detail.Quote;
        if (party.PartyId != quote.PartyId)
        {
            throw new InvalidOperationException("The party display does not belong to the quote.");
        }

        var pricesVisible = detail.Lines.Count > 0 && detail.Lines.All(line => line.PriceDisclosure == SalesPriceDisclosure.Visible);
        if (pricesVisible && string.IsNullOrWhiteSpace(currencyCode))
        {
            throw new InvalidOperationException("Visible prices need the quote's currency code.");
        }

        var blocks = new List<DocumentPdfBlock>
        {
            new DocumentPdfHeading(T("HeadingQuote")),
            new DocumentPdfFields(QuoteFields(detail, party, pricesVisible ? currencyCode : null)),
            new DocumentPdfHeading(T("HeadingLines")),
        };
        if (detail.Lines.Count == 0)
        {
            blocks.Add(new DocumentPdfParagraph(T("NoLines")));
        }
        else
        {
            blocks.Add(LineTable(detail.Lines.OrderBy(line => line.LineNo).ToArray(), pricesVisible ? currencyCode : null, unitCodes));
        }

        blocks.Add(new DocumentPdfParagraph(pricesVisible ? T("TotalsNotComputed") : T("PricesNotShown")));
        var request = new DocumentPdfRequest(
            technicalId,
            new PdfDocumentFileNameParts(
                pricesVisible ? T("DocumentType") : T("DocumentTypeWithoutPrices"),
                quote.DocumentNo,
                FileNameParty(party),
                quote.BusinessDate,
                FileNameVersion(quote.Revision),
                StateText(quote.State)),
            Format("Title", quote.DocumentNo),
            blocks,
            Format("Footer", quote.DocumentNo, quote.Revision),
            T("PageLabel"),
            quote.State == "Open" ? null : T("Watermark" + quote.State));
        return (request, pricesVisible);
    }

    private List<DocumentPdfField> QuoteFields(SalesQuoteDetailView detail, SalesPartyDisplayV1 party, string? currency)
    {
        var quote = detail.Quote;
        var fields = new List<DocumentPdfField>
        {
            new(T("FieldDocumentNo"), quote.DocumentNo),
            new(T("FieldRevision"), quote.Revision),
            new(T("FieldState"), StateText(quote.State)),
            new(T("FieldCustomer"), party.TradeName is { Length: > 0 } trade
                ? Format("PartyWithTradeName", party.Code, party.LegalName, trade)
                : Format("Party", party.Code, party.LegalName)),
            new(T("FieldBusinessDate"), quote.BusinessDate.ToString(DateFormat, CultureInfo.InvariantCulture)),
            new(T("FieldValidUntil"), quote.ValidUntil is { } until ? until.ToString(DateFormat, CultureInfo.InvariantCulture) : T("NotGiven")),
        };
        if (detail.Expiry.IsExpired)
        {
            fields.Add(new(T("FieldExpiry"), detail.Expiry.IsApproved ? T("ExpiredApproved") : T("Expired")));
        }

        fields.Add(new(T("FieldRequestedDelivery"), detail.RequestedDeliveryDate is { } date
            ? date.ToString(DateFormat, CultureInfo.InvariantCulture)
            : T("NotGiven")));
        if (currency is not null)
        {
            fields.Add(new(T("FieldCurrency"), currency));
        }

        if (!string.IsNullOrWhiteSpace(detail.Note))
        {
            fields.Add(new(T("FieldNote"), detail.Note));
        }

        return fields;
    }

    private DocumentPdfTable LineTable(IReadOnlyList<SalesQuoteLineView> lines, string? currency, IReadOnlyDictionary<Guid, string> unitCodes)
    {
        var columns = new List<DocumentPdfColumn>
        {
            new(T("ColumnLineNo"), NarrowColumn, DocumentPdfAlignment.End),
            new(T("ColumnProductCode"), CodeColumn),
            new(T("ColumnProductName"), NameColumn),
            new(T("ColumnQuantity"), NumberColumn, DocumentPdfAlignment.End),
            new(T("ColumnUnit"), UnitColumn),
        };
        if (currency is not null)
        {
            columns.Add(new(T("ColumnUnitPrice"), PriceColumn, DocumentPdfAlignment.End));
            columns.Add(new(T("ColumnDiscount"), UnitColumn, DocumentPdfAlignment.End));
        }

        columns.Add(new(T("ColumnDelivery"), DateColumn));
        columns.Add(new(T("ColumnNote"), NoteColumn));
        var rows = new List<IReadOnlyList<string>>(lines.Count);
        foreach (var line in lines)
        {
            var row = new List<string>
            {
                line.LineNo.ToString(CultureInfo.InvariantCulture),
                line.ProductCode,
                line.ProductName,
                line.Quantity,
                line.OrderUnitCode,
            };
            if (currency is not null)
            {
                row.Add(line.UnitPrice is { } price && line.PriceUnitId is { } priceUnit
                    ? Format("UnitPrice", price, currency, unitCodes.TryGetValue(priceUnit, out var code) && !string.IsNullOrWhiteSpace(code)
                        ? code
                        : throw new InvalidOperationException("A printed price unit has no code."))
                    : T("Unpriced"));
                row.Add(line.DiscountPercent is { } discount ? Format("Percent", discount) : string.Empty);
            }

            row.Add(line.RequestedDeliveryDate is { } date ? date.ToString(DateFormat, CultureInfo.InvariantCulture) : string.Empty);
            row.Add(line.Note ?? string.Empty);
            rows.Add(row);
        }

        return new DocumentPdfTable(columns, rows);
    }

    private string StateText(string state) => T("State" + state);

    private static int FileNameVersion(string revision) =>
        revision.StartsWith(RevisionPrefix, StringComparison.Ordinal)
        && int.TryParse(revision.AsSpan(RevisionPrefix.Length), NumberStyles.None, CultureInfo.InvariantCulture, out var number)
            ? checked(number + 1)
            : throw new InvalidOperationException("The quote revision is not a Rev.N text.");

    private static string FileNameParty(SalesPartyDisplayV1 party)
    {
        var name = string.IsNullOrWhiteSpace(party.TradeName) ? party.LegalName : party.TradeName;
        var elements = StringInfo.GetTextElementEnumerator(name.Trim());
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
            ? throw new InvalidOperationException($"The sales quote PDF text '{key}' is missing.")
            : text.Value;
    }
}
