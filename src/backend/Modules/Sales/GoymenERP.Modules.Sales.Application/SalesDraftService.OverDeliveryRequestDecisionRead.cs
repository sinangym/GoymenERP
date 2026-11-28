using System.Globalization;
using GoymenERP.Modules.Sales.Application.Contracts;
using GoymenERP.Modules.Sales.Domain.ApprovalRequests;

namespace GoymenERP.Modules.Sales.Application;

public sealed partial class SalesDraftService : ISalesOverDeliveryRequestDecisionReadV1
{
    public async Task<SalesOverDeliveryRequestDecisionReadResultV1> ReadOverDeliveryRequestDecisionAsync(Guid requestId,
        Func<CancellationToken, Task> freshConsumerFence, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(freshConsumerFence); cancellationToken.ThrowIfCancellationRequested();
        var permission = bindings.OverDeliveryApprovePermission; Scope? scope = null;
        try
        {
            scope = await RequireAsync(permission, cancellationToken);
            await Fence(cancellationToken);
            if (requestId == Guid.Empty) return new(SalesOverDeliveryRequestDecisionReadStateV1.NotFound, null);
            if (store is not ISalesOverDeliveryRequestDecisionReadStoreV1 reader) return new(SalesOverDeliveryRequestDecisionReadStateV1.Unavailable, null);
            SalesOverDeliveryRequestDecisionSourceV1? found;
            try { found = await reader.ReadOverDeliveryRequestDecisionSourceAsync(requestId, Fence, cancellationToken); await Fence(cancellationToken); }
            catch (OperationCanceledException) { throw; }
            catch (SalesDraftServiceException failure) when (failure.Status is 401 or 403) { return new(SalesOverDeliveryRequestDecisionReadStateV1.Denied, null); }
            catch (InvalidOperationException) { await Fence(cancellationToken); return new(SalesOverDeliveryRequestDecisionReadStateV1.Unavailable, null); }
            if (found is null) return new(SalesOverDeliveryRequestDecisionReadStateV1.NotFound, null);
            var selected = found.Request; var order = found.Order;
            if (selected is null || order is null || selected.Id != requestId || selected.CompanyId != scope!.CompanyId
                || selected.Kind != SalesApprovalRequestKind.OverDelivery || selected.SubjectId != order.Id || order.CompanyId != scope!.CompanyId)
                return new(SalesOverDeliveryRequestDecisionReadStateV1.Unavailable, null);
            var pending = selected.State == SalesApprovalRequestState.Requested;
            if (pending && selected.RequestedBy == scope!.ActorId) return new(SalesOverDeliveryRequestDecisionReadStateV1.Denied, null);
            var line = order.Lines.SingleOrDefault(x => x.Id == selected.LineId);
            var header = order.Header;
            var humanOrder = new SalesOverDeliveryRequestOrderV1(order.Id, order.DocumentNo, order.Version, order.Revision, order.State.ToString(), order.ApprovalState.ToString(),
                header.PartyId, header.CompanyAccountId, header.CurrencyId, header.BusinessDate,
                line is null ? null : new(line.Id, line.LineNo, line.Product.ProductId, line.Product.Code, line.Product.Name, line.OrderUnit.UnitId, line.OrderUnit.UnitCode, line.QuantityText));
            var request = ApprovalRequestView(selected, null);
            // Action availability is the existing Requested state, not a second band/eligibility calculation. Native 404/409/422 remain native.
            var detail = new SalesOverDeliveryRequestDecisionDetailV1(scope!.CompanyId, selected.Id, request, humanOrder, pending, pending,
                SalesOverDeliveryRequestDecisionDigestsV1.SourceFactsDigest(scope!.CompanyId, selected.Id, request, order.DocumentNo), "");
            await Fence(cancellationToken);
            return new(SalesOverDeliveryRequestDecisionReadStateV1.Found, detail with { PreviewDigest = SalesOverDeliveryRequestDecisionDigestsV1.PreviewDigest(detail) });

        }
        catch (OperationCanceledException) { throw; }
        catch (SalesDraftServiceException failure) when (failure.Status is 401 or 403) { return new(SalesOverDeliveryRequestDecisionReadStateV1.Denied, null); }
        catch (Exception failure) when (failure is InvalidOperationException or System.Data.Common.DbException or System.Text.Json.JsonException or ArgumentException or SalesDraftServiceException)
        {
            if (scope is not null)
            {
                try { await Fence(cancellationToken); }
                catch (SalesDraftServiceException denied) when (denied.Status is 401 or 403) { return new(SalesOverDeliveryRequestDecisionReadStateV1.Denied, null); }
            }
            return new(SalesOverDeliveryRequestDecisionReadStateV1.Unavailable, null);
        }
        async Task Fence(CancellationToken token)
        { token.ThrowIfCancellationRequested(); scope = await RequireStillAsync(scope!, permission, token); await freshConsumerFence(token); token.ThrowIfCancellationRequested(); scope = await RequireStillAsync(scope!, permission, token); token.ThrowIfCancellationRequested(); }
    }

    public async Task<SalesOverDeliveryRequestOriginalVersionReadResultV1> ReadOverDeliveryRequestOriginalVersionAsync(Guid requestId,
        string expectedRequestVersion, Func<CancellationToken, Task> freshConsumerFence, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(freshConsumerFence); cancellationToken.ThrowIfCancellationRequested();
        var permission = bindings.OverDeliveryApprovePermission; Scope? scope = null;
        try
        {
            scope = await RequireAsync(permission, cancellationToken);
            await Fence(cancellationToken);
            if (!long.TryParse(expectedRequestVersion, NumberStyles.None, CultureInfo.InvariantCulture, out var version) || version < 1
                || version == long.MaxValue || version.ToString(CultureInfo.InvariantCulture) != expectedRequestVersion
                || store is not ISalesOverDeliveryRequestDecisionReadStoreV1 reader) return new(SalesOverDeliveryRequestDecisionReadStateV1.Unavailable, null);
            var result = await reader.ReadOverDeliveryRequestOriginalVersionAsync(requestId, version, scope!.ActorId, Fence, cancellationToken);
            await Fence(cancellationToken);
            if (result is null || !Enum.IsDefined(result.State) || result.State != SalesOverDeliveryRequestDecisionReadStateV1.Found && result.Receipt is not null)
                return new(SalesOverDeliveryRequestDecisionReadStateV1.Unavailable, null);
            if (result.State == SalesOverDeliveryRequestDecisionReadStateV1.Found && (result.Receipt is not { } receipt || receipt.Target is null
                || receipt.CompanyId != scope!.CompanyId || receipt.Target.RequestId != requestId || receipt.ActorId != scope!.ActorId
                || receipt.ExpectedRequestVersion != version || receipt.ResultRequestVersion != version + 1
                || receipt.OriginalDecisionDigest != SalesOverDeliveryRequestDecisionDigestsV1.OriginalDecisionDigest(receipt)))
                return new(SalesOverDeliveryRequestDecisionReadStateV1.Unavailable, null);
            return result;
        }
        catch (OperationCanceledException) { throw; }
        catch (SalesDraftServiceException failure) when (failure.Status is 401 or 403) { return new(SalesOverDeliveryRequestDecisionReadStateV1.Denied, null); }
        catch (Exception failure) when (failure is InvalidOperationException or System.Data.Common.DbException or System.Text.Json.JsonException or ArgumentException or SalesDraftServiceException)
        {
            if (scope is not null)
            {
                try { await Fence(cancellationToken); }
                catch (SalesDraftServiceException denied) when (denied.Status is 401 or 403) { return new(SalesOverDeliveryRequestDecisionReadStateV1.Denied, null); }
            }
            return new(SalesOverDeliveryRequestDecisionReadStateV1.Unavailable, null);
        }
        async Task Fence(CancellationToken token)
        { token.ThrowIfCancellationRequested(); scope = await RequireStillAsync(scope!, permission, token); await freshConsumerFence(token); token.ThrowIfCancellationRequested(); scope = await RequireStillAsync(scope!, permission, token); token.ThrowIfCancellationRequested(); }
    }

    // Narrow pure delegate to the existing native formatter; Infrastructure never maintains a second public result projection.
    public static SalesApprovalRequestView ProjectOverDeliveryRequest(SalesApprovalRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (request.Kind != SalesApprovalRequestKind.OverDelivery) throw new ArgumentException("Wrong request kind.", nameof(request));
        return ApprovalRequestView(request, null);
    }

    public Task<SalesApprovalRequestCommandResult> ApproveSelectedOverDeliveryRequestAsync(SalesOverDeliveryRequestTargetV1 target,
        SalesDraftStateCommandRequest request, SalesCommandMetadata metadata, CancellationToken cancellationToken = default)
    { ArgumentNullException.ThrowIfNull(target); ArgumentNullException.ThrowIfNull(request); return RunOverDeliveryRequestApprovalAsync(target.OrderId, target.RequestNo, request, metadata, true, cancellationToken, new(target, request.ExpectedVersion, request.Reason)); }
    public Task<SalesApprovalRequestCommandResult> RejectSelectedOverDeliveryRequestAsync(SalesOverDeliveryRequestTargetV1 target,
        SalesDraftStateCommandRequest request, SalesCommandMetadata metadata, CancellationToken cancellationToken = default)
    { ArgumentNullException.ThrowIfNull(target); ArgumentNullException.ThrowIfNull(request); return RunOverDeliveryRequestApprovalAsync(target.OrderId, target.RequestNo, request, metadata, false, cancellationToken, new(target, request.ExpectedVersion, request.Reason)); }
}
