namespace GoymenERP.Modules.Inventory.Application;

/// <summary>How the Inventory owner answered one stock-aging read. Anything but <see cref="Found"/> is fail-closed (never "no stock").</summary>
public enum InventoryStockAgingOutcomeV1
{
    /// <summary>The owner cannot answer (store failure): fail-closed, never a default.</summary>
    Unavailable = 0,

    /// <summary>No resolved company context, or the query names another company: nothing was read.</summary>
    Denied = 1,

    /// <summary>The owner's company-filtered positions (an empty page is a recorded "nothing").</summary>
    Found = 2,
}

/// <summary>
/// WP14 (DALGA_PLANI_v2 1.15): one page request of the stock-aging read. <paramref name="Page"/> is 1-based; the order is
/// (label id, location id), so pages are stable while stock does not move. Product and location narrow the read.
/// </summary>
public sealed record InventoryStockAgingQueryV1(Guid CompanyId, int Page, int PageSize, Guid? ProductId, Guid? LocationId);

/// <summary>
/// One label's physical stock at one location, with the two dates an age can be measured from: when the label was created
/// in Inventory and when its first incoming movement occurred (null when the label has no incoming movement line). Which
/// one is the "age" is the report caller's parameter, never decided here. Kilograms are lossless invariant text.
/// </summary>
public sealed record InventoryStockAgingPositionV1(
    Guid LabelId,
    string LabelCode,
    Guid ProductId,
    Guid LocationId,
    string? HeatNumber,
    string? CoilNumber,
    string PhysicalKg,
    DateTimeOffset LabelCreatedAtUtc,
    DateTimeOffset? FirstIncomingAtUtc);

public sealed record InventoryStockAgingPageV1(
    InventoryStockAgingOutcomeV1 Outcome,
    Guid CompanyId,
    IReadOnlyList<InventoryStockAgingPositionV1> Items,
    bool HasMore);

/// <summary>
/// WP14: the Inventory-owned read port behind the report centre's aging report. The owner answers from its own
/// company-filtered tables only; it carries NO authority (the consumer measures its own permissions before and after asking)
/// and it CREATES NOTHING (no attempt, audit, version or stock effect). No age bucket or threshold is decided here.
/// </summary>
public interface IInventoryStockAgingReadPortV1
{
    /// <summary>The technical page bound (not a business threshold).</summary>
    const int MaximumPageSize = 500;

    Task<InventoryStockAgingPageV1> ReadAsync(InventoryStockAgingQueryV1 query, CancellationToken cancellationToken = default);
}
