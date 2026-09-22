namespace GoymenERP.Modules.Product.Application.Contracts;

/// <summary>
/// Bounded operational discovery, gated by the server company's operational-selection.read.
/// The consumer also requires its own permission. This is not global master listing or
/// command authorization. Revalidate the chosen product/version/unit when saving.
/// </summary>
public interface IProductSalesCandidatesV1
{
    Task<ProductSalesCandidatesV1> SearchAsync(ProductSalesCandidateQueryV1 query, CancellationToken cancellationToken);
}

/// <summary>Limit bounds records examined, not a promised number of eligible results. Same query limits as Product search.</summary>
public sealed record ProductSalesCandidateQueryV1(string? Q, DateOnly EffectiveOn, int Limit = 25);

/// <summary>
/// SearchWindowFull means more matching records exist outside this bounded window;
/// refine the search. No total count, raw cursor, rejected identity or eligibility reason is exposed.
/// Empty items do not assert that the complete catalogue has no eligible products.
/// </summary>
public sealed record ProductSalesCandidatesV1(IReadOnlyList<ProductSalesCandidateV1> Items, bool SearchWindowFull);

public sealed record ProductSalesCandidateV1(Guid ProductId, string ProductVersion, string Code, string Name,
    IReadOnlyList<ProductSalesCandidateUnitV1> OrderUnits);

/// <summary>Settings identity code and exact effective profile; no inferred unit or conversion.</summary>
public sealed record ProductSalesCandidateUnitV1(Guid UnitId, string Code, Guid ProfileId, string ProfileVersion);
