using System.Globalization;
using System.IO.Compression;
using System.Security;
using System.Security.Cryptography;
using System.Text;

namespace GoymenERP.Modules.Reporting.Domain;

/// <summary>A column of an exported sheet: its id (for the header text) and the kind that decides how a cell is written.</summary>
public sealed record ReportingSheetColumnV1(string Id, ReportingColumnKindV1 Kind);

/// <summary>
/// WP14 export writers. Pure and deterministic: the same table gives the same bytes (fixed zip entry times), so the export
/// record's content hash identifies what left the system. Text cells that a spreadsheet would read as a formula (=, +, -, @, tab,
/// CR) are prefixed with an apostrophe (CSV/formula injection); numbers are written as numbers only in XLSX and only when they
/// are invariant decimals; flags are "Evet"/"Hayır".
/// </summary>
public static class ReportingFileWritersV1
{
    private static readonly DateTimeOffset FixedEntryTime = new(1980, 1, 1, 0, 0, 0, TimeSpan.Zero);

    public static string Sha256(byte[] content) => Convert.ToHexStringLower(SHA256.HashData(content));

    public static string SafeText(string value) =>
        value.Length > 0 && value[0] is '=' or '+' or '-' or '@' or '\t' or '\r' ? "'" + value : value;

    private static string Cell(ReportingSheetColumnV1 column, string? value) => value is null
        ? ""
        : column.Kind switch
        {
            ReportingColumnKindV1.Flag => value == "true" ? ReportingTextsTrV1.FlagTrue : value == "false" ? ReportingTextsTrV1.FlagFalse : SafeText(value),
            ReportingColumnKindV1.Number when IsInvariantNumber(value) => value,
            _ => SafeText(value),
        };

    public static bool IsInvariantNumber(string value) =>
        value.Length is > 0 and <= 40 && decimal.TryParse(value, NumberStyles.AllowLeadingSign | NumberStyles.AllowDecimalPoint,
            CultureInfo.InvariantCulture, out _) && !value.StartsWith('.') && !value.EndsWith('.') && !value.StartsWith('+');

    /// <summary>RFC 4180 CSV, UTF-8 with BOM, CRLF line ends, Turkish headers.</summary>
    public static byte[] Csv(IReadOnlyList<ReportingSheetColumnV1> columns, IReadOnlyList<IReadOnlyList<string?>> rows)
    {
        ArgumentNullException.ThrowIfNull(columns);
        ArgumentNullException.ThrowIfNull(rows);
        var builder = new StringBuilder();
        builder.Append(string.Join(',', columns.Select(column => Quote(SafeText(ReportingTextsTrV1.Header(column.Id)))))).Append("\r\n");
        foreach (var row in rows)
        {
            if (row.Count != columns.Count) throw new ArgumentException("Every row has one cell per column.", nameof(rows));
            builder.Append(string.Join(',', columns.Select((column, index) => Quote(Cell(column, row[index]))))).Append("\r\n");
        }

        var preamble = Encoding.UTF8.GetPreamble();
        var body = Encoding.UTF8.GetBytes(builder.ToString());
        return [.. preamble, .. body];
    }

    private static string Quote(string value) =>
        value.IndexOfAny([',', '"', '\r', '\n']) >= 0 || value.Length != value.Trim().Length
            ? "\"" + value.Replace("\"", "\"\"", StringComparison.Ordinal) + "\""
            : value;

    /// <summary>A minimal single-sheet Office Open XML workbook (inline strings; no styles, macros or external links).</summary>
    public static byte[] Xlsx(string sheetName, IReadOnlyList<ReportingSheetColumnV1> columns, IReadOnlyList<IReadOnlyList<string?>> rows)
    {
        ArgumentNullException.ThrowIfNull(columns);
        ArgumentNullException.ThrowIfNull(rows);
        var sheet = new StringBuilder();
        sheet.Append("<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>")
            .Append("<worksheet xmlns=\"http://schemas.openxmlformats.org/spreadsheetml/2006/main\"><sheetData>");
        AppendRow(sheet, 1, columns.Select(column => (ReportingTextsTrV1.Header(column.Id), false)).ToArray());
        var rowNumber = 2;
        foreach (var row in rows)
        {
            if (row.Count != columns.Count) throw new ArgumentException("Every row has one cell per column.", nameof(rows));
            AppendRow(sheet, rowNumber++, columns.Select((column, index) =>
            {
                var value = row[index];
                var numeric = value is not null && column.Kind == ReportingColumnKindV1.Number && IsInvariantNumber(value);
                return (Cell(column, value), numeric);
            }).ToArray());
        }

        sheet.Append("</sheetData></worksheet>");
        var safeSheetName = SecurityElement.Escape(sheetName.Length > 31 ? sheetName[..31] : sheetName);
        return Zip(
        [
            ("[Content_Types].xml", "<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>"
                + "<Types xmlns=\"http://schemas.openxmlformats.org/package/2006/content-types\">"
                + "<Default Extension=\"rels\" ContentType=\"application/vnd.openxmlformats-package.relationships+xml\"/>"
                + "<Default Extension=\"xml\" ContentType=\"application/xml\"/>"
                + "<Override PartName=\"/xl/workbook.xml\" ContentType=\"application/vnd.openxmlformats-officedocument.spreadsheetml.sheet.main+xml\"/>"
                + "<Override PartName=\"/xl/worksheets/sheet1.xml\" ContentType=\"application/vnd.openxmlformats-officedocument.spreadsheetml.worksheet+xml\"/>"
                + "</Types>"),
            ("_rels/.rels", "<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>"
                + "<Relationships xmlns=\"http://schemas.openxmlformats.org/package/2006/relationships\">"
                + "<Relationship Id=\"rId1\" Type=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships/officeDocument\" Target=\"xl/workbook.xml\"/>"
                + "</Relationships>"),
            ("xl/workbook.xml", "<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>"
                + "<workbook xmlns=\"http://schemas.openxmlformats.org/spreadsheetml/2006/main\" "
                + "xmlns:r=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships\">"
                + "<sheets><sheet name=\"" + safeSheetName + "\" sheetId=\"1\" r:id=\"rId1\"/></sheets></workbook>"),
            ("xl/_rels/workbook.xml.rels", "<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>"
                + "<Relationships xmlns=\"http://schemas.openxmlformats.org/package/2006/relationships\">"
                + "<Relationship Id=\"rId1\" Type=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships/worksheet\" Target=\"worksheets/sheet1.xml\"/>"
                + "</Relationships>"),
            ("xl/worksheets/sheet1.xml", sheet.ToString()),
        ]);
    }

    private static void AppendRow(StringBuilder sheet, int rowNumber, IReadOnlyList<(string Value, bool Numeric)> cells)
    {
        sheet.Append("<row r=\"").Append(rowNumber.ToString(CultureInfo.InvariantCulture)).Append("\">");
        for (var index = 0; index < cells.Count; index++)
        {
            var reference = ColumnName(index) + rowNumber.ToString(CultureInfo.InvariantCulture);
            var (value, numeric) = cells[index];
            if (value.Length == 0) continue;
            if (numeric)
            {
                sheet.Append("<c r=\"").Append(reference).Append("\"><v>").Append(value).Append("</v></c>");
            }
            else
            {
                sheet.Append("<c r=\"").Append(reference).Append("\" t=\"inlineStr\"><is><t xml:space=\"preserve\">")
                    .Append(SecurityElement.Escape(StripInvalidXml(value))).Append("</t></is></c>");
            }
        }

        sheet.Append("</row>");
    }

    private static string StripInvalidXml(string value) =>
        new(value.Where(character => character is '\t' or '\n' or '\r' || character >= ' ').ToArray());

    public static string ColumnName(int index)
    {
        var name = "";
        for (var number = index + 1; number > 0; number = (number - 1) / 26)
            name = (char)('A' + (number - 1) % 26) + name;
        return name;
    }

    /// <summary>A deterministic zip (fixed entry time, given entry order, optimal compression).</summary>
    public static byte[] Zip(IReadOnlyList<(string Name, string Content)> entries) =>
        ZipBytes(entries.Select(entry => (entry.Name, Encoding.UTF8.GetBytes(entry.Content))).ToArray());

    public static byte[] ZipBytes(IReadOnlyList<(string Name, byte[] Content)> entries)
    {
        using var stream = new MemoryStream();
        using (var archive = new ZipArchive(stream, ZipArchiveMode.Create, leaveOpen: true))
        {
            foreach (var (name, content) in entries)
            {
                var entry = archive.CreateEntry(name, CompressionLevel.Optimal);
                entry.LastWriteTime = FixedEntryTime;
                using var target = entry.Open();
                target.Write(content);
            }
        }

        return stream.ToArray();
    }
}
