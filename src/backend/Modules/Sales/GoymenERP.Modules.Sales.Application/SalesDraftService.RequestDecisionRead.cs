using GoymenERP.Modules.Sales.Application.Contracts;
using GoymenERP.Modules.Sales.Domain.Requests;

namespace GoymenERP.Modules.Sales.Application;

public sealed partial class SalesDraftService : ISalesRequestDecisionReadV1
{
    public async Task<SalesRequestDecisionReadResultV1> ReadRequestDecisionAsync(Guid requestId,
        Func<CancellationToken, Task> freshConsumerFence, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(freshConsumerFence);
        cancellationToken.ThrowIfCancellationRequested();
        Scope? scope = null;
        var permission = bindings.RequestApprovePermission;
        try
        {
            scope = await RequireAsync(permission, cancellationToken);
            await Fence(cancellationToken);
            if (requestId == Guid.Empty) return new(SalesRequestDecisionReadStateV1.NotFound, null);
            if (requests is not ISalesRequestDecisionReadStoreV1 reader) return new(SalesRequestDecisionReadStateV1.Unavailable, null);
            var found = await reader.ReadRequestDecisionAsync(requestId, Fence, cancellationToken);
            await Fence(cancellationToken);
            if (found is null) return new(SalesRequestDecisionReadStateV1.NotFound, null);
            if (found.CompanyId != scope!.CompanyId || found.Id != requestId) return new(SalesRequestDecisionReadStateV1.Unavailable, null);
            var detail = RequestDetail(found);
            var result = new SalesRequestDecisionDetailV1(detail, found.State == SalesRequestState.Submitted,
                found.State is SalesRequestState.Submitted or SalesRequestState.Approved,
                SalesRequestDecisionDigestsV1.SourceFactsDigest(detail), "");
            return new(SalesRequestDecisionReadStateV1.Found, result with { PreviewDigest = SalesRequestDecisionDigestsV1.PreviewDigest(result) });
        }
        catch (OperationCanceledException) { throw; }
        catch (SalesDraftServiceException failure) when (failure.Status is 401 or 403)
        { return new(SalesRequestDecisionReadStateV1.Denied, null); }
        catch
        {
            // A read failure cannot release stale private facts or hide a concurrent permission loss.
            try { if (scope is not null) await Fence(cancellationToken); }
            catch (OperationCanceledException) { throw; }
            catch (SalesDraftServiceException failure) when (failure.Status is 401 or 403)
            { return new(SalesRequestDecisionReadStateV1.Denied, null); }
            catch { return new(SalesRequestDecisionReadStateV1.Unavailable, null); }
            return new(SalesRequestDecisionReadStateV1.Unavailable, null);
        }

        async Task Fence(CancellationToken token)
        {
            token.ThrowIfCancellationRequested();
            scope = await RequireStillAsync(scope!, permission, token);
            await freshConsumerFence(token); token.ThrowIfCancellationRequested();
            // The consumer can complete normally after revoking the owner's permission or changing the actor.
            scope = await RequireStillAsync(scope, permission, token); token.ThrowIfCancellationRequested();
        }
    }
}
