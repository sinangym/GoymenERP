using System.Globalization;
using System.Text;
using GoymenERP.Modules.Purchasing.Application.Contracts;

namespace GoymenERP.Modules.Purchasing.Infrastructure.Imports;

public sealed partial class PurchaseInvoiceFileReader
{
    private static readonly string[] RequiredTableFields = ["Number", "Ettn", "InvoiceDate", "InvoiceType", "Currency",
        "SupplierTaxId", "SupplierTaxScheme", "BuyerTaxId", "BuyerTaxScheme", "LineId", "Description", "UnitCode", "Quantity",
        "LineNet", "LineExtensionAmount", "TaxExclusiveAmount", "TaxInclusiveAmount", "PayableAmount"];
    private static readonly string[] OptionalTableFields = ["SellerCode", "BuyerCode", "AllowanceTotalAmount", "ChargeTotalAmount",
        "PrepaidAmount", "PayableRoundingAmount", "LineTaxCode", "LineTaxPercent", "LineTaxAmount", "LineTaxableAmount",
        "LineWithholdingCode", "LineWithholdingPercent", "LineWithholdingAmount", "LineWithholdingTaxableAmount",
        "TaxCode", "TaxPercent", "TaxAmount", "TaxableAmount", "WithholdingCode", "WithholdingPercent", "WithholdingAmount", "WithholdingTaxableAmount",
        "OfficialRate", "RateSourceCurrency", "RateTargetCurrency", "RateDate", "PaymentDueDate", "PaymentAmount"];

    /// <summary>Each row is one source invoice line; explicitly mapped header facts must repeat identically.
    /// The map describes an export's fields. It is not a vendor adapter or permission to invent missing facts.</summary>
    private static PurchaseInvoiceFileFactsV1 ReadTable(IReadOnlyList<IReadOnlyList<string>> rows, PurchaseInvoiceFileReadOptionsV1 options)
    {
        if (options.DecimalSeparator is not ("." or ",") || options.DateFormat is not ("yyyy-MM-dd" or "dd.MM.yyyy"))
            throw Invalid("mapping", "format_mapping_invalid");
        if (rows.Count is < 2 or > PurchaseInvoiceFileBoundsV1.MaximumRows + 1) throw Invalid("rows", "row_limit");
        var map = options.Columns;
        if (map is null || RequiredTableFields.Any(field => !map.ContainsKey(field))
            || map.Keys.Any(field => !RequiredTableFields.Contains(field) && !OptionalTableFields.Contains(field))
            || map.Values.Any(string.IsNullOrWhiteSpace) || map.Values.Distinct(StringComparer.Ordinal).Count() != map.Count)
            throw Invalid("mapping", "column_mapping_invalid");
        var headers = rows[0];
        if (headers.Count > PurchaseInvoiceFileBoundsV1.MaximumColumns || headers.Any(string.IsNullOrWhiteSpace)
            || headers.Distinct(StringComparer.Ordinal).Count() != headers.Count) throw Invalid("header", "header_invalid");
        var indexes = headers.Select((header, index) => (header, index)).ToDictionary(item => item.header, item => item.index, StringComparer.Ordinal);
        if (map.Values.Any(header => !indexes.ContainsKey(header))) throw Invalid("mapping", "column_missing");
        string? Read(IReadOnlyList<string> row, string field) => map.TryGetValue(field, out var header)
            ? string.IsNullOrWhiteSpace(row[indexes[header]]) ? null : Bounded(row[indexes[header]]) : null;
        string Required(IReadOnlyList<string> row, string field) => Read(row, field) ?? throw Invalid(field, "field_required");
        string Scalar(string value)
        {
            if (value.Contains(options.DecimalSeparator == "," ? '.' : ',')) throw Invalid("amount", "decimal_separator_mismatch");
            return Number(options.DecimalSeparator == "," ? value.Replace(',', '.') : value);
        }
        var first = rows[1];
        if (rows.Skip(1).Any(row => row.Count != headers.Count)) throw Invalid("rows", "row_shape_invalid");
        var currency = Currency(Required(first, "Currency"));
        PurchaseInvoiceFileAmountV1? Money(IReadOnlyList<string> row, string field) => Read(row, field) is { } value ? new(currency, Scalar(value)) : null;
        var headerFields = map.Keys.Where(field => !field.StartsWith("Line", StringComparison.Ordinal) || field == "LineExtensionAmount")
            .Except(["SellerCode", "BuyerCode", "Description", "UnitCode", "Quantity"], StringComparer.Ordinal).ToArray();
        foreach (var row in rows.Skip(2))
            if (headerFields.Any(field => Read(row, field) != Read(first, field))) throw Invalid("header", "multiple_invoice_headers");

        IReadOnlyList<PurchaseInvoiceFileTaxV1> RowTaxes(IReadOnlyList<string> row, bool line)
        {
            var result = new List<PurchaseInvoiceFileTaxV1>();
            foreach (var withholding in new[] { false, true })
            {
                var prefix = (line ? "Line" : "") + (withholding ? "Withholding" : "Tax");
                var amount = Money(row, prefix + "Amount");
                var code = Read(row, prefix + "Code"); var percent = Read(row, prefix + "Percent");
                var basis = Money(row, (line ? "Line" : "") + (withholding ? "WithholdingTaxableAmount" : "TaxableAmount"));
                if (amount is null)
                {
                    if (code is not null || percent is not null || basis is not null) throw Invalid(prefix, "tax_amount_required");
                    continue;
                }
                result.Add(new(code, percent is null ? null : Scalar(percent), amount, basis, withholding));
            }
            return result;
        }
        var rates = new List<PurchaseInvoiceFileRateV1>();
        if (Read(first, "OfficialRate") is { } rate)
            rates.Add(new("OfficialInvoiceRate", Currency(Required(first, "RateSourceCurrency")), Currency(Required(first, "RateTargetCurrency")),
                Scalar(rate), null, null, null, Read(first, "RateDate") is { } date ? Date(date, options.DateFormat) : null));
        else if (new[] { "RateSourceCurrency", "RateTargetCurrency", "RateDate" }.Any(field => Read(first, field) is not null))
            throw Invalid("OfficialRate", "rate_required");
        var dueOn = Read(first, "PaymentDueDate"); var paymentAmount = Money(first, "PaymentAmount");
        return new(Required(first, "Number"), Identifier(Required(first, "Ettn")), Date(Required(first, "InvoiceDate"), options.DateFormat),
            Required(first, "InvoiceType"), currency,
            [new(Required(first, "SupplierTaxScheme"), Required(first, "SupplierTaxId"))],
            [new(Required(first, "BuyerTaxScheme"), Required(first, "BuyerTaxId"))],
            TotalNames.Where(name => Money(first, name) is not null).ToDictionary(name => name, name => Money(first, name)!, StringComparer.Ordinal),
            RowTaxes(first, false), [], rates,
            dueOn is null && paymentAmount is null ? [] : [new(dueOn is null ? null : Date(dueOn, options.DateFormat), paymentAmount, Source: "MappedExport")],
            rows.Skip(1).Select(row => new PurchaseInvoiceFileLineV1(Required(row, "LineId"), Read(row, "SellerCode"), Read(row, "BuyerCode"),
                Required(row, "Description"), Required(row, "UnitCode"), Scalar(Required(row, "Quantity")),
                Money(row, "LineNet") ?? throw Invalid("LineNet", "field_required"), RowTaxes(row, true), [])).ToArray());
    }

    private static IReadOnlyList<IReadOnlyList<string>> ReadCsv(byte[] bytes, char delimiter)
    {
        if (delimiter is not (',' or ';' or '\t')) throw Invalid("delimiter", "delimiter_invalid");
        var text = new UTF8Encoding(false, true).GetString(bytes);
        if (text.StartsWith('\uFEFF')) text = text[1..];
        var rows = new List<IReadOnlyList<string>>(); var row = new List<string>(); var cell = new StringBuilder();
        var quoted = false; var closed = false;
        void Cell()
        {
            if (row.Count == PurchaseInvoiceFileBoundsV1.MaximumColumns) throw Invalid("rows", "column_limit");
            row.Add(cell.ToString()); cell.Clear(); closed = false;
        }
        void Row()
        {
            Cell();
            if (rows.Count == PurchaseInvoiceFileBoundsV1.MaximumRows + 1) throw Invalid("rows", "row_limit");
            rows.Add(row.ToArray()); row.Clear();
        }
        for (var i = 0; i < text.Length; i++)
        {
            var c = text[i];
            if (quoted)
            {
                if (c == '"')
                {
                    if (i + 1 < text.Length && text[i + 1] == '"') { cell.Append('"'); i++; }
                    else { quoted = false; closed = true; }
                }
                else if (char.IsControl(c)) throw Invalid("file", "control_character");
                else cell.Append(c);
            }
            else if (c == delimiter) Cell();
            else if (c is '\r' or '\n')
            {
                if (c == '\r' && i + 1 < text.Length && text[i + 1] == '\n') i++;
                Row();
            }
            else if (closed) throw Invalid("file", "csv_quote_invalid");
            else if (c == '"') { if (cell.Length != 0) throw Invalid("file", "csv_quote_invalid"); quoted = true; }
            else if (char.IsControl(c)) throw Invalid("file", "control_character");
            else cell.Append(c);
            if (cell.Length > PurchaseInvoiceFileBoundsV1.MaximumCellCharacters) throw Invalid("file", "cell_limit");
        }
        if (quoted) throw Invalid("file", "csv_quote_invalid");
        if (cell.Length > 0 || closed || row.Count > 0) Row();
        return rows;
    }
}
