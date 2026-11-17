using GoymenERP.BuildingBlocks.Security;
using GoymenERP.Modules.Accounting.Application.Contracts;
using GoymenERP.Modules.Manufacturing.Domain.Common;
using GoymenERP.Modules.Manufacturing.Domain.Orders;

namespace GoymenERP.Modules.Manufacturing.Application;

public sealed record ReopenManufacturingOrderRequestV1(long ExpectedVersion, string? Reason);
public sealed record ManufacturingOrderReopeningCommandV1(Guid CompanyId, Guid OrderId, long ExpectedOrderVersion, string? Reason = null);

/// <summary>Shared Inventory transaction: proves/reverses the prior posted cost generation and saves the reopen atomically.</summary>
public interface IManufacturingOrderReopeningInventoryPortV1
{
    Task<ProductionOrder> ReopenAsync(ManufacturingOrderReopeningCommandV1 command,
        Func<CancellationToken, Task<ProductionOrder>> writeReopening, ManufacturingCommandMetadata metadata, CancellationToken cancellationToken);
}

public interface IManufacturingOrderReopeningStoreV1
{
    Task<ProductionOrder> ReopenOrderAsync(Guid orderId, long expectedVersion, Guid actorId, DateTimeOffset reopenedAt,
        string reason, ManufacturingCommandMetadata metadata,
        Func<DateTimeOffset, CancellationToken, Task<ManufacturingApiResult<object>?>> finalFence, CancellationToken cancellationToken = default);
}

public sealed class ManufacturingOrderReopenFenceExceptionV1(ManufacturingApiResult<object> refusal) : Exception("Yeniden açma çiti reddetti.")
{
    public ManufacturingApiResult<object> Refusal { get; } = refusal;
}

public sealed partial class ManufacturingCommandService
{
    public async Task<ManufacturingApiResult<OrderViewV1>> ReopenOrderAsync(Guid orderId, ReopenManufacturingOrderRequestV1? request,
        string idempotencyKey, CancellationToken cancellationToken)
    {
        if (orderId == Guid.Empty || request is null || request.ExpectedVersion < 1) return Invalid<OrderViewV1>("expectedVersion");
        if (DecisionReason(request.Reason) is not { } reason) return Invalid<OrderViewV1>("reason");
        if (!HasContext() || ActorGuid() is not { } capturedActor) return Denied<OrderViewV1>();
        var capturedCompany = companyContext.CompanyId;
        if (await FenceCapturedAsync<object>(ErpPermissions.ManufacturingOrdersRead, capturedCompany, capturedActor, cancellationToken) is { } initialRead)
            return Refusal<OrderViewV1>(initialRead);
        var answer = await RunAsync<OrderViewV1>(ErpPermissions.ManufacturingOrdersReopen, idempotencyKey,
            "manufacturing.order.reopen.v1", new { orderId, request.ExpectedVersion, reason }, ManufacturingApiStatus.Ok,
            async (metadata, actor, companyId) =>
            {
                var order = await orders.FindOrderAsync(orderId, cancellationToken);
                if (order is null) return NotFoundResult<OrderViewV1>();
                order.RequireReopenable(request.ExpectedVersion);
                if (orderReopenings is null || accountingPeriods is null || reopeningInventory is null)
                    return new(ManufacturingApiStatus.Unavailable, null, false, null);
                async Task<ManufacturingApiResult<object>?> Fence(DateTimeOffset closedAt, CancellationToken token)
                {
                    DateOnly closedOn;
                    try { closedOn = ManufacturingBusinessDay.DateOf(closedAt); }
                    catch (ManufacturingBusinessDayUnavailableException) { return new(ManufacturingApiStatus.Unavailable, null, false, null); }
                    AccountingPeriodStatusAnswerV1 period;
                    try { period = await accountingPeriods.GetStatusAsync(closedOn, token); }
                    catch (OperationCanceledException) when (token.IsCancellationRequested) { throw; }
                    catch (Exception) { return new(ManufacturingApiStatus.Unavailable, null, false, null); }
                    if (await FenceCapturedAsync<object>(ErpPermissions.ManufacturingOrdersReopen, companyId, actor, token) is { } writeRefusal) return writeRefusal;
                    if (await FenceCapturedAsync<object>(ErpPermissions.ManufacturingOrdersRead, companyId, actor, token) is { } readRefusal) return readRefusal;
                    return period switch
                    {
                        { Outcome: AccountingPeriodStatusOutcomeV1.Open, PeriodId: { } id, Status: "Open" } when id != Guid.Empty => null,
                        { Outcome: AccountingPeriodStatusOutcomeV1.Denied } => Denied<object>(),
                        { Outcome: AccountingPeriodStatusOutcomeV1.None, PeriodId: null, Status: null }
                            => new(ManufacturingApiStatus.Unprocessable, null, false, "manufacturing_order_reopen_period_not_open"),
                        { Outcome: AccountingPeriodStatusOutcomeV1.NotOpen, PeriodId: { } id, Status: "Closed" or "Proposed" } when id != Guid.Empty
                            => new(ManufacturingApiStatus.Unprocessable, null, false, "manufacturing_order_reopen_period_not_open"),
                        _ => new(ManufacturingApiStatus.Unavailable, null, false, null),
                    };
                }
                if (await Fence(order.ClosedAtUtc!.Value, cancellationToken) is { } refused) return Refusal<OrderViewV1>(refused);
                try
                {
                    var reopened = await reopeningInventory.ReopenAsync(new(companyId, orderId, request.ExpectedVersion, reason),
                        token => orderReopenings.ReopenOrderAsync(orderId, request.ExpectedVersion, actor, timeProvider.GetUtcNow(), reason,
                            metadata, Fence, token), metadata, cancellationToken);
                    var history = reopened?.Reopenings.LastOrDefault();
                    if (reopened is null || reopened.CompanyId != companyId || reopened.Id != orderId || reopened.Status != OrderStatus.Open
                        || reopened.Version != request.ExpectedVersion + 1 || history is null
                        || history.ClosedOrderVersion != request.ExpectedVersion || history.ReopenedByUserId != actor
                        || history.Reason != reason)
                        return new(ManufacturingApiStatus.Unavailable, null, false, null);
                    return new(ManufacturingApiStatus.Ok, OrderView(reopened), false, null);
                }
                catch (ManufacturingOrderReopenFenceExceptionV1 finalRefusal) { return Refusal<OrderViewV1>(finalRefusal.Refusal); }
            }, stored => OrderView((ProductionOrder)stored), cancellationToken);
        return await FenceCapturedAsync<object>(ErpPermissions.ManufacturingOrdersRead, capturedCompany, capturedActor, cancellationToken) is { } finalRead
            ? Refusal<OrderViewV1>(finalRead) : answer;
    }
}
