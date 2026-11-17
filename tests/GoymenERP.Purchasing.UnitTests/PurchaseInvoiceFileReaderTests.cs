using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Xml.Linq;
using GoymenERP.Modules.Purchasing.Application.Contracts;
using GoymenERP.Modules.Purchasing.Infrastructure.Imports;

namespace GoymenERP.Purchasing.UnitTests;

public sealed class PurchaseInvoiceFileReaderTests
{
    private readonly PurchaseInvoiceFileReader _reader = new();
    private static readonly XNamespace Cbc = "urn:oasis:names:specification:ubl:schema:xsd:CommonBasicComponents-2";
    private static readonly XNamespace Cac = "urn:oasis:names:specification:ubl:schema:xsd:CommonAggregateComponents-2";
    private static readonly XNamespace Sheet = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
    private const string Ettn = "aaaaaaaa-aaaa-4aaa-8aaa-aaaaaaaaaaaa";
    private static readonly string[] Columns = ["Number", "Ettn", "InvoiceDate", "InvoiceType", "Currency", "SupplierTaxId",
        "SupplierTaxScheme", "BuyerTaxId", "BuyerTaxScheme", "LineId", "Description", "UnitCode", "Quantity", "LineNet",
        "LineExtensionAmount", "TaxExclusiveAmount", "TaxInclusiveAmount", "PayableAmount", "LineTaxAmount", "LineTaxCode", "TaxAmount", "TaxCode"];
    private static readonly string[] Values = ["INV2026000000001", Ettn, "2026-11-17", "SATIS", "TRY", "0123456789", "VKN",
        "9876543210", "VKN", "001", "Sentetik ürün", "KGM", "2.500", "100.00", "100.00", "100.00", "120.00", "120.00",
        "20.00", "0015", "20.00", "0015"];
    private static readonly IReadOnlyDictionary<string, string> Mapping = Columns.ToDictionary(value => value, value => value, StringComparer.Ordinal);

    [Fact]
    public void Ubl_keeps_source_hash_exact_quantities_separate_rate_purposes_and_header_adjustments()
    {
        var root = Invoice();
        root.Add(new XElement(Cac + "TaxExchangeRate", new XElement(Cbc + "SourceCurrencyCode", "USD"),
            new XElement(Cbc + "TargetCurrencyCode", "TRY"), new XElement(Cbc + "CalculationRate", "40.12345678")));
        root.Add(new XElement(Cac + "PaymentExchangeRate", new XElement(Cbc + "SourceCurrencyCode", "USD"),
            new XElement(Cbc + "TargetCurrencyCode", "EUR"), new XElement(Cbc + "CalculationRate", "0.92")));
        root.Add(new XElement(Cac + "AllowanceCharge", new XElement(Cbc + "ChargeIndicator", "false"), Money("Amount", "1.25")));
        root.Element(Cac + "LegalMonetaryTotal")!.Add(Money("PayableRoundingAmount", "-0.01"));
        root.Add(new XElement(Cbc + "DueDate", "2026-12-17"));
        var bytes = Bytes(root);
        var answer = _reader.Read(bytes, new(PurchaseInvoiceFileFormatV1.UblXml));
        Assert.Empty(answer.Issues);
        Assert.Equal(Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant(), answer.Sha256);
        var invoice = Assert.IsType<PurchaseInvoiceFileFactsV1>(answer.Invoice);
        Assert.Equal(Guid.Parse(Ettn), invoice.Ettn);
        Assert.Equal("2.5", Assert.Single(invoice.Lines).Quantity);
        Assert.Equal("0123456789", Assert.Single(invoice.SupplierIdentities).Value);
        Assert.Equal(new[] { "TaxExchangeRate", "PaymentExchangeRate" }, invoice.Rates.Select(rate => rate.Purpose));
        Assert.Equal("40.12345678", invoice.Rates[0].Rate);
        Assert.Equal("EUR", invoice.Rates[1].TargetCurrency);
        Assert.False(Assert.Single(invoice.Adjustments).IsCharge);
        Assert.Equal("-0.01", invoice.Totals["PayableRoundingAmount"].Value);
        Assert.Equal(new DateOnly(2026, 12, 17), Assert.Single(invoice.Payments).DueOn);
        // These deliberately unreconciled totals are source facts, not an accepted accounting document.
        Assert.Equal("120", invoice.Totals["PayableAmount"].Value);
    }

    [Fact]
    public void Ubl_preserves_other_tax_codes_and_does_not_relabel_them_as_vat()
    {
        var root = Invoice();
        root.Element(Cac + "TaxTotal")!.Descendants(Cbc + "TaxTypeCode").Single().Value = "CUSTOM";
        var result = _reader.Read(Bytes(root), new(PurchaseInvoiceFileFormatV1.UblXml));
        Assert.Empty(result.Issues);
        Assert.Equal("CUSTOM", Assert.Single(result.Invoice!.Taxes).Code);
    }

    [Fact]
    public void Ubl_refuses_dtd_duplicate_identity_wrong_namespace_nested_scalar_and_tax_mismatch()
    {
        var cases = new List<byte[]> { Encoding.UTF8.GetBytes("<!DOCTYPE Invoice [<!ENTITY secret SYSTEM 'file:///unavailable'>]><Invoice>&secret;</Invoice>") };
        var duplicate = Invoice(); duplicate.Add(new XElement(Cbc + "UUID", Guid.NewGuid())); cases.Add(Bytes(duplicate));
        var namespaceSpoof = Invoice(); namespaceSpoof.Name = "Invoice"; cases.Add(Bytes(namespaceSpoof));
        var scalar = Invoice(); scalar.Element(Cbc + "ID")!.Add(new XElement("hidden", "changed")); cases.Add(Bytes(scalar));
        var tax = Invoice(); tax.Element(Cac + "TaxTotal")!.Element(Cbc + "TaxAmount")!.Value = "21"; cases.Add(Bytes(tax));
        foreach (var bytes in cases)
        {
            var result = _reader.Read(bytes, new(PurchaseInvoiceFileFormatV1.UblXml));
            Assert.Null(result.Invoice); Assert.NotEmpty(result.Issues);
        }
    }

    [Fact]
    public void Decimal_source_precision_is_never_rounded_by_the_parser()
    {
        var root = Invoice(); root.Element(Cac + "InvoiceLine")!.Element(Cbc + "InvoicedQuantity")!.Value = "1.123456789012345678901234567890123";
        var result = _reader.Read(Bytes(root), new(PurchaseInvoiceFileFormatV1.UblXml));
        Assert.Null(result.Invoice); Assert.Contains(result.Issues, item => item.Code == "decimal_precision_loss");
    }

    [Theory]
    [InlineData(PurchaseInvoiceFileFormatV1.Csv)]
    [InlineData(PurchaseInvoiceFileFormatV1.Xlsx)]
    public void Explicit_mapped_exports_keep_leading_zero_identifiers_and_exact_source_line_values(PurchaseInvoiceFileFormatV1 format)
    {
        var bytes = format == PurchaseInvoiceFileFormatV1.Csv ? Csv(Values) : Workbook(Values);
        var result = _reader.Read(bytes, new(format, Mapping));
        Assert.Empty(result.Issues);
        Assert.Equal("0123456789", Assert.Single(result.Invoice!.SupplierIdentities).Value);
        var line = Assert.Single(result.Invoice.Lines);
        Assert.Equal("001", line.Id); Assert.Equal("2.5", line.Quantity); Assert.Equal("100", line.Net.Value);
        Assert.Equal("20", Assert.Single(line.Taxes).Amount.Value);
        Assert.Empty(result.Invoice.Rates); Assert.Empty(result.Invoice.Payments);
    }

    [Fact]
    public void Csv_requires_explicit_locale_and_rejects_mixed_invoices_and_incomplete_mapping()
    {
        var localized = Values.ToArray(); localized[2] = "17.11.2026"; localized[12] = "2,5";
        foreach (var index in new[] { 13, 14, 15, 16, 17, 18, 20 }) localized[index] = localized[index].Replace('.', ',');
        Assert.NotEmpty(_reader.Read(Csv(localized), new(PurchaseInvoiceFileFormatV1.Csv, Mapping)).Issues);
        Assert.Empty(_reader.Read(Csv(localized), new(PurchaseInvoiceFileFormatV1.Csv, Mapping,
            DecimalSeparator: ",", DateFormat: "dd.MM.yyyy")).Issues);
        Assert.NotEmpty(_reader.Read(Csv(Values), new(PurchaseInvoiceFileFormatV1.Csv)).Issues);
        var second = Values.ToArray(); second[0] = "ANOTHER"; second[9] = "002";
        var mixed = Encoding.UTF8.GetBytes(string.Join(';', Columns) + "\n" + string.Join(';', Values) + "\n" + string.Join(';', second));
        Assert.Contains(_reader.Read(mixed, new(PurchaseInvoiceFileFormatV1.Csv, Mapping)).Issues, issue => issue.Code == "multiple_invoice_headers");
    }

    [Fact]
    public void Csv_accepts_escaped_delimiters_and_refuses_unclosed_quotes_or_partial_rows()
    {
        var row = Values.ToArray(); row[10] = "\"Sentetik; \"\"ürün\"\"\"";
        var good = _reader.Read(Csv(row), new(PurchaseInvoiceFileFormatV1.Csv, Mapping));
        Assert.Empty(good.Issues); Assert.Equal("Sentetik; \"ürün\"", good.Invoice!.Lines[0].Description);
        row[10] = "\"unfinished";
        Assert.Null(_reader.Read(Csv(row), new(PurchaseInvoiceFileFormatV1.Csv, Mapping)).Invoice);
        Assert.Null(_reader.Read(Csv(Values[..^1]), new(PurchaseInvoiceFileFormatV1.Csv, Mapping)).Invoice);
    }

    [Theory]
    [InlineData("formula")]
    [InlineData("external")]
    [InlineData("macro")]
    [InlineData("numeric-identity")]
    [InlineData("duplicate-part")]
    [InlineData("mixed-text")]
    [InlineData("numeric-line-id")]
    public void Workbook_refuses_active_parts_formulas_and_lossy_identity_cells(string defect)
    {
        var result = _reader.Read(Workbook(Values, defect), new(PurchaseInvoiceFileFormatV1.Xlsx, Mapping));
        Assert.Null(result.Invoice); Assert.NotEmpty(result.Issues);
    }

    private static byte[] Csv(string[] row) => Encoding.UTF8.GetBytes(string.Join(';', Columns) + "\r\n" + string.Join(';', row) + "\r\n");
    private static byte[] Bytes(XElement value) => Encoding.UTF8.GetBytes(value.ToString(SaveOptions.DisableFormatting));
    private static XElement Money(string name, string value) => new(Cbc + name, new XAttribute("currencyID", "TRY"), value);
    private static XElement Tax() => new(Cac + "TaxTotal", Money("TaxAmount", "20"), new XElement(Cac + "TaxSubtotal",
        Money("TaxableAmount", "100"), Money("TaxAmount", "20"), new XElement(Cbc + "Percent", "20"),
        new XElement(Cac + "TaxCategory", new XElement(Cac + "TaxScheme", new XElement(Cbc + "TaxTypeCode", "0015")))));
    private static XElement Party(string name, string id) => new(Cac + name, new XElement(Cac + "Party", new XElement(Cac + "PartyIdentification",
        new XElement(Cbc + "ID", new XAttribute("schemeID", "VKN"), id))));
    private static XElement Invoice() => new(XName.Get("Invoice", "urn:oasis:names:specification:ubl:schema:xsd:Invoice-2"),
        new XAttribute(XNamespace.Xmlns + "cbc", Cbc), new XAttribute(XNamespace.Xmlns + "cac", Cac),
        new XElement(Cbc + "UBLVersionID", "2.1"), new XElement(Cbc + "ID", Values[0]), new XElement(Cbc + "UUID", Ettn),
        new XElement(Cbc + "IssueDate", "2026-11-17"), new XElement(Cbc + "InvoiceTypeCode", "SATIS"), new XElement(Cbc + "DocumentCurrencyCode", "TRY"),
        Party("AccountingSupplierParty", "0123456789"), Party("AccountingCustomerParty", "9876543210"), Tax(),
        new XElement(Cac + "LegalMonetaryTotal", Money("LineExtensionAmount", "100"), Money("TaxExclusiveAmount", "100"),
            Money("TaxInclusiveAmount", "120"), Money("PayableAmount", "120")),
        new XElement(Cac + "InvoiceLine", new XElement(Cbc + "ID", "001"),
            new XElement(Cbc + "InvoicedQuantity", new XAttribute("unitCode", "KGM"), "2.500"), Money("LineExtensionAmount", "100"), Tax(),
            new XElement(Cac + "Item", new XElement(Cbc + "Name", "Sentetik ürün"))));
    private static byte[] Workbook(string[] values, string? defect = null)
    {
        using var buffer = new MemoryStream();
        using (var archive = new ZipArchive(buffer, ZipArchiveMode.Create, leaveOpen: true))
        {
            void Part(string name, string value) { using var writer = new StreamWriter(archive.CreateEntry(name).Open(), new UTF8Encoding(false)); writer.Write(value); }
            Part("[Content_Types].xml", "<Types xmlns=\"http://schemas.openxmlformats.org/package/2006/content-types\" />");
            Part("xl/workbook.xml", "<workbook xmlns=\"http://schemas.openxmlformats.org/spreadsheetml/2006/main\" xmlns:r=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships\"><sheets><sheet name=\"Fatura\" sheetId=\"1\" r:id=\"r1\"/></sheets></workbook>");
            Part("xl/_rels/workbook.xml.rels", "<Relationships xmlns=\"http://schemas.openxmlformats.org/package/2006/relationships\"><Relationship Id=\"r1\" Type=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships/worksheet\" Target=\"worksheets/sheet1.xml\"" + (defect == "external" ? " TargetMode=\"External\"" : "") + "/></Relationships>");
            XElement Row(int number, string[] cells) => new(Sheet + "row", new XAttribute("r", number), cells.Select((text, i) =>
            {
                var cell = new XElement(Sheet + "c", new XAttribute("r", $"{(char)('A' + i)}{number}"),
                    new XAttribute("t", "inlineStr"), new XElement(Sheet + "is", new XElement(Sheet + "t", text)));
                if (number == 2 && i == 5 && defect == "numeric-identity") { cell.SetAttributeValue("t", "n"); cell.RemoveNodes(); cell.Add(new XElement(Sheet + "v", "123456789")); }
                if (number == 2 && i == 12 && defect == "formula") cell.Add(new XElement(Sheet + "f", "1+1"));
                if (number == 2 && i == 10 && defect == "mixed-text") cell.Element(Sheet + "is")!.Add(new XElement(Sheet + "r", new XElement(Sheet + "t", "extra")));
                if (number == 2 && i == 9 && defect == "numeric-line-id") { cell.SetAttributeValue("t", "n"); cell.RemoveNodes(); cell.Add(new XElement(Sheet + "v", "1")); }
                return cell;
            }));
            Part("xl/worksheets/sheet1.xml", new XElement(Sheet + "worksheet", new XElement(Sheet + "sheetData", Row(1, Columns), Row(2, values))).ToString());
            if (defect == "macro") Part("xl/vbaProject.bin", "synthetic");
            if (defect == "duplicate-part") Part("xl/workbook.xml", "<empty/>");
        }
        return buffer.ToArray();
    }
}
