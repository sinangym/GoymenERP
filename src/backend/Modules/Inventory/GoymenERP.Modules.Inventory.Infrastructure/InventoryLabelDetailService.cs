using System.Globalization;
using GoymenERP.BuildingBlocks.Multitenancy;
using GoymenERP.BuildingBlocks.Security;
using GoymenERP.Modules.Inventory.Application;
using GoymenERP.Modules.Inventory.Domain;
using GoymenERP.Modules.Inventory.Domain.Balances;
using GoymenERP.Modules.Inventory.Domain.Labels;
using GoymenERP.Modules.Inventory.Domain.Lots;

namespace GoymenERP.Modules.Inventory.Infrastructure;

internal sealed record InventoryLabelDetailRecord(
    long Version,
    MaterialLabel Label,
    MaterialLot Lot,
    IReadOnlyList<StockBalance> Balances);

internal interface IInventoryLabelDetailReader
{
    Task<InventoryLabelDetailRecord?> ReadAsync(
        Guid companyId,
        Guid labelId,
        CancellationToken cancellationToken);
}

internal sealed class InventoryLabelDetailService(
    ICompanyContext company,
    ICurrentUser user,
    IErpAuthorityRevalidationService authority,
    IInventoryLabelDetailReader? reader) : IInventoryLabelDetailsV1
{
    public async Task<InventoryLabelDetailV1> GetAsync(
        Guid labelId,
        CancellationToken cancellationToken = default)
    {
        var scope = await RequireAsync(null, cancellationToken);
        if (labelId == Guid.Empty)
        {
            throw Error(404, InventoryLabelDetailCodesV1.NotFound);
        }

        var source = reader ?? throw Error(503, InventoryLabelDetailCodesV1.Unavailable);
        var record = await FencedAsync(
            () => source.ReadAsync(scope.CompanyId, labelId, cancellationToken),
            scope,
            cancellationToken);
        if (record is null)
        {
            throw Error(404, InventoryLabelDetailCodesV1.NotFound);
        }

        return View(record, scope.CompanyId, labelId);
    }

    private static InventoryLabelDetailV1 View(
        InventoryLabelDetailRecord record,
        Guid companyId,
        Guid labelId)
    {
        var label = record.Label;
        if (record.Version <= 0 || label.Id != labelId || label.CompanyId != companyId
            || record.Lot.CompanyId != companyId || record.Lot.Id != label.LotId
            || record.Lot.ProductId != label.ProductId
            || record.Balances.Any(balance => balance.Position.CompanyId != companyId
                || balance.Position.LabelId != label.Id
                || balance.Position.ProductId != label.ProductId))
        {
            throw Error(503, InventoryLabelDetailCodesV1.Unavailable);
        }

        try
        {
            _ = StockLedger.FromStoredBalances(record.Balances);
            return new InventoryLabelDetailV1(
                record.Version.ToString(CultureInfo.InvariantCulture),
                InventoryReadModelsV1.LabelView(label, record.Lot),
                InventoryReadModelsV1.Balances(label.Id, record.Balances));
        }
        catch (InventoryRuleException exception)
        {
            throw Error(503, InventoryLabelDetailCodesV1.Unavailable, exception);
        }
        catch (ArgumentException exception)
        {
            throw Error(503, InventoryLabelDetailCodesV1.Unavailable, exception);
        }
    }

    private async Task<T> FencedAsync<T>(
        Func<Task<T>> read,
        Scope scope,
        CancellationToken cancellationToken)
    {
        T result;
        try
        {
            result = await read();
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception exception)
        {
            await RequireAsync(scope, cancellationToken);
            if (exception is InventoryLabelDetailException)
            {
                throw;
            }

            throw Error(503, InventoryLabelDetailCodesV1.Unavailable, exception);
        }

        await RequireAsync(scope, cancellationToken);
        return result;
    }

    private async Task<Scope> RequireAsync(Scope? expected, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!user.IsAuthenticated || string.IsNullOrWhiteSpace(user.UserId))
        {
            throw Error(expected is null ? 401 : 403, expected is null
                ? InventoryLabelDetailCodesV1.Unauthenticated
                : InventoryLabelDetailCodesV1.Forbidden);
        }

        if (!company.IsResolved)
        {
            throw Error(403, InventoryLabelDetailCodesV1.Forbidden);
        }

        var scope = new Scope(company.CompanyId, user.UserId);
        if (expected is not null && expected != scope)
        {
            throw Error(403, InventoryLabelDetailCodesV1.Forbidden);
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
                throw Error(503, InventoryLabelDetailCodesV1.Unavailable, exception);
            }

            cancellationToken.ThrowIfCancellationRequested();
            RequireIdentity(scope);
            if (!grant.IsGranted || !grant.IsCompanyWide)
            {
                throw Error(403, InventoryLabelDetailCodesV1.Forbidden);
            }
        }

        return scope;
    }

    private void RequireIdentity(Scope scope)
    {
        if (!company.IsResolved || company.CompanyId != scope.CompanyId || scope.CompanyId == Guid.Empty
            || !user.IsAuthenticated || user.UserId != scope.ActorId || !user.CanAccessCompany(scope.CompanyId))
        {
            throw Error(403, InventoryLabelDetailCodesV1.Forbidden);
        }
    }

    private static InventoryLabelDetailException Error(int statusCode, string code, Exception? exception = null) =>
        new(statusCode, code, exception);

    private sealed record Scope(Guid CompanyId, string ActorId);
}
