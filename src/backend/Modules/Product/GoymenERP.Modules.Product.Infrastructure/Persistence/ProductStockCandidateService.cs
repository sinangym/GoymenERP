using System.Globalization;
using GoymenERP.BuildingBlocks.Multitenancy;
using GoymenERP.BuildingBlocks.Security;
using GoymenERP.Modules.Product.Application.Contracts;

namespace GoymenERP.Modules.Product.Infrastructure.Persistence;

/// <summary>
/// Releases only real stock-eligibility positives. The surrounding Inventory command retains its
/// own permission and write fences; this read independently remeasures Product's narrow operational
/// selection permission before and after every awaited owner read.
/// </summary>
public sealed class ProductStockCandidateService(
    ICompanyContext company,
    ICurrentUser user,
    IErpAuthorityRevalidationService authority,
    IProductStockCandidateReader records,
    IProductStockEligibilityV1 eligibility,
    ProductUnitProfileAdapter units) : IProductStockCandidatesV1
{
    public async Task<ProductStockCandidatesV1> SearchAsync(
        ProductStockCandidateQueryV1 query,
        CancellationToken cancellationToken)
    {
        var scope = await RequireScopeAsync(null, cancellationToken);
        ArgumentNullException.ThrowIfNull(query);
        var normalized = ProductQueryRules.Normalize(new ProductListQuery(Q: query.Q, PageSize: query.Limit));
        query = query with { Q = normalized.Q };

        var window = await FencedAsync(
            () => records.ReadAsync(scope, query, cancellationToken),
            scope,
            cancellationToken);
        if (window.Items.Count > query.Limit)
            throw new InvalidOperationException("Candidate reader exceeded the bounded window.");

        var result = new List<ProductStockCandidateV1>();
        foreach (var record in window.Items)
        {
            var stockUnit = record.RestoreReferences().Units?.Stock;
            if (stockUnit is null || stockUnit == Guid.Empty) continue;

            var version = record.Version.ToString(CultureInfo.InvariantCulture);
            var answer = await FencedAsync(
                () => eligibility.EvaluateAsync(
                    new(record.Id, version, query.EffectiveOn),
                    cancellationToken),
                scope,
                cancellationToken);
            if (answer.Snapshot is not { } snapshot) continue;

            var profile = snapshot.StockUnit;
            if (answer.Errors.Count != 0
                || snapshot.ProductId != record.Id
                || snapshot.ProductVersion != version
                || profile.UnitId != stockUnit
                || profile.CompanyId != scope.CompanyId
                || snapshot.EffectiveOn != query.EffectiveOn)
                throw new InvalidOperationException("Candidate eligibility returned inconsistent identity.");

            var labelledUnit = await FencedAsync(
                () => units.ReadAsync(stockUnit.Value, query.EffectiveOn, requiresKilogram: true, cancellationToken),
                scope,
                cancellationToken);
            if (!labelledUnit.IsUsable
                || labelledUnit.Profile.Profile.ProfileId != profile.ProfileId
                || labelledUnit.Profile.Profile.Version.ToString(CultureInfo.InvariantCulture) != profile.Version)
                continue;

            result.Add(new(
                snapshot.ProductId,
                snapshot.ProductVersion,
                snapshot.Code,
                snapshot.Name,
                labelledUnit.Profile.UnitId,
                labelledUnit.Profile.UnitCode,
                labelledUnit.Profile.Profile.ProfileId,
                profile.Version));
        }

        await RequireScopeAsync(scope, cancellationToken);
        return new(result, window.HasMore);
    }

    private async Task<T> FencedAsync<T>(
        Func<Task<T>> read,
        ProductStockReadScope scope,
        CancellationToken cancellationToken)
    {
        T value;
        try
        {
            value = await read();
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch
        {
            await RequireScopeAsync(scope, cancellationToken);
            throw;
        }

        await RequireScopeAsync(scope, cancellationToken);
        return value;
    }

    private async Task<ProductStockReadScope> RequireScopeAsync(
        ProductStockReadScope? expected,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!user.IsAuthenticated
            || string.IsNullOrWhiteSpace(user.UserId)
            || !company.IsResolved
            || company.CompanyId == Guid.Empty
            || !user.CanAccessCompany(company.CompanyId))
            throw Denied();

        var scope = new ProductStockReadScope(company.CompanyId, user.UserId);
        if (expected is not null && expected != scope) throw Denied();

        var grant = await authority.RevalidatePermissionScopeAsync(
            ProductPermissionManifestV1.OperationalSelectionRead,
            scope.CompanyId,
            cancellationToken);
        cancellationToken.ThrowIfCancellationRequested();
        if (!grant.IsGranted
            || !user.IsAuthenticated
            || !company.IsResolved
            || company.CompanyId != scope.CompanyId
            || user.UserId != scope.ActorId
            || !user.CanAccessCompany(scope.CompanyId))
            throw Denied();
        return scope;
    }

    private static ProductOperationException Denied() =>
        new(ProductSalesEligibilityService.AccessDeniedCode, 403);
}
