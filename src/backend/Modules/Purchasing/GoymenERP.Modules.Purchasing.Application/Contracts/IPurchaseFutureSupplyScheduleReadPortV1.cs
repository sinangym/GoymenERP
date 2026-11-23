namespace GoymenERP.Modules.Purchasing.Application.Contracts;

/// <summary>
/// F17-1b / KLN-D30: the current delivery schedule, read under the caller's purchase-line lock and host transaction.
/// Company identity comes only from the resolved server context. ALS-D81 covers slices in their stored slice order,
/// not date order; accepted shares are never assigned to a different slice by this read. No price or authority is carried.
/// </summary>
public interface IPurchaseFutureSupplyScheduleReadPortV1
{
    Task<PurchaseFutureSupplyScheduleV1> GetLineScheduleAsync(Guid orderId, Guid lineId, CancellationToken cancellationToken = default);
}

public enum PurchaseFutureSupplyScheduleOutcomeV1
{
    Found = 1,
    NotFound = 2,
    Unreadable = 3,
}

/// <summary>Found with no slices is an undated line. Unreadable must never be treated as undated.</summary>
public sealed record PurchaseFutureSupplyScheduleV1(PurchaseFutureSupplyScheduleOutcomeV1 Outcome, Guid CompanyId,
    Guid OrderId, Guid LineId, Guid UnitId, long? ObservedOrderVersion, IReadOnlyList<PurchaseFutureSupplyScheduleSliceV1> Slices);

/// <summary>Quantity and unreceived remainder in the purchase line's own unit, canonical invariant decimal text.</summary>
public sealed record PurchaseFutureSupplyScheduleSliceV1(Guid SliceId, int SliceNo, DateOnly DeliveryDate,
    string QuantityText, string RemainingText);
