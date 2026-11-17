using System.Globalization;
using System.Security.Cryptography;
using System.Xml;
using System.Xml.Linq;
using GoymenERP.Modules.Purchasing.Application.Contracts;

namespace GoymenERP.Modules.Purchasing.Infrastructure.Imports;

/// <summary>Only extracts the uploaded source. No tax calculation, currency selection, default due date,
/// party/product inference, signature verification, database write or Finance event occurs here.</summary>
public sealed partial class PurchaseInvoiceFileReader : IPurchaseInvoiceFileReaderV1
{
    private static readonly XNamespace InvoiceNs = "urn:oasis:names:specification:ubl:schema:xsd:Invoice-2";
    private static readonly XNamespace Cbc = "urn:oasis:names:specification:ubl:schema:xsd:CommonBasicComponents-2";
    private static readonly XNamespace Cac = "urn:oasis:names:specification:ubl:schema:xsd:CommonAggregateComponents-2";
    private static readonly string[] TotalNames = ["LineExtensionAmount", "TaxExclusiveAmount", "TaxInclusiveAmount",
        "AllowanceTotalAmount", "ChargeTotalAmount", "PrepaidAmount", "PayableRoundingAmount", "PayableAmount"];

    public PurchaseInvoiceFileReadV1 Read(ReadOnlyMemory<byte> content, PurchaseInvoiceFileReadOptionsV1 options)
    {
        ArgumentNullException.ThrowIfNull(options);
        if (content.Length is 0 or > PurchaseInvoiceFileBoundsV1.MaximumBytes)
            return new(string.Empty, null, [new("file", "file_size_invalid")]);
        // Freeze once: provenance and parsing must observe identical bytes even for a caller-owned buffer.
        var frozen = content.ToArray();
        var hash = Convert.ToHexString(SHA256.HashData(frozen)).ToLowerInvariant();
        try
        {
            var facts = options.Format switch
            {
                PurchaseInvoiceFileFormatV1.UblXml => ReadUbl(frozen),
                PurchaseInvoiceFileFormatV1.Csv => ReadTable(ReadCsv(frozen, options.Delimiter), options),
                PurchaseInvoiceFileFormatV1.Xlsx => ReadTable(ReadWorkbook(frozen, options.Worksheet, options.Columns), options),
                _ => throw Invalid("format", "format_unsupported"),
            };
            if (facts.Lines.Count is 0 or > PurchaseInvoiceFileBoundsV1.MaximumRows
                || facts.Lines.Select(line => line.Id).Distinct(StringComparer.Ordinal).Count() != facts.Lines.Count)
                throw Invalid("lines", "line_identity_invalid");
            return new(hash, facts, []);
        }
        catch (SourceException exception) { return new(hash, null, [new(exception.Field, exception.Code)]); }
        catch (Exception exception) when (exception is XmlException or InvalidDataException or IOException
            or FormatException or OverflowException or ArgumentException)
        { return new(hash, null, [new("file", "source_invalid")]); }
    }

    private static PurchaseInvoiceFileFactsV1 ReadUbl(byte[] bytes)
    {
        using var stream = new MemoryStream(bytes, writable: false);
        var root = ReadXml(stream, PurchaseInvoiceFileBoundsV1.MaximumBytes).Root;
        if (root?.Name != InvoiceNs + "Invoice") throw Invalid("file", "ubl_invoice_required");
        if (Optional(root, Cbc + "UBLVersionID") is { } version && version != "2.1")
            throw Invalid("UBLVersionID", "ubl_version_unsupported");
        var totals = One(root, Cac + "LegalMonetaryTotal");
        var lines = root.Elements(Cac + "InvoiceLine").ToArray();
        if (lines.Length > PurchaseInvoiceFileBoundsV1.MaximumRows) throw Invalid("lines", "row_limit");
        // A nested invoice line changes the source's quantity/money model. Do not flatten it silently.
        if (lines.Any(line => line.Elements(Cac + "SubInvoiceLine").Any())) throw Invalid("lines", "nested_lines_unsupported");
        if (lines.Any(line => line.Descendants(Cac + "PricingExchangeRate").Any()))
            throw Invalid("lines", "line_exchange_rate_unsupported");
        var rates = new List<PurchaseInvoiceFileRateV1>();
        foreach (var purpose in new[] { "TaxExchangeRate", "PricingExchangeRate", "PaymentExchangeRate", "PaymentAlternativeExchangeRate" })
        {
            var rate = Maybe(root, Cac + purpose);
            if (rate is null) continue;
            rates.Add(new(purpose, Currency(Text(rate, Cbc + "SourceCurrencyCode")), Currency(Text(rate, Cbc + "TargetCurrencyCode")),
                Number(Text(rate, Cbc + "CalculationRate"), positive: true), OptionalNumber(rate, "SourceCurrencyBaseRate"),
                OptionalNumber(rate, "TargetCurrencyBaseRate"), Optional(rate, Cbc + "MathematicOperatorCode"), OptionalDate(rate, "Date")));
        }
        return new(Text(root, Cbc + "ID"), Identifier(Text(root, Cbc + "UUID")), Date(Text(root, Cbc + "IssueDate")),
            Text(root, Cbc + "InvoiceTypeCode"), Currency(Text(root, Cbc + "DocumentCurrencyCode")),
            Identities(One(One(root, Cac + "AccountingSupplierParty"), Cac + "Party")),
            Identities(One(One(root, Cac + "AccountingCustomerParty"), Cac + "Party")),
            TotalNames.Where(name => Maybe(totals, Cbc + name) is not null)
                .ToDictionary(name => name, name => Amount(One(totals, Cbc + name)), StringComparer.Ordinal),
            Taxes(root), Adjustments(root), rates,
            root.Elements(Cac + "PaymentTerms").Select(term => new PurchaseInvoiceFilePaymentV1(
                OptionalDate(term, "PaymentDueDate"), Maybe(term, Cbc + "Amount") is { } amount ? Amount(amount) : null,
                OptionalDate(term, "InstallmentDueDate")))
                .Concat(root.Elements(Cac + "PaymentMeans").Select(term => new PurchaseInvoiceFilePaymentV1(OptionalDate(term, "PaymentDueDate"), null, Source: "PaymentMeans")))
                .Concat(OptionalDate(root, "DueDate") is { } due ? [new PurchaseInvoiceFilePaymentV1(due, null, Source: "InvoiceDueDate")] : [])
                .ToArray(),
            lines.Select(line =>
            {
                var item = One(line, Cac + "Item"); var quantity = One(line, Cbc + "InvoicedQuantity");
                return new PurchaseInvoiceFileLineV1(Text(line, Cbc + "ID"), ItemCode(item, "SellersItemIdentification"),
                    ItemCode(item, "BuyersItemIdentification"), Text(item, Cbc + "Name"), Attribute(quantity, "unitCode"),
                    Number(Scalar(quantity), positive: true), Amount(One(line, Cbc + "LineExtensionAmount")), Taxes(line), Adjustments(line));
            }).ToArray());
    }

    private static IReadOnlyList<PurchaseInvoiceFileIdentityV1> Identities(XElement party) => party.Elements(Cac + "PartyIdentification")
        .Select(item => One(item, Cbc + "ID")).Select(id => new PurchaseInvoiceFileIdentityV1(Attribute(id, "schemeID"), Scalar(id))).ToArray();
    private static string? ItemCode(XElement item, string kind) => Maybe(item, Cac + kind) is { } source ? Text(source, Cbc + "ID") : null;
    private static IReadOnlyList<PurchaseInvoiceFileTaxV1> Taxes(XElement owner)
    {
        var result = new List<PurchaseInvoiceFileTaxV1>();
        foreach (var withholding in new[] { false, true })
            foreach (var total in owner.Elements(Cac + (withholding ? "WithholdingTaxTotal" : "TaxTotal")))
            {
                var amount = Amount(One(total, Cbc + "TaxAmount"));
                var subtotals = total.Elements(Cac + "TaxSubtotal").ToArray();
                if (subtotals.Length == 0) { result.Add(new(null, null, amount, null, withholding)); continue; }
                var parts = subtotals.Select(subtotal =>
                {
                    var category = One(subtotal, Cac + "TaxCategory");
                    var scheme = One(category, Cac + "TaxScheme");
                    // Preserve unknown tax codes. A later owner normalizer must resolve them, never call all tax VAT.
                    return new PurchaseInvoiceFileTaxV1(Optional(scheme, Cbc + "TaxTypeCode"), OptionalNumber(subtotal, "Percent"),
                        Amount(One(subtotal, Cbc + "TaxAmount")), Maybe(subtotal, Cbc + "TaxableAmount") is { } basis ? Amount(basis) : null, withholding);
                }).ToArray();
                if (parts.Any(part => part.Amount.Currency != amount.Currency)
                    || parts.Sum(part => decimal.Parse(part.Amount.Value, CultureInfo.InvariantCulture)) != decimal.Parse(amount.Value, CultureInfo.InvariantCulture))
                    throw Invalid("TaxTotal", "tax_total_mismatch");
                result.AddRange(parts);
            }
        return result;
    }
    private static IReadOnlyList<PurchaseInvoiceFileAdjustmentV1> Adjustments(XElement owner) => owner.Elements(Cac + "AllowanceCharge")
        .Select(item => new PurchaseInvoiceFileAdjustmentV1(Text(item, Cbc + "ChargeIndicator") switch
            { "true" or "1" => true, "false" or "0" => false, _ => throw Invalid("ChargeIndicator", "boolean_invalid") },
            Amount(One(item, Cbc + "Amount")), Optional(item, Cbc + "AllowanceChargeReason"))).ToArray();

    private static XDocument ReadXml(Stream stream, long maximumCharacters)
    {
        using var reader = XmlReader.Create(stream, new XmlReaderSettings { DtdProcessing = DtdProcessing.Prohibit,
            XmlResolver = null, MaxCharactersInDocument = maximumCharacters, MaxCharactersFromEntities = 1024 });
        var document = XDocument.Load(reader, LoadOptions.None);
        if (document.Descendants().Any(element => element.Ancestors().Take(65).Count() > 64)) throw Invalid("file", "xml_depth_limit");
        return document;
    }
    private static XElement One(XElement parent, XName name) => Maybe(parent, name) ?? throw Invalid(name.LocalName, "field_required");
    private static XElement? Maybe(XElement parent, XName name)
    {
        var values = parent.Elements(name).Take(2).ToArray();
        return values.Length > 1 ? throw Invalid(name.LocalName, "duplicate_field") : values.SingleOrDefault();
    }
    private static string Scalar(XElement value) => value.HasElements ? throw Invalid(value.Name.LocalName, "scalar_required") : Bounded(value.Value);
    private static string Text(XElement parent, XName name) => Scalar(One(parent, name));
    private static string? Optional(XElement parent, XName name) => Maybe(parent, name) is { } value ? Scalar(value) : null;
    private static string Attribute(XElement element, string name) => Bounded(element.Attribute(name)?.Value ?? string.Empty);
    private static PurchaseInvoiceFileAmountV1 Amount(XElement value) => new(Currency(Attribute(value, "currencyID")), Number(Scalar(value)));
    private static string? OptionalNumber(XElement parent, string name) => Optional(parent, Cbc + name) is { } value ? Number(value) : null;
    private static DateOnly? OptionalDate(XElement parent, string name) => Optional(parent, Cbc + name) is { } value ? Date(value) : null;
    private static string Bounded(string value)
    {
        var trimmed = value.Trim();
        if (trimmed.Length is 0 or > PurchaseInvoiceFileBoundsV1.MaximumCellCharacters || trimmed.Any(char.IsControl))
            throw Invalid("value", "text_invalid");
        return trimmed;
    }
    private static string Currency(string value) => value.Length == 3 && value.All(c => c is >= 'A' and <= 'Z')
        ? value : throw Invalid("currency", "currency_invalid");
    private static string Number(string value, bool positive = false)
    {
        value = value.Trim();
        if (value.Length == 0 || value.Length > 40 || value.Any(c => !char.IsAsciiDigit(c) && c is not '.' and not '-')
            || !decimal.TryParse(value, NumberStyles.AllowDecimalPoint | NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out var number)
            || positive && number <= 0) throw Invalid("amount", "decimal_invalid");
        // Decimal parsing may round excess source precision: reject unless the lexical value survives exactly.
        var canonical = number.ToString("0.############################", CultureInfo.InvariantCulture);
        var negative = value.StartsWith('-');
        var magnitude = negative ? value[1..] : value;
        var lexical = magnitude.Contains('.') ? magnitude.TrimEnd('0').TrimEnd('.') : magnitude;
        lexical = lexical.TrimStart('0');
        if (lexical.StartsWith('.')) lexical = "0" + lexical;
        if (lexical.Length == 0) lexical = "0";
        if (negative && lexical != "0") lexical = "-" + lexical;
        if (lexical != canonical) throw Invalid("amount", "decimal_precision_loss");
        return canonical;
    }
    private static DateOnly Date(string value, string format = "yyyy-MM-dd") => DateOnly.TryParseExact(value, format,
        CultureInfo.InvariantCulture, DateTimeStyles.None, out var day) && day != default ? day : throw Invalid("date", "date_invalid");
    private static Guid Identifier(string value) => Guid.TryParseExact(value, "D", out var id) && id != Guid.Empty
        ? id : throw Invalid("ettn", "ettn_invalid");
    private static SourceException Invalid(string field, string code) => new(field, code);
    private sealed class SourceException(string field, string code) : Exception(code)
    { public string Field { get; } = field; public string Code { get; } = code; }
}
