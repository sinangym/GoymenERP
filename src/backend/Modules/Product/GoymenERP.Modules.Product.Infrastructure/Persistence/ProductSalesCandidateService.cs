using System.Globalization;
using GoymenERP.BuildingBlocks.Multitenancy;
using GoymenERP.BuildingBlocks.Security;
using GoymenERP.Modules.Product.Application.Contracts;

namespace GoymenERP.Modules.Product.Infrastructure.Persistence;

public sealed class ProductSalesCandidateService(ICompanyContext company, ICurrentUser user,
    IErpAuthorityRevalidationService authority, IProductSalesCandidateReader records,
    IProductSalesEligibilityV1 eligibility, ProductUnitProfileAdapter units) : IProductSalesCandidatesV1
{
    public async Task<ProductSalesCandidatesV1> SearchAsync(ProductSalesCandidateQueryV1 query,
        CancellationToken cancellationToken)
    {
        var scope = await RequireScopeAsync(null, cancellationToken);
        ArgumentNullException.ThrowIfNull(query);
        var normalized = ProductQueryRules.Normalize(new ProductListQuery(Q: query.Q, PageSize: query.Limit));
        query = query with { Q = normalized.Q };
        var window = await FencedAsync(() => records.ReadAsync(scope, query, cancellationToken), scope, cancellationToken);
        if (window.Items.Count > query.Limit)
            throw new InvalidOperationException("Candidate reader exceeded the bounded window.");
        var result = new List<ProductSalesCandidateV1>();
        foreach (var record in window.Items)
        {
            var salesUnit = record.RestoreReferences().Units?.Sales;
            if (salesUnit is null || salesUnit == Guid.Empty) continue;
            var answer = await FencedAsync(() => eligibility.EvaluateAsync(new(record.Id,
                record.Version.ToString(CultureInfo.InvariantCulture), salesUnit.Value, query.EffectiveOn), cancellationToken),
                scope, cancellationToken);
            if (answer.Snapshot is not { } snapshot) continue;
            if (answer.Errors.Count != 0 || snapshot.ProductId != record.Id
                || snapshot.ProductVersion != record.Version.ToString(CultureInfo.InvariantCulture)
                || snapshot.OrderUnit.UnitId != salesUnit || snapshot.OrderUnit.CompanyId != scope.CompanyId
                || snapshot.EffectiveOn != query.EffectiveOn)
                throw new InvalidOperationException("Candidate eligibility returned inconsistent identity.");
            var unit = await FencedAsync(() => units.ReadAsync(salesUnit.Value, query.EffectiveOn, false, cancellationToken),
                scope, cancellationToken);
            // A concurrent Settings profile change is not a coherent selectable result.
            if (!unit.IsUsable || unit.Profile.Profile.ProfileId != snapshot.OrderUnit.ProfileId
                || unit.Profile.Profile.Version.ToString(CultureInfo.InvariantCulture) != snapshot.OrderUnit.Version) continue;
            result.Add(new(snapshot.ProductId, snapshot.ProductVersion, snapshot.Code, snapshot.Name,
                [new(unit.Profile.UnitId, unit.Profile.UnitCode, unit.Profile.Profile.ProfileId,
                    snapshot.OrderUnit.Version)]));
        }
        await RequireScopeAsync(scope, cancellationToken);
        return new(result, window.HasMore);
    }

    private async Task<T> FencedAsync<T>(Func<Task<T>> read, ProductSalesReadScope scope, CancellationToken ct)
    {
        T value;
        try { value = await read(); }
        catch (OperationCanceledException) { throw; }
        catch { await RequireScopeAsync(scope, ct); throw; }
        await RequireScopeAsync(scope, ct);
        return value;
    }

    private async Task<ProductSalesReadScope> RequireScopeAsync(ProductSalesReadScope? expected, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        if (!user.IsAuthenticated || string.IsNullOrWhiteSpace(user.UserId) || !company.IsResolved
            || company.CompanyId == Guid.Empty || !user.CanAccessCompany(company.CompanyId)) throw Denied();
        var scope = new ProductSalesReadScope(company.CompanyId, user.UserId);
        if (expected is not null && expected != scope) throw Denied();
        var grant = await authority.RevalidatePermissionScopeAsync(ProductPermissionManifestV1.OperationalSelectionRead,
            scope.CompanyId, ct);
        ct.ThrowIfCancellationRequested();
        if (!grant.IsGranted || !user.IsAuthenticated || !company.IsResolved || company.CompanyId != scope.CompanyId
            || user.UserId != scope.ActorId || !user.CanAccessCompany(scope.CompanyId)) throw Denied();
        return scope;
    }

    private static ProductOperationException Denied() => new(ProductSalesEligibilityService.AccessDeniedCode, 403);
}
