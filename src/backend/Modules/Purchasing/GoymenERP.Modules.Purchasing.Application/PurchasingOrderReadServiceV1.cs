using GoymenERP.BuildingBlocks.Multitenancy;
using GoymenERP.BuildingBlocks.Security;
using GoymenERP.Modules.Purchasing.Application.Contracts;

namespace GoymenERP.Modules.Purchasing.Application;

/// <summary>
/// Adds current supplier presentation to authorized order reads without widening Purchasing authority
/// or granting a generic Cari permission. Missing owner rows fail closed instead of exposing raw ids alone.
/// </summary>
public sealed class PurchasingOrderReadServiceV1(
    IPurchasingOrderRecordReadSourceV1 orders,
    IPurchasingSupplierDisplaySourceV1 supplierDisplays,
    ICompanyContext companyContext,
    ICurrentUser currentUser,
    IErpAuthorityRevalidationService authority,
    PurchasingServiceBindings bindings) : IPurchasingOrderReadServiceV1
{
    private const int MaximumBatchSize = 100;

    public async Task<PurchaseOrderDetailView> GetOrderDetailAsync(
        Guid orderId,
        CancellationToken cancellationToken = default)
    {
        var (scope, detail) = await ReadOwnerAsync(
            () => orders.GetOrderDetailAsync(orderId, cancellationToken),
            cancellationToken);
        var displays = await ReadSupplierDisplaysAsync(
            scope,
            [detail.Order.SupplierPartyId],
            detail.Order.CompanyId,
            cancellationToken);

        return detail with
        {
            Order = detail.Order with
            {
                SupplierDisplay = displays[detail.Order.SupplierPartyId],
            },
        };
    }

    public async Task<PurchaseOrderListResponse> GetOrderListAsync(
        PurchaseOrderListQuery query,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);
        var (scope, page) = await ReadOwnerAsync(
            () => orders.GetOrderListAsync(query, cancellationToken),
            cancellationToken);
        if (page.Items.Count == 0)
        {
            return page;
        }

        var companyId = page.Items[0].CompanyId;
        if (page.Items.Any(item => item.CompanyId != companyId))
        {
            throw Unavailable();
        }

        var displays = await ReadSupplierDisplaysAsync(
            scope,
            page.Items.Select(item => item.SupplierPartyId),
            companyId,
            cancellationToken);
        return page with
        {
            Items = [.. page.Items.Select(item => item with
            {
                SupplierDisplay = displays[item.SupplierPartyId],
            })],
        };
    }

    private async Task<(ReadScope Scope, T Result)> ReadOwnerAsync<T>(
        Func<Task<T>> read,
        CancellationToken cancellationToken)
    {
        var scope = await RequireScopeAsync(cancellationToken);
        T result;
        try
        {
            result = await read();
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch
        {
            await RequireStillAsync(scope, cancellationToken);
            throw;
        }

        await RequireStillAsync(scope, cancellationToken);
        return (scope, result);
    }

    private async Task<IReadOnlyDictionary<Guid, PurchasingSupplierDisplayV1>> ReadSupplierDisplaysAsync(
        ReadScope scope,
        IEnumerable<Guid> supplierPartyIds,
        Guid resultCompanyId,
        CancellationToken cancellationToken)
    {
        var ids = supplierPartyIds.Distinct().Order().ToArray();
        if (ids.Length is < 1 or > MaximumBatchSize
            || ids.Any(id => id == Guid.Empty)
            || scope.CompanyId != resultCompanyId)
        {
            throw Unavailable();
        }

        PurchasingSupplierDisplayBatchAnswerV1 answer;
        try
        {
            answer = await supplierDisplays.GetAuthorizedBatchAsync(
                new PurchasingSupplierDisplayBatchRequestV1(scope.CompanyId, scope.ActorId, ids),
                cancellationToken);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch
        {
            await RequireStillAsync(scope, cancellationToken);
            throw Unavailable();
        }

        await RequireStillAsync(scope, cancellationToken);
        if (answer is null)
        {
            throw Unavailable();
        }

        if (answer.Outcome == PurchasingSupplierDisplayBatchOutcomeV1.Denied)
        {
            throw new PurchasingServiceException(403, "forbidden");
        }

        if (answer.Outcome != PurchasingSupplierDisplayBatchOutcomeV1.Success
            || answer.Items is null
            || answer.Items.Count != ids.Length)
        {
            throw Unavailable();
        }

        var requested = ids.ToHashSet();
        var byId = new Dictionary<Guid, PurchasingSupplierDisplayV1>();
        foreach (var item in answer.Items)
        {
            if (item is null
                || item.PartyId == Guid.Empty
                || !requested.Contains(item.PartyId)
                || string.IsNullOrWhiteSpace(item.Code)
                || string.IsNullOrWhiteSpace(item.LegalName)
                || item.TradeName is not null && string.IsNullOrWhiteSpace(item.TradeName)
                || !byId.TryAdd(item.PartyId, item))
            {
                throw Unavailable();
            }
        }

        return byId;
    }

    private async Task<ReadScope> RequireScopeAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!currentUser.IsAuthenticated || string.IsNullOrWhiteSpace(currentUser.UserId))
        {
            throw new PurchasingServiceException(401, "unauthenticated");
        }

        if (!companyContext.IsResolved
            || companyContext.CompanyId == Guid.Empty
            || !currentUser.CanAccessCompany(companyContext.CompanyId))
        {
            throw new PurchasingServiceException(403, "forbidden");
        }

        if (string.IsNullOrWhiteSpace(bindings.ReadPermission)
            || !ErpPermissions.IsKnown(bindings.ReadPermission))
        {
            throw new PurchasingServiceException(503, PurchasingProblemCodesV1.PermissionUnregistered);
        }

        var companyId = companyContext.CompanyId;
        var actorId = currentUser.UserId;
        var permission = await authority.RevalidatePermissionScopeAsync(
            bindings.ReadPermission,
            companyId,
            cancellationToken);
        if (!permission.IsGranted || !permission.IsCompanyWide || !Matches(companyId, actorId))
        {
            throw new PurchasingServiceException(403, "forbidden");
        }

        return new ReadScope(companyId, actorId);
    }

    private async Task RequireStillAsync(ReadScope expected, CancellationToken cancellationToken)
    {
        var current = await RequireScopeAsync(cancellationToken);
        if (current != expected)
        {
            throw new PurchasingServiceException(403, "forbidden");
        }
    }

    private bool Matches(Guid companyId, string actorId) =>
        currentUser.IsAuthenticated
        && string.Equals(currentUser.UserId, actorId, StringComparison.Ordinal)
        && companyContext.IsResolved
        && companyContext.CompanyId == companyId
        && currentUser.CanAccessCompany(companyId);

    private static PurchasingServiceException Unavailable() =>
        new(503, PurchasingProblemCodesV1.CounterpartyProviderUnavailable);

    private sealed record ReadScope(Guid CompanyId, string ActorId);
}
