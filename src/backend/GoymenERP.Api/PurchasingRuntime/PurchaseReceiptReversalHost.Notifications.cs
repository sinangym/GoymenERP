using System.Data.Common;
using GoymenERP.Modules.Inventory.Application;
using GoymenERP.Modules.Inventory.Infrastructure.Persistence;
using GoymenERP.Modules.Purchasing.Application.Contracts;

namespace GoymenERP.Api.PurchasingRuntime;

internal sealed partial class PurchaseReceiptReversalHost
{
    /// <summary>ALS-D92: captured after union prelocks and before any owner Stage; no delayed current-state enrichment.</summary>
    private static async Task<IReadOnlyList<PurchaseRevivalNotificationSnapshotV1>> CaptureRevivalNotificationsAsync(
        DbConnection connection, DbTransaction transaction, Guid companyId, Guid orderId,
        IReadOnlyCollection<PurchaseReceiptAdjustmentTargetV1> targets, Func<CancellationToken, Task> freshAuthority,
        CancellationToken token)
    {
        await using var owner = await InventoryHistoricalSalesLinksV1.EnlistAsync(connection, transaction, companyId, token);
        var snapshots = new List<PurchaseRevivalNotificationSnapshotV1>();
        foreach (var group in targets.GroupBy(target => target.GeneralLineId).OrderBy(group => group.Key))
        {
            var version = group.First().ObservedOrderVersion;
            if (group.Key == Guid.Empty || group.Any(target => target.CompanyId != companyId || target.OrderId != orderId
                || target.ObservedOrderVersion != version)) throw Unavailable();
            var answer = await owner.ReadAsync(orderId, group.Key, token);
            await freshAuthority(token);
            if (answer is null || answer.Outcome == InventoryHistoricalSalesLinksOutcomeV1.Unavailable) continue;
            if (answer.Outcome != InventoryHistoricalSalesLinksOutcomeV1.Available || answer.CompanyId != companyId
                || answer.PurchaseOrderId != orderId || answer.PurchaseOrderLineId != group.Key || answer.Lines is null
                || answer.Lines.Any(line => line is null || line.SalesOrderId == Guid.Empty || line.SalesOrderLineId == Guid.Empty)) throw Unavailable();
            snapshots.Add(new(companyId, orderId, group.Key, version, answer.Lines.Select(line =>
                new PurchaseRevivalSalesTargetV1(line.SalesOrderId, line.SalesOrderLineId)).Distinct()
                .OrderBy(line => line.SalesOrderId).ThenBy(line => line.SalesOrderLineId).ToArray()));
        }
        // A successful zero-link read is an explicit empty snapshot. Missing evidence stays missing; the Purchasing
        // owner requires it only if its authoritative net operation actually revives this exact general line.
        return snapshots;
    }
}
