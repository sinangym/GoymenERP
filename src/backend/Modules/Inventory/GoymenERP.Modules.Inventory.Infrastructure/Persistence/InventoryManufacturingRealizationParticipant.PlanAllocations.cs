using System.Globalization;
using System.Text.Json;
using GoymenERP.Modules.Inventory.Application;
using GoymenERP.Modules.Inventory.Domain;
using GoymenERP.Modules.Inventory.Domain.Allocations;
using GoymenERP.Modules.Inventory.Domain.Quantities;
using GoymenERP.Modules.Inventory.Domain.Stock;
using Microsoft.EntityFrameworkCore;

namespace GoymenERP.Modules.Inventory.Infrastructure.Persistence;

/// <summary>
/// B-01: plan-input allocations (URT-D3, URT-D21, URT-D22), staged in Manufacturing's transaction. They use the same
/// <c>inventory.Allocations</c> rows, the same free-kg rule and the same label-version fence as the Sales physical
/// allocation, so Sales and Manufacturing can never hold the same kilogram twice.
/// </summary>
public sealed partial class InventoryManufacturingRealizationParticipant
{
    private const string AllocatedEvent = "inventory.manufacturing-plan-allocation.created.v1";
    private const string ReleasedEvent = "inventory.manufacturing-plan-allocation.released.v1";
    private const string ConsumedEvent = "inventory.manufacturing-plan-allocation.consumed.v1";

    public async Task<InventoryManufacturingPlanAllocationResultV1> StagePlanInputAllocationAsync(
        InventoryManufacturingPlanAllocationRequestV1 request,
        InventoryCommandMetadata metadata,
        CancellationToken cancellationToken = default)
    {
        Begin();
        var quantities = InventoryManufacturingPlanAllocationContractV1.Validate(request, metadata, companyId);
        var keyHash = Hash(metadata.IdempotencyKey);
        var payloadHash = RequestHash(request);
        if (await FindCommandAsync(InventoryManufacturingPlanAllocationContractV1.AllocateCommandName, keyHash,
                cancellationToken) is { } existing)
        {
            EnsureReplay(existing, metadata, payloadHash);
            var replay = JsonSerializer.Deserialize<InventoryManufacturingPlanAllocationResultV1>(existing.ResultJson,
                Json) ?? throw Refused(503, InventoryManufacturingRealizationCodesV1.Unavailable);
            return replay with { IsReplay = true, OriginalCorrelationId = existing.CorrelationId };
        }

        // A plan is allocated once. A second approval of the same plan without its first command's key would hold
        // the same labels twice; the filtered unique index enforces this too, this only makes the refusal exact.
        if (await context.Allocations.AsNoTracking().AnyAsync(row =>
                row.DemandKind == InventoryAllocationDemandKindsV1.ManufacturingPlanInput
                && row.DemandId == request.PlanId && row.Status == (int)StockAllocationStatus.Active,
                cancellationToken))
        {
            throw Refused(409, InventoryManufacturingRealizationCodesV1.ConcurrentChange);
        }

        var now = clock.GetUtcNow();
        var allocations = new List<InventoryManufacturingPlanAllocationV1>(request.Lines.Count);
        for (var index = 0; index < request.Lines.Count; index++)
        {
            var line = request.Lines[index];
            var quantity = quantities[index];
            var parent = await LoadParentAsync(line.LabelId, line.ExpectedLabelVersion, requireReleasable: true,
                cancellationToken);
            var source = await SingleFreeBalanceAsync(parent.Label, cancellationToken);
            if (!OwnedAsPlanned(source, line))
            {
                throw Refused(409, InventoryManufacturingRealizationCodesV1.AllocationOwnerMismatch);
            }

            var active = await ActiveAllocatedKgAsync(parent.Label.Id, source, cancellationToken);
            if (quantity.Value > source.QuantityKg - active)
            {
                throw Refused(409, InventoryManufacturingRealizationCodesV1.AllocationInsufficientFreeStock);
            }

            StockAllocation allocation;
            try
            {
                allocation = StockAllocation.Create(Guid.NewGuid(), Position(source, parent.Label), quantity);
            }
            catch (Exception exception) when (exception is InventoryRuleException or ArgumentException)
            {
                throw Refused(503, InventoryManufacturingRealizationCodesV1.Unavailable);
            }

            var row = new AllocationRow
            {
                Id = allocation.Id,
                CompanyId = companyId,
                OwnerKind = source.OwnerKind,
                OwnerId = source.OwnerId,
                CustodianKind = source.CustodianKind,
                CustodianId = source.CustodianId,
                LocationId = source.LocationId,
                LabelId = parent.Label.Id,
                Block = (int)BlockState.Free,
                DemandKind = InventoryAllocationDemandKindsV1.ManufacturingPlanInput,
                DemandId = request.PlanId,
                DemandLineId = parent.Label.Id,
                DemandProductId = parent.Row.ProductId,
                DemandProductVersion = parent.Row.ProductVersion,
                Reason = request.Reason,
                AllocatedKg = allocation.Allocated.Value,
                RemainingKg = allocation.Remaining.Value,
                Status = (int)allocation.Status,
                Version = allocation.Version,
                CreatedAtUtc = now,
                UpdatedAtUtc = now,
            };
            context.Allocations.Add(row);
            parent.Row.Version = checked(parent.Row.Version + 1);
            parent.Row.UpdatedAtUtc = now;
            var view = new InventoryManufacturingPlanAllocationV1(row.Id, row.LabelId,
                parent.Row.Version.ToString(CultureInfo.InvariantCulture),
                row.Version.ToString(CultureInfo.InvariantCulture), quantity.ToString());
            allocations.Add(view);
            store.AuditManufacturingPlanAllocation(metadata, companyId, row.Id, AllocatedEvent, "Create", null,
                row.Version, "{}", JsonSerializer.Serialize(view, Json), request.Reason);
        }

        var result = new InventoryManufacturingPlanAllocationResultV1(companyId, request.PlanId,
            request.PlanVersionAfter, allocations, IsReplay: false, OriginalCorrelationId: metadata.CorrelationId);
        await SaveCommandAsync(InventoryManufacturingPlanAllocationContractV1.AllocateCommandName, keyHash,
            payloadHash, request.PlanId, result, metadata, cancellationToken);
        return result;
    }

    public async Task<InventoryManufacturingPlanReleaseResultV1> StagePlanRemainderReleaseAsync(
        InventoryManufacturingPlanReleaseRequestV1 request,
        InventoryCommandMetadata metadata,
        CancellationToken cancellationToken = default)
    {
        Begin();
        InventoryManufacturingPlanAllocationContractV1.Validate(request, metadata, companyId);
        var keyHash = Hash(metadata.IdempotencyKey);
        var payloadHash = RequestHash(request);
        if (await FindCommandAsync(InventoryManufacturingPlanAllocationContractV1.ReleaseCommandName, keyHash,
                cancellationToken) is { } existing)
        {
            EnsureReplay(existing, metadata, payloadHash);
            var replay = JsonSerializer.Deserialize<InventoryManufacturingPlanReleaseResultV1>(existing.ResultJson,
                Json) ?? throw Refused(503, InventoryManufacturingRealizationCodesV1.Unavailable);
            return replay with { IsReplay = true, OriginalCorrelationId = existing.CorrelationId };
        }

        var reasonCode = InventoryManufacturingPlanAllocationContractV1.ReleaseReasonCode(request.Reason);
        var rows = await context.Allocations
            .Where(row => row.DemandKind == InventoryAllocationDemandKindsV1.ManufacturingPlanInput
                && row.DemandId == request.PlanId && row.Status == (int)StockAllocationStatus.Active)
            .OrderBy(row => row.LabelId)
            .ToListAsync(cancellationToken);
        var now = clock.GetUtcNow();
        var released = new List<InventoryManufacturingPlanReleasedAllocationV1>(rows.Count);
        foreach (var row in rows)
        {
            var label = await context.Labels.SingleOrDefaultAsync(candidate => candidate.Id == row.LabelId,
                cancellationToken) ?? throw Refused(503, InventoryManufacturingRealizationCodesV1.Unavailable);
            var versionBefore = row.Version;
            Kilograms kilograms;
            try
            {
                var allocation = RestoreAllocation(row, await RestoreLabelAsync(label, cancellationToken));
                kilograms = allocation.Release(row.Version);
                row.RemainingKg = allocation.Remaining.Value;
                row.Status = (int)allocation.Status;
                row.Version = allocation.Version;
            }
            catch (Exception exception) when (exception is InventoryRuleException or ArgumentException
                or StockAllocationException or JsonException)
            {
                throw Refused(503, InventoryManufacturingRealizationCodesV1.Unavailable);
            }

            // Server time, as every Sales release and change records it; the caller's time stays in the audit trail.
            row.ReleasedAtUtc = now;
            row.ReleaseReason = reasonCode;
            row.ReleasedKg = kilograms.Value;
            row.UpdatedAtUtc = now;
            label.Version = checked(label.Version + 1);
            label.UpdatedAtUtc = now;
            var view = new InventoryManufacturingPlanReleasedAllocationV1(row.Id, row.LabelId,
                label.Version.ToString(CultureInfo.InvariantCulture), kilograms.ToString());
            released.Add(view);
            store.AuditManufacturingPlanAllocation(metadata, companyId, row.Id, ReleasedEvent, "Release",
                versionBefore, row.Version, "{}", JsonSerializer.Serialize(view, Json), reasonCode);
        }

        var result = new InventoryManufacturingPlanReleaseResultV1(companyId, request.PlanId,
            request.PlanVersionAfter, released, IsReplay: false, OriginalCorrelationId: metadata.CorrelationId);
        await SaveCommandAsync(InventoryManufacturingPlanAllocationContractV1.ReleaseCommandName, keyHash,
            payloadHash, request.PlanId, result, metadata, cancellationToken);
        return result;
    }

    /// <summary>
    /// API-21: an input issue that names its plan-input allocation consumes it. The allocation must be an active
    /// plan input of this exact label and position; the issue may take at most its remaining kg, and the physical
    /// balance must still cover it after every OTHER active allocation of the position.
    /// </summary>
    private async Task<StockPosition> ConsumePlanAllocationAsync(Guid allocationId, Guid planId, ParentLabel parent,
        Kilograms quantity, InventoryCommandMetadata metadata, CancellationToken cancellationToken)
    {
        var row = await context.Allocations.SingleOrDefaultAsync(candidate => candidate.Id == allocationId,
            cancellationToken);
        if (row is null || row.DemandKind != InventoryAllocationDemandKindsV1.ManufacturingPlanInput
            || row.DemandId != planId || row.LabelId != parent.Label.Id || row.Status != (int)StockAllocationStatus.Active
            || row.Block != (int)BlockState.Free)
        {
            throw Refused(409, InventoryManufacturingRealizationCodesV1.AllocationNotUsable);
        }

        var source = await SingleFreeBalanceAsync(parent.Label, cancellationToken);
        if (source.OwnerKind != row.OwnerKind || source.OwnerId != row.OwnerId
            || source.CustodianKind != row.CustodianKind || source.CustodianId != row.CustodianId
            || source.LocationId != row.LocationId)
        {
            throw Refused(409, InventoryManufacturingRealizationCodesV1.AllocationNotUsable);
        }

        if (quantity.Value > row.RemainingKg)
        {
            throw Refused(409, InventoryManufacturingRealizationCodesV1.AllocationExceeded);
        }

        var others = await ActiveAllocatedKgAsync(parent.Label.Id, source, cancellationToken) - row.RemainingKg;
        if (quantity.Value > source.QuantityKg - others)
        {
            throw Refused(409, InventoryManufacturingRealizationCodesV1.PhysicalStockInsufficient);
        }

        var versionBefore = row.Version;
        try
        {
            var allocation = RestoreAllocation(row, parent.Label);
            allocation.Consume(row.Version, parent.Label.Id, quantity);
            row.RemainingKg = allocation.Remaining.Value;
            row.Status = (int)allocation.Status;
            row.Version = allocation.Version;
        }
        catch (Exception exception) when (exception is InventoryRuleException or ArgumentException
            or StockAllocationException)
        {
            throw Refused(503, InventoryManufacturingRealizationCodesV1.Unavailable);
        }

        row.UpdatedAtUtc = clock.GetUtcNow();
        store.AuditManufacturingPlanAllocation(metadata, companyId, row.Id, ConsumedEvent, "ConsumeForInputIssue",
            versionBefore, row.Version, "{}", JsonSerializer.Serialize(new
            {
                allocationId = row.Id,
                planId = row.DemandId,
                labelId = row.LabelId,
                consumedKg = quantity.ToString(),
                remainingKg = InventoryManufacturingPlanAllocationContractV1.Format(row.RemainingKg),
            }, Json), "Üretim girdi çıkışı ve plan tahsisinin tüketimi");
        return Position(source, parent.Label);
    }

    private async Task<BalanceRow> SingleFreeBalanceAsync(Domain.Labels.MaterialLabel label,
        CancellationToken cancellationToken)
    {
        var rows = await context.Balances.Where(row => row.LabelId == label.Id).ToListAsync(cancellationToken);
        ValidateStoredPositions(label, rows);
        var free = rows.Where(row => row.Block == (int)BlockState.Free && row.QuantityKg > 0m).ToArray();
        if (free.Length != 1)
        {
            throw free.Length == 0
                ? Refused(409, InventoryManufacturingRealizationCodesV1.AllocationInsufficientFreeStock)
                : Refused(503, InventoryManufacturingRealizationCodesV1.Unavailable);
        }

        return free[0];
    }

    /// <summary>Active remaining kg of EVERY demand kind on this exact Free position: one free-kg rule for all.</summary>
    private async Task<decimal> ActiveAllocatedKgAsync(Guid labelId, BalanceRow source,
        CancellationToken cancellationToken) => await context.Allocations.AsNoTracking()
        .Where(row => row.LabelId == labelId
            && row.OwnerKind == source.OwnerKind && row.OwnerId == source.OwnerId
            && row.CustodianKind == source.CustodianKind && row.CustodianId == source.CustodianId
            && row.LocationId == source.LocationId && row.Block == (int)BlockState.Free
            && row.Status == (int)StockAllocationStatus.Active)
        .SumAsync(row => (decimal?)row.RemainingKg, cancellationToken) ?? 0m;

    /// <summary>
    /// URT-D22: custody is always the company's. Company stock serves a company input; customer-owned stock serves only
    /// the input the plan names for that same customer. Manufacturing has already verified the fason order's customer.
    /// </summary>
    private bool OwnedAsPlanned(BalanceRow source, InventoryManufacturingPlanInputLineV1 line)
    {
        if (source.CustodianKind != (int)StockPartyKind.Company || source.CustodianId != companyId)
        {
            return false;
        }

        return line.Ownership switch
        {
            InventoryManufacturingPlanInputOwnershipV1.Company =>
                source.OwnerKind == (int)StockPartyKind.Company && source.OwnerId == companyId,
            InventoryManufacturingPlanInputOwnershipV1.CustomerOwned =>
                source.OwnerKind == (int)StockPartyKind.Counterparty && source.OwnerId == line.CustomerPartyId,
            _ => false,
        };
    }

    private StockAllocation RestoreAllocation(AllocationRow row, Domain.Labels.MaterialLabel label) =>
        StockAllocation.Restore(row.Id,
            new StockPosition(companyId,
                new StockOwner((StockPartyKind)row.OwnerKind, row.OwnerId),
                new StockCustodian((StockPartyKind)row.CustodianKind, row.CustodianId),
                row.LocationId, label, (BlockState)row.Block),
            Kilograms.Positive(row.AllocatedKg), Kilograms.Of(row.RemainingKg), row.Version,
            (StockAllocationStatus)row.Status);
}
