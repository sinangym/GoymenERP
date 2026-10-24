using GoymenERP.Modules.Inventory.Application;
using GoymenERP.Modules.Inventory.Infrastructure.Persistence;

namespace GoymenERP.Api.IntegrationTests;

/// <summary>
/// Raw persistence fixtures have no HTTP actor. Keep their explicit test-only fence here rather
/// than adding an optional authority bypass to the production writer. Host authorization is
/// exercised separately through the real authority service and disposable Identity records.
/// </summary>
internal static class InventoryAllocationSeedExtensions
{
    public static Task<InventoryPhysicalAllocationResultV1> CreateAsync(
        this InventoryStore store,
        CreatePhysicalSalesAllocationV1 request,
        InventoryCommandMetadata metadata,
        CancellationToken cancellationToken = default) =>
        store.CreateAsync(request, metadata, SeedFenceAsync, cancellationToken);

    public static Task<InventoryPhysicalAllocationResultV1> ReleaseAsync(
        this InventoryStore store,
        Guid allocationId,
        ReleasePhysicalAllocationV1 request,
        InventoryCommandMetadata metadata,
        CancellationToken cancellationToken = default) =>
        store.ReleaseAsync(allocationId, request, metadata, SeedFenceAsync, cancellationToken);

    public static Task<InventoryPhysicalAllocationChangeResultV1> ChangeAsync(
        this InventoryStore store,
        Guid allocationId,
        ChangePhysicalSalesAllocationV1 request,
        InventoryCommandMetadata metadata,
        CancellationToken cancellationToken = default) =>
        store.ChangeAsync(allocationId, request, metadata, SeedFenceAsync, cancellationToken);

    private static Task SeedFenceAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.CompletedTask;
    }
}
