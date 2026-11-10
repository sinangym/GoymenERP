using System.Globalization;
using System.IO.Compression;
using System.Text;
using System.Xml;
using GoymenERP.Modules.Sales.Application.Contracts;
using Microsoft.Extensions.Localization;

namespace GoymenERP.Api.SalesRuntime;

/// <summary>
/// S5 API-34 (SAT-D236 (1), SAT-D248): lays the sales invoice export out as one Excel workbook (.xlsx) for the Accounting
/// lane. It writes the service's answer verbatim and computes nothing: amounts, VAT, withholding and the instalment plan are
/// the invoice's own recorded figures. One row per invoice line; the invoice's fields repeat on each of its lines, its
/// orders are named together in one cell (every order of a multi-order invoice, SAT-D248) and each line also names its own
/// order. Beside the computed TL VAT and withheld VAT the official document's own VAT and withholding TL amounts, as the
/// API-35 match recorded them (SAT-D241), have their own columns (Issue #66 comment 6771235622 / EK55 (pending)); they are
/// empty when no official amounts are recorded. The column set is the SAT-D248 wide set; its names, order and layout are not decided and stay in
/// <c>Resources/SalesRuntime.SalesInvoiceExportWorkbookComposer.resx</c> (a missing text is a server fault, never a key in
/// its place). Built with the BCL only (no spreadsheet package): every text is an inline string (never a formula), an amount
/// is a numeric cell only when Excel's double can hold every digit (otherwise the exact text), dates are ISO text, and the
/// archive is deterministic (fixed entry times, fixed entry order): the same answer gives the same bytes.
/// </summary>
public sealed class SalesInvoiceExportWorkbookComposer(IStringLocalizer<SalesInvoiceExportWorkbookComposer> texts)
{
    private const string DateFormat = "yyyy-MM-dd";
    // Excel keeps 15 significant decimal digits; a longer amount stays exact as text.
    private const int MaximumNumericDigits = 15;
    private static readonly DateTimeOffset EntryTime = new(1980, 1, 1, 0, 0, 0, TimeSpan.Zero);
    private static readonly UTF8Encoding Utf8 = new(encoderShouldEmitUTF8Identifier: false);

    private const string SpreadsheetNamespace = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
    private const string RelationshipNamespace = "http://schemas.openxmlformats.org/officeDocument/2006/relationships";

    private static readonly string[] Columns =
    [
        "ColumnDocumentNo", "ColumnOfficialNo", "ColumnEttn", "ColumnFinalizedOn", "ColumnBusinessDate", "ColumnPartyId",
        "ColumnCompanyAccountId", "ColumnCurrencyId", "ColumnRate", "ColumnRateDay",
        "ColumnInvoiceNet", "ColumnInvoiceVat", "ColumnInvoiceWithheld", "ColumnInvoiceGross", "ColumnInvoicePayable",
        "ColumnInvoiceNetFunctional", "ColumnInvoiceVatFunctional", "ColumnOfficialVatFunctional", "ColumnInvoiceWithheldFunctional",
        "ColumnOfficialWithheldFunctional", "ColumnInvoiceGrossFunctional", "ColumnInvoicePayableFunctional",
        "ColumnInstalments", "ColumnSalesOrderNos",
        "ColumnLineNo", "ColumnDispatchNoteNumber", "ColumnNoteLineNo", "ColumnShipmentDate", "ColumnLineSalesOrderNo",
        "ColumnProductId", "ColumnUnitId", "ColumnQuantity", "ColumnNetUnitPrice", "ColumnDiscountPercent",
        "ColumnDiscountAmount", "ColumnSurchargePercent", "ColumnSurchargeAmount", "ColumnVatPercent", "ColumnWithholdingShare",
        "ColumnLineNet", "ColumnLineVat", "ColumnLineWithheld", "ColumnLineGross", "ColumnLinePayable",
        "ColumnLineNetFunctional", "ColumnLineVatFunctional", "ColumnLineWithheldFunctional", "ColumnLineGrossFunctional",
        "ColumnLinePayableFunctional",
    ];

    /// <summary>The number of columns of one row (the SAT-D248 wide set).</summary>
    public static int ColumnCount => Columns.Length;

    /// <summary>The workbook bytes and the attachment's file name.</summary>
    public (byte[] Content, string FileName) Compose(SalesInvoiceExportV1 export)
    {
        ArgumentNullException.ThrowIfNull(export);
        if (!string.Equals(export.Format, SalesInvoiceApiV1.ExportFormatXlsx, StringComparison.Ordinal))
        {
            throw new InvalidOperationException("The export answer is not a workbook.");
        }

        var rows = new List<IReadOnlyList<Cell>> { Columns.Select(key => Cell.Text(T(key))).ToArray() };
        foreach (var invoice in export.Invoices)
        {
            var invoiceCells = InvoiceCells(invoice);
            if (invoice.Lines.Count == 0)
            {
                throw new InvalidOperationException("An exported invoice has no lines.");
            }

            foreach (var line in invoice.Lines)
            {
                rows.Add([.. invoiceCells, .. LineCells(line)]);
            }
        }

        var fileName = Format("FileName", export.From.ToString(DateFormat, CultureInfo.InvariantCulture),
            export.To.ToString(DateFormat, CultureInfo.InvariantCulture));
        return (Package(T("SheetName"), rows), fileName);
    }

    private List<Cell> InvoiceCells(SalesInvoiceExportInvoiceV1 invoice)
    {
        var cells = new List<Cell>
        {
            Cell.Text(invoice.DocumentNo), Cell.Text(invoice.OfficialNo), Cell.Text(invoice.Ettn),
            Cell.Date(invoice.FinalizedOn), Cell.Date(invoice.BusinessDate), Cell.Id(invoice.PartyId),
            Cell.Id(invoice.CompanyAccountId), Cell.Id(invoice.CurrencyId), Cell.Number(invoice.Rate?.Rate),
            invoice.Rate is { } rate ? Cell.Date(rate.RateDay) : Cell.Empty,
        };
        AddAmounts(cells, invoice.Totals);
        // Issue #66 comment 6771235622 / EK55 (pending): the official document's VAT and withholding (TL, SAT-D241) sit beside the
        // computed TL VAT and withheld VAT as their own columns; empty when the service answers none.
        var functional = invoice.FunctionalTotals;
        cells.Add(Cell.Number(functional?.NetAmount));
        cells.Add(Cell.Number(functional?.VatAmount));
        cells.Add(Cell.Number(invoice.OfficialVatFunctionalAmount));
        cells.Add(Cell.Number(functional?.WithheldVatAmount));
        cells.Add(Cell.Number(invoice.OfficialWithholdingFunctionalAmount));
        cells.Add(Cell.Number(functional?.GrossAmount));
        cells.Add(Cell.Number(functional?.PayableAmount));
        cells.Add(Cell.Text(string.Join(T("ListSeparator"), invoice.Instalments.OrderBy(item => item.SequenceNo).Select(Instalment))));
        cells.Add(Cell.Text(string.Join(T("OrderSeparator"), invoice.SalesOrderNos)));
        return cells;
    }

    private static List<Cell> LineCells(SalesInvoiceExportLineV1 line)
    {
        var cells = new List<Cell>
        {
            Cell.Integer(line.LineNo), Cell.Text(line.DispatchNoteNumber), Cell.Integer(line.NoteLineNo), Cell.Date(line.ShipmentDate),
            Cell.Text(line.SalesOrderNo), Cell.Id(line.ProductId), Cell.Id(line.UnitId), Cell.Number(line.Quantity),
            Cell.Number(line.NetUnitPrice), Cell.Number(line.DiscountPercent), Cell.Number(line.DiscountAmount),
            Cell.Number(line.SurchargePercent), Cell.Number(line.SurchargeAmount), Cell.Number(line.VatPercent),
            Cell.Number(line.WithholdingShare),
        };
        AddAmounts(cells, line.Amounts);
        AddAmounts(cells, line.FunctionalAmounts);
        return cells;
    }

    private static void AddAmounts(List<Cell> cells, SalesInvoiceAmountsView? amounts)
    {
        cells.Add(Cell.Number(amounts?.NetAmount));
        cells.Add(Cell.Number(amounts?.VatAmount));
        cells.Add(Cell.Number(amounts?.WithheldVatAmount));
        cells.Add(Cell.Number(amounts?.GrossAmount));
        cells.Add(Cell.Number(amounts?.PayableAmount));
    }

    private string Instalment(SalesInvoiceFinalInstalmentView item)
    {
        var due = item.DueDate.ToString(DateFormat, CultureInfo.InvariantCulture);
        return item.FunctionalAmount is { } functional
            ? Format("InstalmentItemFunctional", item.SequenceNo, due, item.Amount, functional)
            : Format("InstalmentItem", item.SequenceNo, due, item.Amount);
    }

    private static byte[] Package(string sheetName, IReadOnlyList<IReadOnlyList<Cell>> rows)
    {
        using var stream = new MemoryStream();
        using (var archive = new ZipArchive(stream, ZipArchiveMode.Create, leaveOpen: true))
        {
            Entry(archive, "[Content_Types].xml", ContentTypes);
            Entry(archive, "_rels/.rels", RootRelationships);
            Entry(archive, "xl/workbook.xml", writer => Workbook(writer, sheetName));
            Entry(archive, "xl/_rels/workbook.xml.rels", WorkbookRelationships);
            Entry(archive, "xl/worksheets/sheet1.xml", writer => Sheet(writer, rows));
        }

        return stream.ToArray();
    }

    private static void Entry(ZipArchive archive, string name, Action<XmlWriter> write)
    {
        var entry = archive.CreateEntry(name, CompressionLevel.Optimal);
        entry.LastWriteTime = EntryTime;
        using var output = entry.Open();
        // CheckCharacters: a text Excel cannot hold (a control character) is a fault, never a broken file.
        using var writer = XmlWriter.Create(output, new XmlWriterSettings { Encoding = Utf8, CheckCharacters = true });
        writer.WriteStartDocument(standalone: true);
        write(writer);
        writer.WriteEndDocument();
    }

    private static void ContentTypes(XmlWriter writer)
    {
        const string ns = "http://schemas.openxmlformats.org/package/2006/content-types";
        writer.WriteStartElement("Types", ns);
        writer.WriteStartElement("Default", ns);
        writer.WriteAttributeString("Extension", "rels");
        writer.WriteAttributeString("ContentType", "application/vnd.openxmlformats-package.relationships+xml");
        writer.WriteEndElement();
        writer.WriteStartElement("Default", ns);
        writer.WriteAttributeString("Extension", "xml");
        writer.WriteAttributeString("ContentType", "application/xml");
        writer.WriteEndElement();
        Override(writer, ns, "/xl/workbook.xml", "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet.main+xml");
        Override(writer, ns, "/xl/worksheets/sheet1.xml", "application/vnd.openxmlformats-officedocument.spreadsheetml.worksheet+xml");
        writer.WriteEndElement();
    }

    private static void Override(XmlWriter writer, string ns, string part, string contentType)
    {
        writer.WriteStartElement("Override", ns);
        writer.WriteAttributeString("PartName", part);
        writer.WriteAttributeString("ContentType", contentType);
        writer.WriteEndElement();
    }

    private static void RootRelationships(XmlWriter writer) => Relationships(writer, "rId1",
        "http://schemas.openxmlformats.org/officeDocument/2006/relationships/officeDocument", "xl/workbook.xml");

    private static void WorkbookRelationships(XmlWriter writer) => Relationships(writer, "rId1",
        "http://schemas.openxmlformats.org/officeDocument/2006/relationships/worksheet", "worksheets/sheet1.xml");

    private static void Relationships(XmlWriter writer, string id, string type, string target)
    {
        const string ns = "http://schemas.openxmlformats.org/package/2006/relationships";
        writer.WriteStartElement("Relationships", ns);
        writer.WriteStartElement("Relationship", ns);
        writer.WriteAttributeString("Id", id);
        writer.WriteAttributeString("Type", type);
        writer.WriteAttributeString("Target", target);
        writer.WriteEndElement();
        writer.WriteEndElement();
    }

    private static void Workbook(XmlWriter writer, string sheetName)
    {
        writer.WriteStartElement("workbook", SpreadsheetNamespace);
        writer.WriteAttributeString("xmlns", "r", null, RelationshipNamespace);
        writer.WriteStartElement("sheets", SpreadsheetNamespace);
        writer.WriteStartElement("sheet", SpreadsheetNamespace);
        writer.WriteAttributeString("name", sheetName);
        writer.WriteAttributeString("sheetId", "1");
        writer.WriteAttributeString("id", RelationshipNamespace, "rId1");
        writer.WriteEndElement();
        writer.WriteEndElement();
        writer.WriteEndElement();
    }

    private static void Sheet(XmlWriter writer, IReadOnlyList<IReadOnlyList<Cell>> rows)
    {
        writer.WriteStartElement("worksheet", SpreadsheetNamespace);
        writer.WriteStartElement("sheetData", SpreadsheetNamespace);
        for (var rowIndex = 0; rowIndex < rows.Count; rowIndex++)
        {
            var rowNumber = (rowIndex + 1).ToString(CultureInfo.InvariantCulture);
            writer.WriteStartElement("row", SpreadsheetNamespace);
            writer.WriteAttributeString("r", rowNumber);
            var row = rows[rowIndex];
            for (var columnIndex = 0; columnIndex < row.Count; columnIndex++)
            {
                var cell = row[columnIndex];
                if (cell.Value is null)
                {
                    continue;
                }

                writer.WriteStartElement("c", SpreadsheetNamespace);
                writer.WriteAttributeString("r", ColumnName(columnIndex) + rowNumber);
                if (cell.Numeric)
                {
                    writer.WriteElementString("v", SpreadsheetNamespace, cell.Value);
                }
                else
                {
                    writer.WriteAttributeString("t", "inlineStr");
                    writer.WriteStartElement("is", SpreadsheetNamespace);
                    writer.WriteStartElement("t", SpreadsheetNamespace);
                    writer.WriteAttributeString("xml", "space", null, "preserve");
                    writer.WriteString(cell.Value);
                    writer.WriteEndElement();
                    writer.WriteEndElement();
                }

                writer.WriteEndElement();
            }

            writer.WriteEndElement();
        }

        writer.WriteEndElement();
        writer.WriteEndElement();
    }

    /// <summary>The A1 column letters of a zero-based column index.</summary>
    public static string ColumnName(int index)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(index);
        var name = string.Empty;
        for (var number = index + 1; number > 0; number = (number - 1) / 26)
        {
            name = (char)('A' + ((number - 1) % 26)) + name;
        }

        return name;
    }

    private string Format(string key, params object[] values) => string.Format(CultureInfo.InvariantCulture, T(key), values);

    private string T(string key)
    {
        var text = texts[key];
        return text.ResourceNotFound || string.IsNullOrEmpty(text.Value)
            ? throw new InvalidOperationException($"The sales invoice export text '{key}' is missing.")
            : text.Value;
    }

    private readonly record struct Cell(string? Value, bool Numeric)
    {
        public static Cell Empty => new(null, false);

        public static Cell Text(string? value) => new(string.IsNullOrEmpty(value) ? null : value, false);

        public static Cell Id(Guid value) => new(value.ToString("D", CultureInfo.InvariantCulture), false);

        public static Cell Date(DateOnly value) => new(value.ToString(DateFormat, CultureInfo.InvariantCulture), false);

        public static Cell Integer(int value) => new(value.ToString(CultureInfo.InvariantCulture), true);

        /// <summary>A decimal text as a number cell when every digit fits Excel's double; otherwise the exact text.</summary>
        public static Cell Number(string? value)
        {
            if (string.IsNullOrEmpty(value))
            {
                return Empty;
            }

            return IsPlainDecimal(value) && SignificantDigits(value) <= MaximumNumericDigits ? new(value, true) : new(value, false);
        }

        private static bool IsPlainDecimal(string value)
        {
            var start = value[0] == '-' ? 1 : 0;
            var point = value.IndexOf('.', StringComparison.Ordinal);
            var integerEnd = point < 0 ? value.Length : point;
            if (integerEnd <= start || (point >= 0 && point == value.Length - 1))
            {
                return false;
            }

            for (var index = start; index < value.Length; index++)
            {
                if (index != point && !char.IsAsciiDigit(value[index]))
                {
                    return false;
                }
            }

            return true;
        }

        private static int SignificantDigits(string value) =>
            value.Where(char.IsAsciiDigit).SkipWhile(digit => digit == '0').Count();
    }
}
