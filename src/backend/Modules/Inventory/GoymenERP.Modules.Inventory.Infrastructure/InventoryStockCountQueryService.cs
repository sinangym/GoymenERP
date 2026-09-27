using GoymenERP.BuildingBlocks.Multitenancy;
using GoymenERP.BuildingBlocks.Security;
using GoymenERP.Modules.Inventory.Application;

namespace GoymenERP.Modules.Inventory.Infrastructure;

/// <summary>
/// ST-05 read of one label's counts, behind the ST-02 label-detail fence: company-wide
/// <c>inventory.labels.read</c> and <c>inventory.balances.read</c>, measured with the actor/company identity before
/// and again after the awaited owner read. A label of another company is the same 404 as an unknown one. No new
/// permission, no write.
/// </summary>
internal sealed class InventoryStockCountQueryService(
    ICompanyContext company,
    ICurrentUser user,
    IErpAuthorityRevalidationService authority,
    IInventoryStockCountReaderV1 reader) : IInventoryStockCountQueryV1
{
    public async Task<InventoryLabelStockCountsV1> GetAsync(Guid labelId, CancellationToken cancellationToken = default)
    {
        var scope = await RequireAsync(null, cancellationToken);
        if (labelId == Guid.Empty)
        {
            throw Error(404, InventoryStockCountCodesV1.LabelNotFound);
        }

        InventoryLabelStockCountsV1? counts;
        try
        {
            counts = await reader.ReadStockCountsAsync(labelId, cancellationToken: cancellationToken);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception exception)
        {
            await RequireAsync(scope, cancellationToken);
            throw exception as InventoryStockCountException
                ?? Error(503, InventoryStockCountCodesV1.Unavailable, exception);
        }

        await RequireAsync(scope, cancellationToken);
        if (counts is null)
        {
            throw Error(404, InventoryStockCountCodesV1.LabelNotFound);
        }

        if (counts.LabelId != labelId || counts.Items.Any(item => item is null || item.LabelId != labelId
                || item.CompanyId != scope.CompanyId))
        {
            throw Error(503, InventoryStockCountCodesV1.Unavailable);
        }

        return counts;
    }

    private async Task<Scope> RequireAsync(Scope? expected, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!user.IsAuthenticated || string.IsNullOrWhiteSpace(user.UserId))
        {
            throw Error(expected is null ? 401 : 403, expected is null
                ? InventoryStockCountCodesV1.Unauthenticated
                : InventoryStockCountCodesV1.Forbidden);
        }

        if (!company.IsResolved)
        {
            throw Error(403, InventoryStockCountCodesV1.Forbidden);
        }

        var scope = new Scope(company.CompanyId, user.UserId);
        if (expected is not null && expected != scope)
        {
            throw Error(403, InventoryStockCountCodesV1.Forbidden);
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
                throw Error(503, InventoryStockCountCodesV1.Unavailable, exception);
            }

            cancellationToken.ThrowIfCancellationRequested();
            RequireIdentity(scope);
            if (!grant.IsGranted || !grant.IsCompanyWide)
            {
                throw Error(403, InventoryStockCountCodesV1.Forbidden);
            }
        }

        return scope;
    }

    private void RequireIdentity(Scope scope)
    {
        if (!company.IsResolved || company.CompanyId != scope.CompanyId || scope.CompanyId == Guid.Empty
            || !user.IsAuthenticated || user.UserId != scope.ActorId || !user.CanAccessCompany(scope.CompanyId))
        {
            throw Error(403, InventoryStockCountCodesV1.Forbidden);
        }
    }

    private static InventoryStockCountException Error(int status, string code, Exception? exception = null) =>
        new(status, code, exception);

    private sealed record Scope(Guid CompanyId, string ActorId);
}
