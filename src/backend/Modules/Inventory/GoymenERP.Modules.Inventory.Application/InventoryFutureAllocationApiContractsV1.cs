namespace GoymenERP.Modules.Inventory.Application;

/// <summary>F017-b2 requires receipt conversion, purchase cancellation protection and Sales demand reduction together.</summary>
public static class InventoryFutureAllocationApiV1
{
    public const string Path = "/api/v1/inventory/future-allocations";
    public const string ReleasePath = Path + "/{allocationId:guid}/release";
    public const string DemandPath = "/api/v1/inventory/sales-orders/{salesOrderId:guid}/lines/{salesOrderLineId:guid}/future-allocations";
    public const int ReadLimit = 100;
}

public sealed record InventoryFutureAllocationListV1(Guid CompanyId, Guid SalesOrderId, Guid SalesOrderLineId,
    IReadOnlyList<InventoryFutureAllocationViewV1> Items, bool WindowFull, IReadOnlyDictionary<Guid, string> UnitCodes);

/// <summary>User command boundary; trusted actor, company, roles and a freshly measured company-wide permission are derived here.</summary>
public interface IInventoryFutureAllocationCommandV1
{
    Task<InventoryFutureAllocationResultV1> CreateAsync(CreateFutureSupplyAllocationV1 request,
        InventoryCommandMetadata transport, CancellationToken cancellationToken = default);
    Task<InventoryFutureAllocationResultV1> ReleaseAsync(Guid allocationId, ReleaseFutureSupplyAllocationV1 request,
        InventoryCommandMetadata transport, CancellationToken cancellationToken = default);
}

public interface IInventoryFutureAllocationQueryV1
{
    Task<InventoryFutureAllocationListV1> ListByDemandAsync(Guid salesOrderId, Guid salesOrderLineId,
        CancellationToken cancellationToken = default);
}
