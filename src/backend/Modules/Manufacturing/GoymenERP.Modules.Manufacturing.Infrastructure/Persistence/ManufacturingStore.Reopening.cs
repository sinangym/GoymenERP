using System.Text.Json;
using GoymenERP.Modules.Manufacturing.Application;
using GoymenERP.Modules.Manufacturing.Domain.Orders;
using Microsoft.EntityFrameworkCore;

namespace GoymenERP.Modules.Manufacturing.Infrastructure.Persistence;

public sealed partial class ManufacturingStore : IManufacturingOrderReopeningStoreV1
{
    public async Task<ProductionOrder> ReopenOrderAsync(Guid orderId, long expectedVersion, Guid actorId,
        DateTimeOffset reopenedAt, string reason, ManufacturingCommandMetadata metadata,
        Func<DateTimeOffset, CancellationToken, Task<ManufacturingApiResult<object>?>> finalFence,
        CancellationToken cancellationToken = default)
    {
        RequireMetadata(metadata); var companyId = RequireResolvedCompany();
        // The Inventory participant owns the shared transaction, including any old cost reversal. No source-only
        // state transition may be committed on its own when that participant is absent.
        if (dbContext.Database.CurrentTransaction is null)
            throw new InvalidOperationException("Yeniden açma Inventory owner işlemi içinde kaydedilmelidir.");
        var row = await dbContext.Orders.SingleOrDefaultAsync(item => item.CompanyId == companyId && item.Id == orderId, cancellationToken)
            ?? throw Refused(ManufacturingStoreCodes.OrderNotFound, "Bu şirketin böyle bir emri yok.");
        var order = await RestoreOrderAsync(row, cancellationToken); order.RequireReopenable(expectedVersion);
        if (await finalFence(order.ClosedAtUtc!.Value, cancellationToken) is { } refusal)
            throw new ManufacturingOrderReopenFenceExceptionV1(refusal);
        var reopening = order.Reopen(expectedVersion, actorId, reopenedAt, reason);
        dbContext.OrderReopenings.Add(new OrderReopeningRow
        {
            CompanyId = companyId, OrderId = orderId, ClosedOrderVersion = reopening.ClosedOrderVersion,
            ClosedByUserId = reopening.ClosedByUserId, ClosedAtUtc = reopening.ClosedAtUtc,
            ReopenedOrderVersion = reopening.ReopenedOrderVersion, ReopenedByUserId = reopening.ReopenedByUserId,
            ReopenedAtUtc = reopening.ReopenedAtUtc, Reason = reopening.Reason, CorrelationId = metadata.CorrelationId.Trim(),
        });
        row.Status = (int)order.Status; row.ClosedByUserId = null; row.ClosedAtUtc = null;
        row.Version = order.Version; row.UpdatedAtUtc = reopenedAt.ToUniversalTime();
        Audit(metadata, companyId, OrderEntityType, orderId, "manufacturing.order.reopened.v1", "Reopen",
            expectedVersion, order.Version, JsonSerializer.Serialize(new { status = "Closed", reopening.ClosedOrderVersion }, Json),
            JsonSerializer.Serialize(reopening, Json), reopening.Reason);
        Complete(metadata, companyId, order); await SaveAsync(cancellationToken); return order;
    }
}
