using System.Globalization;
using System.IO.Compression;
using System.Text;
using System.Xml;
using System.Xml.Linq;
using GoymenERP.Modules.Purchasing.Application.Contracts;

namespace GoymenERP.Api.Ai.DocumentDrafts;

/// <summary>A structured file that could not be read; a stable code only, never file content.</summary>
public sealed class AiDocumentUnreadableException(string code) : Exception(code)
{
    public string Code { get; } = code;
}

/// <summary>
/// Tier 1 for an e-fatura (UBL-TR XML): the Purchasing owner's own file reader (<see cref="IPurchaseInvoiceFileReaderV1"/>,
/// ALS-D7) extracts the facts — XXE-safe, depth- and size-bounded, exact decimals — so the draft and the owner's import
/// read one invoice the same way. Nothing is admitted, matched or calculated by that reader; this adapter only maps its
/// facts to draft values. The supplier's item code is kept but marked as the supplier's code (never matched to ours);
/// the buyer's item code, when the supplier wrote it, is ours.
/// </summary>
public static class AiUblInvoiceDraftReader
{
    private static readonly string[] TaxIdSchemes = ["VKN", "TCKN"];

    public static AiExtractedDocumentV1 Read(IPurchaseInvoiceFileReaderV1 reader, ReadOnlyMemory<byte> content, int maximumLines)
    {
        ArgumentNullException.ThrowIfNull(reader);
        var read = reader.Read(content, new PurchaseInvoiceFileReadOptionsV1(PurchaseInvoiceFileFormatV1.UblXml));
        if (read.Invoice is not { } invoice)
        {
            throw new AiDocumentUnreadableException(read.Issues.FirstOrDefault()?.Code ?? "source_invalid");
        }

        if (invoice.Lines.Count > maximumLines) throw new AiDocumentUnreadableException("row_limit");
        const string source = AiDocumentReadingTiersV1.StructuredFile;
        var document = new AiExtractedDocumentV1
        {
            DocumentNumber = new(invoice.Number, source),
            DocumentDate = new(invoice.InvoiceDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture), source),
            Currency = new(invoice.Currency, source),
            CounterpartyTaxId = invoice.SupplierIdentities
                .Where(identity => TaxIdSchemes.Contains(identity.Scheme, StringComparer.OrdinalIgnoreCase))
                .Select(identity => new AiExtractedValueV1(identity.Value, source))
                .FirstOrDefault(),
            PayableAmount = invoice.Totals.TryGetValue("PayableAmount", out var payable)
                ? new(payable.Value, source, Canonical: true)
                : null,
        };

        foreach (var line in invoice.Lines)
        {
            var ours = line.BuyerCode;
            document.Lines.Add(new AiExtractedLineV1
            {
                ProductCode = (ours ?? line.SellerCode) is { } code ? new(code, source) : null,
                ProductCodeIsCounterpartyCode = ours is null && line.SellerCode is not null,
                Description = new(line.Description, source),
                Quantity = new(line.Quantity, source, Canonical: true),
                Unit = new(line.UnitCode, source),
                LineAmount = new(line.Net.Value, source, Canonical: true),
                Currency = new(line.Net.Currency, source),
            });
        }

        return document;
    }
}

/// <summary>
/// Tier 1 for a table (CSV or XLSX): the first row is a header of KNOWN column names (Turkish ASCII or English, see
/// <see cref="Columns"/>); a layout is never guessed. Unknown columns are ignored as data. Each non-empty row is one
/// line; document-level columns (belge_no, tarih, vkn, cari_adi, para_birimi) are read from the first row that holds
/// them. Numbers stay as written and go through the free-text number rule (a "1.250" stays ambiguous).
/// </summary>
public static class AiTableDraftReader
{
    private const string Source = AiDocumentReadingTiersV1.StructuredFile;

    /// <summary>Technical archive bounds (not business limits): a workbook with more parts or columns is refused.</summary>
    private const int MaximumArchiveEntries = 512;
    private const int MaximumColumnIndex = 128;
    private const int MaximumColumnLetters = 3;
    private const int CopyBufferBytes = 81920;

    /// <summary>Header name → field code. Header names are compared case-insensitively after trimming.</summary>
    public static IReadOnlyDictionary<string, string> Columns { get; } = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
    {
        ["belge_no"] = AiDraftFieldCodesV1.DocumentNumber, ["document_number"] = AiDraftFieldCodesV1.DocumentNumber,
        ["tarih"] = AiDraftFieldCodesV1.DocumentDate, ["date"] = AiDraftFieldCodesV1.DocumentDate,
        ["vkn"] = AiDraftFieldCodesV1.CounterpartyTaxId, ["tax_id"] = AiDraftFieldCodesV1.CounterpartyTaxId,
        ["cari_adi"] = AiDraftFieldCodesV1.CounterpartyName, ["counterparty_name"] = AiDraftFieldCodesV1.CounterpartyName,
        ["para_birimi"] = AiDraftFieldCodesV1.Currency, ["currency"] = AiDraftFieldCodesV1.Currency,
        ["urun_kodu"] = AiDraftFieldCodesV1.ProductCode, ["product_code"] = AiDraftFieldCodesV1.ProductCode,
        ["aciklama"] = AiDraftFieldCodesV1.Description, ["description"] = AiDraftFieldCodesV1.Description,
        ["miktar"] = AiDraftFieldCodesV1.Quantity, ["quantity"] = AiDraftFieldCodesV1.Quantity,
        ["birim"] = AiDraftFieldCodesV1.Unit, ["unit"] = AiDraftFieldCodesV1.Unit,
        ["birim_fiyat"] = AiDraftFieldCodesV1.UnitPrice, ["unit_price"] = AiDraftFieldCodesV1.UnitPrice,
        ["tutar"] = AiDraftFieldCodesV1.LineAmount, ["amount"] = AiDraftFieldCodesV1.LineAmount,
        ["kesit"] = AiDraftFieldCodesV1.Section, ["section"] = AiDraftFieldCodesV1.Section,
        ["et_kalinligi"] = AiDraftFieldCodesV1.WallThickness, ["wall_thickness"] = AiDraftFieldCodesV1.WallThickness,
        ["boy"] = AiDraftFieldCodesV1.Length, ["length"] = AiDraftFieldCodesV1.Length,
    };

    public static AiExtractedDocumentV1 ReadCsv(ReadOnlyMemory<byte> content, int maximumLines, int maximumCellCharacters)
    {
        string text;
        try
        {
            text = new UTF8Encoding(false, true).GetString(content.Span).TrimStart('﻿');
        }
        catch (DecoderFallbackException)
        {
            throw new AiDocumentUnreadableException("text_encoding_invalid");
        }

        var firstLine = text.Split('\n', 2)[0];
        var delimiter = new[] { ';', ',', '\t' }.OrderByDescending(candidate => firstLine.Count(c => c == candidate)).First();
        return FromRows(ParseCsv(text, delimiter, maximumLines + 1, maximumCellCharacters), maximumLines);
    }

    public static AiExtractedDocumentV1 ReadXlsx(ReadOnlyMemory<byte> content, int maximumLines, int maximumCellCharacters,
        long maximumExpandedBytes)
    {
        try
        {
            using var stream = new MemoryStream(content.ToArray(), writable: false);
            using var archive = new ZipArchive(stream, ZipArchiveMode.Read);
            if (archive.Entries.Count > MaximumArchiveEntries) throw new AiDocumentUnreadableException("archive_entry_limit");
            var shared = archive.GetEntry("xl/sharedStrings.xml") is { } sharedEntry
                ? ReadSharedStrings(ReadEntry(sharedEntry, maximumExpandedBytes))
                : [];
            var sheet = archive.GetEntry("xl/worksheets/sheet1.xml") ?? throw new AiDocumentUnreadableException("worksheet_missing");
            return FromRows(ReadSheet(ReadEntry(sheet, maximumExpandedBytes), shared, maximumLines + 1, maximumCellCharacters), maximumLines);
        }
        catch (Exception exception) when (exception is InvalidDataException or XmlException or IOException)
        {
            throw new AiDocumentUnreadableException("workbook_invalid");
        }
    }

    private static AiExtractedDocumentV1 FromRows(IReadOnlyList<IReadOnlyList<string>> rows, int maximumLines)
    {
        if (rows.Count == 0) throw new AiDocumentUnreadableException("table_empty");
        var header = rows[0].Select(name => Columns.TryGetValue(name.Trim(), out var code) ? code : null).ToArray();
        if (header.All(code => code is null)) throw new AiDocumentUnreadableException("table_header_unknown");
        if (header.Where(code => code is not null).GroupBy(code => code).Any(group => group.Count() > 1))
            throw new AiDocumentUnreadableException("table_header_duplicate");
        if (rows.Count - 1 > maximumLines) throw new AiDocumentUnreadableException("row_limit");

        var document = new AiExtractedDocumentV1();
        foreach (var row in rows.Skip(1))
        {
            if (row.All(string.IsNullOrWhiteSpace)) continue;
            var line = new AiExtractedLineV1();
            var hasLineValue = false;
            for (var index = 0; index < header.Length && index < row.Count; index++)
            {
                if (header[index] is not { } code || string.IsNullOrWhiteSpace(row[index])) continue;
                var value = new AiExtractedValueV1(row[index].Trim(), Source);
                if (document.TryFillHeader(code, value) || IsHeaderField(code)) continue;
                hasLineValue = true;
                switch (code)
                {
                    case AiDraftFieldCodesV1.ProductCode: line.ProductCode = value; break;
                    case AiDraftFieldCodesV1.Description: line.Description = value; break;
                    case AiDraftFieldCodesV1.Quantity: line.Quantity = value; break;
                    case AiDraftFieldCodesV1.Unit: line.Unit = value; break;
                    case AiDraftFieldCodesV1.UnitPrice: line.UnitPrice = value; break;
                    case AiDraftFieldCodesV1.LineAmount: line.LineAmount = value; break;
                    case AiDraftFieldCodesV1.Section: line.Section = value; break;
                    case AiDraftFieldCodesV1.WallThickness: line.WallThickness = value; break;
                    case AiDraftFieldCodesV1.Length: line.Length = value; break;
                }
            }

            if (hasLineValue) document.Lines.Add(line);
        }

        return document;
    }

    private static bool IsHeaderField(string code) => code is AiDraftFieldCodesV1.DocumentNumber or AiDraftFieldCodesV1.DocumentDate
        or AiDraftFieldCodesV1.CounterpartyTaxId or AiDraftFieldCodesV1.CounterpartyName or AiDraftFieldCodesV1.Currency;

    private static List<IReadOnlyList<string>> ParseCsv(string text, char delimiter, int maximumRows, int maximumCellCharacters)
    {
        var rows = new List<IReadOnlyList<string>>();
        var row = new List<string>();
        var cell = new StringBuilder();
        var quoted = false;
        for (var index = 0; index < text.Length; index++)
        {
            var character = text[index];
            if (quoted)
            {
                if (character == '"' && index + 1 < text.Length && text[index + 1] == '"') { cell.Append('"'); index++; }
                else if (character == '"') quoted = false;
                else cell.Append(character);
            }
            else if (character == '"' && cell.Length == 0) quoted = true;
            else if (character == delimiter) { row.Add(cell.ToString()); cell.Clear(); }
            else if (character == '\n')
            {
                row.Add(cell.ToString().TrimEnd('\r')); cell.Clear();
                rows.Add(row); row = [];
                if (rows.Count > maximumRows) throw new AiDocumentUnreadableException("row_limit");
            }
            else cell.Append(character);

            if (cell.Length > maximumCellCharacters) throw new AiDocumentUnreadableException("cell_too_long");
        }

        if (quoted) throw new AiDocumentUnreadableException("csv_quote_unterminated");
        if (cell.Length > 0 || row.Count > 0) { row.Add(cell.ToString().TrimEnd('\r')); rows.Add(row); }
        if (rows.Count > maximumRows) throw new AiDocumentUnreadableException("row_limit");
        return rows;
    }

    private static byte[] ReadEntry(ZipArchiveEntry entry, long maximumExpandedBytes)
    {
        if (entry.Length > maximumExpandedBytes) throw new AiDocumentUnreadableException("archive_expanded_limit");
        using var input = entry.Open();
        using var output = new MemoryStream();
        var buffer = new byte[CopyBufferBytes];
        int read;
        while ((read = input.Read(buffer, 0, buffer.Length)) > 0)
        {
            // The declared length is not trusted: count what actually inflates.
            if (output.Length + read > maximumExpandedBytes) throw new AiDocumentUnreadableException("archive_expanded_limit");
            output.Write(buffer, 0, read);
        }

        return output.ToArray();
    }

    private static XDocument LoadXml(byte[] bytes)
    {
        using var stream = new MemoryStream(bytes, writable: false);
        using var reader = XmlReader.Create(stream, new XmlReaderSettings
        {
            DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null, MaxCharactersFromEntities = 1024,
        });
        return XDocument.Load(reader, LoadOptions.None);
    }

    private static List<string> ReadSharedStrings(byte[] bytes)
    {
        XNamespace main = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
        return LoadXml(bytes).Root?.Elements(main + "si")
            .Select(item => string.Concat(item.Descendants(main + "t").Select(text => text.Value)))
            .ToList() ?? [];
    }

    private static List<IReadOnlyList<string>> ReadSheet(byte[] bytes, IReadOnlyList<string> shared, int maximumRows, int maximumCellCharacters)
    {
        XNamespace main = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
        var rows = new List<IReadOnlyList<string>>();
        var data = LoadXml(bytes).Root?.Element(main + "sheetData") ?? throw new AiDocumentUnreadableException("worksheet_invalid");
        foreach (var rowElement in data.Elements(main + "row"))
        {
            if (rows.Count >= maximumRows) throw new AiDocumentUnreadableException("row_limit");
            var cells = new SortedDictionary<int, string>();
            foreach (var cell in rowElement.Elements(main + "c"))
            {
                var column = ColumnIndex((string?)cell.Attribute("r"));
                if (column is < 0 or > MaximumColumnIndex) throw new AiDocumentUnreadableException("column_limit");
                var type = (string?)cell.Attribute("t");
                var raw = type == "inlineStr"
                    ? string.Concat(cell.Descendants(main + "t").Select(text => text.Value))
                    : (string?)cell.Element(main + "v") ?? string.Empty;
                var value = type == "s"
                    ? int.TryParse(raw, NumberStyles.None, CultureInfo.InvariantCulture, out var sharedIndex) && sharedIndex < shared.Count
                        ? shared[sharedIndex]
                        : throw new AiDocumentUnreadableException("shared_string_invalid")
                    : raw;
                if (value.Length > maximumCellCharacters) throw new AiDocumentUnreadableException("cell_too_long");
                cells[column] = value;
            }

            var width = cells.Count == 0 ? 0 : cells.Keys.Max() + 1;
            rows.Add(Enumerable.Range(0, width).Select(index => cells.TryGetValue(index, out var value) ? value : string.Empty).ToArray());
        }

        return rows;
    }

    private static int ColumnIndex(string? reference)
    {
        if (string.IsNullOrEmpty(reference)) return -1;
        var index = 0;
        var letters = 0;
        foreach (var character in reference)
        {
            if (character is < 'A' or > 'Z') break;
            index = index * 26 + (character - 'A' + 1);
            if (++letters > MaximumColumnLetters) return -1;
        }

        return letters == 0 ? -1 : index - 1;
    }
}
