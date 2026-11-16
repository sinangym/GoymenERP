using System.Globalization;
using System.IO.Compression;
using System.Text;
using System.Xml;
using GoymenERP.Modules.Product.Application.Contracts;

namespace GoymenERP.Modules.Product.Infrastructure.PriceLists;

/// <summary>
/// BS-10 (STK-S12): reads the package standard rows from the first worksheet of an <c>.xlsx</c> file
/// (<see cref="ProductPackageStandardFileFormatV2"/>). Deterministic and minimal: it never executes formulas, macros or external
/// links (a formula cell refuses the file), resolves the first sheet only inside the package, prohibits DTDs and keeps only the
/// two mapped cells of a row. Product codes are resolved by the caller against the shared card.
/// </summary>
internal static class ProductPackageStandardWorkbook
{
    /// <summary>The product card code column's storage length (the shared card's <c>Code</c>).</summary>
    internal const int ProductCodeMaximumLength = 64;

    // Shared strings beyond this bound are not read; a cell pointing past it refuses the file (the mapped cells of every accepted
    // row plus a margin for ignored columns).
    private const int MaximumSharedStrings = (ProductPackageStandardInputLimitsV2.MaximumRows + 1) * 16;

    internal static PackageStandardWorkbookResult Read(byte[] content)
    {
        ArgumentNullException.ThrowIfNull(content);
        var errors = new List<string>();
        var rows = new List<PackageStandardSourceRow>();
        try
        {
            using var stream = new MemoryStream(content, writable: false);
            using var archive = new ZipArchive(stream, ZipArchiveMode.Read, leaveOpen: false);
            var shared = ReadSharedStrings(archive);
            int? codeColumn = null;
            int? piecesColumn = null;
            var headerSeen = false;
            var dataRows = 0;
            var seen = new HashSet<string>(StringComparer.Ordinal);
            foreach (var row in ReadFirstSheet(archive, shared))
            {
                if (!headerSeen)
                {
                    headerSeen = true;
                    foreach (var cell in row.Cells.Where(cell => cell.IsLosslessText))
                    {
                        var title = cell.Value.Trim();
                        if (string.Equals(title, ProductPackageStandardFileFormatV2.ProductCodeHeader, StringComparison.Ordinal))
                        {
                            if (codeColumn is not null) return Refused("Başlık satırında 'Ürün kodu' kolonu yineleniyor.");
                            codeColumn = cell.Column;
                        }
                        else if (string.Equals(title, ProductPackageStandardFileFormatV2.PiecesPerPackageHeader,
                                     StringComparison.Ordinal))
                        {
                            if (piecesColumn is not null) return Refused("Başlık satırında 'Paket içi adet' kolonu yineleniyor.");
                            piecesColumn = cell.Column;
                        }
                    }

                    if (codeColumn is null || piecesColumn is null)
                    {
                        return Refused(string.Create(CultureInfo.InvariantCulture,
                            $"İlk çalışma sayfasının ilk satırı '{ProductPackageStandardFileFormatV2.ProductCodeHeader}' ve "
                            + $"'{ProductPackageStandardFileFormatV2.PiecesPerPackageHeader}' kolon başlıklarını içermelidir."));
                    }

                    continue;
                }

                var codeCell = row.Cells.FirstOrDefault(cell => cell.Column == codeColumn);
                var piecesCell = row.Cells.FirstOrDefault(cell => cell.Column == piecesColumn);
                var code = (codeCell?.Value ?? string.Empty).Trim();
                var piecesText = (piecesCell?.Value ?? string.Empty).Trim();
                if (code.Length == 0 && piecesText.Length == 0)
                {
                    continue;
                }

                dataRows++;
                if (dataRows > ProductPackageStandardInputLimitsV2.MaximumRows)
                {
                    return Refused(string.Create(CultureInfo.InvariantCulture,
                        $"Dosya en fazla {ProductPackageStandardInputLimitsV2.MaximumRows} veri satırı içerebilir."));
                }

                var before = errors.Count;
                if (code.Length == 0)
                {
                    errors.Add(RowError(row.Number, "ürün kodu boş."));
                }
                else if (codeCell is not { IsLosslessText: true })
                {
                    errors.Add(RowError(row.Number, "ürün kodu metin hücresi olmalıdır."));
                }
                else if (code.Length > ProductCodeMaximumLength)
                {
                    errors.Add(RowError(row.Number, "ürün kodu çok uzun."));
                }
                else if (!seen.Add(code))
                {
                    errors.Add(RowError(row.Number, $"'{code}' ürün kodu dosyada birden çok kez bulunuyor."));
                }

                if (!TryParsePieces(piecesText, out var pieces))
                {
                    errors.Add(RowError(row.Number, "paket içi adet pozitif tam sayı olmalıdır."));
                }

                if (errors.Count == before)
                {
                    rows.Add(new PackageStandardSourceRow(row.Number, code, pieces));
                }
            }

            if (!headerSeen)
            {
                return Refused("Çalışma sayfası boş.");
            }

            if (dataRows == 0)
            {
                return Refused("Dosyada veri satırı yok.");
            }

            return new PackageStandardWorkbookResult(rows, errors);
        }
        catch (Exception exception) when (exception is InvalidDataException or XmlException or IOException or FormatException
                                          or ArgumentException)
        {
            return Refused("Dosya geçerli bir .xlsx çalışma kitabı değil veya formül hücresi içeriyor.");
        }
    }

    /// <summary>A positive whole number written without sign, decimal mark, exponent or leading zero.</summary>
    internal static bool TryParsePieces(string text, out int pieces)
    {
        pieces = 0;
        return text.Length > 0 && text.All(char.IsAsciiDigit) && text[0] != '0'
            && int.TryParse(text, NumberStyles.None, CultureInfo.InvariantCulture, out pieces) && pieces > 0;
    }

    internal static string RowError(int rowNumber, string message) =>
        string.Create(CultureInfo.InvariantCulture, $"Satır {rowNumber}: {message}");

    private static PackageStandardWorkbookResult Refused(string message) => new([], [message]);

    private static IReadOnlyList<string> ReadSharedStrings(ZipArchive archive)
    {
        var entry = archive.GetEntry("xl/sharedStrings.xml");
        if (entry is null) return [];
        using var reader = XmlReader.Create(entry.Open(), SafeXml());
        var values = new List<string>();
        while (reader.Read())
        {
            if (reader.NodeType != XmlNodeType.Element || reader.LocalName != "si") continue;
            if (values.Count == MaximumSharedStrings) break;
            using var item = reader.ReadSubtree();
            var value = new StringBuilder();
            while (item.Read())
                if (item.NodeType == XmlNodeType.Element && item.LocalName == "t")
                    value.Append(item.ReadElementContentAsString());
            values.Add(value.ToString());
        }

        return values;
    }

    private static IEnumerable<SheetRow> ReadFirstSheet(ZipArchive archive, IReadOnlyList<string> shared)
    {
        var entry = FirstWorksheet(archive);
        using var reader = XmlReader.Create(entry.Open(), SafeXml());
        var cells = new List<SheetCell>();
        var rowNumber = 0;
        while (reader.Read())
        {
            if (reader.NodeType == XmlNodeType.Element && reader.LocalName == "row")
            {
                cells.Clear();
                rowNumber = int.TryParse(reader.GetAttribute("r"), NumberStyles.None, CultureInfo.InvariantCulture, out var parsed)
                    ? parsed : rowNumber + 1;
                if (reader.IsEmptyElement)
                {
                    yield return new SheetRow(rowNumber, []);
                }
            }
            else if (reader.NodeType == XmlNodeType.Element && reader.LocalName == "c")
            {
                var reference = reader.GetAttribute("r") ?? throw new InvalidDataException("Hücre başvurusu yok.");
                var type = reader.GetAttribute("t");
                var column = Column(reference);
                var value = ReadCell(reader.ReadSubtree(), type, shared);
                if (cells.Any(cell => cell.Column == column))
                {
                    throw new InvalidDataException("Aynı hücre bir satırda yineleniyor.");
                }

                cells.Add(new SheetCell(column, value.Value, value.IsLosslessText));
            }
            else if (reader.NodeType == XmlNodeType.EndElement && reader.LocalName == "row")
            {
                yield return new SheetRow(rowNumber, cells.ToArray());
            }
        }
    }

    private static ZipArchiveEntry FirstWorksheet(ZipArchive archive)
    {
        const string relationshipsNamespace = "http://schemas.openxmlformats.org/officeDocument/2006/relationships";
        var workbook = archive.GetEntry("xl/workbook.xml")
            ?? throw new InvalidDataException("Çalışma kitabı tanımı bulunamadı.");
        string? relationshipId = null;
        using (var reader = XmlReader.Create(workbook.Open(), SafeXml()))
            while (reader.Read())
                if (reader.NodeType == XmlNodeType.Element && reader.LocalName == "sheet")
                {
                    relationshipId = reader.GetAttribute("id", relationshipsNamespace);
                    break;
                }

        if (string.IsNullOrWhiteSpace(relationshipId))
            throw new InvalidDataException("İlk çalışma sayfası ilişkisi bulunamadı.");

        var relationships = archive.GetEntry("xl/_rels/workbook.xml.rels")
            ?? throw new InvalidDataException("Çalışma kitabı ilişkileri bulunamadı.");
        string? target = null;
        using (var reader = XmlReader.Create(relationships.Open(), SafeXml()))
            while (reader.Read())
                if (reader.NodeType == XmlNodeType.Element && reader.LocalName == "Relationship"
                    && string.Equals(reader.GetAttribute("Id"), relationshipId, StringComparison.Ordinal))
                {
                    if (string.Equals(reader.GetAttribute("TargetMode"), "External", StringComparison.OrdinalIgnoreCase)
                        || !(reader.GetAttribute("Type") ?? string.Empty).EndsWith("/worksheet", StringComparison.Ordinal))
                        throw new InvalidDataException("İlk çalışma sayfası ilişkisi geçersiz.");
                    target = reader.GetAttribute("Target");
                    break;
                }

        var path = ResolveWorkbookTarget(target);
        return archive.GetEntry(path) ?? throw new InvalidDataException("İlk çalışma sayfası bulunamadı.");
    }

    private static string ResolveWorkbookTarget(string? target)
    {
        if (string.IsNullOrWhiteSpace(target) || target.Contains('\\')
            || target.Contains(':') || target.Contains('?') || target.Contains('#'))
            throw new InvalidDataException("Çalışma sayfası hedefi geçersiz.");
        var parts = new List<string>();
        if (!target.StartsWith("/", StringComparison.Ordinal)) parts.Add("xl");
        foreach (var part in target.Split('/', StringSplitOptions.RemoveEmptyEntries))
        {
            if (part == ".") continue;
            if (part == "..")
            {
                if (parts.Count == 0) throw new InvalidDataException("Çalışma sayfası hedefi paket dışına çıkıyor.");
                parts.RemoveAt(parts.Count - 1);
                continue;
            }

            parts.Add(part);
        }

        var path = string.Join('/', parts);
        if (!path.StartsWith("xl/", StringComparison.Ordinal) || path.EndsWith("/", StringComparison.Ordinal))
            throw new InvalidDataException("Çalışma sayfası hedefi geçersiz.");
        return path;
    }

    private static SheetValue ReadCell(XmlReader reader, string? type, IReadOnlyList<string> shared)
    {
        string? value = null;
        var inline = new StringBuilder();
        var formula = false;
        while (reader.Read())
        {
            if (reader.NodeType == XmlNodeType.Element && reader.LocalName == "f") formula = true;
            else if (reader.NodeType == XmlNodeType.Element && reader.LocalName == "v") value = reader.ReadElementContentAsString();
            else if (reader.NodeType == XmlNodeType.Element && reader.LocalName == "t") inline.Append(reader.ReadElementContentAsString());
        }

        if (formula) throw new InvalidDataException("Formül hücreleri içe aktarılamaz.");
        if (type == "inlineStr") return new SheetValue(inline.ToString(), true);
        if (type == "s")
        {
            if (!int.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out var index)
                || index < 0 || index >= shared.Count)
                throw new InvalidDataException("Paylaşılan metin başvurusu sınır dışında.");
            return new SheetValue(shared[index], true);
        }

        if (type == "str") return new SheetValue(value ?? inline.ToString(), true);
        return new SheetValue(value ?? inline.ToString(), false);
    }

    private static int Column(string reference)
    {
        var value = 0;
        try
        {
            foreach (var character in reference.TakeWhile(char.IsAsciiLetter))
                value = checked(value * 26 + (char.ToUpperInvariant(character) - 'A' + 1));
        }
        catch (OverflowException exception)
        {
            throw new InvalidDataException("Hücre sütunu geçersiz.", exception);
        }

        if (value == 0) throw new InvalidDataException("Hücre sütunu geçersiz.");
        return value;
    }

    private static XmlReaderSettings SafeXml() => new()
    {
        DtdProcessing = DtdProcessing.Prohibit,
        XmlResolver = null,
        MaxCharactersInDocument = 16_000_000,
    };

    private sealed record SheetRow(int Number, IReadOnlyList<SheetCell> Cells);
    private sealed record SheetCell(int Column, string Value, bool IsLosslessText);
    private sealed record SheetValue(string Value, bool IsLosslessText);
}

/// <summary>One accepted file row: the worksheet row number, the product card code and the pieces per package.</summary>
internal sealed record PackageStandardSourceRow(int RowNumber, string ProductCode, int PiecesPerPackage);

/// <summary>The rows read from a file and the Turkish row/file errors; any error refuses the whole file.</summary>
internal sealed record PackageStandardWorkbookResult(IReadOnlyList<PackageStandardSourceRow> Rows, IReadOnlyList<string> Errors);
