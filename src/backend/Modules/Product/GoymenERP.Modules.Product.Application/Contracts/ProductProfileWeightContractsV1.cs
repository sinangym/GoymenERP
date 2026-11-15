namespace GoymenERP.Modules.Product.Application.Contracts;

/// <summary>
/// KLN-D54 (Sinan, 2026-11-15, <c>docs/decisions/SINAN_KARARLARI_2026-11-15_MALIYET_VE_RISK.md</c>): the kg/m of a structural
/// profile (KLN-D48 (2)) comes from a versioned, source-labelled table that an administrator imports from the standard or the
/// catalogue in hand (for example EN 10365). The table is the EK-23 / DEC-PRD-009 "verified / standard master"; a geometric
/// value stays an estimate. Nothing is typed by hand, nothing is built into the code and no value is invented: while the table
/// or its row is missing the weight reads "no source" (<see cref="ProductKgPerMetreSourcesV1.NoSource"/>).
/// <para>
/// STK-S37 slice spec (reversible application detail of KLN-D54):
/// <list type="bullet">
/// <item><b>Schema</b>: per company, an append-only header per import (<c>product.ProductProfileWeightTables</c>: version, source
/// label, reason, row count, who / when) and its rows (<c>product.ProductProfileWeightRows</c>: series, size designation, kg/m
/// with six decimals). An import never edits a row: it writes the whole table again as the next version; the highest version is
/// the current one, the older versions stay as history.</item>
/// <item><b>Import format</b>: a UTF-8 text file with the header line <c>series;size;kgPerMetre</c> and one profile per line,
/// <c>;</c> separated, the decimal point a dot (<c>IPE;200;22.4</c>). The browser reads the file and sends the rows as JSON; the
/// server stores the rows, the source label and the reason, not the file (B-26: no file is uploaded by this command).</item>
/// <item><b>Permission</b>: <see cref="ProductProfileWeightPermissionsV1.Import"/>, company-wide; registered, granted to no
/// role (granting it is Sinan's step).</item>
/// </list>
/// </para>
/// </summary>
public static class ProductProfileWeightPermissionsV1
{
    /// <summary>
    /// ONE company-wide name for reading the current structural profile weight table on its management screen and importing a
    /// new version. Registered, NOT granted here: the grant is a separate step (human gate). Not Critical.
    /// </summary>
    public const string Import = "product.profile-weights.import";
}

public static class ProductProfileWeightProblemCodesV1
{
    public const string RequestInvalid = "product_profile_weight_request_invalid";
    public const string RowsInvalid = "product_profile_weight_rows_invalid";
    public const string SourceRequired = "product_profile_weight_source_required";
    public const string SourceTooLong = "product_profile_weight_source_too_long";
    public const string ReasonRequired = "product_profile_weight_reason_required";
    public const string ReasonTooLong = "product_profile_weight_reason_too_long";
    public const string CommandMetadataInvalid = "product_profile_weight_command_metadata_invalid";
    public const string IdempotencyConflict = "product_profile_weight_idempotency_conflict";
    public const string VersionConflict = "product_profile_weight_version_conflict";
    public const string ProviderUnavailable = "product_profile_weight_provider_unavailable";
}

public static class ProductProfileWeightInputLimitsV1
{
    public const int SourceLabelMaximumLength = 200;

    public const int ReasonMaximumLength = 4000;

    /// <summary>A request bound (not a business rule): the standard series together hold a few hundred sizes.</summary>
    public const int MaximumRows = 2000;
}

/// <summary>One imported profile: a series of <see cref="ProductProfileSeriesV1"/>, the size designation and the kg/m.</summary>
public sealed record ProductProfileWeightRowV1(string? Series, string? Size, decimal? KgPerMetre);

/// <summary>
/// Import the whole table as the next version. <c>ExpectedVersion</c> is the current version as decimal text ("0" = none yet);
/// the source label (which standard or catalogue, which edition) and the reason are mandatory.
/// </summary>
public sealed record ImportProductProfileWeightTableRequestV1(
    string? ExpectedVersion,
    string? SourceLabel,
    string? Reason,
    IReadOnlyList<ProductProfileWeightRowV1>? Rows);

public sealed record ProductProfileWeightTableRowViewV1(string Series, string Size, decimal KgPerMetre);

/// <summary>The current table of the server-resolved company ("0", nulls and no rows while none is imported).</summary>
public sealed record ProductProfileWeightTableViewV1(
    Guid CompanyId,
    string Version,
    string? SourceLabel,
    string? Reason,
    DateTimeOffset? ImportedAtUtc,
    string? ImportedBy,
    IReadOnlyList<ProductProfileWeightTableRowViewV1> Rows);

/// <summary>
/// The table's use cases, both authorized first on <see cref="ProductProfileWeightPermissionsV1.Import"/> company-wide and
/// re-measured before the answer / the save. The import follows the command skeleton: a mandatory reason and source label,
/// Idempotency-Key, the expected version and one audit event per real change. Answers reuse the dimension answer shapes.
/// </summary>
public interface IProductProfileWeightTableServiceV1
{
    Task<ProductProfileWeightTableViewV1> GetCurrentAsync(CancellationToken cancellationToken = default);

    Task<ProductDimensionResultV1> ImportAsync(ImportProductProfileWeightTableRequestV1 request,
        ProductDimensionCommandMetadataV1 metadata, CancellationToken cancellationToken = default);
}
