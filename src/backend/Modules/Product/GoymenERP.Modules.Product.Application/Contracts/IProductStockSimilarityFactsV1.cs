namespace GoymenERP.Modules.Product.Application.Contracts;

/// <summary>
/// Product-owned, authorized batch read of the facts an Inventory "benzer ürün" check needs (KLN-D39/D70). Like the
/// technical matcher, one ProductId may occur with several stored versions; Product reads the current records once
/// and answers exactly one item per requested product/version pair. Facts are published only for the exact currently
/// stored version with an accepted real family/template release; anything else answers with null facts, never a
/// guessed or current-version value. This read does not decide similarity, tolerance, stock or allocation.
/// </summary>
public interface IProductStockSimilarityFactsV1
{
    Task<ProductStockSimilarityBatchV1> ReadSimilarityAsync(
        ProductStockSimilarityQueryV1 query,
        CancellationToken cancellationToken = default);
}

public sealed record ProductStockSimilarityQueryV1(
    IReadOnlyList<ProductStockTechnicalMatchSelectionV1> Selections);

public sealed record ProductStockSimilarityBatchV1(
    IReadOnlyList<ProductStockSimilarityResultV1> Items);

/// <summary>
/// One requested pair. <see cref="Identity"/> is set only after exact-version validation. <see cref="SimilarityKey"/>
/// is an opaque key, equal exactly when two products share the family (Cins/Tür/Tip), the template release and every
/// controlled (dictionary) attribute value, among them the quality; it is null when the accepted technical facts are
/// incomplete. Measures are the card's nominal millimetres; <see cref="LengthMm"/> is null when the template has no
/// length role. <see cref="CoatingCode"/> is the stored code of the template's coating field (KLN-D95: a similar product
/// must carry the same coating), published separately so the consumer can check it explicitly; it is null whenever the
/// similarity key is null or the coating is not stated.
/// </summary>
public sealed record ProductStockSimilarityResultV1(
    Guid ProductId,
    string ExpectedProductVersion,
    ProductStockDisplayIdentityV1? Identity,
    string? SimilarityKey,
    decimal? ThicknessMm,
    decimal? WidthMm,
    decimal? LengthMm,
    string? CoatingCode);
