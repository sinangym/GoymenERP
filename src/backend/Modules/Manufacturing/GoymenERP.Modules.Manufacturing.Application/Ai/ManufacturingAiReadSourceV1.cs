namespace GoymenERP.Modules.Manufacturing.Application.Ai;

/// <summary>
/// WP-A3b: the narrow Manufacturing-owned read seam behind the module's AI READ tools — exactly the owner's existing
/// list/queue reads (<see cref="ManufacturingCommandService.ListOrdersAsync"/>, <see cref="ManufacturingCommandService.ListPlansAsync"/>,
/// <see cref="ManufacturingCommandService.GetPlanWorkCenterQueueAsync"/>), unchanged: the same company filter, the same
/// stored rows, nothing computed or written here. The tools measure their permission names around every use.
/// </summary>
public interface IManufacturingAiReadSourceV1
{
    Task<ManufacturingApiResult<OrderListViewV1>> ListOrdersAsync(int pageSize, string? cursor, string? status,
        CancellationToken cancellationToken);

    Task<ManufacturingApiResult<PlanListViewV1>> ListPlansAsync(int pageSize, string? cursor, string? status,
        CancellationToken cancellationToken);

    Task<ManufacturingApiResult<PlanWorkCenterQueueViewV1>> GetPlanWorkCenterQueueAsync(Guid planId,
        CancellationToken cancellationToken);
}

public sealed class ManufacturingAiReadSourceV1(ManufacturingCommandService service) : IManufacturingAiReadSourceV1
{
    public Task<ManufacturingApiResult<OrderListViewV1>> ListOrdersAsync(int pageSize, string? cursor, string? status,
        CancellationToken cancellationToken) => service.ListOrdersAsync(pageSize, cursor, status, cancellationToken);

    public Task<ManufacturingApiResult<PlanListViewV1>> ListPlansAsync(int pageSize, string? cursor, string? status,
        CancellationToken cancellationToken) => service.ListPlansAsync(pageSize, cursor, status, cancellationToken);

    public Task<ManufacturingApiResult<PlanWorkCenterQueueViewV1>> GetPlanWorkCenterQueueAsync(Guid planId,
        CancellationToken cancellationToken) => service.GetPlanWorkCenterQueueAsync(planId, cancellationToken);
}

public static class ManufacturingAiUnitsV1
{
    /// <summary>Manufacturing plans, orders and queues are measured in kilograms; no piece count is derived here.</summary>
    public const string Kilogram = "kg";
}
