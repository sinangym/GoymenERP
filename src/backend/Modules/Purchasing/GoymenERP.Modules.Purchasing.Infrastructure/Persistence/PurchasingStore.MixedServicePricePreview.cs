using System.Data;
using GoymenERP.Modules.Purchasing.Application.Contracts;
using GoymenERP.Modules.Purchasing.Domain.Orders;
using GoymenERP.Modules.Purchasing.Domain.Services;
using Microsoft.EntityFrameworkCore;

namespace GoymenERP.Modules.Purchasing.Infrastructure.Persistence;

public sealed partial class PurchasingStore : IPurchaseMixedServicePriceRevisionPreviewStoreV1
{
    public async Task<PurchaseMixedOrderPreparedPreviewV1?> ReadMixedServicePriceRevisionPreviewAsync(Guid id, long expectedVersion,
        Func<CancellationToken, Task> freshFence, CancellationToken token = default)
    {
        ArgumentNullException.ThrowIfNull(freshFence); token.ThrowIfCancellationRequested();
        if (!CleanMixedPreviewEntry()) throw MixedSupplierUnavailable();
        var company = RequireResolvedCompany(); await Fence(token);
        if (!CleanMixedPreviewEntry()) throw MixedSupplierUnavailable();
        await using var transaction = await dbContext.Database.BeginTransactionAsync(IsolationLevel.Serializable, token);
        await Fence(token);
        var parent = await ReadMixedParentAsync(id, Fence, token);
        if (parent is null) { RequireReadOnly(); await transaction.CommitAsync(token); await Fence(token); return null; }
        if (parent.Order.Version != expectedVersion || parent.Order.Status != PurchaseOrderStatus.Approved)
            throw new PurchasingServiceException(409, PurchasingProblemCodesV1.VersionConflict);
        if (parent.Order.ServiceMemberships.Count == 0) throw MixedSupplierUnavailable();
        var current = await ReadCurrentMixedChildrenAsync(parent, Fence, token, requireDraft: false);
        var row = await ReadMixedOwnerAsync(() => dbContext.Orders.AsNoTracking().SingleAsync(x => x.Id == id, token), Fence, token);
        var approval = await ReadMixedRestoreReceiptAsync(row, Fence, token) ?? throw MixedSupplierUnavailable();
        if (approval.ResultVersion != parent.Order.ApprovalVersion || approval.Original.Children.Count != current.Count
            || current.Any(x => x.Status != PurchaseServiceOrderStatus.Approved || !HasNativeApproval(x, approval)))
            throw MixedSupplierUnavailable();
        var latest = await ReadCurrentMixedChildrenAsync(parent, Fence, token, requireDraft: false);
        var finalParent = await ReadMixedParentAsync(id, Fence, token);
        if (!current.SequenceEqual(latest, MixedChildComparer.Instance) || finalParent is null || finalParent.Order.Version != expectedVersion
            || finalParent.Order.Status != PurchaseOrderStatus.Approved) throw new PurchasingServiceException(409, PurchasingProblemCodesV1.VersionConflict);
        await Fence(token); RequireReadOnly(); await transaction.CommitAsync(token); await Fence(token); RequireReadOnly();
        return new(parent, current);
        async Task Fence(CancellationToken ct)
        { ct.ThrowIfCancellationRequested(); if (RequireResolvedCompany() != company) throw new PurchasingServiceException(403, "forbidden");
            await freshFence(ct); ct.ThrowIfCancellationRequested();
            if (RequireResolvedCompany() != company) throw new PurchasingServiceException(403, "forbidden"); }
        void RequireReadOnly()
        { if (dbContext.ChangeTracker.HasChanges() || decisionAuditWriter!.HasPendingDrafts)
            throw new InvalidOperationException("Price revision preview cannot produce a command or adopt caller work."); }
    }
}
