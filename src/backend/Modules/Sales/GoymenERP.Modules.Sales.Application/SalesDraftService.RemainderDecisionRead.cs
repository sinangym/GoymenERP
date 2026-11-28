using System.Globalization;
using GoymenERP.BuildingBlocks.Work;
using GoymenERP.Modules.Sales.Application.Contracts;
using GoymenERP.Modules.Sales.Domain.Drafts;

namespace GoymenERP.Modules.Sales.Application;

public sealed partial class SalesDraftService : ISalesRemainderDecisionReadV1
{
    public static SalesDraftRemainderChangeV1 ProjectRemainderChange(SalesOrderDraft order, RemainderChangeFacts change) => RemainderChangeView(order, change);
    public async Task<SalesRemainderPendingReadResultV1> ListPendingRemainderDecisionsAsync(int take,
        Func<CancellationToken, Task> freshConsumerFence, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(freshConsumerFence); cancellationToken.ThrowIfCancellationRequested(); Scope? scope = null; bool? support = null;
        var bound = Math.Clamp(take, 1, NativeApprovalQueueRulesV1.MaxItemsPerSource);
        try
        {
            scope = await RequireAsync(bindings.ApprovePermission, cancellationToken); support = scope.IsSalesSupport; await Fence(cancellationToken);
            if (!remainderDecisionSafety || store is not ISalesRemainderDecisionReadStoreV1 reader) return Empty(SalesRemainderDecisionReadStateV1.Unavailable);
            var rows = await reader.ListPendingRemainderDecisionSourceAsync(bound + 1, Fence, cancellationToken); await Fence(cancellationToken);
            if (rows is null) return Empty(SalesRemainderDecisionReadStateV1.Unavailable);
            var detached = rows.ToArray();
            if (detached.Length > bound + 1 || detached.Any(x => x.CompanyId != scope.CompanyId || x.DraftId == Guid.Empty || x.LineId == Guid.Empty
                || x.ChangeNo < 1 || x.RequestRevision < 0 || x.CurrentVersion < 1 || x.CurrentVersion == long.MaxValue || string.IsNullOrWhiteSpace(x.DocumentNo)
                || string.IsNullOrWhiteSpace(x.RequestedBy) || x.RequestedBy != x.RequestedBy.Trim() || x.RequestedAtUtc.Offset != TimeSpan.Zero)
                || detached.Select(x => x.DraftId).Distinct().Count() != detached.Length) return Empty(SalesRemainderDecisionReadStateV1.Unavailable);
            return new(SalesRemainderDecisionReadStateV1.Found, scope.CompanyId, Array.AsReadOnly(detached.Take(bound).ToArray()), detached.Length > bound, scope.IsSalesSupport);
        }
        catch (OperationCanceledException) { throw; }
        catch (SalesDraftServiceException e) when (e.Status is 401 or 403) { return Empty(SalesRemainderDecisionReadStateV1.Denied); }
        catch (Exception e) when (e is InvalidOperationException or ArgumentException or System.Data.Common.DbException or SalesDraftServiceException)
        {
            if (scope is not null)
            {
                try { await Fence(cancellationToken); }
                catch (SalesDraftServiceException denied) when (denied.Status is 401 or 403) { return Empty(SalesRemainderDecisionReadStateV1.Denied); }
                catch (InvalidOperationException) { return Empty(SalesRemainderDecisionReadStateV1.Unavailable); }
            }
            return Empty(SalesRemainderDecisionReadStateV1.Unavailable);
        }
        SalesRemainderPendingReadResultV1 Empty(SalesRemainderDecisionReadStateV1 state) => new(state, Guid.Empty, null, false, null);
        async Task Fence(CancellationToken token)
        {
            token.ThrowIfCancellationRequested(); scope = await RequireStillAsync(scope!, bindings.ApprovePermission, token);
            await freshConsumerFence(token); token.ThrowIfCancellationRequested(); scope = await RequireStillAsync(scope!, bindings.ApprovePermission, token); token.ThrowIfCancellationRequested();
            if (scope.IsSalesSupport != support) throw new InvalidOperationException("Satır kalanı karar kişiliği okuma sırasında değişti.");
        }
    }
    public async Task<SalesRemainderDecisionReadResultV1> ReadRemainderDecisionAsync(Guid draftId,
        Func<CancellationToken, Task> freshConsumerFence, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(freshConsumerFence); cancellationToken.ThrowIfCancellationRequested(); Scope? scope = null;
        try
        {
            scope = await RequireAsync(bindings.ApprovePermission, cancellationToken); await Fence(cancellationToken);
            if (!remainderDecisionSafety || store is not ISalesRemainderDecisionReadStoreV1 reader) return new(SalesRemainderDecisionReadStateV1.Unavailable, null);
            if (draftId == Guid.Empty) return new(SalesRemainderDecisionReadStateV1.NotFound, null);
            var found = await reader.ReadRemainderDecisionSourceAsync(draftId, Fence, cancellationToken); await Fence(cancellationToken);
            if (found is null) return new(SalesRemainderDecisionReadStateV1.NotFound, null);
            if (found.Order is not { } order || found.Change is not { } change || order.CompanyId != scope.CompanyId || order.Id != draftId
                || !order.RemainderChanges.Contains(change)) return new(SalesRemainderDecisionReadStateV1.Unavailable, null);
            var pending = change.State == RemainderChangeState.Pending;
            if (pending && scope.IsSalesSupport && (IsOwn(scope, order) || change.Request.RequestedBy == scope.ActorId)) return new(SalesRemainderDecisionReadStateV1.Denied, null);
            var line = order.Lines.SingleOrDefault(x => x.Id == change.Request.LineId);
            if (line is null) return new(SalesRemainderDecisionReadStateV1.Unavailable, null);
            var recorded = RemainderChangeView(order, change);
            var detail = new SalesRemainderDecisionDetailV1(scope.CompanyId, order.Id, order.DocumentNo, order.Version.ToString(CultureInfo.InvariantCulture),
                order.Revision, order.State.ToString(), order.ApprovalState.ToString(), recorded,
                new(line.Id, line.LineNo, line.Product.ProductId, line.Product.Code, line.Product.Name, line.OrderUnit.UnitId, line.OrderUnit.UnitCode, line.QuantityText, RemainderName(line.Remainder)),
                pending, pending, SalesRemainderDecisionDigestsV1.SourceFactsDigest(scope.CompanyId, order.Id, order.DocumentNo, recorded), "");
            // Materialize the quantity-only snapshot before the final external callback; no mutable domain object is returned.
            await Fence(cancellationToken);
            if (pending && scope!.IsSalesSupport && (IsOwn(scope, order) || change.Request.RequestedBy == scope.ActorId)) return new(SalesRemainderDecisionReadStateV1.Denied, null);
            return new(SalesRemainderDecisionReadStateV1.Found, detail with { PreviewDigest = SalesRemainderDecisionDigestsV1.PreviewDigest(detail), DraftCreatedBy = order.CreatedBy, IsSalesSupport = scope!.IsSalesSupport });
        }
        catch (OperationCanceledException) { throw; }
        catch (SalesDraftServiceException e) when (e.Status is 401 or 403) { return new(SalesRemainderDecisionReadStateV1.Denied, null); }
        catch (Exception e) when (e is InvalidOperationException or ArgumentException or System.Data.Common.DbException or System.Text.Json.JsonException or SalesDraftServiceException)
        {
            if (scope is not null) { try { await Fence(cancellationToken); } catch (SalesDraftServiceException denied) when (denied.Status is 401 or 403) { return new(SalesRemainderDecisionReadStateV1.Denied, null); } }
            return new(SalesRemainderDecisionReadStateV1.Unavailable, null);
        }
        async Task Fence(CancellationToken token)
        { token.ThrowIfCancellationRequested(); scope = await RequireStillAsync(scope!, bindings.ApprovePermission, token); await freshConsumerFence(token); token.ThrowIfCancellationRequested(); scope = await RequireStillAsync(scope!, bindings.ApprovePermission, token); token.ThrowIfCancellationRequested(); }
    }

    public async Task<SalesRemainderOriginalVersionReadResultV1> ReadRemainderOriginalVersionAsync(Guid draftId, string expectedVersion,
        Func<CancellationToken, Task> freshConsumerFence, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(freshConsumerFence); cancellationToken.ThrowIfCancellationRequested(); Scope? scope = null;
        try
        {
            scope = await RequireAsync(bindings.ApprovePermission, cancellationToken); await Fence(cancellationToken);
            if (!remainderDecisionSafety || store is not ISalesRemainderDecisionReadStoreV1 reader
                || !long.TryParse(expectedVersion, NumberStyles.None, CultureInfo.InvariantCulture, out var n) || n < 1 || n == long.MaxValue
                || n.ToString(CultureInfo.InvariantCulture) != expectedVersion) return new(SalesRemainderDecisionReadStateV1.Unavailable, null);
            if (draftId == Guid.Empty) return new(SalesRemainderDecisionReadStateV1.NotFound, null);
            var answer = await reader.ReadRemainderOriginalVersionAsync(draftId, n, scope.ActorId, Fence, cancellationToken); await Fence(cancellationToken);
            if (answer is null || answer.State == SalesRemainderDecisionReadStateV1.Found && (answer.Receipt is null || answer.CurrentVersion is null)
                || answer.State != SalesRemainderDecisionReadStateV1.Found && answer.Receipt is not null
                || answer.CurrentVersion is <= 0 || !Enum.IsDefined(answer.State)) return new(SalesRemainderDecisionReadStateV1.Unavailable, null);
            if (answer.Receipt is { } receipt && (receipt.CompanyId != scope.CompanyId || receipt.DraftId != draftId || receipt.ActorId != scope.ActorId
                || receipt.ExpectedVersion != n || receipt.ResultVersion != n + 1 || answer.CurrentVersion < receipt.ResultVersion || !receipt.CreatorProofEstablished
                || receipt.OriginalDecisionDigest != SalesRemainderDecisionDigestsV1.OriginalDecisionDigest(receipt)))
                return new(SalesRemainderDecisionReadStateV1.Unavailable, null);
            return answer;
        }
        catch (OperationCanceledException) { throw; }
        catch (SalesDraftServiceException e) when (e.Status is 401 or 403) { return new(SalesRemainderDecisionReadStateV1.Denied, null); }
        catch (Exception e) when (e is InvalidOperationException or ArgumentException or System.Data.Common.DbException or System.Text.Json.JsonException or SalesDraftServiceException)
        {
            if (scope is not null) { try { await Fence(cancellationToken); } catch (SalesDraftServiceException denied) when (denied.Status is 401 or 403) { return new(SalesRemainderDecisionReadStateV1.Denied, null); } }
            return new(SalesRemainderDecisionReadStateV1.Unavailable, null);
        }
        async Task Fence(CancellationToken token)
        { token.ThrowIfCancellationRequested(); scope = await RequireStillAsync(scope!, bindings.ApprovePermission, token); await freshConsumerFence(token); token.ThrowIfCancellationRequested(); scope = await RequireStillAsync(scope!, bindings.ApprovePermission, token); token.ThrowIfCancellationRequested(); }
    }
}
