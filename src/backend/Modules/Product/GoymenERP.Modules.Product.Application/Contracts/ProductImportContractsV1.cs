using System.Text;

namespace GoymenERP.Modules.Product.Application.Contracts;

/// <summary>
/// Q16 / AT-019 / AT-020. A Product-card workbook is previewed and approved as one immutable byte sequence.
/// Preview never writes; apply must bind the same file hash, mapping and preview token and is all-or-nothing.
/// Imported cards are independent Drafts. A legacy code is retained as a reserved alias; it never updates an
/// existing card and therefore makes a replay under another idempotency key fail closed.
/// </summary>
public static class ProductImportApiV1
{
    public const string PreviewPath = "/api/v1/products/imports/preview";
    public const string ApplyPath = "/api/v1/products/imports";
    public const string PreviewOperationId = "PreviewProductImportV1";
    public const string ApplyOperationId = "ApplyProductImportV1";
    public const int MaximumRows = 10_000;
}

public static class ProductImportCodesV1
{
    public const string InvalidWorkbook = "product_import_invalid_workbook";
    public const string RequiredColumnMissing = "product_import_required_column_missing";
    public const string Required = "required";
    public const string InvalidName = "invalid_name";
    public const string InvalidGroup = "invalid_group";
    public const string InvalidLegacyCode = "invalid_legacy_code";
    public const string RepeatedLegacyCode = "repeated_legacy_code";
    public const string ExistingLegacyCode = "existing_legacy_code";
    public const string TooManyRows = "product_import_too_many_rows";
    public const string PreviewMismatch = "product_import_preview_mismatch";
    public const string NotReady = "product_import_not_ready";
}

/// <summary>Header names selected by the user. The server resolves them ordinally after trimming.</summary>
public sealed record ProductImportColumnMappingV1(string LegacyCode, string Name, string GroupId);

public sealed record ProductImportSourceRowV1(int RowNumber, string LegacyCode, string Name, Guid GroupId);

public sealed record ProductImportErrorV1(int? RowNumber, string Field, string Code, string Message);

public sealed record ProductImportPreviewV1(
    string FileSha256,
    string PreviewToken,
    int SourceRowCount,
    IReadOnlyList<ProductImportSourceRowV1> Rows,
    IReadOnlyList<ProductImportErrorV1> Errors)
{
    public bool Ready => SourceRowCount > 0 && Errors.Count == 0 && Rows.Count == SourceRowCount;
}

public sealed record ProductImportApplyBindingV1(
    string FileSha256,
    string PreviewToken,
    ProductImportColumnMappingV1 Mapping);

public sealed record ProductImportCreatedV1(Guid ProductId, string Code, string LegacyCode, string Name, Guid GroupId);

public sealed record ProductImportResultV1(
    IReadOnlyList<ProductImportCreatedV1> Products,
    bool IsReplay,
    string OriginalCorrelationId);

/// <summary>The exact .xlsx byte reader. It performs no database read and no write.</summary>
public interface IProductImportWorkbookReaderV1
{
    ProductImportPreviewV1 Read(ReadOnlyMemory<byte> workbook, ProductImportColumnMappingV1 mapping,
        IReadOnlySet<string> existingCodes, IReadOnlySet<Guid> existingGroupIds);
}

/// <summary>Stable downloadable UTF-8 CSV representation of preview errors.</summary>
public static class ProductImportErrorCsvV1
{
    public static byte[] Write(IReadOnlyList<ProductImportErrorV1> errors)
    {
        ArgumentNullException.ThrowIfNull(errors);
        var text = new StringBuilder("row,field,code,message\r\n");
        foreach (var error in errors.OrderBy(value => value.RowNumber).ThenBy(value => value.Field, StringComparer.Ordinal)
                     .ThenBy(value => value.Code, StringComparer.Ordinal))
        {
            text.Append(error.RowNumber?.ToString(System.Globalization.CultureInfo.InvariantCulture) ?? string.Empty).Append(',')
                .Append(Csv(error.Field)).Append(',').Append(Csv(error.Code)).Append(',').Append(Csv(error.Message)).Append("\r\n");
        }

        var encoding = new UTF8Encoding(encoderShouldEmitUTF8Identifier: true);
        return encoding.GetPreamble().Concat(encoding.GetBytes(text.ToString())).ToArray();
    }

    private static string Csv(string value) => $"\"{value.Replace("\"", "\"\"", StringComparison.Ordinal)}\"";
}
