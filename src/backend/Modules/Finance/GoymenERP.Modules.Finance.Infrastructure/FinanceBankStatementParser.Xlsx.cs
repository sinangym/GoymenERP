using System.Globalization;
using System.IO.Compression;
using System.Xml;
using System.Xml.Linq;
using GoymenERP.Modules.Finance.Contracts;

namespace GoymenERP.Modules.Finance.Infrastructure;

/// <summary>
/// WP12 / NEEDS-SINAN WP12-S2: an XLSX statement read only through the known header names
/// (<see cref="FinanceBankStatementXlsxHeadersV1"/>). The header row is the first row that has a known transaction date
/// header; rows above it (bank banners) are ignored. Every non-empty header cell must be known and used once; date,
/// description and the inflow/outflow pair are required. The first worksheet is read; formulas, macros, external links,
/// embedded objects and the 1904 date system are refused. Amounts must be numeric cells (no culture guessing on text).
/// </summary>
internal static partial class FinanceBankStatementParser
{
    private static readonly XNamespace SheetNs = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
    private static readonly XNamespace PackageRelNs = "http://schemas.openxmlformats.org/package/2006/relationships";
    private static readonly XNamespace OfficeRelNs = "http://schemas.openxmlformats.org/officeDocument/2006/relationships";
    private const int MaximumArchiveEntries = 200;
    private const long MaximumExpandedBytes = 64L * 1024 * 1024;
    private const int MaximumColumns = 64;
    private static readonly CultureInfo Turkish = CultureInfo.GetCultureInfo("tr-TR");

    private enum XlsxField { TransactionDate, ValueDate, Description, Inflow, Outflow, Balance, Reference, Currency }

    public static FinanceStatementParseResult ParseXlsx(ReadOnlySpan<byte> content)
    {
        var issues = new Issues();
        if (content.IsEmpty)
        {
            issues.Add(0, "file", "file_empty");
            return issues.Result();
        }

        List<(int Row, Dictionary<int, XlsxCell> Cells)> rows;
        try
        {
            rows = ReadWorksheet(content.ToArray());
        }
        catch (Exception exception) when (exception is XlsxRefusal or InvalidDataException or XmlException or InvalidOperationException
                                              or OverflowException or FormatException or IOException)
        {
            issues.Add(0, "file", exception is XlsxRefusal refusal ? refusal.Code : "xlsx_invalid");
            return issues.Result();
        }

        var headerIndex = rows.FindIndex(row => row.Cells.Values.Any(cell => cell.Text is { } text
            && Known(FinanceBankStatementXlsxHeadersV1.TransactionDate, text)));
        if (headerIndex < 0)
        {
            issues.Add(0, "header", "xlsx_header_unknown");
            return issues.Result();
        }

        var (headerRow, headerCells) = rows[headerIndex];
        var columns = new Dictionary<XlsxField, int>();
        foreach (var (column, cell) in headerCells.OrderBy(pair => pair.Key))
        {
            if (string.IsNullOrWhiteSpace(cell.Text) && cell.Number is null) continue;
            var field = cell.Text is { } text ? FieldOf(text) : null;
            if (field is null || !columns.TryAdd(field.Value, column))
            {
                issues.Add(headerRow, "header", "xlsx_header_unknown");
                return issues.Result();
            }
        }

        if (!columns.ContainsKey(XlsxField.TransactionDate) || !columns.ContainsKey(XlsxField.Description)
            || !columns.ContainsKey(XlsxField.Inflow) || !columns.ContainsKey(XlsxField.Outflow))
        {
            issues.Add(headerRow, "header", "xlsx_header_required_missing");
            return issues.Result();
        }

        var lines = new List<FinanceParsedStatementLine>();
        foreach (var (rowNumber, cells) in rows.Skip(headerIndex + 1))
        {
            if (cells.Values.All(cell => string.IsNullOrWhiteSpace(cell.Text) && cell.Number is null)) continue;
            if (lines.Count >= FinanceBankStatementLimitsV1.MaximumLines)
            {
                issues.Add(rowNumber, "file", "line_limit_exceeded");
                break;
            }

            XlsxCell? Cell(XlsxField field) => columns.TryGetValue(field, out var column) && cells.TryGetValue(column, out var cell)
                ? cell : null;
            var before = issues.Count;
            var transactionOn = XlsxDate(Cell(XlsxField.TransactionDate));
            if (transactionOn is null) issues.Add(rowNumber, "transactionDate", "date_invalid");
            DateOnly? valueOn = null;
            if (Cell(XlsxField.ValueDate) is { } valueCell && !valueCell.IsEmpty)
            {
                valueOn = XlsxDate(valueCell);
                if (valueOn is null) issues.Add(rowNumber, "valueDate", "date_invalid");
            }

            var inflowOk = XlsxAmount(Cell(XlsxField.Inflow), allowNegative: false, out var inflow);
            var outflowOk = XlsxAmount(Cell(XlsxField.Outflow), allowNegative: false, out var outflow);
            var inflowSet = inflow is not null && !IsZero(inflow);
            var outflowSet = outflow is not null && !IsZero(outflow);
            if (!inflowOk || !outflowOk || inflowSet == outflowSet) issues.Add(rowNumber, "amount", "amount_invalid");

            string? balance = null;
            if (Cell(XlsxField.Balance) is { IsEmpty: false } balanceCell
                && !XlsxAmount(balanceCell, allowNegative: true, out balance))
                issues.Add(rowNumber, "balance", "amount_invalid");

            var currency = Cell(XlsxField.Currency)?.Text?.Trim();
            if (string.IsNullOrEmpty(currency)) currency = null;
            else if (currency.Length != 3 || !currency.All(char.IsAsciiLetterUpper)) issues.Add(rowNumber, "currency", "currency_invalid");
            var reference = Text(Cell(XlsxField.Reference)?.Display);
            if (reference is { Length: > FinanceBankStatementLimitsV1.ReferenceMaximumLength })
                issues.Add(rowNumber, "reference", "text_too_long");
            var description = Text(Cell(XlsxField.Description)?.Display) ?? string.Empty;
            if (description.Length == 0) issues.Add(rowNumber, "description", "text_required");
            else if (description.Length > FinanceBankStatementLimitsV1.DescriptionMaximumLength)
                issues.Add(rowNumber, "description", "text_too_long");

            if (issues.Count == before && transactionOn is not null)
            {
                lines.Add(new FinanceParsedStatementLine(rowNumber, transactionOn.Value, valueOn,
                    inflowSet ? FinanceBankStatementDirectionV1.Inflow : FinanceBankStatementDirectionV1.Outflow,
                    inflowSet ? inflow! : outflow!, balance, currency, reference, description));
            }

            if (issues.Full) break;
        }

        return issues.Result(lines, []);
    }

    private static XlsxField? FieldOf(string text) =>
        Known(FinanceBankStatementXlsxHeadersV1.TransactionDate, text) ? XlsxField.TransactionDate
        : Known(FinanceBankStatementXlsxHeadersV1.ValueDate, text) ? XlsxField.ValueDate
        : Known(FinanceBankStatementXlsxHeadersV1.Description, text) ? XlsxField.Description
        : Known(FinanceBankStatementXlsxHeadersV1.Inflow, text) ? XlsxField.Inflow
        : Known(FinanceBankStatementXlsxHeadersV1.Outflow, text) ? XlsxField.Outflow
        : Known(FinanceBankStatementXlsxHeadersV1.Balance, text) ? XlsxField.Balance
        : Known(FinanceBankStatementXlsxHeadersV1.Reference, text) ? XlsxField.Reference
        : Known(FinanceBankStatementXlsxHeadersV1.Currency, text) ? XlsxField.Currency
        : null;

    private static bool Known(IReadOnlyList<string> names, string text) =>
        names.Any(name => string.Compare(name, text.Trim(), Turkish, CompareOptions.IgnoreCase) == 0);

    private static DateOnly? XlsxDate(XlsxCell? cell)
    {
        if (cell is null || cell.IsEmpty) return null;
        if (cell.Number is { } number)
        {
            // A whole-day serial of the 1900 system (the 1904 system is refused at the workbook level).
            if (number < 1 || number > 2958465) return null;
            try { return DateOnly.FromDateTime(DateTime.FromOADate((double)decimal.Truncate(number))); }
            catch (ArgumentException) { return null; }
        }

        var text = cell.Text!.Trim();
        return DateOnly.TryParseExact(text, ["dd.MM.yyyy", "yyyy-MM-dd"], CultureInfo.InvariantCulture, DateTimeStyles.None,
            out var date) ? date : null;
    }

    /// <summary>Empty is "no amount" (true, null). Only a numeric cell is an amount; text amounts are refused.</summary>
    private static bool XlsxAmount(XlsxCell? cell, bool allowNegative, out string? amount)
    {
        amount = null;
        if (cell is null || cell.IsEmpty) return true;
        if (cell.Number is not { } number || cell.RawNumber is not { } raw) return false;
        var negative = number < 0;
        if (negative && !allowNegative) return false;
        var digits = negative ? raw[1..] : raw;
        var dot = digits.IndexOf('.');
        var canonical = Canonical(dot < 0 ? digits : digits[..dot], dot < 0 ? string.Empty : digits[(dot + 1)..]);
        if (canonical is null) return false;
        amount = negative && !IsZero(canonical) ? "-" + canonical : canonical;
        return true;
    }

    private sealed record XlsxCell(string? Text, decimal? Number, string? RawNumber)
    {
        public bool IsEmpty => string.IsNullOrWhiteSpace(Text) && Number is null;
        public string? Display => Text ?? RawNumber;
    }

    private sealed class XlsxRefusal(string code) : Exception(code)
    {
        public string Code { get; } = code;
    }

    private static List<(int Row, Dictionary<int, XlsxCell> Cells)> ReadWorksheet(byte[] bytes)
    {
        using var buffer = new MemoryStream(bytes, writable: false);
        using var archive = new ZipArchive(buffer, ZipArchiveMode.Read);
        if (archive.Entries.Count is 0 or > MaximumArchiveEntries || archive.Entries.Sum(entry => entry.Length) > MaximumExpandedBytes
            || archive.Entries.Select(entry => entry.FullName).Distinct(StringComparer.OrdinalIgnoreCase).Count() != archive.Entries.Count)
            throw new XlsxRefusal("xlsx_archive_limit");
        foreach (var entry in archive.Entries)
        {
            var name = entry.FullName;
            if (name.StartsWith('/') || name.Contains('\\') || name.Contains(':') || name.Split('/').Any(part => part is "." or "..")
                || new[] { "vbaProject", "macrosheets", "activeX", "embeddings", "externalLinks" }
                    .Any(marker => name.Contains(marker, StringComparison.OrdinalIgnoreCase)))
                throw new XlsxRefusal("xlsx_active_content");
            if (name.EndsWith(".rels", StringComparison.OrdinalIgnoreCase)
                && Part(entry).Descendants(PackageRelNs + "Relationship").Any(relation =>
                    string.Equals(relation.Attribute("TargetMode")?.Value, "External", StringComparison.OrdinalIgnoreCase)))
                throw new XlsxRefusal("xlsx_active_content");
        }

        var workbook = Part(Entry(archive, "xl/workbook.xml"));
        if (workbook.Root?.Name != SheetNs + "workbook") throw new XlsxRefusal("xlsx_invalid");
        if (workbook.Root.Element(SheetNs + "workbookPr")?.Attribute("date1904")?.Value is "1" or "true")
            throw new XlsxRefusal("xlsx_date1904_not_supported");
        var sheet = workbook.Root.Element(SheetNs + "sheets")?.Elements(SheetNs + "sheet").FirstOrDefault()
                    ?? throw new XlsxRefusal("xlsx_invalid");
        var relationId = sheet.Attribute(OfficeRelNs + "id")?.Value;
        var relation = Part(Entry(archive, "xl/_rels/workbook.xml.rels")).Root?.Elements(PackageRelNs + "Relationship")
            .SingleOrDefault(item => item.Attribute("Id")?.Value == relationId) ?? throw new XlsxRefusal("xlsx_invalid");
        var target = relation.Attribute("Target")?.Value ?? string.Empty;
        var path = target.StartsWith("/xl/", StringComparison.Ordinal) ? target[1..] : "xl/" + target;
        if (!path.StartsWith("xl/worksheets/", StringComparison.Ordinal) || path.Split('/').Any(part => part is "" or "." or ".."))
            throw new XlsxRefusal("xlsx_invalid");

        var shared = archive.GetEntry("xl/sharedStrings.xml") is { } sharedEntry
            ? Part(sharedEntry).Root?.Elements(SheetNs + "si").Select(SharedText).ToArray() ?? []
            : [];
        var data = Part(Entry(archive, path)).Root?.Element(SheetNs + "sheetData") ?? throw new XlsxRefusal("xlsx_invalid");
        var rows = new List<(int, Dictionary<int, XlsxCell>)>();
        var last = 0;
        foreach (var row in data.Elements(SheetNs + "row"))
        {
            var number = int.Parse(row.Attribute("r")?.Value ?? throw new XlsxRefusal("xlsx_invalid"), NumberStyles.None,
                CultureInfo.InvariantCulture);
            if (number <= last || number > FinanceBankStatementLimitsV1.MaximumLines + 1000) throw new XlsxRefusal("xlsx_row_invalid");
            last = number;
            var cells = new Dictionary<int, XlsxCell>();
            foreach (var cell in row.Elements(SheetNs + "c"))
            {
                if (cell.Elements(SheetNs + "f").Any()) throw new XlsxRefusal("xlsx_formula_not_supported");
                var reference = cell.Attribute("r")?.Value ?? throw new XlsxRefusal("xlsx_invalid");
                var letters = new string(reference.TakeWhile(c => c is >= 'A' and <= 'Z').ToArray());
                var column = letters.Aggregate(0, (value, c) => checked(value * 26 + c - 'A' + 1));
                if (column is < 1 or > MaximumColumns || reference[letters.Length..] != number.ToString(CultureInfo.InvariantCulture)
                    || cells.ContainsKey(column)) throw new XlsxRefusal("xlsx_cell_invalid");
                var scalar = cell.Element(SheetNs + "v")?.Value;
                var value = cell.Attribute("t")?.Value switch
                {
                    "s" => new XlsxCell(int.TryParse(scalar, NumberStyles.None, CultureInfo.InvariantCulture, out var index)
                        && index < shared.Length ? shared[index] : throw new XlsxRefusal("xlsx_cell_invalid"), null, null),
                    "inlineStr" => new XlsxCell(SharedText(cell.Element(SheetNs + "is") ?? throw new XlsxRefusal("xlsx_cell_invalid")),
                        null, null),
                    "str" => new XlsxCell(scalar, null, null),
                    null or "n" => scalar is null or "" ? new XlsxCell(null, null, null) : Numeric(scalar),
                    _ => throw new XlsxRefusal("xlsx_cell_invalid"),
                };
                if (value.Text is { } text && text.Any(c => char.IsControl(c) && c is not ('\t' or '\r' or '\n')))
                    throw new XlsxRefusal("control_character");
                cells.Add(column, value);
            }

            rows.Add((number, cells));
        }

        return rows;
    }

    private static XlsxCell Numeric(string scalar)
    {
        // Plain decimal only (no exponent, no grouping), read with the invariant culture.
        if (!decimal.TryParse(scalar, NumberStyles.AllowLeadingSign | NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture,
                out var number)) throw new XlsxRefusal("xlsx_number_invalid");
        return new XlsxCell(null, number, scalar);
    }

    private static string SharedText(XElement item) =>
        string.Concat(item.Descendants(SheetNs + "t").Where(t => t.Parent?.Name != SheetNs + "rPh").Select(t => t.Value));

    private static ZipArchiveEntry Entry(ZipArchive archive, string name) =>
        archive.GetEntry(name) ?? throw new XlsxRefusal("xlsx_invalid");

    private static XDocument Part(ZipArchiveEntry entry)
    {
        if (entry.Length > MaximumExpandedBytes) throw new XlsxRefusal("xlsx_archive_limit");
        using var stream = entry.Open();
        using var reader = XmlReader.Create(stream, new XmlReaderSettings
        {
            DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null, MaxCharactersInDocument = MaximumExpandedBytes,
        });
        return XDocument.Load(reader);
    }
}
