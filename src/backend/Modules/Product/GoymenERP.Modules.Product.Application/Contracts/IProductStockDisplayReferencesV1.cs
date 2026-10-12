namespace GoymenERP.Modules.Product.Application.Contracts;

/// <summary>
/// Product-owned, authorized batch projection for Inventory stock rows. A reference is returned only
/// when the requested version is the exact currently stored Product version; an older label is never
/// relabelled with the current Product name or nominal dimensions. This read does not decide stock
/// eligibility, availability, allocation, lifecycle or location display.
/// </summary>
public interface IProductStockDisplayReferencesV1
{
    Task<ProductStockDisplayBatchV1> ReadAsync(
        ProductStockDisplayBatchQueryV1 query,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// Product-owned, authorized batch predicate for Inventory filters over exact-version technical facts.
/// Unlike the display port, one ProductId may occur with multiple stored versions; Product reads the
/// current records once and returns only the exact product/version pairs whose accepted typed facts match.
/// </summary>
public interface IProductStockTechnicalMatcherV1
{
    Task<ProductStockTechnicalMatchBatchV1> MatchAsync(
        ProductStockTechnicalMatchQueryV1 query,
        CancellationToken cancellationToken = default);
}

public sealed record ProductStockTechnicalMatchQueryV1(
    IReadOnlyList<ProductStockTechnicalMatchSelectionV1> Selections,
    Guid? ProductFamilyId,
    decimal? ThicknessMm,
    decimal? ThicknessMinMm,
    decimal? ThicknessMaxMm,
    decimal? WidthMm,
    decimal? WidthMinMm,
    decimal? WidthMaxMm);

public sealed record ProductStockTechnicalMatchSelectionV1(
    Guid ProductId,
    string ExpectedProductVersion);

public sealed record ProductStockTechnicalMatchBatchV1(
    IReadOnlyList<ProductStockTechnicalMatchSelectionV1> Matches);

public sealed record ProductStockDisplayBatchQueryV1(
    IReadOnlyList<ProductStockDisplaySelectionV1> Selections);

public sealed record ProductStockDisplaySelectionV1(
    Guid ProductId,
    string ExpectedProductVersion);

public sealed record ProductStockDisplayBatchV1(
    IReadOnlyList<ProductStockDisplayResultV1> Items);

/// <summary>
/// Identity is available only after exact-version validation. Technical is additionally gated by the
/// accepted real family/template release and complete canonical millimetre values. Field errors never
/// authorize a caller to infer a missing value.
/// </summary>
public sealed record ProductStockDisplayResultV1(
    Guid ProductId,
    string ExpectedProductVersion,
    ProductStockDisplayIdentityV1? Identity,
    ProductStockNominalTechnicalV1? Technical,
    IReadOnlyList<ProductFieldError> Errors);

public sealed record ProductStockDisplayIdentityV1(
    Guid ProductId,
    string ProductVersion,
    string Code,
    string Name);

public sealed record ProductStockNominalTechnicalV1(
    ProductManufacturingFamilyRevisionV1 Family,
    ProductManufacturingTemplateRevisionV1 Template,
    decimal ThicknessMm,
    decimal WidthMm);

public static class ProductStockDisplayCodesV1
{
    public const int MaximumBatchSize = 100;
    public const string InvalidRequest = "product_stock_display_invalid_request";
}
