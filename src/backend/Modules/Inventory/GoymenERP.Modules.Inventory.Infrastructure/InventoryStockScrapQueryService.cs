using GoymenERP.BuildingBlocks.Multitenancy;
using GoymenERP.BuildingBlocks.Security;
using GoymenERP.Modules.Inventory.Application;

namespace GoymenERP.Modules.Inventory.Infrastructure;

/// <summary>
/// ST-02 read of one label's scrap write-offs, behind the ST-02 label-detail fence: company-wide
/// <c>inventory.labels.read</c> and <c>inventory.balances.read</c>, measured with the actor/company identity before
/// and again after the awaited owner read. A label of another company is the same 404 as an unknown one. No new
/// permission, no write.
/// </summary>
internal sealed class InventoryStockScrapQueryService(
    ICompanyContext company,
    ICurrentUser user,
    IErpAuthorityRevalidationService authority,
    IInventoryStockScrapReaderV1 reader) : IInventoryStockScrapQueryV1
{
    public async Task<InventoryLabelStockScrapsV1> GetAsync(Guid labelId, CancellationToken cancellationToken = default)
    {
        var scope = await RequireAsync(null, cancellationToken);
        if (labelId == Guid.Empty)
        {
            throw Error(404, InventoryStockScrapCodesV1.LabelNotFound);
        }

        InventoryLabelStockScrapsV1? scraps;
        try
        {
            scraps = await reader.ReadStockScrapsAsync(labelId, cancellationToken: cancellationToken);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception exception)
        {
            await RequireAsync(scope, cancellationToken);
            throw exception as InventoryStockScrapException
                ?? Error(503, InventoryStockScrapCodesV1.Unavailable, exception);
        }

        await RequireAsync(scope, cancellationToken);
        if (scraps is null)
        {
            throw Error(404, InventoryStockScrapCodesV1.LabelNotFound);
        }

        if (scraps.LabelId != labelId || scraps.Items.Any(item => item is null || item.LabelId != labelId
                || item.CompanyId != scope.CompanyId))
        {
            throw Error(503, InventoryStockScrapCodesV1.Unavailable);
        }

        return scraps;
    }

    private async Task<Scope> RequireAsync(Scope? expected, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!user.IsAuthenticated || string.IsNullOrWhiteSpace(user.UserId))
        {
            throw Error(expected is null ? 401 : 403, expected is null
                ? InventoryStockScrapCodesV1.Unauthenticated
                : InventoryStockScrapCodesV1.Forbidden);
        }

        if (!company.IsResolved)
        {
            throw Error(403, InventoryStockScrapCodesV1.Forbidden);
        }

        var scope = new Scope(company.CompanyId, user.UserId);
        if (expected is not null && expected != scope)
        {
            throw Error(403, InventoryStockScrapCodesV1.Forbidden);
        }

        foreach (var permission in new[] { ErpPermissions.InventoryLabelsRead, ErpPermissions.InventoryBalancesRead })
        {
            RequireIdentity(scope);
            PermissionScope grant;
            try
            {
                grant = await authority.RevalidatePermissionScopeAsync(permission, scope.CompanyId, cancellationToken);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception exception)
            {
                RequireIdentity(scope);
                throw Error(503, InventoryStockScrapCodesV1.Unavailable, exception);
            }

            cancellationToken.ThrowIfCancellationRequested();
            RequireIdentity(scope);
            if (!grant.IsGranted || !grant.IsCompanyWide)
            {
                throw Error(403, InventoryStockScrapCodesV1.Forbidden);
            }
        }

        return scope;
    }

    private void RequireIdentity(Scope scope)
    {
        if (!company.IsResolved || company.CompanyId != scope.CompanyId || scope.CompanyId == Guid.Empty
            || !user.IsAuthenticated || user.UserId != scope.ActorId || !user.CanAccessCompany(scope.CompanyId))
        {
            throw Error(403, InventoryStockScrapCodesV1.Forbidden);
        }
    }

    private static InventoryStockScrapException Error(int status, string code, Exception? exception = null) =>
        new(status, code, exception);

    private sealed record Scope(Guid CompanyId, string ActorId);
}
