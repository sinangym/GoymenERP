namespace GoymenERP.Modules.Inventory.Application;

public enum InventoryHistoricalSalesLinksOutcomeV1 { Available = 1, Unavailable = 2 }
public sealed record InventoryHistoricalSalesLineV1(Guid SalesOrderId, Guid SalesOrderLineId);
public sealed record InventoryHistoricalSalesLinksAnswerV1(InventoryHistoricalSalesLinksOutcomeV1 Outcome,
    Guid CompanyId, Guid PurchaseOrderId, Guid PurchaseOrderLineId, IReadOnlyList<InventoryHistoricalSalesLineV1> Lines);

/// <summary>
/// Actual historical future-allocation links, including Released and Consumed rows. Available with an empty list means
/// a successful owner read found no links. This is notification evidence, never allocation reactivation or authority.
/// The caller fences its own operation; company identity comes only from the server-resolved ambient context.
/// </summary>
public interface IInventoryHistoricalSalesLinksV1
{
    Task<InventoryHistoricalSalesLinksAnswerV1> ReadAsync(Guid purchaseOrderId, Guid purchaseOrderLineId,
        CancellationToken cancellationToken = default);
}
