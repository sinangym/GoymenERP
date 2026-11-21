using System.Data.Common;
using GoymenERP.BuildingBlocks.Concurrency;
using GoymenERP.Modules.Inventory.Application;
using GoymenERP.Modules.Inventory.Domain.FutureSupply;
using GoymenERP.Modules.Purchasing.Application.Contracts;

namespace GoymenERP.Api.PurchasingRuntime;

internal sealed partial class PurchaseReceiptReversalHost
{
    private async Task<bool> PrepareBatchAsync(Guid company, PurchaseReceiptReversalBatchWriteV1 write,
        IPurchaseReceiptAdjustmentBatchParticipantV1 purchase, IInventoryGoodsReceiptReversalParticipantV1 inventory,
        DbConnection connection, DbTransaction transaction, Func<CancellationToken, Task> authority, CancellationToken token)
    {
        await using var allocations = await services.GetRequiredService<IInventoryFutureAllocationReleasePortV1>().EnlistAsync(connection, transaction, token);
        return await PrepareBatchLockedAsync(company, write, purchase, inventory, allocations,
            services.GetRequiredService<ISalesOrderLineSerializationLockBinderV1>().Bind(connection, transaction),
            services.GetRequiredService<IPurchaseLineSerializationLockBinderV1>().Bind(connection, transaction), authority, token);
    }

    internal static async Task<bool> PrepareBatchLockedAsync(Guid company, PurchaseReceiptReversalBatchWriteV1 write,
        IPurchaseReceiptAdjustmentBatchParticipantV1 purchase, IInventoryGoodsReceiptReversalParticipantV1 inventory,
        IInventoryFutureAllocationReleaseSessionV1 allocations, ISalesOrderLineSerializationLockV1 salesLocks,
        IPurchaseLineSerializationLockV1 purchaseLocks, Func<CancellationToken, Task> authority, CancellationToken token)
    {
        var acceptanceIds = write.Request.Lines.Select(line => line.AcceptanceId).ToArray();
        var first = RequireBatchTargets(await Read(() => purchase.ReadTargetsAsync(write.OrderId, acceptanceIds, token)), company, write);
        var originals = new Dictionary<Guid, InventoryGoodsReceiptReversalTargetV1>();
        var initialByPurchase = new Dictionary<Guid, IReadOnlyList<InventoryFutureAllocationViewV1>>();
        var sales = new Dictionary<Guid, Guid>();
        foreach (var target in first.Values)
        {
            var original = RequireInventory(await Read(() => inventory.ReadTargetAsync(target.AcceptanceId, token)), target);
            originals.Add(target.AcceptanceId, original);
            foreach (var link in original.Links) AddSales(link.SalesOrderId, link.SalesOrderLineId);
            if (initialByPurchase.ContainsKey(target.GeneralLineId)) continue;
            var active = RequireAllocations(await Read(() => allocations.ListActiveByPurchaseLineAsync(target.GeneralLineId, token)),
                row => row.PurchaseOrderId == target.OrderId && row.PurchaseOrderLineId == target.GeneralLineId);
            initialByPurchase.Add(target.GeneralLineId, active);
            foreach (var row in active) AddSales(row.SalesOrderId, row.SalesOrderLineId);
        }
        foreach (var id in sales.Keys.Order()) { await salesLocks.AcquireAsync(id, token); await authority(token); }
        var purchases = first.Values.Select(target => target.GeneralLineId).ToHashSet();
        var initialByDemand = new Dictionary<Guid, HashSet<Guid>>();
        foreach (var (line, order) in sales)
        {
            var rows = RequireAllocations(await Read(() => allocations.ListActiveByDemandLineAsync(line, token)),
                row => row.SalesOrderId == order && row.SalesOrderLineId == line);
            initialByDemand.Add(line, rows.Select(row => row.AllocationId).ToHashSet());
            foreach (var row in rows) purchases.Add(row.PurchaseOrderLineId);
        }
        // This is one union for the entire batch: never interleave per-acceptance Sales and Purchase locking.
        foreach (var id in purchases.Order()) { await purchaseLocks.AcquireAsync(id, token); await authority(token); }
        var current = RequireBatchTargets(await Read(() => purchase.ReadTargetsAsync(write.OrderId, acceptanceIds, token)), company, write);
        foreach (var target in current.Values)
        {
            var initial = first[target.AcceptanceId];
            if (target.GeneralLineId != initial.GeneralLineId || target.LineId != initial.LineId) return false;
            var fresh = RequireInventory(await Read(() => inventory.ReadTargetAsync(target.AcceptanceId, token)), target);
            var ids = originals[target.AcceptanceId].Links.Select(link => link.ConversionId).ToHashSet();
            if (fresh.Links.Any(link => !ids.Contains(link.ConversionId)
                || !sales.TryGetValue(link.SalesOrderLineId, out var order) || order != link.SalesOrderId)) return false;
        }
        foreach (var (general, initial) in initialByPurchase)
        {
            var rows = RequireAllocations(await Read(() => allocations.ListActiveByPurchaseLineAsync(general, token)),
                row => row.PurchaseOrderId == write.OrderId && row.PurchaseOrderLineId == general);
            var ids = initial.Select(row => row.AllocationId).ToHashSet();
            if (rows.Any(row => !ids.Contains(row.AllocationId) || !sales.ContainsKey(row.SalesOrderLineId))) return false;
        }
        foreach (var (line, order) in sales)
        {
            var rows = RequireAllocations(await Read(() => allocations.ListActiveByDemandLineAsync(line, token)),
                row => row.SalesOrderId == order && row.SalesOrderLineId == line);
            if (rows.Any(row => !initialByDemand[line].Contains(row.AllocationId) || !purchases.Contains(row.PurchaseOrderLineId))) return false;
        }
        return true;

        async Task<T> Read<T>(Func<Task<T>> source) { var value = await source(); await authority(token); return value; }
        void AddSales(Guid order, Guid line)
        {
            if (order == Guid.Empty || line == Guid.Empty || sales.TryGetValue(line, out var previous) && previous != order) throw Unavailable();
            sales[line] = order;
        }
        InventoryGoodsReceiptReversalTargetV1 RequireInventory(InventoryGoodsReceiptReversalTargetV1? source, PurchaseReceiptAdjustmentTargetV1 target)
        {
            if (source is null || source.Original is null || source.Original.OriginalRequest is null) throw Unavailable();
            var original = source.Original.OriginalRequest;
            if (original.CompanyId != company || original.AcceptanceId != target.AcceptanceId || original.PurchaseOrderId != target.OrderId
                || original.PurchaseOrderLineId != target.LineId || original.ProductId != target.ProductId || original.ExpectedProductVersion != target.ProductVersion
                || original.PurchaseUnitId != target.UnitId || source.Links is null || source.Links.Any(link => link is null
                    || link.ConversionId == Guid.Empty || link.FutureAllocationId == Guid.Empty || link.PhysicalAllocationId == Guid.Empty
                    || link.PurchaseOrderGeneralLineId != target.GeneralLineId)
                || source.Links.Select(link => link.ConversionId).Distinct().Count() != source.Links.Count) throw Unavailable();
            return source;
        }
        IReadOnlyList<InventoryFutureAllocationViewV1> RequireAllocations(IReadOnlyList<InventoryFutureAllocationViewV1>? rows,
            Func<InventoryFutureAllocationViewV1, bool> identity)
        {
            if (rows is null || rows.Any(row => row is null || row.CompanyId != company || !identity(row)
                || row.AllocationId == Guid.Empty || row.SalesOrderId == Guid.Empty || row.SalesOrderLineId == Guid.Empty
                || row.PurchaseOrderId == Guid.Empty || row.PurchaseOrderLineId == Guid.Empty || row.Status != nameof(FutureSupplyAllocationStatus.Active)
                || !InventoryFutureAllocationTextV1.TryPositiveQuantity(row.RemainingQuantity, out _) || !CanonicalVersion(row.Version))
                || rows.Select(row => row.AllocationId).Distinct().Count() != rows.Count) throw Unavailable();
            return rows;
        }
    }
}
