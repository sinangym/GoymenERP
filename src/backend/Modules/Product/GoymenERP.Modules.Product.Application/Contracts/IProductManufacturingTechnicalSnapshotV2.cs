namespace GoymenERP.Modules.Product.Application.Contracts;

/// <summary>
/// B-09 (<c>docs/delivery/KALAN_MODULLER_HAZIRLIK_PLANI_v1.md</c>): the Product-owned technical snapshot, second version, for the
/// Manufacturing recipe/plan snapshot (DEC-PRDCT-008: the recipe, the formula and the profile weight are frozen at plan approval).
/// <see cref="IProductManufacturingTechnicalSnapshotV1"/> stays unchanged and keeps its consumers; V2 publishes the same V1
/// answer and adds, from the shared card's "Ebat/kalınlık" row (BS-9, <see cref="IProductDimensionReaderV1"/>):
/// <list type="bullet">
/// <item>the recipe–product binding — product, template and dimension versions a recipe/plan keeps and re-checks;</item>
/// <item>the stored dimension row, with the explicit source (a product without a row is <c>NoSource</c>, never zero);</item>
/// <item>the unit weight (kg/m) with its source rank in the locked priority (DEC-PRD-009, master §28.2).</item>
/// </list>
/// Fail-closed channels are those of V1 (field errors, 403 <c>product_access_denied</c>, 503 codes, the integrity fault),
/// plus 503 <see cref="ProductDimensionProblemCodesV1.ProviderUnavailable"/> when the dimension row cannot be read.
/// In-process only: no route, permission, grant or migration of its own.
/// </summary>
public interface IProductManufacturingTechnicalSnapshotV2
{
    Task<ProductManufacturingTechnicalSnapshotResultV2> ReadAsync(
        ProductManufacturingTechnicalSnapshotSelectionV2 selection,
        CancellationToken cancellationToken);
}

/// <summary>
/// The V1 selection plus the optional dimension version the caller has bound (a recipe/plan re-reading its snapshot).
/// <c>ExpectedDimensionVersion</c> is decimal text ("0" = the caller bound a product without a row); null asks for the current
/// row without a binding check. A different current version is the field error
/// <c>expectedDimensionVersion</c> / <see cref="ProductApiV1.VersionConflict"/>; text that is not a version is
/// <c>expectedDimensionVersion</c> / <see cref="ProductManufacturingTechnicalSnapshotCodesV2.InvalidVersion"/>.
/// </summary>
public sealed record ProductManufacturingTechnicalSnapshotSelectionV2(
    Guid ProductId,
    string ExpectedProductVersion,
    DateOnly EffectiveOn,
    string? ExpectedDimensionVersion = null);

public sealed record ProductManufacturingTechnicalSnapshotResultV2(
    ProductManufacturingTechnicalSnapshotV2? Snapshot,
    IReadOnlyList<ProductFieldError> Errors);

/// <summary>
/// One V2 answer. <c>Technical</c> is exactly the V1 snapshot of the same read; the other blocks never change it.
/// </summary>
public sealed record ProductManufacturingTechnicalSnapshotV2(
    ProductManufacturingTechnicalSnapshotV1 Technical,
    ProductManufacturingRecipeBindingV2 RecipeBinding,
    ProductManufacturingDimensionV2 Dimension,
    ProductManufacturingUnitWeightV2 UnitWeight);

/// <summary>
/// The key a Manufacturing recipe/plan keeps for "which product facts was this built on" (B-09 reçete-ürün bağı). The product
/// version and the dimension version live in two Product stores (the card and the "Ebat/kalınlık" row); a later read whose
/// binding differs means the product facts changed after the recipe/plan was built. Product publishes the key; what the
/// consumer does on a change (re-approve, refuse) is the consumer's rule, not chosen here.
/// </summary>
public sealed record ProductManufacturingRecipeBindingV2(
    Guid ProductId,
    string ProductVersion,
    Guid TemplateId,
    string TemplateVersion,
    string DimensionVersion);

public enum ProductManufacturingDimensionSourceV2
{
    /// <summary>BS-9: the product has no "Ebat/kalınlık" row; every measure is null and the version is "0".</summary>
    NoSource = 0,

    /// <summary>The stored row; the measures its section kind needs are present (see <see cref="ProductDimensionSectionKindsV1"/>).</summary>
    Stored = 1,
}

/// <summary>
/// The shared card's "Ebat/kalınlık" row as stored: millimetres at full stored precision (at most three decimals), never
/// rounded or inferred here. <c>SectionKind</c> is one of <see cref="ProductDimensionSectionKindsV1"/>. The kg/m of the row is
/// published in <see cref="ProductManufacturingUnitWeightV2"/>, with its source. The trailing KLN-D87 fields (structural
/// profile series and size, welded mesh spacing, painted family surface finish) are null for every other section kind.
/// </summary>
public sealed record ProductManufacturingDimensionV2(
    ProductManufacturingDimensionSourceV2 Source,
    string Version,
    string? SectionKind,
    decimal? ThicknessMm,
    decimal? WidthMm,
    decimal? LengthMm,
    decimal? HeightMm,
    decimal? OuterDiameterMm,
    decimal? WallThicknessMm,
    string? ProfileSeries = null,
    string? ProfileSize = null,
    decimal? MeshSpacingMm = null,
    string? SurfaceFinish = null);

/// <summary>
/// Where a unit weight (kg/m) comes from, in the locked priority of DEC-PRD-009 / master §28.2
/// (<c>manuel doğrulanmış → ürün/paket masterı → geometrik tahmin</c>); see <see cref="ProductManufacturingUnitWeightPriorityV2"/>.
/// </summary>
public enum ProductManufacturingUnitWeightSourceV2
{
    /// <summary>Product publishes no kg/m for this product (no row, or a section kind whose row holds none).</summary>
    None = 0,

    /// <summary>
    /// A manually verified weight. Product has no store for it yet, so V2 never answers it (its owner and store are an open
    /// question, not chosen here).
    /// </summary>
    ManualVerified = 1,

    /// <summary>
    /// The product master: the kg/m the "Ebat/kalınlık" read answers — the company's current imported kg/m table value of a
    /// profile (#66 m.21, URN-D16) or of a round tube, where it wins over the formula (#66 m.24). The card never stores a typed
    /// kg/m (URN-D11); the table version is not part of the recipe binding yet.
    /// </summary>
    ProductMaster = 2,

    /// <summary>
    /// A geometric estimate (formula families F-A…F-D with the company Settings density). Product never answers it: the density
    /// is not a Product fact (BS-9), and the consumer that computes it uses the shared calculator and keeps the label.
    /// </summary>
    GeometricEstimate = 3,
}

/// <summary>The unit weight Product publishes; <c>KgPerMetre</c> is null exactly when <c>Source</c> is None.</summary>
public sealed record ProductManufacturingUnitWeightV2(
    ProductManufacturingUnitWeightSourceV2 Source,
    decimal? KgPerMetre);

/// <summary>
/// DEC-PRD-009 (KİLİTLİ): the manually verified / standard master weight wins; the geometric calculation is only an
/// auxiliary estimate. No unit weight ever overrides an actual (weighed) stock quantity or a cost.
/// </summary>
public static class ProductManufacturingUnitWeightPriorityV2
{
    /// <summary>Highest priority first.</summary>
    public static IReadOnlyList<ProductManufacturingUnitWeightSourceV2> Order { get; } =
    [
        ProductManufacturingUnitWeightSourceV2.ManualVerified,
        ProductManufacturingUnitWeightSourceV2.ProductMaster,
        ProductManufacturingUnitWeightSourceV2.GeometricEstimate,
    ];

    /// <summary>True when <paramref name="candidate"/> outranks <paramref name="current"/>; None never outranks anything.</summary>
    public static bool Outranks(ProductManufacturingUnitWeightSourceV2 candidate, ProductManufacturingUnitWeightSourceV2 current)
    {
        var candidateRank = Rank(candidate);
        if (candidateRank < 0) return false;
        var currentRank = Rank(current);
        return currentRank < 0 || candidateRank < currentRank;
    }

    private static int Rank(ProductManufacturingUnitWeightSourceV2 source)
    {
        for (var index = 0; index < Order.Count; index++)
        {
            if (Order[index] == source) return index;
        }

        return -1;
    }
}

public static class ProductManufacturingTechnicalSnapshotCodesV2
{
    /// <summary>Field error: <c>expectedDimensionVersion</c> is not "0" or a positive decimal version.</summary>
    public const string InvalidVersion = "invalid_version";

    /// <summary>The field name of the dimension binding errors.</summary>
    public const string ExpectedDimensionVersionField = "expectedDimensionVersion";
}
