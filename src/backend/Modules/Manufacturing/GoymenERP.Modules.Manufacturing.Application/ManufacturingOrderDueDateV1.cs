using System.Globalization;
using GoymenERP.Modules.Manufacturing.Domain.Common;
using GoymenERP.Modules.Manufacturing.Domain.Orders;

namespace GoymenERP.Modules.Manufacturing.Application;

// URT-D66 (3)-(5): the production order copies its plan's termin once when it opens; afterwards the order's termin is
// changed on the order alone and the plan's termin does not move. The order read shows both and flags the difference.

/// <summary>
/// URT-D66 (3): the order's termin in one save — the order row's termin column and termin revision, the audit event and
/// the command record — or nothing. A port of its own beside <see cref="IManufacturingOrderStore"/>; the one store
/// implements it. The domain decides on the order rebuilt from storage.
/// </summary>
public interface IManufacturingOrderDueDateStore
{
    /// <summary>Changes the order's termin; <paramref name="businessToday"/> is the server's Istanbul business day.</summary>
    Task<ProductionOrder> ChangeOrderDueDateAsync(Guid orderId, long expectedDueDateRevision, DateOnly dueDate,
        DateOnly businessToday, ManufacturingCommandMetadata metadata, CancellationToken cancellationToken = default);
}

/// <summary>
/// <c>POST /orders/{orderId}/due-date</c>: the order's termin revision the caller read and the new termin — a calendar day
/// (<c>yyyy-MM-dd</c>, no time). An order termin is changed, never removed.
/// </summary>
public sealed record ChangeOrderDueDateRequestV1(
    long ExpectedDueDateRevision,
    DateOnly DueDate);

public sealed partial class ManufacturingCommandService
{
    /// <summary>
    /// <c>POST /orders/{orderId}/due-date</c> (URT-D66 (3)) under <c>manufacturing.orders.write</c> (URT-D66 (5): the order
    /// edit permission, no termin permission of its own): changes the open order's termin. The plan's termin does not move
    /// (URT-D66 (4)). The order's version does not move; its termin revision does. A closed order, no change and a day
    /// before the Istanbul business day are 422; a moved termin revision is 409; a host that cannot resolve the business
    /// day's zone is 503. ADR-011 as the other order commands: the same key replays the first answer.
    /// </summary>
    public Task<ManufacturingApiResult<OrderViewV1>> ChangeOrderDueDateAsync(Guid orderId,
        ChangeOrderDueDateRequestV1? request, string idempotencyKey, string permission,
        CancellationToken cancellationToken)
    {
        if (orderId == Guid.Empty || request is null || request.ExpectedDueDateRevision < 0)
        {
            return Task.FromResult(Invalid<OrderViewV1>("expectedDueDateRevision"));
        }

        // The type's default day (0001-01-01) is no entered termin but an unset value; the screen refuses it as well.
        if (request.DueDate == DateOnly.MinValue)
        {
            return Task.FromResult(Invalid<OrderViewV1>("dueDate"));
        }

        return RunAsync<OrderViewV1>(permission, idempotencyKey, ManufacturingCommandNames.ChangeOrderDueDate,
            new
            {
                orderId,
                expectedDueDateRevision = request.ExpectedDueDateRevision,
                // The calendar day as its canonical text, so the hash never depends on a serializer's date settings.
                dueDate = request.DueDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
            }, ManufacturingApiStatus.Ok,
            async (metadata, _, _) =>
            {
                if (orderDueDates is null || BusinessToday() is not { } businessToday)
                {
                    return new ManufacturingApiResult<OrderViewV1>(ManufacturingApiStatus.Unavailable, null, false, null);
                }

                var changed = await orderDueDates.ChangeOrderDueDateAsync(orderId, request.ExpectedDueDateRevision,
                    request.DueDate, businessToday, metadata, cancellationToken);
                return new ManufacturingApiResult<OrderViewV1>(ManufacturingApiStatus.Ok, OrderView(changed), false, null);
            }, stored => OrderView((ProductionOrder)stored), cancellationToken);
    }

    /// <summary>
    /// URT-D66 (1): today's Istanbul business day from the server's clock, never a client's date; null when the host cannot
    /// resolve the zone (the caller answers 503, nothing is guessed).
    /// </summary>
    private DateOnly? BusinessToday()
    {
        try
        {
            return ManufacturingBusinessDay.DateOf(timeProvider.GetUtcNow());
        }
        catch (ManufacturingBusinessDayUnavailableException)
        {
            return null;
        }
    }
}
