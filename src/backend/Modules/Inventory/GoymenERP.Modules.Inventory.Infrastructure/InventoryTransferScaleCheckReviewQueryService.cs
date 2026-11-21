using GoymenERP.BuildingBlocks.Multitenancy;
using GoymenERP.BuildingBlocks.Security;
using GoymenERP.Modules.Inventory.Application;

namespace GoymenERP.Modules.Inventory.Infrastructure;

/// <summary>
/// LOJ-D20: the fenced read of the dispatches whose weighing check could not run. The fence is the transfer read's
/// (authenticated actor, resolved and accessible company, <c>inventory.balances.read</c> granted company-wide, measured
/// before and after the read); a limit outside 1..<see cref="InventoryTransferScaleCheckReviewApiV1.MaximumLimit"/> is a
/// 400, a reader failure a 503, and a page under another company a 503 (never shown).
/// </summary>
public sealed class InventoryTransferScaleCheckReviewQueryService(
    ICompanyContext companyContext,
    ICurrentUser currentUser,
    IErpAuthorityRevalidationService authority,
    IInventoryTransferScaleCheckReviewReaderV1 reader) : IInventoryTransferScaleCheckReviewQueryV1
{
    public async Task<InventoryTransferScaleCheckReviewPageV1> ListNotCheckedAsync(int? limit,
        CancellationToken cancellationToken = default)
    {
        var scope = await RequireAsync(cancellationToken);
        var pageSize = limit ?? InventoryTransferScaleCheckReviewApiV1.DefaultLimit;
        if (pageSize < 1 || pageSize > InventoryTransferScaleCheckReviewApiV1.MaximumLimit)
            throw new InventoryTransferException(400, InventoryTransferCodesV1.ValidationFailed);

        InventoryTransferScaleCheckReviewPageV1 result;
        try
        {
            result = await reader.ListNotCheckedAsync(pageSize, cancellationToken);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            await RequireStillAsync(scope, cancellationToken);
            throw new InventoryTransferException(503, InventoryTransferCodesV1.Unavailable, exception);
        }

        await RequireStillAsync(scope, cancellationToken);
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
