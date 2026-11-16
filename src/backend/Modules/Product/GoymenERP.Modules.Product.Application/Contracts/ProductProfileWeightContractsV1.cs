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

    /// <summary>The largest file one import reads (1 MiB).</summary>
    public const int MaximumFileBytes = 1_048_576;

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
