using System.Globalization;
using GoymenERP.BuildingBlocks.Multitenancy;
using GoymenERP.BuildingBlocks.Security;
using GoymenERP.Modules.Product.Application.Contracts;

namespace GoymenERP.Modules.Product.Infrastructure.Persistence;

public sealed class ProductPurchaseCandidateService(ICompanyContext company, ICurrentUser user,
    IErpAuthorityRevalidationService authority, IProductPurchaseCandidateReader records,
    IProductPurchaseEligibilityV1 eligibility) : IProductPurchaseCandidatesV1
{
    private readonly ProductPurchaseReadFence fence = new(company, user, authority);

    public async Task<ProductPurchaseCandidatesV1> SearchAsync(ProductPurchaseCandidateQueryV1 query,
        CancellationToken cancellationToken)
    {
        var scope = await fence.RequireScopeAsync(null, cancellationToken);
        ArgumentNullException.ThrowIfNull(query);
        var normalized = ProductQueryRules.Normalize(new ProductListQuery(Q: query.Q, PageSize: query.Limit));
        query = query with { Q = normalized.Q };
        var window = await fence.FencedAsync(() => records.ReadAsync(scope, query, cancellationToken), scope, cancellationToken);
        if (window.Items.Count > query.Limit) throw new InvalidOperationException("Candidate reader exceeded the bounded window.");
        var result = new List<ProductPurchaseCandidateV1>();
        foreach (var record in window.Items)
        {
            var purchase = record.RestoreReferences().Units?.Purchase;
            if (purchase is null || purchase == Guid.Empty) continue;
            var version = record.Version.ToString(CultureInfo.InvariantCulture);
            var answer = await fence.FencedAsync(() => eligibility.EvaluateAsync(
                new(record.Id, version, purchase.Value, query.EffectiveOn), cancellationToken), scope, cancellationToken);
            if (answer.Snapshot is not { } snapshot) continue;
            var unit = snapshot.PurchaseUnit;
            if (answer.Errors.Count != 0 || snapshot.ProductId != record.Id || snapshot.ProductVersion != version
                || unit.UnitId != purchase || unit.CompanyId != scope.CompanyId || snapshot.EffectiveOn != query.EffectiveOn)
                throw new InvalidOperationException("Candidate eligibility returned inconsistent identity.");
            result.Add(new(snapshot.ProductId, snapshot.ProductVersion, snapshot.Code, snapshot.Name,
                unit.UnitId, unit.Code, unit.ProfileId, unit.ProfileVersion));
        }
        await fence.RequireScopeAsync(scope, cancellationToken);
        return new(result, window.HasMore);
    }
}
