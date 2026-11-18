using GoymenERP.BuildingBlocks.Multitenancy;
using GoymenERP.BuildingBlocks.Security;
using GoymenERP.Modules.Inventory.Application;
using GoymenERP.Modules.Settings.Application.Contracts.V1;
using Microsoft.EntityFrameworkCore;

namespace GoymenERP.Modules.Inventory.Infrastructure.Persistence;

public sealed class InventoryFutureAllocationQueryService(InventoryDbContext database, ICompanyContext company,
    ICurrentUser user, IErpAuthorityRevalidationService authority, ISettingsUnitIdentityCatalogV1 units,
    IInventoryFutureSupplyScheduleSourceV1 schedules, TimeProvider time) : IInventoryFutureAllocationQueryV1
{
    public async Task<InventoryFutureAllocationListV1> ListByDemandAsync(Guid salesOrderId, Guid salesOrderLineId,
        CancellationToken cancellationToken = default)
    {
        var fence = new InventoryFutureAllocationUserFence(company, user, authority, ErpPermissions.InventoryFutureAllocationRead);
        await fence.RequireStillAsync(cancellationToken);
        if (salesOrderId == Guid.Empty || salesOrderLineId == Guid.Empty)
            throw new InventoryAllocationException(400, InventoryFutureAllocationCodesV1.ValidationFailed);
        InventoryFutureAllocationViewV1[] items;
        var unitCodes = new Dictionary<Guid, string>();
        DateOnly evaluatedOn;
        IReadOnlyDictionary<Guid, InventoryFutureAllocationScheduleViewV1> scheduleViews;
        try
        {
            var rows = await database.FutureSupplyAllocations.AsNoTracking()
                .Where(row => row.DemandId == salesOrderId && row.DemandLineId == salesOrderLineId)
                .OrderByDescending(row => row.CreatedAtUtc).ThenBy(row => row.Id)
                .Take(InventoryFutureAllocationApiV1.ReadLimit + 1).ToListAsync(cancellationToken);
            items = rows.Select(InventoryFutureAllocationEngine.View).ToArray();
            foreach (var unitId in items.Take(InventoryFutureAllocationApiV1.ReadLimit).Select(item => item.UnitId).Distinct())
            {
                var unit = await units.ReadAsync(unitId, cancellationToken);
                if (unit is null || unit.UnitId != unitId || string.IsNullOrWhiteSpace(unit.Code))
                    throw new InvalidOperationException("An allocation unit could not be displayed from its owner source.");
                unitCodes.Add(unitId, unit.Code);
            }
            evaluatedOn = DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(time.GetUtcNow(),
                TimeZoneInfo.FindSystemTimeZoneById("Europe/Istanbul")).DateTime);
            scheduleViews = await InventoryFutureAllocationScheduleProjection.ReadAsync(
                items.Take(InventoryFutureAllocationApiV1.ReadLimit).ToArray(), evaluatedOn, schedules, cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
        catch (Exception exception)
        {
            await fence.RequireStillAsync(cancellationToken);
            throw new InventoryAllocationException(503, InventoryFutureAllocationCodesV1.ServiceUnavailable, innerException: exception);
        }
        await fence.RequireStillAsync(cancellationToken);
        if (items.Any(item => item.CompanyId != fence.CompanyId))
            throw new InventoryAllocationException(503, InventoryFutureAllocationCodesV1.ServiceUnavailable);
        return new InventoryFutureAllocationListV1(fence.CompanyId, salesOrderId, salesOrderLineId,
            items.Take(InventoryFutureAllocationApiV1.ReadLimit).ToArray(), items.Length > InventoryFutureAllocationApiV1.ReadLimit,
            unitCodes, evaluatedOn, scheduleViews);
    }
}
