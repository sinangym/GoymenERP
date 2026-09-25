using GoymenERP.BuildingBlocks.Multitenancy;
using GoymenERP.BuildingBlocks.Security;
using GoymenERP.Modules.Product.Application.Contracts;

namespace GoymenERP.Modules.Product.Infrastructure.Persistence;

internal sealed class ProductPurchaseReadFence(ICompanyContext company, ICurrentUser user,
    IErpAuthorityRevalidationService authority)
{
    public async Task<T> FencedAsync<T>(Func<Task<T>> read, ProductPurchaseReadScope scope, CancellationToken ct)
    {
        T value;
        try { value = await read(); }
        catch (OperationCanceledException) { throw; }
        catch { await RequireScopeAsync(scope, ct); throw; }
        await RequireScopeAsync(scope, ct);
        return value;
    }

    public async Task<ProductPurchaseReadScope> RequireScopeAsync(ProductPurchaseReadScope? expected, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        if (!user.IsAuthenticated || string.IsNullOrWhiteSpace(user.UserId) || !company.IsResolved
            || company.CompanyId == Guid.Empty || !user.CanAccessCompany(company.CompanyId)) throw Denied();
        var scope = new ProductPurchaseReadScope(company.CompanyId, user.UserId);
        if (expected is not null && expected != scope) throw Denied();
        var grant = await authority.RevalidatePermissionScopeAsync(ProductPermissionManifestV1.OperationalSelectionRead,
            scope.CompanyId, ct);
        ct.ThrowIfCancellationRequested();
        if (!grant.IsGranted || !user.IsAuthenticated || !company.IsResolved || company.CompanyId != scope.CompanyId
            || user.UserId != scope.ActorId || !user.CanAccessCompany(scope.CompanyId)) throw Denied();
        return scope;
    }

    private static ProductOperationException Denied() => new("product_access_denied", 403);
}
