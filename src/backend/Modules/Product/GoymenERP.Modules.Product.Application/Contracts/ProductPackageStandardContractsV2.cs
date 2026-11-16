namespace GoymenERP.Modules.Product.Application.Contracts;

/// <summary>
/// B-08 / BS-10 (STK-S12): the company's package standard (pieces per package) of a product. The standard is not a card attribute
/// (2026-10-21 #9, DEC-PRD-008 / D-PACKAGE=A): it is keyed by the company and the product, versioned and read by the consumers
/// (Sales, Production) as a suggestion they snapshot (URT-D27, AT-017: a new version never changes a past transaction).
/// <list type="bullet">
/// <item>BS-10: the table is first imported from an Excel file (<see cref="ProductPackageStandardFileFormatV2"/>) and then edited
/// in the ERP one product at a time. Each import or edit is a new, insert-only version of the company's whole table (the newest
/// is current); every one is audited.</item>
/// <item>While no table or no row exists the read port answers <see cref="ProductPackageStandardReadOutcomeV2.NoSource"/>; no
/// value is guessed or seeded (OPEN-007 stays open).</item>
/// <item>One company-wide permission (<see cref="ProductPackageStandardPermissionsV2.Manage"/>), not Critical; the grant is a
/// human gate. The read port measures no permission (reference data; the consumer fences its own operation).</item>
/// <item>Not implemented here (open, NEEDS-SINAN): the plant override and the customer exception of DEC-PRD-008, and a validity
/// date / approval step for a version (master §7.3). A new version is current at once.</item>
/// </list>
/// The <see cref="ProductPackageStandardStatusV2"/> placeholder of the price resolution stays unchanged for its consumers.
/// </summary>
public static class ProductPackageStandardPermissionsV2
{
    /// <summary>
    /// ONE company-wide name for reading the table screen, importing a file and editing one product's entry. Registered, NOT
    /// granted here: the grant is a separate human gate. Not Critical.
    /// </summary>
    public const string Manage = "product.package-standards.manage";
}

public static class ProductPackageStandardProblemCodesV2
{
    public const string RequestInvalid = "product_package_standard_request_invalid";
    public const string CommandMetadataInvalid = "product_package_standard_command_metadata_invalid";
    public const string FileInvalid = "product_package_standard_file_invalid";
    public const string SourceLabelRequired = "product_package_standard_source_label_required";
    public const string SourceLabelTooLong = "product_package_standard_source_label_too_long";
    public const string ReasonRequired = "product_package_standard_reason_required";
    public const string ReasonTooLong = "product_package_standard_reason_too_long";
    public const string PiecesInvalid = "product_package_standard_pieces_invalid";
    public const string IdempotencyConflict = "product_package_standard_idempotency_conflict";
    public const string VersionConflict = "product_package_standard_version_conflict";
    public const string ProviderUnavailable = "product_package_standard_provider_unavailable";
}

/// <summary>Technical bounds of one command (reversible application details); no tolerance or business threshold.</summary>
public static class ProductPackageStandardInputLimitsV2
{
    public const int SourceLabelMaximumLength = 200;
    public const int ReasonMaximumLength = 4000;
    public const int FileNameMaximumLength = 255;

    /// <summary>The largest file one import reads (2 MiB).</summary>
    public const int MaximumFileBytes = 2_097_152;

    /// <summary>The most data rows one file may hold.</summary>
    public const int MaximumRows = 5000;

    /// <summary>The most row errors one refusal lists (the count of all errors is always named).</summary>
    public const int MaximumListedErrors = 20;
}

/// <summary>
/// The import file (BS-10; the layout is a reversible application detail): an Excel <c>.xlsx</c> workbook whose FIRST worksheet
/// has a header row with exactly the two column titles <see cref="ProductCodeHeader"/> and <see cref="PiecesPerPackageHeader"/>
/// (other columns are ignored), then one row per product: the product card code as a text cell and the pieces per package as a
/// positive whole number. A product appears once per file; empty rows are ignored; formula cells are refused. The file replaces
/// the whole table as a new version.
/// </summary>
public static class ProductPackageStandardFileFormatV2
{
    public const string ProductCodeHeader = "Ürün kodu";
    public const string PiecesPerPackageHeader = "Paket içi adet";
    public const string FileExtension = ".xlsx";
    public const string ContentType = "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet";
}

/// <summary>How a version of the table was made.</summary>
public static class ProductPackageStandardSourceKindsV2
{
    /// <summary>An Excel import replaced the whole table (with a mandatory source label).</summary>
    public const string Import = "Import";

    /// <summary>One product's entry was set or removed in the ERP; the other entries were carried over.</summary>
    public const string Edit = "Edit";
}

/// <summary>
/// Import a new version of the company's table from an Excel file. <c>ExpectedVersion</c> is the current table's version as
/// decimal text ("0" while none exists). <c>ContentBase64</c> is the whole file; the server reads the rows from it.
/// </summary>
public sealed record ImportProductPackageStandardsRequestV2(
    string? ExpectedVersion,
    string? SourceLabel,
    string? Reason,
    string? FileName,
    string? ContentBase64);

/// <summary>
/// Set (a positive whole number) or remove (<c>PiecesPerPackage</c> null) one product's entry; the result is a new version of
/// the whole table. <c>ExpectedVersion</c> is the current table's version as decimal text ("0" while none exists).
/// </summary>
public sealed record SetProductPackageStandardRequestV2(string? ExpectedVersion, int? PiecesPerPackage, string? Reason);

/// <summary>One entry of the current table with the shared card's code and name.</summary>
public sealed record ProductPackageStandardEntryV2(Guid ProductId, string ProductCode, string ProductName, int PiecesPerPackage);

/// <summary>
/// The company's current table, read in <c>CompanyId</c> (the server-resolved company): version "0", no source and no entries
/// while none exists. <c>FileName</c>/<c>FileSha256</c>/<c>SourceLabel</c> are those of the current version when it is an import.
/// </summary>
public sealed record ProductPackageStandardTableViewV2(
    Guid CompanyId,
    string Version,
    string? SourceKind,
    string? SourceLabel,
    string? FileName,
    string? FileSha256,
    DateTimeOffset? CreatedAtUtc,
    string? CreatedBy,
    IReadOnlyList<ProductPackageStandardEntryV2> Entries);

/// <summary>Server-derived command metadata; company and actor never come from a payload.</summary>
public sealed record ProductPackageStandardCommandMetadataV2(
    Guid CompanyId,
    string ActorId,
    string CorrelationId,
    string? CausationId,
    string SourceChannel,
    string? ClientIpAddress,
    string? UserAgent,
    string IdempotencyKey);

/// <summary>A command's terminal answer (stored for replay); <c>Code</c> is null for a success and for a code-less 404.</summary>
public sealed record ProductPackageStandardAnswerV2(int StatusCode, string? Code, string Title, string Detail);

public sealed record ProductPackageStandardResultV2(ProductPackageStandardAnswerV2 Answer, bool IsReplay, string OriginalCorrelationId);

/// <summary>A failure of the table read: 400 / 503 (the code names it).</summary>
public sealed class ProductPackageStandardException(int statusCode, string? code) : Exception(code ?? statusCode.ToString(
    System.Globalization.CultureInfo.InvariantCulture))
{
    public int StatusCode { get; } = statusCode;

    public string? Code { get; } = code;
}

/// <summary>
/// The package standard use cases, authorized on <see cref="ProductPackageStandardPermissionsV2.Manage"/> company-wide and
/// re-measured before the answer / the save. Each command is one person's decision: a mandatory reason (and a source label for
/// an import), Idempotency-Key, the expected table version and ONE audit event per new version.
/// </summary>
public interface IProductPackageStandardServiceV2
{
    Task<ProductPackageStandardTableViewV2> GetCurrentAsync(CancellationToken cancellationToken = default);

    Task<ProductPackageStandardResultV2> ImportAsync(ImportProductPackageStandardsRequestV2 request,
        ProductPackageStandardCommandMetadataV2 metadata, CancellationToken cancellationToken = default);

    Task<ProductPackageStandardResultV2> SetAsync(Guid productId, SetProductPackageStandardRequestV2 request,
        ProductPackageStandardCommandMetadataV2 metadata, CancellationToken cancellationToken = default);
}

public enum ProductPackageStandardReadOutcomeV2
{
    Unavailable = 0,
    Denied = 1,
    NotFound = 2,

    /// <summary>BS-10: the company has no table yet or the table holds no entry for the product; no value is guessed.</summary>
    NoSource = 3,
    Found = 4,
}

/// <summary>
/// The current entry as the consumers read it; every field is null unless <c>Outcome</c> is Found. <c>StandardVersion</c> and
/// <c>StandardVersionId</c> name the table version the value comes from, so a transaction can snapshot it (AT-017).
/// <c>SourceLabel</c> is set when that version is an import.
/// </summary>
public sealed record ProductPackageStandardReadV2(
    ProductPackageStandardReadOutcomeV2 Outcome,
    int? PiecesPerPackage,
    long? StandardVersion,
    Guid? StandardVersionId,
    string? SourceKind,
    string? SourceLabel)
{
    public static ProductPackageStandardReadV2 Without(ProductPackageStandardReadOutcomeV2 outcome) =>
        new(outcome, null, null, null, null, null);
}

/// <summary>
/// The ProductPackageStandard V2 read port (stok_lojistik; consumed by Sales and Production): the current company table's
/// pieces per package of a product, read by an actor of the server-resolved company. Access only (authenticated member of the
/// company); no permission is measured. Never throws for a missing card, table or entry.
/// </summary>
public interface IProductPackageStandardReaderV2
{
    Task<ProductPackageStandardReadV2> GetAsync(Guid productId, CancellationToken cancellationToken = default);
}
