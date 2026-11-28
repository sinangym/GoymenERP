using System.Data;
using System.Text.Json;
using GoymenERP.Modules.Purchasing.Application.Contracts;
using GoymenERP.Modules.Purchasing.Domain.Orders;
using GoymenERP.Modules.Purchasing.Domain.Services;
using Microsoft.EntityFrameworkCore;

namespace GoymenERP.Modules.Purchasing.Infrastructure.Persistence;

public sealed partial class PurchasingStore : IPurchaseMixedOrderApprovalPreviewStoreV1
{
    public async Task<PurchaseMixedOrderPreparedPreviewV1?> ReadMixedOrderApprovalPreviewAsync(Guid orderId, long expectedVersion,
        Func<StoredPurchaseOrder, IReadOnlyList<PurchaseServiceOrderState>, CancellationToken, Task<PurchaseMixedOrderPreparedPreviewV1>> prepare,
        Func<CancellationToken, Task> freshFence, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(prepare); ArgumentNullException.ThrowIfNull(freshFence);
        cancellationToken.ThrowIfCancellationRequested();
        if (!CleanMixedPreviewEntry()) throw new PurchasingServiceException(503, "service_unavailable");
        var company = RequireResolvedCompany();
        await Fence(cancellationToken);
        if (!CleanMixedPreviewEntry()) throw new PurchasingServiceException(503, "service_unavailable");
        // This is a readonly prerequisite, not an attempt, approval or mutating retry runner.
        await using var transaction = await dbContext.Database.BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken);
        try
        {
            await Fence(cancellationToken);
            var parent = await ReadMixedParentAsync(orderId, Fence, cancellationToken);
            if (parent is null) { await transaction.CommitAsync(cancellationToken); await Fence(cancellationToken); return null; }
            if (parent.Order.Version != expectedVersion || parent.Order.Status != PurchaseOrderStatus.Draft)
                throw new PurchasingServiceException(409, PurchasingProblemCodesV1.VersionConflict);
            if (parent.Order.ServiceMemberships.Count == 0) throw new PurchasingServiceException(503, "service_unavailable");
            var current = await ReadCurrentMixedChildrenAsync(parent, Fence, cancellationToken);
            await Fence(cancellationToken);
            var result = await prepare(parent, current, cancellationToken);
            await Fence(cancellationToken);
            if (result is null || result.Services is null || !ReferenceEquals(result.Parent, parent) || result.Services.Count != current.Count
                || result.Services.Where((child, i) => !PurchaseMixedOrderApprovalFactsV1.SameChild(current[i], child, allowSupplierRefresh: true)).Any())
                throw new PurchasingServiceException(503, "service_unavailable");
            var latest = await ReadCurrentMixedChildrenAsync(parent, Fence, cancellationToken);
            if (!current.SequenceEqual(latest, MixedChildComparer.Instance))
                throw new PurchasingServiceException(409, PurchasingProblemCodesV1.VersionConflict);
            var finalParent = await ReadMixedParentAsync(orderId, Fence, cancellationToken);
            if (finalParent is null || finalParent.Order.Version != expectedVersion)
                throw new PurchasingServiceException(409, PurchasingProblemCodesV1.VersionConflict);
            await Fence(cancellationToken); RequireReadOnly();
            await transaction.CommitAsync(cancellationToken);
            await Fence(cancellationToken); RequireReadOnly();
            return result;
        }
        catch
        {
            // All entry state was clean. Dispose/rollback faults remain visible; no command or retry is attempted here.
            RequireReadOnly();
            throw;
        }

        async Task Fence(CancellationToken token)
        {
            token.ThrowIfCancellationRequested();
            if (RequireResolvedCompany() != company) throw new PurchasingServiceException(403, "forbidden");
            await freshFence(token); token.ThrowIfCancellationRequested();
            if (RequireResolvedCompany() != company) throw new PurchasingServiceException(403, "forbidden");
        }
        void RequireReadOnly()
        {
            if (dbContext.ChangeTracker.HasChanges() || decisionAuditWriter!.HasPendingDrafts)
                throw new InvalidOperationException("Mixed preview produced unexpected work; no approval was admitted.");
        }
    }

    private bool CleanMixedPreviewEntry() => decisionAuditWriter is not null && !decisionAuditWriter.HasPendingDrafts
        && System.Transactions.Transaction.Current is null && dbContext.Database.IsRelational()
        && dbContext.Database.CurrentTransaction is null && !dbContext.ChangeTracker.HasChanges();

    private async Task<StoredPurchaseOrder?> ReadMixedParentAsync(Guid id, Func<CancellationToken, Task> fence, CancellationToken token)
    {
        var row = await ReadMixedOwnerAsync(() => dbContext.Orders.AsNoTracking().SingleOrDefaultAsync(x => x.Id == id, token), fence, token);
        return row is null ? null : (await RestoreOrdersAsync([row], token, fence)).Single();
    }

    private async Task<IReadOnlyList<PurchaseServiceOrderState>> ReadCurrentMixedChildrenAsync(StoredPurchaseOrder parent,
        Func<CancellationToken, Task> fence, CancellationToken token)
    {
        var ids = parent.Order.ServiceMemberships.Select(x => x.ServiceOrderId).ToArray();
        var records = await ReadMixedOwnerAsync(() => (from header in dbContext.ServiceOrders.AsNoTracking()
            join revision in dbContext.ServiceOrderRevisions.AsNoTracking()
                on new { header.CompanyId, header.OrderId, header.Version } equals new { revision.CompanyId, revision.OrderId, revision.Version }
            where ids.Contains(header.OrderId)
            select revision).ToListAsync(token), fence, token);
        if (records.Count != ids.Length || records.Select(x => x.OrderId).Distinct().Count() != ids.Length)
            throw new PurchasingServiceException(503, "service_unavailable");
        return Array.AsReadOnly(ids.Select(id =>
        {
            var row = records.Single(x => x.OrderId == id);
            var state = JsonSerializer.Deserialize<PurchaseServiceOrderState>(row.SnapshotJson, PurchaseServiceOrderStoreV1.Json)
                ?? throw new PurchasingServiceException(503, "service_unavailable");
            PurchaseServiceOrder.Validate(state);
            if (state.CompanyId != parent.Order.CompanyId || state.CompanyId != row.CompanyId || state.OrderId != row.OrderId
                || state.Version != row.Version || state.Status != PurchaseServiceOrderStatus.Draft
                || state.SupplierPartyId != parent.Order.SupplierPartyId || state.CompanyAccountId != parent.CompanyAccountId
                || state.Money.CurrencyId != parent.Order.Money?.CurrencyId || string.IsNullOrWhiteSpace(state.DocumentNumber))
                throw new PurchasingServiceException(409, PurchasingProblemCodesV1.VersionConflict);
            return state with { Lines = Array.AsReadOnly(state.Lines.ToArray()), Materials = Array.AsReadOnly(state.Materials.ToArray()),
                ExpectedOutputs = Array.AsReadOnly(state.ExpectedOutputs.ToArray()), Acceptances = Array.AsReadOnly(state.Acceptances.ToArray()) };
        }).ToArray());
    }

    private static async Task<T> ReadMixedOwnerAsync<T>(Func<Task<T>> read, Func<CancellationToken, Task>? fence, CancellationToken token)
    {
        if (fence is not null) { token.ThrowIfCancellationRequested(); await fence(token); token.ThrowIfCancellationRequested(); }
        var result = await read();
        if (fence is not null) { token.ThrowIfCancellationRequested(); await fence(token); token.ThrowIfCancellationRequested(); }
        return result;
    }
    private sealed class MixedChildComparer : IEqualityComparer<PurchaseServiceOrderState>
    {
        internal static readonly MixedChildComparer Instance = new();
        public bool Equals(PurchaseServiceOrderState? x, PurchaseServiceOrderState? y) => x is not null && y is not null && PurchaseMixedOrderApprovalFactsV1.SameChild(x, y);
        public int GetHashCode(PurchaseServiceOrderState obj) => obj.OrderId.GetHashCode();
    }
}
