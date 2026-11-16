namespace GoymenERP.Modules.Product.Application.Contracts;

/// <summary>
/// BS-9 (Sinan, 2026-11-11, <c>docs/decisions/SINAN_KARARLARI_2026-11-11_BEKLEYEN_SORULAR.md</c>): the product card has a separate
/// "Ebat/kalınlık" tab — which size and thickness the product is made of. The U-08 unit conversion reads its input from this
/// row; a product without a row answers <see cref="ProductDimensionOutcomeV1.NoSource"/> (no estimate). The section kinds follow
/// the formula families of <c>docs/delivery/URUN_BIRIM_CEVRIM_KATSAYI_ADAYLARI_v1.md</c> §3:
/// <list type="bullet">
/// <item><see cref="Sheet"/> (F-A, kesilmiş sac/levha): thickness, width and length.</item>
/// <item><see cref="Coil"/> (F-B, rulo / dilinmiş bant): thickness and width.</item>
/// <item><see cref="RoundTube"/> (F-C/F-D, yuvarlak boru): outer diameter and wall thickness (2 × wall ≤ diameter); its kg/m is
/// OPTIONAL and, when the company's imported table holds the (diameter, wall) pair, the table value (URN-D11/D16) — without it
/// an operation that needs kg is refused by its consumer (KLN-D66).</item>
/// <item><see cref="Profile"/> (F-C/F-D, kutu / dikdörtgen profil): width, height and wall thickness; its kg/m is REQUIRED and
/// comes only from the company's imported table (URN-D16, KLN-D54) — a card whose measures the acting company's current table
/// does not hold is not saved.</item>
/// </list>
/// KLN-D87 (SINAN-CEVAP 33, STK-SC33-b): every KLN-D48 family is a section kind too; the four kinds above keep their fields
/// (URN-D11). The field list of each kind is the rule table of the Infrastructure shape (one table feeds the 400 answer and the
/// database check):
/// <list type="bullet">
/// <item>Long products: <see cref="EqualAngle"/>, <see cref="UnequalAngle"/>, <see cref="FlatBar"/> (lama),
/// <see cref="NarrowFlatBar"/> (silme), <see cref="RoundBar"/> (mil), <see cref="SquareBar"/>, <see cref="HexBar"/>,
/// <see cref="TeeBar"/>; the length is optional.</item>
/// <item><see cref="StructuralProfile"/> (NPU/UPN, NPI, IPE, HEA/HEB/HEM): the standard series
/// (<see cref="ProductProfileSeriesV1"/>) and the size designation (length optional); its kg/m is NEVER typed and comes only from the
/// company's imported table row of that series and size (KLN-D54/D87) — without one it reads as no source.</item>
/// <item><see cref="Rebar"/>, <see cref="WireRod"/>, <see cref="WeldedMesh"/> (wire diameter, mesh spacing, width, length).</item>
/// <item>Sheet derivatives <see cref="ChequeredSheet"/>, <see cref="PerforatedSheet"/>, <see cref="CorrugatedSheet"/>: the
/// sheet measures.</item>
/// <item><see cref="ErwTube"/> / <see cref="ErwProfile"/> with the round tube / profile measures and table rule (URN-D16), and
/// their painted forms <see cref="PaintedErwTube"/> / <see cref="PaintedErwProfile"/>, which add the surface (paint/colour).</item>
/// </list>
/// The long products, rebar, wire rod, welded mesh and sheet derivatives carry no kg/m source yet: an operation that needs kg is
/// refused for them by its consumer (KLN-D66, fail-closed). The source and version of the standard table are not chosen here
/// (KLN-D87): the company imports a source-labelled table (KLN-D54).
/// URN-D16 (STK-S37R): a kg/m is NEVER typed on the card; the view and the read port resolve it live from the server-resolved
/// company's current table (<see cref="IProductProfileWeightTableServiceV1"/>) and name its source label and version. Every
/// measure is millimetres (at most three decimals): the inputs stay at full precision and the coefficient is rounded once where
/// it is produced (URN-D8), never here. The density is the company Settings value (BS-9); the card stores none.
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

    /// <summary>Eşit kanatlı köşebent: thickness and leg (width); length optional.</summary>
    public const string EqualAngle = "EqualAngle";

    /// <summary>Eşit olmayan kanatlı köşebent: thickness and the two legs (width, height); length optional.</summary>
    public const string UnequalAngle = "UnequalAngle";

    /// <summary>Lama: thickness and width; length optional.</summary>
    public const string FlatBar = "FlatBar";

    /// <summary>Silme: thickness and width; length optional.</summary>
    public const string NarrowFlatBar = "NarrowFlatBar";

    /// <summary>Dolu yuvarlak (mil): diameter; length optional.</summary>
    public const string RoundBar = "RoundBar";

    /// <summary>Dolu kare: side (width); length optional.</summary>
    public const string SquareBar = "SquareBar";

    /// <summary>Altıgen: across flats (width); length optional.</summary>
    public const string HexBar = "HexBar";

    /// <summary>T demiri: thickness, width and height; length optional.</summary>
    public const string TeeBar = "TeeBar";

    /// <summary>Yapısal profil (NPU/UPN, NPI, IPE, HEA/HEB/HEM): series and size designation, length optional; kg/m from the table.</summary>
    public const string StructuralProfile = "StructuralProfile";

    /// <summary>İnşaat demiri: diameter; length optional.</summary>
    public const string Rebar = "Rebar";

    /// <summary>Filmaşin: diameter.</summary>
    public const string WireRod = "WireRod";

    /// <summary>Hasır çelik: wire diameter, mesh spacing, width and length.</summary>
    public const string WeldedMesh = "WeldedMesh";

    /// <summary>Baklavalı sac: the sheet measures.</summary>
    public const string ChequeredSheet = "ChequeredSheet";

    /// <summary>Delikli sac: the sheet measures.</summary>
    public const string PerforatedSheet = "PerforatedSheet";

    /// <summary>Trapez / oluklu sac: the sheet measures.</summary>
    public const string CorrugatedSheet = "CorrugatedSheet";

    /// <summary>ERW boru: the round tube measures and table rule.</summary>
    public const string ErwTube = "ErwTube";

    /// <summary>Boyalı ERW boru: the round tube measures and the surface (paint/colour).</summary>
    public const string PaintedErwTube = "PaintedErwTube";

    /// <summary>ERW profil: the profile measures and table rule (kg/m required).</summary>
    public const string ErwProfile = "ErwProfile";

    /// <summary>Boyalı ERW profil: the profile measures, table rule and the surface (paint/colour).</summary>
    public const string PaintedErwProfile = "PaintedErwProfile";

    public static IReadOnlyList<string> All { get; } =
    [
        Sheet, Coil, RoundTube, Profile, EqualAngle, UnequalAngle, FlatBar, NarrowFlatBar, RoundBar, SquareBar, HexBar, TeeBar,
        StructuralProfile, Rebar, WireRod, WeldedMesh, ChequeredSheet, PerforatedSheet, CorrugatedSheet, ErwTube, PaintedErwTube,
        ErwProfile, PaintedErwProfile,
    ];

    public static bool IsKnown(string? sectionKind) => sectionKind is not null && All.Contains(sectionKind, StringComparer.Ordinal);

    /// <summary>
    /// The kind whose rows of the company's kg/m table hold this kind's kg/m (URN-D16/KLN-D87): a round tube / ERW tube reads the
    /// round tube rows, a profile / ERW profile the profile rows, a structural profile its series-and-size rows; null for a kind
    /// without a table source.
    /// </summary>
    public static string? WeightTableKind(string? sectionKind) => sectionKind switch
    {
        RoundTube or ErwTube or PaintedErwTube => RoundTube,
        Profile or ErwProfile or PaintedErwProfile => Profile,
        StructuralProfile => StructuralProfile,
        _ => null,
    };

    /// <summary>URN-D16: a profile (and an ERW / painted ERW profile) card is not saved without its table kg/m.</summary>
    public static bool RequiresTableWeight(string? sectionKind) => WeightTableKind(sectionKind) == Profile;
}

/// <summary>
/// KLN-D87: the structural profile series. NPU is written as its EN name UPN (one value per series). The size designation is
/// free within <see cref="ProductDimensionInputLimitsV1.ProfileSizeMaximumLength"/> ASCII capitals, digits and dots ("200",
/// "100A", "22.5"); no standard size list is chosen here.
/// </summary>
public static class ProductProfileSeriesV1
{
    public const string Upn = "UPN";
    public const string Npi = "NPI";
    public const string Ipe = "IPE";
    public const string Hea = "HEA";
    public const string Heb = "HEB";
    public const string Hem = "HEM";

    public static IReadOnlyList<string> All { get; } = [Upn, Npi, Ipe, Hea, Heb, Hem];

    public static bool IsKnown(string? series) => series is not null && All.Contains(series, StringComparer.Ordinal);
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

    /// <summary>URN-D16: a profile card whose measures the acting company's current kg/m table does not hold is not saved.</summary>
    public const string WeightSourceMissing = "product_dimension_weight_source_missing";

    /// <summary>
    /// URN-D19: a profile whose measures stand in the acting company's current kg/m table in both directions (width and height
    /// swapped) with different kg/m is a table conflict; the card is not saved until the table is corrected.
    /// </summary>
    public const string WeightSourceConflict = "product_dimension_weight_source_conflict";

    public const string ProviderUnavailable = "product_dimension_provider_unavailable";
}

public static class ProductDimensionInputLimitsV1
{
    public const int ReasonMaximumLength = 4000;

    /// <summary>Millimetre measures keep at most three decimals (Ö2, the card's nominal measures).</summary>
    public const int MillimetreDecimals = 3;

    /// <summary>The imported table kg/m keeps at most six decimals; the coefficient decimals of Settings round it later (URN-D8).</summary>
    public const int KgPerMetreDecimals = 6;

    /// <summary>The largest millimetre measure the column (decimal(18,3)) holds, exclusive.</summary>
    public const decimal MillimetreExclusiveMaximum = 1_000_000_000_000_000m;

    /// <summary>The largest kg/m the column (decimal(18,6)) holds, exclusive.</summary>
    public const decimal KgPerMetreExclusiveMaximum = 1_000_000_000_000m;

    /// <summary>The section kind column (the longest kind name is 17 characters).</summary>
    public const int SectionKindMaximumLength = 32;

    /// <summary>KLN-D87: a structural profile size designation (ASCII capitals, digits, dots).</summary>
    public const int ProfileSizeMaximumLength = 16;

    /// <summary>The structural profile series column (<see cref="ProductProfileSeriesV1"/>).</summary>
    public const int ProfileSeriesMaximumLength = 8;

    /// <summary>KLN-D87: the painted ERW surface (paint/colour) text.</summary>
    public const int SurfaceFinishMaximumLength = 200;
}

/// <summary>
/// Set the "Ebat/kalınlık" row of a product on the shared card, acting in the server-resolved company. <c>SectionKind</c> is one
/// of <see cref="ProductDimensionSectionKindsV1"/>; the measures the kind needs are required and the others must be null
/// (see the kinds). <c>ExpectedVersion</c> is the row's version as decimal text ("0" = none yet). There is no kg/m field: the kg/m
/// is the company's imported table value (URN-D16). KLN-D87 adds the trailing optional fields: <c>ProfileSeries</c> and
/// <c>ProfileSize</c> (structural profile), <c>MeshSpacingMm</c> (welded mesh; the wire diameter is <c>OuterDiameterMm</c>)
/// and <c>SurfaceFinish</c> (painted ERW). A text field is never trimmed or re-cased: a value that does not match is refused.
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
    string? Reason,
    string? ProfileSeries = null,
    string? ProfileSize = null,
    decimal? MeshSpacingMm = null,
    string? SurfaceFinish = null);

/// <summary>
/// The shared card's "Ebat/kalınlık" row ("0" and nulls while none is stored), read in <c>CompanyId</c> (the server-resolved
/// company); <c>UpdatedInCompanyId</c> is the company in which the last change was made. <c>KgPerMetre</c> is the value of
/// <c>CompanyId</c>'s current kg/m table for the stored measures (null when the table holds none), with that table's
/// <c>KgPerMetreSourceLabel</c> and <c>KgPerMetreTableVersion</c> (URN-D14/D16). The KLN-D87 fields trail.
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
    Guid? UpdatedInCompanyId,
    string? KgPerMetreSourceLabel,
    string? KgPerMetreTableVersion,
    string? ProfileSeries = null,
    string? ProfileSize = null,
    decimal? MeshSpacingMm = null,
    string? SurfaceFinish = null);

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

/// <summary>
/// The stored row as the U-08 conversion reads it; every field is null unless <c>Outcome</c> is Found. <c>KgPerMetre</c> is the
/// server-resolved company's current imported table value for the measures (URN-D16), with its source label and table version;
/// null when the table holds none (a profile or round tube without a table value: an operation needing kg is refused, KLN-D66)
/// and for every KLN-D87 kind without a table source. The KLN-D87 fields trail.
/// A profile matches the table in either direction (URN-D19: width and height swapped, same wall). <c>KgPerMetreConflict</c> is
/// true when both directions stand in the table with different kg/m: <c>KgPerMetre</c> is then null and the source label and
/// table version name the conflicting table, so a consumer refuses with its explicit conflict code instead of "no value".
/// </summary>
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
    long? Version,
    string? KgPerMetreSourceLabel = null,
    long? KgPerMetreTableVersion = null,
    string? ProfileSeries = null,
    string? ProfileSize = null,
    decimal? MeshSpacingMm = null,
    string? SurfaceFinish = null,
    bool KgPerMetreConflict = false)
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
