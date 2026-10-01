using GoymenERP.BuildingBlocks.Multitenancy;
using GoymenERP.BuildingBlocks.Security;
using GoymenERP.Modules.Inventory.Application;

namespace GoymenERP.Modules.Inventory.Infrastructure;

public sealed class InventoryTransferQueryService(
    ICompanyContext companyContext,
    ICurrentUser currentUser,
    IErpAuthorityRevalidationService authority,
    IInventoryTransferReaderV1 reader) : IInventoryTransferQueryV1
{
    public async Task<InventoryTransferViewV1> GetAsync(Guid transferId, CancellationToken cancellationToken = default)
    {
        var scope = await RequireAsync(cancellationToken);
        InventoryTransferViewV1? result;
        try
        {
            result = await reader.ReadAsync(transferId, cancellationToken);
        }
        catch (InventoryTransferException)
        {
            await RequireStillAsync(scope, cancellationToken);
            throw;
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            await RequireStillAsync(scope, cancellationToken);
            throw new InventoryTransferException(503, InventoryTransferCodesV1.Unavailable, exception);
        }
        await RequireStillAsync(scope, cancellationToken);
        if (result is null) throw new InventoryTransferException(404, InventoryTransferCodesV1.TransferNotFound);
        if (result.CompanyId != scope.CompanyId) throw new InventoryTransferException(503, InventoryTransferCodesV1.Unavailable);
        return result;
    }

    private async Task<Scope> RequireAsync(CancellationToken cancellationToken)
    {
        if (!currentUser.IsAuthenticated || string.IsNullOrWhiteSpace(currentUser.UserId))
            throw new InventoryTransferException(401, InventoryTransferCodesV1.Unauthenticated);
        if (!companyContext.IsResolved || companyContext.CompanyId == Guid.Empty
            || !currentUser.CanAccessCompany(companyContext.CompanyId)) throw Forbidden();
        var scope = new Scope(companyContext.CompanyId, currentUser.UserId);
        await RequireStillAsync(scope, cancellationToken);
        return scope;
    }

    private async Task RequireStillAsync(Scope scope, CancellationToken cancellationToken)
    {
        if (!Matches(scope)) throw Forbidden();
        var measured = await authority.RevalidatePermissionScopeAsync(
            ErpPermissions.InventoryBalancesRead, scope.CompanyId, cancellationToken);
        if (!measured.IsGranted || !measured.IsCompanyWide || !Matches(scope)) throw Forbidden();
    }

    private bool Matches(Scope scope) => currentUser.IsAuthenticated
        && string.Equals(currentUser.UserId, scope.ActorId, StringComparison.Ordinal)
        && companyContext.IsResolved && companyContext.CompanyId == scope.CompanyId
        && currentUser.CanAccessCompany(scope.CompanyId);

    private static InventoryTransferException Forbidden() => new(403, InventoryTransferCodesV1.Forbidden);
    private sealed record Scope(Guid CompanyId, string ActorId);
}
