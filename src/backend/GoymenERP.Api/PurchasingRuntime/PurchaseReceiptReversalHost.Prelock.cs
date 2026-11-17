using System.Data.Common;
using GoymenERP.BuildingBlocks.Concurrency;
using GoymenERP.Modules.Inventory.Application;
using GoymenERP.Modules.Inventory.Domain.FutureSupply;
using GoymenERP.Modules.Purchasing.Application.Contracts;
using Microsoft.Extensions.DependencyInjection;

namespace GoymenERP.Api.PurchasingRuntime;

internal sealed partial class PurchaseReceiptReversalHost
{
    private async Task<bool> PrepareAsync(Guid companyId, PurchaseReceiptReversalWriteV1 write,
        IPurchaseReceiptAdjustmentParticipantV1 purchase, IInventoryGoodsReceiptReversalParticipantV1 inventory,
        DbConnection connection, DbTransaction transaction, Func<CancellationToken, Task> freshAuthority, CancellationToken token)
    {
        await using var allocations = await services.GetRequiredService<IInventoryFutureAllocationReleasePortV1>()
            .EnlistAsync(connection, transaction, token);
        return await PrepareLockedAsync(companyId, write, purchase, inventory, allocations,
            services.GetRequiredService<ISalesOrderLineSerializationLockBinderV1>().Bind(connection, transaction),
            services.GetRequiredService<IPurchaseLineSerializationLockBinderV1>().Bind(connection, transaction), freshAuthority, token);
    }

    internal static async Task<bool> PrepareLockedAsync(Guid companyId, PurchaseReceiptReversalWriteV1 write,
        IPurchaseReceiptAdjustmentParticipantV1 purchase, IInventoryGoodsReceiptReversalParticipantV1 inventory,
        IInventoryFutureAllocationReleaseSessionV1 allocations, ISalesOrderLineSerializationLockV1 salesLocks,
        IPurchaseLineSerializationLockV1 purchaseLocks, Func<CancellationToken, Task> freshAuthority, CancellationToken token)
    {
        var first = RequireTarget(await Read(() => purchase.ReadTargetAsync(write.OrderId, write.AcceptanceId, token)), companyId, write);
        var original = RequireInventory(await Read(() => inventory.ReadTargetAsync(write.AcceptanceId, token)), first);
        var initial = RequireAllocations(await Read(() => allocations.ListActiveByPurchaseLineAsync(first.GeneralLineId, token)),
            view => view.PurchaseOrderId == first.OrderId && view.PurchaseOrderLineId == first.GeneralLineId);
        var sales = new Dictionary<Guid, Guid>();
        foreach (var item in original.Links) AddSales(item.SalesOrderId, item.SalesOrderLineId);
        foreach (var item in initial) AddSales(item.SalesOrderId, item.SalesOrderLineId);
        foreach (var id in sales.Keys.Order()) { await salesLocks.AcquireAsync(id, token); await freshAuthority(token); }

        // Read the other purchases only AFTER Sales is locked and BEFORE any purchase lock is acquired.
        var purchases = new HashSet<Guid> { first.GeneralLineId };
        var demandRows = new Dictionary<Guid, HashSet<Guid>>();
        foreach (var (line, order) in sales)
        {
            var rows = RequireAllocations(await Read(() => allocations.ListActiveByDemandLineAsync(line, token)),
                view => view.SalesOrderId == order && view.SalesOrderLineId == line);
            demandRows.Add(line, rows.Select(view => view.AllocationId).ToHashSet());
            foreach (var row in rows) purchases.Add(row.PurchaseOrderLineId);
        }
        foreach (var id in purchases.Order()) { await purchaseLocks.AcquireAsync(id, token); await freshAuthority(token); }

        var current = RequireTarget(await Read(() => purchase.ReadTargetAsync(write.OrderId, write.AcceptanceId, token)), companyId, write);
        if (current.GeneralLineId != first.GeneralLineId || current.LineId != first.LineId) return false;
        var freshOriginal = RequireInventory(await Read(() => inventory.ReadTargetAsync(write.AcceptanceId, token)), current);
        var originalIds = original.Links.Select(link => link.ConversionId).ToHashSet();
        if (freshOriginal.Links.Any(link => !originalIds.Contains(link.ConversionId)
            || !sales.TryGetValue(link.SalesOrderLineId, out var order) || order != link.SalesOrderId)) return false;
        var fresh = RequireAllocations(await Read(() => allocations.ListActiveByPurchaseLineAsync(current.GeneralLineId, token)),
            view => view.PurchaseOrderId == current.OrderId && view.PurchaseOrderLineId == current.GeneralLineId);
        var initialIds = initial.Select(view => view.AllocationId).ToHashSet();
        if (fresh.Any(view => !initialIds.Contains(view.AllocationId) || !sales.ContainsKey(view.SalesOrderLineId))) return false;
        foreach (var (line, order) in sales)
        {
            var rows = RequireAllocations(await Read(() => allocations.ListActiveByDemandLineAsync(line, token)),
                view => view.SalesOrderId == order && view.SalesOrderLineId == line);
            if (rows.Any(view => !demandRows[line].Contains(view.AllocationId) || !purchases.Contains(view.PurchaseOrderLineId))) return false;
        }
        return true;

        void AddSales(Guid order, Guid line)
        {
            if (order == Guid.Empty || line == Guid.Empty || sales.TryGetValue(line, out var previous) && previous != order) throw Unavailable();
            sales[line] = order;
        }
        async Task<T> Read<T>(Func<Task<T>> source)
        {
            var result = await source();
            await freshAuthority(token);
            return result;
        }
        InventoryGoodsReceiptReversalTargetV1 RequireInventory(InventoryGoodsReceiptReversalTargetV1? source,
            PurchaseReceiptAdjustmentTargetV1 target)
        {
            if (source is null) throw Unavailable();
            var request = source.Original.OriginalRequest;
            if (request.CompanyId != companyId || request.AcceptanceId != target.AcceptanceId
                || request.PurchaseOrderId != target.OrderId || request.PurchaseOrderLineId != target.LineId
                || request.ProductId != target.ProductId || request.ExpectedProductVersion != target.ProductVersion
                || request.PurchaseUnitId != target.UnitId || source.Links is null
                || source.Links.Any(link => link is null || link.ConversionId == Guid.Empty || link.FutureAllocationId == Guid.Empty
                    || link.PurchaseOrderGeneralLineId != target.GeneralLineId || link.PhysicalAllocationId == Guid.Empty)
                || source.Links.Select(link => link.ConversionId).Distinct().Count() != source.Links.Count) throw Unavailable();
            return source;
        }
        IReadOnlyList<InventoryFutureAllocationViewV1> RequireAllocations(IReadOnlyList<InventoryFutureAllocationViewV1>? rows,
            Func<InventoryFutureAllocationViewV1, bool> ownerIdentity)
        {
            if (rows is null || rows.Any(view => view is null || view.CompanyId != companyId || !ownerIdentity(view)
                    || view.AllocationId == Guid.Empty || view.SalesOrderId == Guid.Empty || view.SalesOrderLineId == Guid.Empty
                    || view.PurchaseOrderId == Guid.Empty || view.PurchaseOrderLineId == Guid.Empty
                    || view.Status != nameof(FutureSupplyAllocationStatus.Active)
                    || !InventoryFutureAllocationTextV1.TryPositiveQuantity(view.RemainingQuantity, out _)
                    || !CanonicalVersion(view.Version))
                || rows.Select(view => view.AllocationId).Distinct().Count() != rows.Count) throw Unavailable();
            return rows;
        }
    }
}
