using GoymenERP.Modules.Inventory.Application;
using GoymenERP.Modules.Inventory.Domain.Allocations;
using GoymenERP.Modules.Inventory.Domain.Quantities;
using Microsoft.EntityFrameworkCore;
using static GoymenERP.Modules.Inventory.Application.InventorySubcontractDeliveryContractV1;

namespace GoymenERP.Modules.Inventory.Infrastructure.Persistence;

public sealed partial class InventorySubcontractDeliveryParticipant
{
    private async Task<IReadOnlyList<(AllocationRow Row, StockAllocation Allocation)>> PrepareAdditionalAllocationsAsync(
        InventorySubcontractShipmentLineV1 input, AllocationRow primary, LabelRow label,
        GoymenERP.Modules.Inventory.Domain.Stock.StockPosition position, decimal required, CancellationToken token)
    {
        var slices = input.AdditionalAllocations;
        if (required == 0m && slices is null or { Count: 0 }) return [];
        if (slices is null || slices.Count is < 1 or > 500 || slices.Any(slice => slice is null || slice.AllocationId == Guid.Empty
            || slice.AllocationId == primary.Id) || slices.Select(slice => slice.AllocationId).Distinct().Count() != slices.Count)
            throw Refused(409, InventoryDefiniteShipmentCodesV1.AllocationInsufficient);
        var result = new List<(AllocationRow, StockAllocation)>(); decimal total = 0m;
        foreach (var slice in slices)
        {
            var amount = Quantity(slice.QuantityKg); total = checked(total + amount.Value);
            var row = await context.Allocations.SingleOrDefaultAsync(row => row.Id == slice.AllocationId, token)
                ?? throw Refused(404, SourceMismatch);
            var original = await context.SubcontractAllocationSources.AsNoTracking().SingleOrDefaultAsync(source => source.AllocationId == row.Id, token)
                ?? throw Refused(503, SourceMismatch);
            RequireOriginal(original, input.Source);
            if (row.CompanyId != companyId || row.LabelId != label.Id || row.DemandKind != primary.DemandKind || row.DemandId != primary.DemandId
                || row.DemandLineId != primary.DemandLineId || row.DemandProductId != primary.DemandProductId || row.DemandProductVersion != primary.DemandProductVersion
                || row.OwnerKind != primary.OwnerKind || row.OwnerId != primary.OwnerId || row.CustodianKind != primary.CustodianKind
                || row.CustodianId != primary.CustodianId || row.LocationId != primary.LocationId || row.Block != primary.Block)
                throw Refused(409, SourceMismatch);
            var allocation = StockAllocation.Restore(row.Id, position, Kilograms.Positive(row.AllocatedKg),
                Kilograms.Of(row.RemainingKg), row.Version, (StockAllocationStatus)row.Status);
            try { allocation.Consume(Version(slice.ExpectedVersion), input.LabelId, amount); }
            catch (StockAllocationException error)
            {
                throw Refused(409, error.Failure == StockAllocationFailure.VersionConflict ? InventoryDefiniteShipmentCodesV1.AllocationVersionConflict
                    : error.Failure == StockAllocationFailure.Insufficient ? InventoryDefiniteShipmentCodesV1.AllocationInsufficient
                    : InventoryDefiniteShipmentCodesV1.AllocationState, error);
            }
            result.Add((row, allocation));
        }
        if (total != required) throw Refused(409, InventoryDefiniteShipmentCodesV1.AllocationInsufficient);
        return result;
    }
}
