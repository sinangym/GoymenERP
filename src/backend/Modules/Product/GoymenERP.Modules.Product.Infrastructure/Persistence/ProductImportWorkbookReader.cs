using System.Globalization;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Xml;
using GoymenERP.Modules.Product.Application.Contracts;
using GoymenERP.Modules.Product.Domain.Products;

namespace GoymenERP.Modules.Product.Infrastructure.Persistence;

/// <summary>
/// Minimal deterministic OOXML reader for the first worksheet. It accepts the cell forms Excel writes for text and
/// scalar values, never executes formulas/macros/external links and never expands a row beyond the three mapped cells.
/// </summary>
public sealed class ProductImportWorkbookReader : IProductImportWorkbookReaderV1
{
    private const int MaximumLegacyCodeLength = ProductCode.MaximumPrefixLength + 1 + 19;

    public ProductImportPreviewV1 Read(ReadOnlyMemory<byte> workbook, ProductImportColumnMappingV1 mapping,
        IReadOnlySet<string> existingCodes, IReadOnlySet<Guid> existingGroupIds)
    {
        ArgumentNullException.ThrowIfNull(mapping);
        ArgumentNullException.ThrowIfNull(existingCodes);
        ArgumentNullException.ThrowIfNull(existingGroupIds);
        var fileHash = Convert.ToHexString(SHA256.HashData(workbook.Span));
        var errors = new List<ProductImportErrorV1>();
        var accepted = new List<ProductImportSourceRowV1>();

        try
        {
            using var stream = new MemoryStream(workbook.ToArray(), writable: false);
            using var archive = new ZipArchive(stream, ZipArchiveMode.Read, leaveOpen: false);
            var shared = ReadSharedStrings(archive);
            var rows = ReadFirstSheet(archive, shared).ToArray();
            if (rows.Length == 0)
            {
                errors.Add(Error(null, "workbook", ProductImportCodesV1.InvalidWorkbook, "Çalışma sayfası boş."));
                return Preview(fileHash, mapping, rows.Length, accepted, errors);
            }

            var headers = rows[0].Cells.ToDictionary(cell => cell.Value.Trim(), cell => cell.Column, StringComparer.Ordinal);
            var columns = RequiredColumns(mapping);
            foreach (var (field, header) in columns)
                if (string.IsNullOrWhiteSpace(header) || !headers.ContainsKey(header.Trim()))
                    errors.Add(Error(1, field, ProductImportCodesV1.RequiredColumnMissing, $"'{header}' kolonu bulunamadı."));
            if (errors.Count > 0) return Preview(fileHash, mapping, Math.Max(0, rows.Length - 1), accepted, errors);

            var sourceRows = rows.Skip(1).Where(row => row.Cells.Any(cell => !string.IsNullOrWhiteSpace(cell.Value))).ToArray();
            if (sourceRows.Length > ProductImportApiV1.MaximumRows)
            {
                errors.Add(Error(null, "workbook", ProductImportCodesV1.TooManyRows,
                    $"En fazla {ProductImportApiV1.MaximumRows} veri satırı kabul edilir."));
                return Preview(fileHash, mapping, sourceRows.Length, accepted, errors);
            }

            var seen = new HashSet<string>(StringComparer.Ordinal);
            foreach (var row in sourceRows)
            {
                var legacy = Value(row, headers[mapping.LegacyCode.Trim()]).Trim();
                var name = Value(row, headers[mapping.Name.Trim()]);
                var groupText = Value(row, headers[mapping.GroupId.Trim()]).Trim();
                var before = errors.Count;
                if (legacy.Length == 0)
                    errors.Add(Error(row.Number, "legacyCode", ProductImportCodesV1.Required, "Eski kod zorunludur."));
                else if (legacy.Length > MaximumLegacyCodeLength || legacy.Any(char.IsControl))
                    errors.Add(Error(row.Number, "legacyCode", ProductImportCodesV1.InvalidLegacyCode, "Eski kod biçimi geçersiz."));
                else if (!seen.Add(legacy))
                    errors.Add(Error(row.Number, "legacyCode", ProductImportCodesV1.RepeatedLegacyCode, "Dosyada aynı eski kod birden çok kez bulunuyor."));
                else if (existingCodes.Contains(legacy))
                    errors.Add(Error(row.Number, "legacyCode", ProductImportCodesV1.ExistingLegacyCode, "Eski kod daha önce kaydedilmiş; mevcut kart güncellenmez."));

                string normalizedName = string.Empty;
                try { normalizedName = Domain.Products.Product.NormalizeName(name); }
                catch (ArgumentException)
                {
                    errors.Add(Error(row.Number, "name", ProductImportCodesV1.InvalidName, "Ürün adı geçersiz."));
                }

                if (!Guid.TryParseExact(groupText, "D", out var groupId) || groupId == Guid.Empty || !existingGroupIds.Contains(groupId))
                    errors.Add(Error(row.Number, "groupId", ProductImportCodesV1.InvalidGroup, "Ürün grubu bulunamadı."));

                if (errors.Count == before)
                    accepted.Add(new(row.Number, legacy, normalizedName, groupId));
            }

            return Preview(fileHash, mapping, sourceRows.Length, accepted, errors);
        }
        catch (Exception exception) when (exception is InvalidDataException or XmlException or IOException or FormatException
                                          or ArgumentException)
        {
            errors.Add(Error(null, "workbook", ProductImportCodesV1.InvalidWorkbook, "Dosya geçerli bir .xlsx çalışma kitabı değil."));
            return Preview(fileHash, mapping, 0, accepted, errors);
        }
    }

    private static ProductImportPreviewV1 Preview(string fileHash, ProductImportColumnMappingV1 mapping, int count,
        IReadOnlyList<ProductImportSourceRowV1> rows, IReadOnlyList<ProductImportErrorV1> errors)
    {
        var canonical = string.Join("\n", new[] { fileHash, mapping.LegacyCode.Trim(), mapping.Name.Trim(), mapping.GroupId.Trim() }
            .Concat(rows.Select(row => $"{row.RowNumber}|{row.LegacyCode}|{row.Name}|{row.GroupId:D}"))
            .Concat(errors.Select(error => $"{error.RowNumber}|{error.Field}|{error.Code}")));
        var token = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(canonical)));
        return new(fileHash, token, count, rows.ToArray(), errors.ToArray());
    }

    private static IReadOnlyList<string> ReadSharedStrings(ZipArchive archive)
    {
        var entry = archive.GetEntry("xl/sharedStrings.xml");
        if (entry is null) return [];
        using var reader = XmlReader.Create(entry.Open(), SafeXml());
        var values = new List<string>();
        var current = new StringBuilder();
        while (reader.Read())
        {
            if (reader.NodeType == XmlNodeType.Element && reader.LocalName == "si") current.Clear();
            else if (reader.NodeType == XmlNodeType.Element && reader.LocalName == "t") current.Append(reader.ReadElementContentAsString());
            else if (reader.NodeType == XmlNodeType.EndElement && reader.LocalName == "si") values.Add(current.ToString());
        }
        return values;
    }

    private static IEnumerable<SheetRow> ReadFirstSheet(ZipArchive archive, IReadOnlyList<string> shared)
    {
        var entry = archive.GetEntry("xl/worksheets/sheet1.xml")
            ?? throw new InvalidDataException("İlk çalışma sayfası bulunamadı.");
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
            }
            else if (reader.NodeType == XmlNodeType.Element && reader.LocalName == "c")
            {
                var reference = reader.GetAttribute("r") ?? throw new InvalidDataException("Hücre başvurusu yok.");
                var type = reader.GetAttribute("t");
                var column = Column(reference);
                var value = ReadCell(reader.ReadSubtree(), type, shared);
                cells.Add(new(column, value));
            }
            else if (reader.NodeType == XmlNodeType.EndElement && reader.LocalName == "row")
            {
                yield return new(rowNumber, cells.ToArray());
            }
        }
    }

    private static string ReadCell(XmlReader reader, string? type, IReadOnlyList<string> shared)
    {
        string? value = null;
        var inline = new StringBuilder();
        while (reader.Read())
        {
            if (reader.NodeType == XmlNodeType.Element && reader.LocalName == "v") value = reader.ReadElementContentAsString();
            else if (reader.NodeType == XmlNodeType.Element && reader.LocalName == "t") inline.Append(reader.ReadElementContentAsString());
        }
        if (type == "inlineStr") return inline.ToString();
        if (type == "s" && int.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out var index)
                        && index >= 0 && index < shared.Count) return shared[index];
        return value ?? inline.ToString();
    }

    private static int Column(string reference)
    {
        var value = 0;
        foreach (var character in reference.TakeWhile(char.IsAsciiLetter))
            value = checked(value * 26 + (char.ToUpperInvariant(character) - 'A' + 1));
        if (value == 0) throw new InvalidDataException("Hücre sütunu geçersiz.");
        return value;
    }

    private static string Value(SheetRow row, int column) => row.Cells.FirstOrDefault(cell => cell.Column == column)?.Value ?? string.Empty;

    private static IReadOnlyDictionary<string, string> RequiredColumns(ProductImportColumnMappingV1 mapping) =>
        new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["legacyCode"] = mapping.LegacyCode,
            ["name"] = mapping.Name,
            ["groupId"] = mapping.GroupId,
        };

    private static ProductImportErrorV1 Error(int? row, string field, string code, string message) => new(row, field, code, message);

    private static XmlReaderSettings SafeXml() => new()
    {
        DtdProcessing = DtdProcessing.Prohibit,
        XmlResolver = null,
        MaxCharactersInDocument = 16_000_000,
    };

    private sealed record SheetRow(int Number, IReadOnlyList<SheetCell> Cells);
    private sealed record SheetCell(int Column, string Value);
}
