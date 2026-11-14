namespace GoymenERP.Modules.Product.Application.Contracts;

/// <summary>
/// BS-9 (Sinan, 2026-11-11, <c>docs/decisions/SINAN_KARARLARI_2026-11-11_BEKLEYEN_SORULAR.md</c>): the product card has a separate
/// "Ebat/kalınlık" tab — which size and thickness the product is made of. The U-08 unit conversion reads its input from this
/// row; a product without a row answers <see cref="ProductDimensionOutcomeV1.NoSource"/> (no estimate). The section kinds follow
/// the formula families of <c>docs/delivery/URUN_BIRIM_CEVRIM_KATSAYI_ADAYLARI_v1.md</c> §3:
/// <list type="bullet">
/// <item><see cref="Sheet"/> (F-A, kesilmiş sac/levha): thickness, width and length.</item>
/// <item><see cref="Coil"/> (F-B, rulo / dilinmiş bant): thickness and width.</item>
/// <item><see cref="RoundTube"/> (F-C/F-D, yuvarlak boru): outer diameter and wall thickness (2 × wall ≤ diameter); a stored
/// kg/m is the table value and wins over the formula (#66 6810163182 m.24) — without it the formula only suggests.</item>
/// <item><see cref="Profile"/> (F-C/F-D, kutu / dikdörtgen profil): width, height and wall thickness, and the kg/m itself —
/// there is no corner formula; the kg/m comes from this table (#66 6810051185 m.21).</item>
/// </list>
/// Every measure is millimetres (at most three decimals) and kg/m at most six decimals: the inputs stay at full precision and
/// the coefficient is rounded once where it is produced (URN-D8), never here. The density is the company Settings value
/// (BS-9); the card stores none. Storing kg/m directly keeps the section-area unit question (mm² / m²) out of the table.
/// </summary>
public static class ProductDimensionSectionKindsV1
{
    /// <summary>Kesilmiş sac / levha (F-A).</summary>
    public const string Sheet = "Sheet";

    /// <summary>Rulo / dilinmiş bant (F-B).</summary>
    public const string Coil = "Coil";

    /// <summary>Yuvarlak boru (F-C / F-D).</summary>
    public const string RoundTube = "RoundTube";

    /// <summary>Kutu / dikdörtgen profil (F-C / F-D with the table kg/m).</summary>
    public const string Profile = "Profile";

    public static IReadOnlyList<string> All { get; } = [Sheet, Coil, RoundTube, Profile];

    public static bool IsKnown(string? sectionKind) => sectionKind is Sheet or Coil or RoundTube or Profile;
}

public static class ProductDimensionPermissionsV1
{
    /// <summary>
    /// ONE company-wide name for reading and setting the "Ebat/kalınlık" row of a product (BS-9). The row is held once on the
    /// shared card like the item type (SAT-D149): a holder in ANY company changes the shared value, each change audited in the
    /// acting company. Registered, NOT granted here: the grant is a separate PR (human gate). Not Critical.
    /// </summary>
    public const string Manage = "product.dimension.manage";
}

public static class ProductDimensionProblemCodesV1
{
    public const string RequestInvalid = "product_dimension_request_invalid";
    public const string MeasureInvalid = "product_dimension_measure_invalid";
    public const string CommandMetadataInvalid = "product_dimension_command_metadata_invalid";
    public const string ReasonRequired = "product_dimension_reason_required";
    public const string ReasonTooLong = "product_dimension_reason_too_long";
    public const string IdempotencyConflict = "product_dimension_idempotency_conflict";
    public const string VersionConflict = "product_dimension_version_conflict";
    public const string ProviderUnavailable = "product_dimension_provider_unavailable";
}

public static class ProductDimensionInputLimitsV1
{
    public const int ReasonMaximumLength = 4000;

    /// <summary>Millimetre measures keep at most three decimals (Ö2, the card's nominal measures).</summary>
    public const int MillimetreDecimals = 3;

    /// <summary>The table kg/m keeps at most six decimals; the coefficient decimals of Settings round it later (URN-D8).</summary>
    public const int KgPerMetreDecimals = 6;

    /// <summary>The largest millimetre measure the column (decimal(18,3)) holds, exclusive.</summary>
    public const decimal MillimetreExclusiveMaximum = 1_000_000_000_000_000m;

    /// <summary>The largest kg/m the column (decimal(18,6)) holds, exclusive.</summary>
    public const decimal KgPerMetreExclusiveMaximum = 1_000_000_000_000m;
}

/// <summary>
/// Set the "Ebat/kalınlık" row of a product on the shared card, acting in the server-resolved company. <c>SectionKind</c> is one
/// of <see cref="ProductDimensionSectionKindsV1"/>; the measures the kind needs are required and the others must be null
/// (see the kinds). <c>ExpectedVersion</c> is the row's version as decimal text ("0" = none yet).
/// </summary>
public sealed record SetProductDimensionRequestV1(
    string? ExpectedVersion,
    string? SectionKind,
    decimal? ThicknessMm,
    decimal? WidthMm,
    decimal? LengthMm,
    decimal? HeightMm,
    decimal? OuterDiameterMm,
    decimal? WallThicknessMm,
    decimal? KgPerMetre,
    string? Reason);

/// <summary>
/// The shared card's "Ebat/kalınlık" row ("0" and nulls while none is stored), read in <c>CompanyId</c> (the server-resolved
/// company); <c>UpdatedInCompanyId</c> is the company in which the last change was made.
/// </summary>
public sealed record ProductDimensionViewV1(
    Guid CompanyId,
    Guid ProductId,
    string Version,
    string? SectionKind,
    decimal? ThicknessMm,
    decimal? WidthMm,
    decimal? LengthMm,
    decimal? HeightMm,
    decimal? OuterDiameterMm,
    decimal? WallThicknessMm,
    decimal? KgPerMetre,
    DateTimeOffset? UpdatedAtUtc,
    string? UpdatedBy,
    Guid? UpdatedInCompanyId);

/// <summary>Server-derived command metadata; company and actor never come from a payload.</summary>
public sealed record ProductDimensionCommandMetadataV1(
    Guid CompanyId,
    string ActorId,
    string CorrelationId,
    string? CausationId,
    string SourceChannel,
    string? ClientIpAddress,
    string? UserAgent,
    string IdempotencyKey);

/// <summary>A command's terminal answer (stored for replay); <c>Code</c> is null for a success and the code-less 404.</summary>
public sealed record ProductDimensionAnswerV1(int StatusCode, string? Code, string Title, string Detail);

public sealed record ProductDimensionResultV1(ProductDimensionAnswerV1 Answer, bool IsReplay, string OriginalCorrelationId);

/// <summary>A failure of the dimension read or write: 400 / 404 / 503 (the code names it).</summary>
public sealed class ProductDimensionException(int statusCode, string? code) : Exception(code ?? statusCode.ToString(
    System.Globalization.CultureInfo.InvariantCulture))
{
    public int StatusCode { get; } = statusCode;

    public string? Code { get; } = code;
}

/// <summary>
/// The "Ebat/kalınlık" use cases. Both are authorized first on <see cref="ProductDimensionPermissionsV1.Manage"/> company-wide
/// and re-measured before the answer / the save. One person decides: a mandatory reason, Idempotency-Key, the expected version
/// and one audit event per real change.
/// </summary>
public interface IProductDimensionServiceV1
{
    Task<ProductDimensionViewV1> GetAsync(Guid productId, CancellationToken cancellationToken = default);

    Task<ProductDimensionResultV1> SetAsync(Guid productId, SetProductDimensionRequestV1 request,
        ProductDimensionCommandMetadataV1 metadata, CancellationToken cancellationToken = default);
}

public enum ProductDimensionOutcomeV1
{
    Unavailable = 0,
    Denied = 1,
    NotFound = 2,

    /// <summary>BS-9: the product has no "Ebat/kalınlık" row; the conversion makes no estimate.</summary>
    NoSource = 3,
    Found = 4,
}

/// <summary>The stored row as the U-08 conversion reads it; every field is null unless <c>Outcome</c> is Found.</summary>
public sealed record ProductDimensionReadV1(
    ProductDimensionOutcomeV1 Outcome,
    string? SectionKind,
    decimal? ThicknessMm,
    decimal? WidthMm,
    decimal? LengthMm,
    decimal? HeightMm,
    decimal? OuterDiameterMm,
    decimal? WallThicknessMm,
    decimal? KgPerMetre,
    long? Version)
{
    public static ProductDimensionReadV1 Without(ProductDimensionOutcomeV1 outcome) =>
        new(outcome, null, null, null, null, null, null, null, null, null);
}

/// <summary>
/// The read port the U-08 conversion uses (STK-S11): the shared card's "Ebat/kalınlık" row of a product, read by an actor of the
/// server-resolved company. Access only (authenticated member of the company); no permission is measured — the answer is
/// reference data and the consumer fences its own operation. Never throws for a missing card or row.
/// </summary>
public interface IProductDimensionReaderV1
{
    Task<ProductDimensionReadV1> GetAsync(Guid productId, CancellationToken cancellationToken = default);
}
