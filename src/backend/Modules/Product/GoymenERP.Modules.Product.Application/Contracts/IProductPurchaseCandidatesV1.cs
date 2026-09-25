namespace GoymenERP.Modules.Product.Application.Contracts;

/// <summary>Bounded purchase-only discovery. Requires company operational-selection.read;
/// the consuming endpoint must also enforce its own authority. Saving requires fresh eligibility.</summary>
public interface IProductPurchaseCandidatesV1
{
    Task<ProductPurchaseCandidatesV1> SearchAsync(ProductPurchaseCandidateQueryV1 query,
        CancellationToken cancellationToken);
}
/// <summary>Limit (1..100, default 25) bounds examined records, not promised eligible results.</summary>
public sealed record ProductPurchaseCandidateQueryV1(string? Q, DateOnly EffectiveOn, int Limit = 25);
/// <summary>Refine the search when SearchWindowFull. Empty is not an exhaustive catalogue claim.
/// No rejected identity, reason, total count or raw cursor is exposed.</summary>
public sealed record ProductPurchaseCandidatesV1(IReadOnlyList<ProductPurchaseCandidateV1> Items, bool SearchWindowFull);
public sealed record ProductPurchaseCandidateV1(Guid ProductId, string ProductVersion, string Code, string Name,
    Guid UnitId, string UnitCode, Guid ProfileId, string ProfileVersion);
