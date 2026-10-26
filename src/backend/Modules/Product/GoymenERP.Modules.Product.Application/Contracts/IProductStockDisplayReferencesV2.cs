namespace GoymenERP.Modules.Product.Application.Contracts;

/// <summary>
/// Product-owned, authorized batch projection for the opt-in Inventory stock-display V2 contract.
/// It preserves the exact Product/version identity and V1 nominal facts while adding the Product-owned
/// nominal length. The length key is always part of the result: accepted legacy families publish null;
/// a cut-sheet/plate family publishes its exact stored positive millimetres or fails closed with field errors.
/// </summary>
public interface IProductStockDisplayReferencesV2
{
    Task<ProductStockDisplayBatchV2> ReadAsync(
        ProductStockDisplayBatchQueryV1 query,
        CancellationToken cancellationToken = default);
}

public sealed record ProductStockDisplayBatchV2(
    IReadOnlyList<ProductStockDisplayResultV2> Items);

public sealed record ProductStockDisplayResultV2(
    Guid ProductId,
    string ExpectedProductVersion,
    ProductStockDisplayIdentityV1? Identity,
    ProductStockNominalTechnicalV2? Technical,
    IReadOnlyList<ProductFieldError> Errors);

/// <summary>
/// Accepted nominal Product facts for stock display. <c>LengthMm</c> is deliberately nullable but is
/// not optional: null means the accepted legacy family/template has no length role. No lot measure,
/// zero, description inference, conversion, rounding or fallback is applied.
/// </summary>
public sealed record ProductStockNominalTechnicalV2(
    ProductManufacturingFamilyRevisionV1 Family,
    ProductManufacturingTemplateRevisionV1 Template,
    decimal ThicknessMm,
    decimal WidthMm,
    decimal? LengthMm);
