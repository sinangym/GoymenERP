using System.Globalization;
using System.IO.Compression;
using System.Text;
using System.Xml;
using System.Xml.Linq;
using GoymenERP.Modules.Product.Application.Contracts;

namespace GoymenERP.Modules.Product.Infrastructure.PriceLists;

/// <summary>
/// URN-D18: reads the first worksheet of a kg/m table workbook (<c>.xlsx</c>, <see cref="ProductProfileWeightFileKindV1"/>) into
/// the same source lines as the CSV, with the platform's own ZIP and XML readers (no third-party parser, no DTD, no external
/// resolver). The content proves the kind: a ZIP whose package names a plain workbook (not a macro-enabled one or a template),
/// with no VBA project and no external link. A formula cell refuses the file (reversible detail: no cached result is trusted).
/// Every part is read within <see cref="ProductProfileWeightInputLimitsV1.MaximumWorkbookPartBytes"/>.
/// </summary>
internal static class ProductProfileWeightWorkbook
{
    private const string MainNamespace = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
    private const string RelationshipNamespace = "http://schemas.openxmlformats.org/officeDocument/2006/relationships";
    private const string PackageRelationshipNamespace = "http://schemas.openxmlformats.org/package/2006/relationships";
    private const string ContentTypesNamespace = "http://schemas.openxmlformats.org/package/2006/content-types";
    private const string OfficeDocumentRelationship = RelationshipNamespace + "/officeDocument";
    private const string SharedStringsRelationship = RelationshipNamespace + "/sharedStrings";
    private const string WorkbookMainContentType = "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet.main+xml";
    private const string ContentTypesPart = "[Content_Types].xml";
    private const string PackageRelationshipsPart = "_rels/.rels";
    private const string NotWorkbook = "Dosya içeriği .xlsx uzantısıyla uyuşmuyor; geçerli bir Excel çalışma kitabı (.xlsx) olmalıdır.";
    private const string Unreadable = "Excel çalışma kitabı okunamadı.";
    private const int ColumnLetterCount = 26;
    private const int ReadChunkBytes = 81_920;

    private static readonly XNamespace Main = MainNamespace;
    private static readonly XNamespace Relationships = RelationshipNamespace;
    private static readonly XNamespace PackageRelationships = PackageRelationshipNamespace;
    private static readonly XNamespace ContentTypes = ContentTypesNamespace;
    private static readonly byte[] ZipSignature = [0x50, 0x4B, 0x03, 0x04];

    private static readonly XmlReaderSettings XmlSettings = new()
    {
        DtdProcessing = DtdProcessing.Prohibit,
        XmlResolver = null,
        IgnoreComments = true,
        IgnoreProcessingInstructions = true,
        CloseInput = false,
    };

    /// <summary>A workbook (and any ZIP) starts with a local file header; a CSV never does.</summary>
    internal static bool HasZipSignature(byte[] content)
    {
        ArgumentNullException.ThrowIfNull(content);
        return content.AsSpan().StartsWith(ZipSignature);
    }

    /// <summary>Null and the non-empty rows of the first worksheet (row number, columns A… as text); otherwise the reason.</summary>
    internal static string? Lines(byte[] content, int fieldCount, out IReadOnlyList<SourceLine> lines)
    {
        ArgumentNullException.ThrowIfNull(content);
        lines = [];
        if (!HasZipSignature(content))
        {
            return NotWorkbook;
        }

        try
        {
            using var archive = new ZipArchive(new MemoryStream(content, writable: false), ZipArchiveMode.Read);
            return Read(archive, fieldCount, out lines);
        }
        catch (Exception exception) when (exception is InvalidDataException or XmlException or IOException
                                              or NotSupportedException or FormatException or OverflowException)
        {
            lines = [];
            return Unreadable;
        }
    }

    private static string? Read(ZipArchive archive, int fieldCount, out IReadOnlyList<SourceLine> lines)
    {
        lines = [];
        foreach (var entry in archive.Entries)
        {
            var name = entry.FullName;
            if (name.EndsWith("vbaProject.bin", StringComparison.OrdinalIgnoreCase)
                || name.Contains("/externalLinks/", StringComparison.OrdinalIgnoreCase))
            {
                return "Makro veya dış bağlantı içeren çalışma kitabı kabul edilmez (XLSM/XLS değil, düz .xlsx olmalıdır).";
            }
        }

        if (Load(archive, PackageRelationshipsPart) is not { } packageRelationships)
        {
            return NotWorkbook;
        }

        var workbookPath = packageRelationships.Root?.Elements(PackageRelationships + "Relationship")
            .Where(relationship => (string?)relationship.Attribute("Type") == OfficeDocumentRelationship
                && (string?)relationship.Attribute("TargetMode") is null or "Internal")
            .Select(relationship => Resolve(string.Empty, (string?)relationship.Attribute("Target")))
            .FirstOrDefault();
        if (workbookPath is null || Load(archive, ContentTypesPart) is not { } contentTypes)
        {
            return NotWorkbook;
        }

        var workbookType = contentTypes.Root?.Elements(ContentTypes + "Override")
            .Where(part => string.Equals((string?)part.Attribute("PartName"), "/" + workbookPath, StringComparison.OrdinalIgnoreCase))
            .Select(part => (string?)part.Attribute("ContentType"))
            .FirstOrDefault();
        if (!string.Equals(workbookType, WorkbookMainContentType, StringComparison.Ordinal))
        {
            // A macro-enabled workbook (XLSM), a template or another package names another main content type.
            return "Yalnız düz Excel çalışma kitabı (.xlsx) kabul edilir; makrolu (XLSM), şablon veya eski (XLS) dosya kabul edilmez.";
        }

        if (Load(archive, workbookPath)?.Root is not { } workbook || workbook.Name != Main + "workbook")
        {
            return NotWorkbook;
        }

        var workbookDirectory = PartDirectory(workbookPath);
        var workbookRelationships = Load(archive, RelationshipsPathOf(workbookPath));
        var relationships = workbookRelationships?.Root?.Elements(PackageRelationships + "Relationship").ToList() ?? [];
        if (relationships.Any(relationship => string.Equals((string?)relationship.Attribute("TargetMode"), "External",
                StringComparison.OrdinalIgnoreCase)))
        {
            return "Makro veya dış bağlantı içeren çalışma kitabı kabul edilmez (XLSM/XLS değil, düz .xlsx olmalıdır).";
        }

        var firstSheetId = workbook.Element(Main + "sheets")?.Elements(Main + "sheet")
            .Select(sheet => (string?)sheet.Attribute(Relationships + "id"))
            .FirstOrDefault();
        var sheetPath = relationships
            .Where(relationship => firstSheetId is not null && (string?)relationship.Attribute("Id") == firstSheetId)
            .Select(relationship => Resolve(workbookDirectory, (string?)relationship.Attribute("Target")))
            .FirstOrDefault();
        if (sheetPath is null || Load(archive, sheetPath)?.Root is not { } sheet || sheet.Name != Main + "worksheet")
        {
            return "Çalışma kitabının ilk sayfası okunamadı.";
        }

        var sharedStrings = new List<string>();
        var sharedStringsPath = relationships
            .Where(relationship => (string?)relationship.Attribute("Type") == SharedStringsRelationship)
            .Select(relationship => Resolve(workbookDirectory, (string?)relationship.Attribute("Target")))
            .FirstOrDefault();
        if (sharedStringsPath is not null)
        {
            if (Load(archive, sharedStringsPath) is not { } strings)
            {
                return Unreadable;
            }

            sharedStrings.AddRange(strings.Root?.Elements(Main + "si").Select(Text) ?? []);
        }

        return Rows(sheet, sharedStrings, fieldCount, out lines);
    }

    private static string? Rows(XElement sheet, IReadOnlyList<string> sharedStrings, int fieldCount, out IReadOnlyList<SourceLine> lines)
    {
        lines = [];
        var parsed = new List<SourceLine>();
        var previousRow = 0;
        foreach (var row in sheet.Element(Main + "sheetData")?.Elements(Main + "row") ?? [])
        {
            var rowNo = int.TryParse((string?)row.Attribute("r"), NumberStyles.None, CultureInfo.InvariantCulture, out var number)
                && number > previousRow
                    ? number
                    : previousRow + 1;
            previousRow = rowNo;
            var cells = new SortedDictionary<int, string>();
            var previousColumn = 0;
            foreach (var cell in row.Elements(Main + "c"))
            {
                var column = Column((string?)cell.Attribute("r")) is { } parsedColumn && parsedColumn > previousColumn
                    ? parsedColumn
                    : previousColumn + 1;
                previousColumn = column;
                if (cell.Element(Main + "f") is not null)
                {
                    return $"Satır {rowNo}: formül hücresi kabul edilmez; hücreye değeri yazın.";
                }

                if (CellText(cell, sharedStrings) is not { } text)
                {
                    return $"Satır {rowNo}: hücre metin veya sayı olmalıdır.";
                }

                if (text.Trim().Length > 0)
                {
                    cells[column] = text.Trim();
                }
            }

            if (cells.Count == 0)
            {
                continue;
            }

            var width = Math.Max(fieldCount, cells.Keys.Max());
            var fields = new string[width];
            for (var index = 0; index < width; index++)
            {
                fields[index] = cells.TryGetValue(index + 1, out var value) ? value : string.Empty;
            }

            parsed.Add(new SourceLine(rowNo, fields));
            if (parsed.Count > ProductProfileWeightInputLimitsV1.MaximumRows + 1)
            {
                return $"Dosya en fazla {ProductProfileWeightInputLimitsV1.MaximumRows} satır içerebilir.";
            }
        }

        lines = parsed;
        return null;
    }

    /// <summary>
    /// The text of one value cell, or null for a kind the table never holds (boolean, error, date). A number is the exact double
    /// Excel stored, written as its shortest round-trip text, so the column scale rules decide (nothing is rounded to fit).
    /// </summary>
    private static string? CellText(XElement cell, IReadOnlyList<string> sharedStrings)
    {
        var type = (string?)cell.Attribute("t") ?? "n";
        var value = (string?)cell.Element(Main + "v");
        switch (type)
        {
            case "s":
                if (value is null)
                {
                    return string.Empty;
                }

                return int.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out var index)
                    && index < sharedStrings.Count
                        ? sharedStrings[index]
                        : null;
            case "inlineStr":
                return cell.Element(Main + "is") is { } inline ? Text(inline) : string.Empty;
            case "str":
                return value ?? string.Empty;
            case "n":
                if (string.IsNullOrWhiteSpace(value))
                {
                    return string.Empty;
                }

                return double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var number) && double.IsFinite(number)
                    ? number.ToString("R", CultureInfo.InvariantCulture)
                    : null;
            default:
                return null;
        }
    }

    /// <summary>The text of a shared or inline string: its plain text runs, without phonetic hints.</summary>
    private static string Text(XElement item)
    {
        var text = new StringBuilder();
        foreach (var child in item.Elements())
        {
            if (child.Name == Main + "t")
            {
                text.Append(child.Value);
            }
            else if (child.Name == Main + "r" && child.Element(Main + "t") is { } run)
            {
                text.Append(run.Value);
            }
        }

        return text.ToString();
    }

    /// <summary>The 1-based column of a cell reference such as <c>B12</c>; null when it has none.</summary>
    private static int? Column(string? reference)
    {
        if (string.IsNullOrEmpty(reference))
        {
            return null;
        }

        var column = 0;
        foreach (var character in reference)
        {
            if (!char.IsAsciiLetterUpper(character))
            {
                break;
            }

            column = checked((column * ColumnLetterCount) + (character - 'A' + 1));
        }

        return column == 0 ? null : column;
    }

    /// <summary>One package part parsed without DTD or resolver, within the part bound; null when it is missing or too large.</summary>
    private static XDocument? Load(ZipArchive archive, string path)
    {
        var entry = archive.GetEntry(path);
        if (entry is null || entry.Length > ProductProfileWeightInputLimitsV1.MaximumWorkbookPartBytes)
        {
            return null;
        }

        using var source = entry.Open();
        using var buffer = new MemoryStream();
        var chunk = new byte[ReadChunkBytes];
        long total = 0;
        int read;
        while ((read = source.Read(chunk, 0, chunk.Length)) > 0)
        {
            total += read;
            if (total > ProductProfileWeightInputLimitsV1.MaximumWorkbookPartBytes)
            {
                return null;
            }

            buffer.Write(chunk, 0, read);
        }

        buffer.Position = 0;
        using var reader = XmlReader.Create(buffer, XmlSettings);
        return XDocument.Load(reader, LoadOptions.None);
    }

    private static string PartDirectory(string partPath)
    {
        var slash = partPath.LastIndexOf('/');
        return slash < 0 ? string.Empty : partPath[..slash];
    }

    private static string RelationshipsPathOf(string partPath)
    {
        var directory = PartDirectory(partPath);
        var name = partPath[(directory.Length == 0 ? 0 : directory.Length + 1)..];
        return (directory.Length == 0 ? string.Empty : directory + "/") + "_rels/" + name + ".rels";
    }

    /// <summary>A relationship target as a package path (no leading slash, <c>..</c> folded); null when it leaves the package.</summary>
    private static string? Resolve(string baseDirectory, string? target)
    {
        if (string.IsNullOrWhiteSpace(target))
        {
            return null;
        }

        var combined = target.StartsWith('/') ? target[1..] : (baseDirectory.Length == 0 ? target : baseDirectory + "/" + target);
        var segments = new List<string>();
        foreach (var segment in combined.Split('/'))
        {
            if (segment.Length == 0 || segment == ".")
            {
                continue;
            }

            if (segment == "..")
            {
                if (segments.Count == 0)
                {
                    return null;
                }

                segments.RemoveAt(segments.Count - 1);
                continue;
            }

            segments.Add(segment);
        }

        return segments.Count == 0 ? null : string.Join('/', segments);
    }
}
