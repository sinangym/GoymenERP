using System.Globalization;
using System.IO.Compression;
using System.Xml.Linq;
using GoymenERP.Modules.Purchasing.Application.Contracts;

namespace GoymenERP.Modules.Purchasing.Infrastructure.Imports;

public sealed partial class PurchaseInvoiceFileReader
{
    private static readonly XNamespace SheetNs = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
    private static readonly XNamespace PackageRelNs = "http://schemas.openxmlformats.org/package/2006/relationships";
    private static readonly XNamespace OfficeRelNs = "http://schemas.openxmlformats.org/officeDocument/2006/relationships";

    private static IReadOnlyList<IReadOnlyList<string>> ReadWorkbook(byte[] bytes, string? selectedWorksheet, IReadOnlyDictionary<string, string>? mapping)
    {
        using var buffer = new MemoryStream(bytes, writable: false);
        using var archive = new ZipArchive(buffer, ZipArchiveMode.Read);
        if (archive.Entries.Count is 0 or > PurchaseInvoiceFileBoundsV1.MaximumArchiveEntries
            || archive.Entries.Select(entry => entry.FullName).Distinct(StringComparer.OrdinalIgnoreCase).Count() != archive.Entries.Count
            || archive.Entries.Sum(entry => entry.Length) > PurchaseInvoiceFileBoundsV1.MaximumExpandedBytes)
            throw Invalid("workbook", "archive_limit");
        foreach (var entry in archive.Entries)
        {
            var name = entry.FullName;
            if (name.StartsWith('/') || name.Contains('\\') || name.Contains(':') || name.Split('/').Any(part => part is "." or "..")
                || new[] { "vbaProject", "macrosheets", "activeX", "embeddings", "externalLinks" }.Any(marker => name.Contains(marker, StringComparison.OrdinalIgnoreCase)))
                throw Invalid("workbook", "active_or_unsafe_part");
            if (name.EndsWith(".rels", StringComparison.OrdinalIgnoreCase))
            {
                var relationships = Part(entry);
                if (relationships.Descendants(PackageRelNs + "Relationship")
                    .Any(relation => string.Equals(relation.Attribute("TargetMode")?.Value, "External", StringComparison.OrdinalIgnoreCase)))
                    throw Invalid("workbook", "external_relationship");
            }
        }
        var contentTypes = Part(Entry(archive, "[Content_Types].xml"));
        if (contentTypes.Descendants().Attributes("ContentType").Any(type =>
            type.Value.Contains("macroEnabled", StringComparison.OrdinalIgnoreCase) || type.Value.Contains("vba", StringComparison.OrdinalIgnoreCase)
            || type.Value.Contains("oleObject", StringComparison.OrdinalIgnoreCase))) throw Invalid("workbook", "active_or_unsafe_part");
        var workbook = Part(Entry(archive, "xl/workbook.xml"));
        if (workbook.Root?.Name != SheetNs + "workbook") throw Invalid("workbook", "workbook_invalid");
        var sheets = workbook.Root.Element(SheetNs + "sheets")?.Elements(SheetNs + "sheet").ToArray() ?? [];
        if (sheets.Length == 0 || sheets.Select(sheet => sheet.Attribute("name")?.Value).Distinct(StringComparer.Ordinal).Count() != sheets.Length)
            throw Invalid("worksheet", "worksheet_invalid");
        var selected = selectedWorksheet is null ? sheets.Length == 1 ? sheets[0] : null
            : sheets.SingleOrDefault(sheet => sheet.Attribute("name")?.Value == selectedWorksheet);
        if (selected is null) throw Invalid("worksheet", "worksheet_selection_required");
        var relationId = selected.Attribute(OfficeRelNs + "id")?.Value;
        var relations = Part(Entry(archive, "xl/_rels/workbook.xml.rels")).Root?.Elements(PackageRelNs + "Relationship")
            .Where(relation => relation.Attribute("Id")?.Value == relationId).ToArray() ?? [];
        if (relationId is null || relations.Length != 1
            || relations[0].Attribute("Type")?.Value != OfficeRelNs.NamespaceName + "/worksheet")
            throw Invalid("worksheet", "worksheet_relationship_invalid");
        var target = relations[0].Attribute("Target")?.Value ?? string.Empty;
        var path = target.StartsWith("/xl/", StringComparison.Ordinal) ? target[1..] : "xl/" + target;
        if (!path.StartsWith("xl/worksheets/", StringComparison.Ordinal) || path.Contains('\\')
            || path.Split('/').Any(part => part is "" or "." or "..")) throw Invalid("worksheet", "worksheet_relationship_invalid");

        var shared = archive.GetEntry("xl/sharedStrings.xml") is { } sharedEntry
            ? Part(sharedEntry).Root?.Elements(SheetNs + "si").Select(CellText).ToArray() ?? [] : [];
        if (shared.Length > (PurchaseInvoiceFileBoundsV1.MaximumRows + 1) * PurchaseInvoiceFileBoundsV1.MaximumColumns)
            throw Invalid("workbook", "shared_string_limit");
        var document = Part(Entry(archive, path));
        if (document.Root?.Name != SheetNs + "worksheet") throw Invalid("worksheet", "worksheet_invalid");
        var data = document.Root.Element(SheetNs + "sheetData") ?? throw Invalid("worksheet", "worksheet_empty");
        var result = new List<IReadOnlyList<string>>(); var lastRowNumber = 0; var width = 0;
        var identityHeaders = new[] { "Number", "Ettn", "SupplierTaxId", "BuyerTaxId", "SellerCode", "BuyerCode", "LineId" }
            .Where(field => mapping?.ContainsKey(field) == true).Select(field => mapping![field]).ToHashSet(StringComparer.Ordinal);
        var textColumns = new HashSet<int>();
        foreach (var row in data.Elements(SheetNs + "row"))
        {
            var number = PositiveInt(row.Attribute("r")?.Value);
            if (number <= lastRowNumber || number > PurchaseInvoiceFileBoundsV1.MaximumRows + 1) throw Invalid("rows", "row_limit");
            lastRowNumber = number;
            var cells = new SortedDictionary<int, string>();
            foreach (var cell in row.Elements(SheetNs + "c"))
            {
                if (cell.Elements(SheetNs + "f").Any()) throw Invalid("workbook", "formula_not_supported");
                var reference = cell.Attribute("r")?.Value ?? string.Empty;
                var letters = new string(reference.TakeWhile(c => c is >= 'A' and <= 'Z').ToArray());
                var column = letters.Aggregate(0, (value, c) => checked(value * 26 + c - 'A' + 1));
                if (column is < 1 or > PurchaseInvoiceFileBoundsV1.MaximumColumns
                    || PositiveInt(reference[letters.Length..]) != number || cells.ContainsKey(column))
                    throw Invalid("cell", "cell_reference_invalid");
                var type = cell.Attribute("t")?.Value;
                var scalar = Maybe(cell, SheetNs + "v")?.Value ?? string.Empty;
                if (result.Count > 0 && textColumns.Contains(column) && type is not ("s" or "inlineStr" or "str") && scalar.Length != 0)
                    throw Invalid("cell", "identity_text_required");
                var value = type switch
                {
                    "s" => int.TryParse(scalar, NumberStyles.None, CultureInfo.InvariantCulture, out var index)
                        && index >= 0 && index < shared.Length ? shared[index] : throw Invalid("cell", "shared_string_invalid"),
                    "inlineStr" => CellText(One(cell, SheetNs + "is")),
                    null or "n" or "str" => scalar,
                    _ => throw Invalid("cell", "cell_type_unsupported"),
                };
                if (value.Length > PurchaseInvoiceFileBoundsV1.MaximumCellCharacters || value.Any(char.IsControl))
                    throw Invalid("cell", "cell_text_invalid");
                cells.Add(column, value);
            }
            // Empty physical rows are not inferred as invoice lines; a populated row after a gap keeps source order.
            if (cells.Count == 0 || cells.Values.All(string.IsNullOrEmpty)) continue;
            if (result.Count == 0)
            {
                width = cells.Keys.Max();
                foreach (var cell in cells.Where(cell => identityHeaders.Contains(cell.Value))) textColumns.Add(cell.Key);
            }
            if (cells.Keys.Max() > width) throw Invalid("row", "row_shape_invalid");
            result.Add(Enumerable.Range(1, width).Select(column => cells.GetValueOrDefault(column, string.Empty)).ToArray());
        }
        return result;
    }

    private static ZipArchiveEntry Entry(ZipArchive archive, string name) => archive.GetEntry(name) ?? throw Invalid("workbook", "part_missing");
    private static XDocument Part(ZipArchiveEntry entry)
    {
        if (entry.Length > PurchaseInvoiceFileBoundsV1.MaximumExpandedBytes) throw Invalid("workbook", "archive_limit");
        using var stream = entry.Open();
        return ReadXml(stream, PurchaseInvoiceFileBoundsV1.MaximumExpandedBytes);
    }
    private static string CellText(XElement value)
    {
        var direct = value.Elements(SheetNs + "t").ToArray();
        var runs = value.Elements(SheetNs + "r").ToArray();
        if (direct.Length > 1 || direct.Length != 0 && runs.Length != 0 || direct.Any(item => item.HasElements)
            || runs.Any(run => run.Elements(SheetNs + "t").Count() != 1 || run.Element(SheetNs + "t")!.HasElements))
            throw Invalid("cell", "cell_text_invalid");
        var text = string.Concat(direct.Select(item => item.Value)
            .Concat(runs.Select(run => run.Element(SheetNs + "t")!.Value)));
        if (text.Length > PurchaseInvoiceFileBoundsV1.MaximumCellCharacters || text.Any(char.IsControl)) throw Invalid("cell", "cell_text_invalid");
        return text;
    }
    private static int PositiveInt(string? value) => int.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out var number)
        && number > 0 ? number : throw Invalid("cell", "cell_reference_invalid");
}
