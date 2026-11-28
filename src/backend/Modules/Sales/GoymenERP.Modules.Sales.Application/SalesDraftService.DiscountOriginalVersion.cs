using System.Globalization;
using GoymenERP.Modules.Sales.Application.Contracts;

namespace GoymenERP.Modules.Sales.Application;

public sealed partial class SalesDraftService
{
    public async Task<SalesDiscountOriginalVersionReadResultV1> ReadDiscountOriginalVersionAsync(Guid draftId, string expectedVersion,
        Func<CancellationToken, Task> freshConsumerFence, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(freshConsumerFence); cancellationToken.ThrowIfCancellationRequested();
        Scope? scope = null; var permission = bindings.DiscountExceptionApprovePermission;
        try
        {
            scope = await RequireAsync(permission, cancellationToken); await Fence(cancellationToken);
            if (draftId == Guid.Empty || !long.TryParse(expectedVersion, NumberStyles.None, CultureInfo.InvariantCulture, out var version)
                || version < 1 || version == long.MaxValue || version.ToString(CultureInfo.InvariantCulture) != expectedVersion)
                return new(SalesDiscountDecisionReadStateV1.Unavailable, null);
            if (store is not ISalesDiscountDecisionReadStoreV1 reader) return new(SalesDiscountDecisionReadStateV1.Unavailable, null);
            var result = await reader.ReadDiscountOriginalVersionAsync(draftId, version, scope.ActorId, Fence, cancellationToken);
            await Fence(cancellationToken);
            if (result is { State: SalesDiscountDecisionReadStateV1.NotFound, Receipt: null }) return result;
            if (result is not { State: SalesDiscountDecisionReadStateV1.Found, Receipt: { } receipt }
                || receipt.CompanyId != scope.CompanyId || receipt.DraftId != draftId || receipt.ActorId != scope.ActorId
                || receipt.ExpectedVersion != version || receipt.ResultVersion != version + 1
                || receipt.OriginalDecisionDigest != SalesDiscountDecisionDigestsV1.OriginalDecisionDigest(receipt))
                return new(SalesDiscountDecisionReadStateV1.Unavailable, null);
            // No current pending maker/price/row is read: original SoD is already in the genuine immutable receipt.
            return result;
        }
        catch (OperationCanceledException) { throw; }
        catch (SalesDraftServiceException failure) when (failure.Status is 401 or 403)
        { return new(SalesDiscountDecisionReadStateV1.Denied, null); }
        catch (Exception failure) when (failure is InvalidOperationException or ArgumentException or System.Text.Json.JsonException
            or SalesDraftServiceException or SalesDraftStoreException or System.Data.Common.DbException or TimeoutException)
        { return new(SalesDiscountDecisionReadStateV1.Unavailable, null); }

        async Task Fence(CancellationToken token)
        {
            token.ThrowIfCancellationRequested(); scope = await RequireStillAsync(scope!, permission, token);
            await freshConsumerFence(token); token.ThrowIfCancellationRequested();
            scope = await RequireStillAsync(scope, permission, token); token.ThrowIfCancellationRequested();
        }
    }
}
