namespace GoymenERP.Modules.Product.Application.Contracts;

/// <summary>
/// URN-D13…D16 (STK-S37R) and KLN-D54: the company-wide kg/m table of the two section kinds whose kg/m is not typed on the card —
/// <see cref="ProductDimensionSectionKindsV1.Profile"/> (required) and <see cref="ProductDimensionSectionKindsV1.RoundTube"/>
/// (optional) — and, KLN-D87, of <see cref="ProductDimensionSectionKindsV1.StructuralProfile"/> rows keyed by the standard series
/// and size designation (the ERW / painted ERW kinds read the round tube / profile rows,
/// <see cref="ProductDimensionSectionKindsV1.WeightTableKind"/>). The source and version of the standard table are NOT chosen
/// here (KLN-D87): whichever source-labelled table the company imports is its table. The table is imported from a file only:
/// <list type="bullet">
/// <item>URN-D13: the uploaded file is kept in the Platform document vault under the user-upload rules (allowlist, content type,
/// malware scan) and linked to the import record; without a scanner the import is refused fail-closed. The rows read from the
/// file and the source label are stored separately.</item>
/// <item>URN-D14: every file carries a mandatory source label (e.g. the supplier catalogue it comes from).</item>
/// <item>URN-D15: one company-wide permission (<see cref="ProductProfileWeightPermissionsV1.Import"/>), not Critical; the grant
/// is a human gate.</item>
/// <item>URN-D16: the card never takes a kg/m; an unapproved draft table is never imported or seeded; a profile card whose measures
/// the table does not hold is not saved.</item>
/// </list>
/// Each import is a new, insert-only version of the company's table (the newest one is current); every import is audited. The
/// file format below is a reversible application detail (KLN-D54).
/// </summary>
public static class ProductProfileWeightPermissionsV1
{
    /// <summary>
    /// ONE company-wide name for reading and importing the company's kg/m table (URN-D15). Registered, NOT granted here: the grant
    /// is a separate human gate. Not Critical.
    /// </summary>
    public const string Import = "product.profile-weights.import";
}

public static class ProductProfileWeightProblemCodesV1
{
    public const string RequestInvalid = "product_profile_weight_request_invalid";
    public const string CommandMetadataInvalid = "product_profile_weight_command_metadata_invalid";
    public const string FileInvalid = "product_profile_weight_file_invalid";
    public const string SourceLabelRequired = "product_profile_weight_source_label_required";
    public const string SourceLabelTooLong = "product_profile_weight_source_label_too_long";
    public const string ReasonRequired = "product_profile_weight_reason_required";
    public const string ReasonTooLong = "product_profile_weight_reason_too_long";
    public const string IdempotencyConflict = "product_profile_weight_idempotency_conflict";
    public const string VersionConflict = "product_profile_weight_version_conflict";

    /// <summary>The document vault refused the file (not an allowed kind, or the malware scan did not find it clean).</summary>
    public const string FileRejected = "product_profile_weight_file_rejected";

    /// <summary>URN-D13: no vault or no malware scanner can take the file now; nothing is imported (fail-closed).</summary>
    public const string FileVaultUnavailable = "product_profile_weight_file_vault_unavailable";

    public const string ProviderUnavailable = "product_profile_weight_provider_unavailable";
}

/// <summary>Technical bounds of one import (reversible application details, KLN-D54); no tolerance or business threshold.</summary>
public static class ProductProfileWeightInputLimitsV1
{
    public const int SourceLabelMaximumLength = 200;
    public const int ReasonMaximumLength = 4000;
    public const int FileNameMaximumLength = 255;

    /// <summary>
    /// URN-D21/D22: the starting value of the largest file one import reads (5 MB = 5,242,880 bytes, 5 MiB), used while the
    /// Settings group-level record (<see cref="IProductProfileWeightImportLimitSourceV1"/>) has no value yet. The recorded value
    /// replaces it. URN-D23: the platform's general upload bound (<see cref="IProductProfileWeightUploadCeilingSourceV1"/>) is the
    /// ceiling of both; it is never a second value written here.
    /// </summary>
    public const int MaximumFileBytes = 5_242_880;

    /// <summary>
    /// URN-D18 technical bound (reversible): the most uncompressed bytes one XLSX part may expand to while it is read, so a
    /// compressed workbook cannot grow without bound on the server.
    /// </summary>
    public const long MaximumWorkbookPartBytes = 67_108_864;

    /// <summary>The most data rows one file may hold.</summary>
    public const int MaximumRows = 5000;
}

/// <summary>
/// The import file (KLN-D54 reversible detail): UTF-8 text (a byte-order mark is allowed), one header line exactly
/// <see cref="Header"/> or <see cref="ExtendedHeader"/>, then one row per line with <see cref="Separator"/> between the fields (as
/// many as the header names) and a dot as the decimal mark.
/// A <c>Profile</c> row fills width, height, wall and kg/m and leaves the diameter empty; a <c>RoundTube</c> row fills diameter,
/// wall and kg/m and leaves width and height empty. KLN-D87: under <see cref="ExtendedHeader"/> a <c>StructuralProfile</c> row
/// fills kg/m, the series (<see cref="ProductProfileSeriesV1"/>) and the size designation and leaves every measure empty; the
/// other kinds leave the two trailing fields empty. Millimetres at most three decimals, kg/m at most six; no thousands
/// separator, no sign, no exponent. A measure pair (or series and size) appears once per file. Empty lines are ignored.
/// </summary>
public static class ProductProfileWeightFileFormatV1
{
    public const char Separator = ';';
    public const string Header = "sectionKind;widthMm;heightMm;outerDiameterMm;wallThicknessMm;kgPerMetre";

    /// <summary>KLN-D87: the header with the structural profile series and size designation fields.</summary>
    public const string ExtendedHeader = Header + ";profileSeries;profileSize";
    public const string FileExtension = ".csv";
    public const string ContentType = "text/csv";

    /// <summary>URN-D18: an Excel workbook (<c>.xlsx</c>) is accepted as well; XLSM (macros) and the old XLS are not.</summary>
    public const string WorkbookFileExtension = ".xlsx";

    public const string WorkbookContentType = "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet";
}

/// <summary>
/// URN-D18 (reversible reading details): an <c>.xlsx</c> import holds the same table on its FIRST worksheet. The first non-empty
/// row carries the six header names of <see cref="ProductProfileWeightFileFormatV1.Header"/> in columns A…F, one per cell; every
/// further row is one table row in the same columns. A number cell is read as the exact value Excel stored (its shortest
/// round-trip text, never rounded to fit); a text cell is read as text under the same number rules as the CSV. A formula cell is
/// refused (no cached result is trusted), as are macros, external links and a non-workbook ZIP. The kind is chosen by the file
/// extension and proven by the content: a <c>.csv</c> name with workbook bytes, or the reverse, is refused.
/// </summary>
public enum ProductProfileWeightFileKindV1
{
    Csv = 1,
    Xlsx = 2,
}

/// <summary>
/// Import a new version of the company's kg/m table. <c>ExpectedVersion</c> is the current table's version as decimal text ("0"
/// while none exists). <c>ContentBase64</c> is the whole file; the server reads the rows from it (the client never sends rows).
/// </summary>
public sealed record ImportProductProfileWeightTableRequestV1(
    string? ExpectedVersion,
    string? SourceLabel,
    string? Reason,
    string? FileName,
    string? ContentBase64);

/// <summary>
/// One kg/m row of a table: a profile (width, height, wall) or a round tube (outer diameter, wall); KLN-D87: a structural profile
/// (series and size designation; no measure, so <c>WallThicknessMm</c> is null).
/// </summary>
public sealed record ProductProfileWeightRowV1(
    string SectionKind,
    decimal? WidthMm,
    decimal? HeightMm,
    decimal? OuterDiameterMm,
    decimal? WallThicknessMm,
    decimal KgPerMetre,
    string? ProfileSeries = null,
    string? ProfileSize = null);

/// <summary>
/// The company's current kg/m table, read in <c>CompanyId</c> (the server-resolved company): version "0", no source and no rows
/// while none was imported. <c>DocumentId</c> is the vault document of the imported file (URN-D13).
/// </summary>
public sealed record ProductProfileWeightTableViewV1(
    Guid CompanyId,
    string Version,
    string? SourceLabel,
    string? FileName,
    string? FileSha256,
    Guid? DocumentId,
    DateTimeOffset? ImportedAtUtc,
    string? ImportedBy,
    IReadOnlyList<ProductProfileWeightRowV1> Rows);

/// <summary>Server-derived command metadata; company and actor never come from a payload.</summary>
public sealed record ProductProfileWeightCommandMetadataV1(
    Guid CompanyId,
    string ActorId,
    string CorrelationId,
    string? CausationId,
    string SourceChannel,
    string? ClientIpAddress,
    string? UserAgent,
    string IdempotencyKey);

/// <summary>A command's terminal answer (stored for replay); <c>Code</c> is null for a success.</summary>
public sealed record ProductProfileWeightAnswerV1(int StatusCode, string? Code, string Title, string Detail);

public sealed record ProductProfileWeightResultV1(ProductProfileWeightAnswerV1 Answer, bool IsReplay, string OriginalCorrelationId);

/// <summary>A failure of the table read: 400 / 503 (the code names it).</summary>
public sealed class ProductProfileWeightException(int statusCode, string? code) : Exception(code ?? statusCode.ToString(
    System.Globalization.CultureInfo.InvariantCulture))
{
    public int StatusCode { get; } = statusCode;

    public string? Code { get; } = code;
}

/// <summary>
/// The kg/m table use cases, authorized on <see cref="ProductProfileWeightPermissionsV1.Import"/> company-wide and re-measured
/// before the answer / the save. The import is one person's decision: a mandatory source label and reason, Idempotency-Key, the
/// expected version, the file kept in the vault and ONE audit event per import.
/// </summary>
public interface IProductProfileWeightTableServiceV1
{
    Task<ProductProfileWeightTableViewV1> GetCurrentAsync(CancellationToken cancellationToken = default);

    Task<ProductProfileWeightResultV1> ImportAsync(ImportProductProfileWeightTableRequestV1 request,
        ProductProfileWeightCommandMetadataV1 metadata, CancellationToken cancellationToken = default);
}

/// <summary>
/// URN-D21: the Product-owned read of the Settings group-level record of the largest kg/m import file (Admin persona, changed on
/// the Settings screen with its audit). Settings implements it; Product only reads it. <c>null</c> means no value is recorded yet
/// and the decided starting value <see cref="ProductProfileWeightInputLimitsV1.MaximumFileBytes"/> applies. An unreachable
/// source throws and the import refuses with <see cref="ProductProfileWeightProblemCodesV1.ProviderUnavailable"/> (no guess).
/// </summary>
public interface IProductProfileWeightImportLimitSourceV1
{
    Task<long?> GetMaximumFileBytesAsync(Guid companyId, CancellationToken cancellationToken = default);
}

/// <summary>
/// URN-D23: the Product-owned read of the platform's general file upload bound (the document vault's configured maximum upload
/// size). It is the ceiling of the kg/m import size: the recorded Settings value (or the URN-D22 starting value while none is
/// recorded) applies only up to it, so a larger value never widens the general bound. The platform owns the value; Product only
/// reads it. An unreachable source throws, and a value that is not a positive size is refused: the import answers
/// <see cref="ProductProfileWeightProblemCodesV1.ProviderUnavailable"/> (fail-closed, no guess).
/// </summary>
public interface IProductProfileWeightUploadCeilingSourceV1
{
    Task<long> GetPlatformMaximumUploadBytesAsync(CancellationToken cancellationToken = default);
}

/// <summary>What the vault did with the import file.</summary>
public enum ProductProfileWeightFileStoreOutcomeV1
{
    /// <summary>No vault, no allowlisted kind for the file or no malware scanner now: nothing is imported (URN-D13 fail-closed).</summary>
    Unavailable = 0,

    /// <summary>The vault refused the file for good (not allowed, or the scan did not find it clean).</summary>
    Rejected = 1,

    /// <summary>The file is stored, its kind allowed and its malware scan clean; it is linked to the import record.</summary>
    Stored = 2,
}

public sealed record ProductProfileWeightFileStoreResultV1(ProductProfileWeightFileStoreOutcomeV1 Outcome, Guid? DocumentId)
{
    public static ProductProfileWeightFileStoreResultV1 Unavailable { get; } =
        new(ProductProfileWeightFileStoreOutcomeV1.Unavailable, null);
}

/// <summary>
/// The import file handed to the vault. <c>ImportId</c> is the import record the document is linked to (derived from the company
/// and the command key, so a retry of the same command links the same record); the idempotency key and correlation are the
/// import command's.
/// </summary>
public sealed record ProductProfileWeightFileV1(
    Guid CompanyId,
    Guid ImportId,
    string FileName,
    string ContentType,
    byte[] Content,
    string Sha256,
    string ActorId,
    string IdempotencyKey,
    string CorrelationId,
    string? CausationId);

/// <summary>
/// URN-D13: the Product-owned outbound port to the Platform document vault for the import file. The adapter answers
/// <see cref="ProductProfileWeightFileStoreOutcomeV1.Stored"/> only for a stored, allowlisted, malware-scan-clean file linked to
/// the import record; anything else is Rejected or Unavailable, and the import stores nothing. Until Platform offers such a
/// port, the only registered implementation answers Unavailable.
/// </summary>
public interface IProductProfileWeightFileVaultV1
{
    Task<ProductProfileWeightFileStoreResultV1> StoreAsync(ProductProfileWeightFileV1 file,
        CancellationToken cancellationToken = default);
}
