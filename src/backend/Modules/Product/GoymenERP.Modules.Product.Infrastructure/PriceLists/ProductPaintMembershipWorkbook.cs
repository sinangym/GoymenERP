using System.Globalization;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Xml;
using System.Xml.Linq;
using GoymenERP.Modules.Product.Application.Contracts;

namespace GoymenERP.Modules.Product.Infrastructure.PriceLists;

/// <summary>BS-10: reads only the actual text product codes of the first logical worksheet. The owner resolves
/// those codes against existing Product cards; this parser creates no products, membership, supplier or color data.</summary>
internal static class ProductPaintMembershipWorkbook
{
    internal const int ProductCodeMaximumLength = 64;
    internal const int MaximumZipEntries = 128;
    internal const int MaximumPartBytes = 8_388_608;
    internal const int MaximumExpandedBytes = 16_777_216;
    internal const int MaximumSharedStrings = (ProductPaintMembershipLimitsV1.MaximumRows + 1) * 16;
    private const int MaximumCellCharacters = 4096;
    private const int MaximumColumnNameCharacters = 200;
    private const int MaximumWorksheetColumns = 16_384;
    private const int MaximumWorksheetRowNumber = 1_048_576;
    private static readonly XNamespace Spreadsheet = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
    private static readonly XNamespace DocumentRelationships = "http://schemas.openxmlformats.org/officeDocument/2006/relationships";
    private static readonly XNamespace PackageRelationships = "http://schemas.openxmlformats.org/package/2006/relationships";
    private static readonly XNamespace ContentTypes = "http://schemas.openxmlformats.org/package/2006/content-types";

    internal static ProductPaintMembershipWorkbookResult Read(byte[] content, string productCodeColumn)
    {
        ArgumentNullException.ThrowIfNull(content);
        var column = string.IsNullOrWhiteSpace(productCodeColumn)
            ? ProductPaintMembershipLimitsV1.DefaultProductCodeColumn : productCodeColumn.Trim();
        var hash = Convert.ToHexStringLower(SHA256.HashData(content));
        var rows = new List<ProductPaintMembershipWorkbookRow>();
        var errors = new List<ProductPaintMembershipImportErrorV1>();
        var sourceRowCount = 0;
        if (column.Length > MaximumColumnNameCharacters || column.Any(char.IsControl))
            return Refused("productCodeColumn", "Ürün kodu sütun eşlemesi geçersiz.");
        if (content.Length is 0 or > ProductPaintMembershipLimitsV1.MaximumFileBytes)
            return Refused("file", "Dosya boş olamaz ve 2 MiB sınırını aşamaz.");

        // Freeze once: the fingerprint and every parsed part must describe the same byte sequence.
        var frozen = content.ToArray();
        hash = Convert.ToHexStringLower(SHA256.HashData(frozen));
        try
        {
            var documents = ReadDocuments(frozen);
            var shared = ReadSharedStrings(documents);
            var sheet = FirstWorksheet(documents);
            if (sheet.Root?.Name != Spreadsheet + "worksheet") throw InvalidWorkbook();
            var data = sheet.Root.Elements(Spreadsheet + "sheetData").SingleOrDefault() ?? throw InvalidWorkbook();
            var worksheetRows = data.Elements(Spreadsheet + "row").Take(ProductPaintMembershipLimitsV1.MaximumRows + 2).ToArray();
            if (worksheetRows.Length > ProductPaintMembershipLimitsV1.MaximumRows + 1)
                return Refused("file", "Çalışma sayfası en fazla 5000 veri satırı içerebilir.");
            if (worksheetRows.Length == 0) return Refused("file", "İlk çalışma sayfası boş.");

            var previousRow = 0;
            int? selectedColumn = null;
            var codes = new HashSet<string>(StringComparer.Ordinal);
            foreach (var worksheetRow in worksheetRows)
            {
                var number = RowNumber(worksheetRow.Attribute("r")?.Value, previousRow + 1);
                if (number <= previousRow) throw InvalidWorkbook();
                previousRow = number;
                var cells = new Dictionary<int, Cell>();
                foreach (var element in worksheetRow.Elements(Spreadsheet + "c"))
                {
                    var address = CellColumn(element.Attribute("r")?.Value, number);
                    if (!cells.TryAdd(address, ReadCell(element, shared))) throw InvalidWorkbook();
                }

                if (selectedColumn is null)
                {
                    var headers = new HashSet<string>(StringComparer.Ordinal);
                    foreach (var (address, cell) in cells)
                    {
                        if (!cell.IsText || string.IsNullOrWhiteSpace(cell.Value)) continue;
                        var title = cell.Value.Trim();
                        if (!headers.Add(title)) return Refused("productCodeColumn", "Başlık satırında yinelenen sütun adı var.");
                        if (string.Equals(title, column, StringComparison.Ordinal)) selectedColumn = address;
                    }
                    if (selectedColumn is null) return Refused("productCodeColumn", "Eşlenen ürün kodu sütunu ilk satırda bulunamadı.");
                    continue;
                }

                if (cells.Values.All(cell => string.IsNullOrWhiteSpace(cell.Value))) continue;
                sourceRowCount++;
                var codeCell = cells.GetValueOrDefault(selectedColumn.Value);
                var code = codeCell?.Value.Trim() ?? "";
                if (code.Length == 0) RowError(number, "Ürün kodu boş olamaz.");
                else if (codeCell is not { IsText: true }) RowError(number, "Ürün kodu metin hücresi olmalıdır.");
                else if (code.Length > ProductCodeMaximumLength || code.Any(char.IsControl))
                    RowError(number, "Ürün kodu en fazla 64 karakter olmalı ve kontrol karakteri içermemelidir.");
                else if (!codes.Add(code)) RowError(number, "Aynı ürün kodu dosyada birden çok kez bulunuyor.");
                else rows.Add(new(number, code));
            }
            if (sourceRowCount == 0) return Refused("file", "Dosyada ürün kodu satırı yok.");
            return Result();
        }
        catch (Exception exception) when (exception is InvalidDataException or XmlException or IOException
            or ArgumentException or InvalidOperationException or OverflowException)
        {
            return Refused("file", "Dosya geçerli ve güvenli bir XLSX çalışma kitabı değil; formül, makro veya dış bağlantı içermemelidir.");
        }

        void RowError(int rowNumber, string message) => errors.Add(new(rowNumber, "productCode",
            ProductPaintMembershipCodesV1.InvalidFile, message));
        ProductPaintMembershipWorkbookResult Result() => new(hash, column, sourceRowCount,
            Array.AsReadOnly(rows.ToArray()), Array.AsReadOnly(errors.ToArray()));
        ProductPaintMembershipWorkbookResult Refused(string field, string message)
        {
            rows.Clear();
            errors.Clear();
            errors.Add(new(null, field, ProductPaintMembershipCodesV1.InvalidFile, message));
            return Result();
        }
    }

    private static Dictionary<string, XDocument> ReadDocuments(byte[] content)
    {
        using var stream = new MemoryStream(content, writable: false);
        using var archive = new ZipArchive(stream, ZipArchiveMode.Read);
        if (archive.Entries.Count is 0 or > MaximumZipEntries) throw InvalidWorkbook();
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var documents = new Dictionary<string, XDocument>(StringComparer.Ordinal);
        var expanded = 0;
        foreach (var entry in archive.Entries)
        {
            var path = entry.FullName;
            if (string.IsNullOrWhiteSpace(path) || path.StartsWith('/') || path.Contains('\\') || path.Contains(':')
                || path.Contains('%') || path.Contains('?') || path.Contains('#')
                || path.Split('/').Any(part => part is "." or "..") || !names.Add(path)
                || path.Contains("vba", StringComparison.OrdinalIgnoreCase)
                || path.StartsWith("xl/macrosheets/", StringComparison.OrdinalIgnoreCase)
                || path.StartsWith("xl/dialogsheets/", StringComparison.OrdinalIgnoreCase)
                || path.StartsWith("xl/activeX/", StringComparison.OrdinalIgnoreCase)
                || path.StartsWith("xl/externalLinks/", StringComparison.OrdinalIgnoreCase)
                || entry.Length > MaximumPartBytes || entry.Length > MaximumExpandedBytes - expanded)
                throw InvalidWorkbook();
            if (path.EndsWith('/')) continue;
            using var input = entry.Open();
            using var part = new MemoryStream();
            var buffer = new byte[4096];
            int read;
            while ((read = input.Read(buffer, 0, buffer.Length)) != 0)
            {
                if (part.Length + read > MaximumPartBytes || expanded + read > MaximumExpandedBytes) throw InvalidWorkbook();
                part.Write(buffer, 0, read);
                expanded += read;
            }
            if (!path.EndsWith(".xml", StringComparison.OrdinalIgnoreCase)
                && !path.EndsWith(".rels", StringComparison.OrdinalIgnoreCase)) continue;
            part.Position = 0;
            using var reader = XmlReader.Create(part, new XmlReaderSettings
            {
                DtdProcessing = DtdProcessing.Prohibit,
                XmlResolver = null,
                MaxCharactersInDocument = MaximumPartBytes,
            });
            var document = XDocument.Load(reader, LoadOptions.None);
            if (document.Descendants(Spreadsheet + "f").Any()
                || document.Descendants(Spreadsheet + "externalReferences").Any()) throw InvalidWorkbook();
            if (path.EndsWith(".rels", StringComparison.OrdinalIgnoreCase)) ValidateRelationships(path, document);
            documents.Add(path, document);
        }

        if (!documents.TryGetValue("[Content_Types].xml", out var types) || types.Root?.Name != ContentTypes + "Types")
            throw InvalidWorkbook();
        foreach (var type in types.Root.Elements())
        {
            var contentType = type.Attribute("ContentType")?.Value ?? "";
            if (contentType.Contains("macro", StringComparison.OrdinalIgnoreCase)
                || contentType.Contains("vba", StringComparison.OrdinalIgnoreCase)
                || contentType.Contains("activeX", StringComparison.OrdinalIgnoreCase)) throw InvalidWorkbook();
        }
        return documents;
    }

    private static void ValidateRelationships(string path, XDocument relationships)
    {
        if (relationships.Root?.Name != PackageRelationships + "Relationships") throw InvalidWorkbook();
        var ids = new HashSet<string>(StringComparer.Ordinal);
        foreach (var relation in relationships.Root.Elements(PackageRelationships + "Relationship"))
        {
            var id = relation.Attribute("Id")?.Value;
            if (string.IsNullOrWhiteSpace(id) || !ids.Add(id)
                || relation.Attribute("TargetMode") is { } mode && mode.Value != "Internal") throw InvalidWorkbook();
            var folder = path[..path.LastIndexOf("_rels/", StringComparison.Ordinal)];
            _ = ResolveTarget(folder, relation.Attribute("Target")?.Value);
        }
    }

    private static XDocument FirstWorksheet(IReadOnlyDictionary<string, XDocument> documents)
    {
        if (!documents.TryGetValue("xl/workbook.xml", out var workbook) || workbook.Root?.Name != Spreadsheet + "workbook"
            || !documents.TryGetValue("xl/_rels/workbook.xml.rels", out var relations)) throw InvalidWorkbook();
        var first = workbook.Root.Element(Spreadsheet + "sheets")?.Elements(Spreadsheet + "sheet").FirstOrDefault();
        var id = first?.Attribute(DocumentRelationships + "id")?.Value;
        if (string.IsNullOrWhiteSpace(id)) throw InvalidWorkbook();
        var relation = relations.Root!.Elements(PackageRelationships + "Relationship")
            .SingleOrDefault(candidate => candidate.Attribute("Id")?.Value == id) ?? throw InvalidWorkbook();
        if (!(relation.Attribute("Type")?.Value ?? "").EndsWith("/worksheet", StringComparison.Ordinal)) throw InvalidWorkbook();
        var path = ResolveTarget("xl/", relation.Attribute("Target")?.Value);
        if (!path.StartsWith("xl/", StringComparison.Ordinal) || !documents.TryGetValue(path, out var sheet)) throw InvalidWorkbook();
        return sheet;
    }

    private static string ResolveTarget(string folder, string? target)
    {
        if (string.IsNullOrWhiteSpace(target) || target.Any(char.IsControl) || target.Contains('\\')
            || target.Contains(':') || target.Contains('?') || target.Contains('#') || target.Contains('%')) throw InvalidWorkbook();
        var parts = target.StartsWith('/') ? new List<string>() : folder.Split('/', StringSplitOptions.RemoveEmptyEntries).ToList();
        foreach (var component in target.Split('/', StringSplitOptions.RemoveEmptyEntries))
        {
            if (component == ".") continue;
            if (component == "..")
            {
                if (parts.Count == 0) throw InvalidWorkbook();
                parts.RemoveAt(parts.Count - 1);
            }
            else parts.Add(component);
        }
        if (parts.Count == 0 || target.EndsWith('/')) throw InvalidWorkbook();
        return string.Join('/', parts);
    }

    private static IReadOnlyList<string> ReadSharedStrings(IReadOnlyDictionary<string, XDocument> documents)
    {
        if (!documents.TryGetValue("xl/sharedStrings.xml", out var document)) return [];
        if (document.Root?.Name != Spreadsheet + "sst") throw InvalidWorkbook();
        var values = new List<string>();
        foreach (var item in document.Root.Elements(Spreadsheet + "si"))
        {
            if (values.Count == MaximumSharedStrings) throw InvalidWorkbook();
            values.Add(ReadText(item));
        }
        return values;
    }

    private static Cell ReadCell(XElement element, IReadOnlyList<string> shared)
    {
        var type = element.Attribute("t")?.Value;
        var value = element.Elements(Spreadsheet + "v").SingleOrDefault()?.Value;
        if (type == "inlineStr")
        {
            if (value is not null) throw InvalidWorkbook();
            var text = element.Elements(Spreadsheet + "is").SingleOrDefault() ?? throw InvalidWorkbook();
            return new(ReadText(text), true);
        }
        if (type == "s")
        {
            if (!int.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out var index)
                || index < 0 || index >= shared.Count) throw InvalidWorkbook();
            return new(shared[index], true);
        }
        if ((value?.Length ?? 0) > MaximumCellCharacters) throw InvalidWorkbook();
        return new(value ?? "", type == "str");
    }

    private static string ReadText(XElement text)
    {
        // Rich-text runs contribute their text once; phonetic annotations and formatting are not product codes.
        var fragments = new List<string>();
        foreach (var child in text.Elements())
        {
            if (child.Name == Spreadsheet + "t") fragments.Add(child.Value);
            else if (child.Name == Spreadsheet + "r")
                fragments.AddRange(child.Elements(Spreadsheet + "t").Select(item => item.Value));
        }
        var value = string.Concat(fragments);
        if (value.Length > MaximumCellCharacters) throw InvalidWorkbook();
        return value;
    }

    private static int RowNumber(string? text, int fallback)
    {
        if (text is null) return fallback;
        if (!int.TryParse(text, NumberStyles.None, CultureInfo.InvariantCulture, out var value)
            || value is < 1 or > MaximumWorksheetRowNumber) throw InvalidWorkbook();
        return value;
    }

    private static int CellColumn(string? reference, int row)
    {
        if (string.IsNullOrEmpty(reference)) throw InvalidWorkbook();
        var index = 0;
        var column = 0;
        while (index < reference.Length && reference[index] is >= 'A' and <= 'Z')
            column = checked(column * 26 + reference[index++] - 'A' + 1);
        if (column is < 1 or > MaximumWorksheetColumns || index == reference.Length
            || !int.TryParse(reference[index..], NumberStyles.None, CultureInfo.InvariantCulture, out var actualRow)
            || actualRow != row || reference[index] == '0') throw InvalidWorkbook();
        return column;
    }

    private static InvalidDataException InvalidWorkbook() => new("XLSX yapısı geçersiz.");
    private sealed record Cell(string Value, bool IsText);
}

internal sealed record ProductPaintMembershipWorkbookRow(int RowNumber, string ProductCode);
internal sealed record ProductPaintMembershipWorkbookResult(string FileSha256, string ProductCodeColumn, int SourceRowCount,
    IReadOnlyList<ProductPaintMembershipWorkbookRow> Rows, IReadOnlyList<ProductPaintMembershipImportErrorV1> Errors)
{
    public bool Ready => SourceRowCount > 0 && Rows.Count == SourceRowCount && Errors.Count == 0;
}
