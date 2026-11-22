using System.Text.Json;
using GoymenERP.Modules.Inventory.Application;
using GoymenERP.Modules.Inventory.Domain.Allocations;
using GoymenERP.Modules.Inventory.Domain.Labels;
using GoymenERP.Modules.Inventory.Domain.Quantities;
using GoymenERP.Modules.Inventory.Domain.Stock;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;

namespace GoymenERP.Modules.Inventory.Infrastructure.Persistence;

/// <summary>
/// INV-AL-1 change of one active physical sales allocation (Master v3 §25.4 "Tahsisi görüntüle/revize et/serbest
/// bırak", AT-063 "Tahsis Revize"). It is composed only of the two decided allocation operations — release
/// (DEC-STK-005: explicit, never silent) and reservation of free stock (DEC-STK-003/004: atomic, idempotent, never
/// above free) — in ONE save of Inventory's own rows:
/// <list type="bullet">
/// <item><b>Resize</b> (target = the allocation's own label): in place, same allocation id, so a reference bound to it
/// (a loading instruction line) stays valid; the consumed part is kept; an increase must fit the position's
/// unreserved free kilograms.</item>
/// <item><b>Move</b> (target = another label of the same product): the source is released with the change reason and
/// a new allocation reserves the target position for the same demand line.</item>
/// </list>
/// Both bump every touched label version (the stock-count and label-transformation fence), write one append-only
/// <see cref="AllocationChangeRow"/>, audit and one ADR-011 command row. A new reservation part is the same as a create:
/// the approved Sales line (host), the Sales ownership gate and the label version are all required again.
/// </summary>
public sealed partial class InventoryStore
{
    internal const string ChangePhysicalAllocationCommand = "inventory.physical-allocation.change.v1";

    private const int ResizeChangeKind = 1;
    private const int MoveChangeKind = 2;

    public async Task<InventoryPhysicalAllocationChangeResultV1> ChangeAsync(
        Guid allocationId,
        ChangePhysicalSalesAllocationV1 request,
        InventoryCommandMetadata metadata,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        RequireMetadata(metadata);
        RequireAllocationMetadata(metadata);
        RequireResolvedCompany();
        cancellationToken.ThrowIfCancellationRequested();

        if (allocationId == Guid.Empty || request.SalesOrderId == Guid.Empty || request.SalesOrderLineId == Guid.Empty
            || request.ProductId == Guid.Empty || request.TargetLabelId == Guid.Empty
            || request.NewAllocationId is { } requested && (requested == Guid.Empty || requested == allocationId)
            || !TryVersion(request.ExpectedAllocationVersion, out var expectedVersion)
            || !TryVersion(request.ExpectedTargetLabelVersion, out var expectedTargetLabelVersion)
            || string.IsNullOrWhiteSpace(request.ProductVersion) || request.ProductVersion.Length > 128
            || string.IsNullOrWhiteSpace(request.Reason) || request.Reason.Length > 1000
            || request.PieceCount is <= 0
            || !TryQuantity(request.QuantityKg, out var quantity))
        {
            throw AllocationError(400, InventoryAllocationCodesV1.ValidationFailed);
        }

        var companyId = companyContext.CompanyId;
        var keyHash = AllocationKeyHash(metadata.IdempotencyKey);
        var payloadHash = WithPieces(AllocationPayloadHash(new
        {
            allocationId,
            expectedVersion,
            request.SalesOrderId,
            request.SalesOrderLineId,
            request.ProductId,
            request.ProductVersion,
            request.TargetLabelId,
            expectedTargetLabelVersion,
            quantityKg = quantity.ToString(),
            request.Reason,
            request.NewAllocationId,
        }), request.PieceCount);
        if (await FindChangeReplayAsync(keyHash, payloadHash, metadata.ActorId, cancellationToken) is { } replay)
        {
            return replay;
        }

        var row = await dbContext.Allocations.SingleOrDefaultAsync(candidate => candidate.Id == allocationId, cancellationToken)
            ?? throw AllocationError(404, InventoryAllocationCodesV1.InvalidReference);
        // The allocation must be the demand the host proved approved: same order, line and product.
        if (!string.Equals(row.DemandKind, InventoryAllocationDemandKindsV1.SalesOrderLine, StringComparison.Ordinal)
            || row.DemandId != request.SalesOrderId || row.DemandLineId != request.SalesOrderLineId
            || row.DemandProductId != request.ProductId)
        {
            throw AllocationError(422, InventoryAllocationCodesV1.InvalidReference);
        }

        var isMove = request.TargetLabelId != row.LabelId;
        if (isMove != request.NewAllocationId.HasValue)
        {
            // A resize keeps its identity (no new id); a move must name the new allocation's id.
            throw AllocationError(400, InventoryAllocationCodesV1.ValidationFailed);
        }

        var sourceLabel = await dbContext.Labels.SingleOrDefaultAsync(candidate => candidate.Id == row.LabelId, cancellationToken)
            ?? throw new InvalidOperationException("Kayıtlı tahsisin etiketi bulunamadı.");
        var targetLabel = isMove
            ? await dbContext.Labels.SingleOrDefaultAsync(candidate => candidate.Id == request.TargetLabelId, cancellationToken)
                ?? throw AllocationError(404, InventoryAllocationCodesV1.InvalidReference)
            : sourceLabel;

        var allocation = Restore(row, PositionOf(row, sourceLabel, companyId));
        if (allocation.Version != expectedVersion)
        {
            throw AllocationError(409, InventoryAllocationCodesV1.AllocationVersionConflict);
        }

        if (allocation.Status != StockAllocationStatus.Active)
        {
            throw AllocationError(409, InventoryAllocationCodesV1.AllocationStateConflict);
        }

        if (targetLabel.Version != expectedTargetLabelVersion)
        {
            throw AllocationError(409, InventoryAllocationCodesV1.LabelVersionConflict);
        }

        // KLN-D39: resizing a substitute ("muadil") allocation keeps its verified label; a move always targets a label of
        // the line's own product (a new substitute is a new confirmed create, re-verified against the tolerance).
        if (targetLabel.ProductId != request.ProductId && !(row.IsSubstitute && !isMove))
        {
            throw AllocationError(422, InventoryAllocationCodesV1.InvalidReference);
        }

        if (!isMove && quantity.Value == allocation.Remaining.Value
            && request.PieceCount == allocation.RemainingPieces?.Value)
        {
            throw AllocationError(422, InventoryAllocationCodesV1.ChangeUnchanged);
        }

        var now = timeProvider.GetUtcNow();
        var before = AllocationView(row);
        var sourceRemaining = row.RemainingKg;
        InventoryPhysicalAllocationChangeViewV1 change;
        AllocationChangeRow history;
        if (isMove)
        {
            var (target, targetPieces) = await ReservableTargetAsync(targetLabel, companyId, quantity, request.PieceCount,
                cancellationToken);
            allocation.Release(expectedVersion);
            row.RemainingKg = allocation.Remaining.Value;
            row.RemainingPieces = allocation.RemainingPieces?.Value;
            row.Status = (int)allocation.Status;
            row.Version = allocation.Version;
            row.UpdatedAtUtc = now;
            row.ReleasedAtUtc = now;
            row.ReleaseReason = request.Reason;

            var identity = new MaterialLabel(targetLabel.Id, targetLabel.CompanyId, targetLabel.Code, targetLabel.ProductId,
                targetLabel.LotId, targetLabel.ProductVersion);
            var position = new StockPosition(companyId,
                new StockOwner((StockPartyKind)target.OwnerKind, target.OwnerId),
                new StockCustodian((StockPartyKind)target.CustodianKind, target.CustodianId),
                target.LocationId, identity, BlockState.Free);
            var created = StockAllocation.Create(request.NewAllocationId!.Value, position, quantity, targetPieces);
            var next = new AllocationRow
            {
                Id = created.Id,
                CompanyId = companyId,
                OwnerKind = (int)position.Owner.Kind,
                OwnerId = position.Owner.Id,
                CustodianKind = (int)position.Custodian.Kind,
                CustodianId = position.Custodian.Id,
                LocationId = position.LocationId,
                LabelId = position.LabelId,
                Block = (int)position.Block,
                DemandKind = InventoryAllocationDemandKindsV1.SalesOrderLine,
                DemandId = request.SalesOrderId,
                DemandLineId = request.SalesOrderLineId,
                DemandProductId = request.ProductId,
                DemandProductVersion = request.ProductVersion,
                Reason = request.Reason,
                ReservedUntilUtc = row.ReservedUntilUtc,
                AllocatedKg = created.Allocated.Value,
                RemainingKg = created.Remaining.Value,
                AllocatedPieces = created.AllocatedPieces?.Value,
                RemainingPieces = created.RemainingPieces?.Value,
                Status = (int)created.Status,
                Version = created.Version,
                CreatedAtUtc = now,
                UpdatedAtUtc = now,
            };
            dbContext.Allocations.Add(next);
            BumpLabel(sourceLabel, now);
            BumpLabel(targetLabel, now);
            var replaced = AllocationView(row);
            var current = AllocationView(next);
            change = new InventoryPhysicalAllocationChangeViewV1(InventoryAllocationChangeKindsV1.Move, current, replaced);
            history = ChangeRow(companyId, MoveChangeKind, row, expectedVersion, sourceRemaining, next.Id, next.LabelId,
                next.RemainingKg, request.Reason, metadata, now);
            Audit(metadata, companyId, "Inventory.StockAllocation", row.Id,
                "inventory.physical-allocation.moved.v1", "Change", expectedVersion, row.Version,
                JsonSerializer.Serialize(before, Json), JsonSerializer.Serialize(replaced, Json), request.Reason);
            Audit(metadata, companyId, "Inventory.StockAllocation", next.Id,
                "inventory.physical-allocation.created.v1", "Create", null, 1, "{}",
                JsonSerializer.Serialize(current, Json), request.Reason);
        }
        else
        {
            if (quantity.Value > allocation.Remaining.Value)
            {
                // The increase is a new reservation part: the same gate and free-stock rule as a create.
                await RequireReservableIncreaseAsync(row, companyId, quantity.Value - allocation.Remaining.Value,
                    cancellationToken);
            }

            // KLN-D119: the pieces the allocation holds afterwards are a reservation of its own position again.
            var heldPieces = await ResizedPiecesAsync(row, allocation, quantity, request.PieceCount, cancellationToken);
            try
            {
                allocation.Resize(expectedVersion, quantity, heldPieces);
            }
            catch (StockAllocationException exception)
            {
                throw exception.Failure switch
                {
                    StockAllocationFailure.VersionConflict => AllocationError(409, InventoryAllocationCodesV1.AllocationVersionConflict),
                    StockAllocationFailure.StateConflict => AllocationError(409, InventoryAllocationCodesV1.AllocationStateConflict),
                    StockAllocationFailure.Unchanged => AllocationError(422, InventoryAllocationCodesV1.ChangeUnchanged),
                    StockAllocationFailure.PiecesRequired or StockAllocationFailure.PiecesNotTracked
                        or StockAllocationFailure.PiecesInsufficient or StockAllocationFailure.PiecesInvalid => PieceError(exception),
                    _ => new InvalidOperationException("Tahsis değişikliği reddi eşlenemedi.", exception),
                };
            }

            row.AllocatedKg = allocation.Allocated.Value;
            row.RemainingKg = allocation.Remaining.Value;
            row.AllocatedPieces = allocation.AllocatedPieces?.Value;
            row.RemainingPieces = allocation.RemainingPieces?.Value;
            row.Version = allocation.Version;
            row.UpdatedAtUtc = now;
            BumpLabel(sourceLabel, now);
            var current = AllocationView(row);
            change = new InventoryPhysicalAllocationChangeViewV1(InventoryAllocationChangeKindsV1.Resize, current, null);
            history = ChangeRow(companyId, ResizeChangeKind, row, expectedVersion, sourceRemaining, row.Id, row.LabelId,
                row.RemainingKg, request.Reason, metadata, now);
            Audit(metadata, companyId, "Inventory.StockAllocation", row.Id,
                "inventory.physical-allocation.resized.v1", "Change", expectedVersion, row.Version,
                JsonSerializer.Serialize(before, Json), JsonSerializer.Serialize(current, Json), request.Reason);
        }

        dbContext.AllocationChanges.Add(history);
        dbContext.Commands.Add(new InventoryCommandRow
        {
            CompanyId = companyId,
            CommandName = ChangePhysicalAllocationCommand,
            IdempotencyKeyHash = keyHash,
            ActorId = metadata.ActorId,
            PayloadHash = payloadHash,
            TargetId = row.Id,
            ResultJson = JsonSerializer.Serialize(new AllocationChangeCommandResult(change), Json),
            CorrelationId = metadata.CorrelationId.Trim(),
            RecordedAtUtc = now,
        });

        try
        {
            await dbContext.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException exception) when (exception is DbUpdateConcurrencyException
            || exception.InnerException is SqlException { Number: 2601 or 2627 })
        {
            dbContext.ChangeTracker.Clear();
            if (await FindChangeReplayAsync(keyHash, payloadHash, metadata.ActorId, cancellationToken) is { } winner)
            {
                return winner;
            }

            throw AllocationError(409, InventoryAllocationCodesV1.ConcurrentChange, exception);
        }

        return new InventoryPhysicalAllocationChangeResultV1(change, false, metadata.CorrelationId.Trim());
    }

    /// <summary>The single Free position of the target label, owned and held by this company, with room for the quantity.</summary>
    private async Task<(BalanceRow Target, PieceCount? Pieces)> ReservableTargetAsync(
        LabelRow label,
        Guid companyId,
        Kilograms quantity,
        int? requestedPieces,
        CancellationToken cancellationToken)
    {
        var freeRows = await dbContext.Balances
            .Where(candidate => candidate.LabelId == label.Id && candidate.Block == (int)BlockState.Free && candidate.QuantityKg > 0m)
            .ToListAsync(cancellationToken);
        if (freeRows.Count != 1)
        {
            throw AllocationError(freeRows.Count == 0 ? 409 : 503,
                freeRows.Count == 0 ? InventoryAllocationCodesV1.InsufficientFreeStock : InventoryAllocationCodesV1.InvalidReference);
        }

        var target = freeRows[0];
        // TODO(#66/5830378162): the same Sales ownership gate as a create, until the customer-owned/fason rule is decided.
        if (!InventorySalesOwnershipGuardV1.IsCompanyOwnedAndHeld((StockPartyKind)target.OwnerKind, target.OwnerId,
                (StockPartyKind)target.CustodianKind, target.CustodianId, companyId))
        {
            throw AllocationError(422, InventoryAllocationCodesV1.OwnerNotCompany);
        }

        var active = await ActiveAllocatedAtAsync(target, exceptAllocationId: null, cancellationToken);
        if (quantity.Value > target.QuantityKg - active.Kilograms)
        {
            throw AllocationError(409, InventoryAllocationCodesV1.InsufficientFreeStock);
        }

        return (target, ReservablePieces(quantity, requestedPieces, target, active));
    }

    /// <summary>
    /// KLN-D119: the pieces an in-place change leaves on the allocation. An allocation that holds pieces re-reserves them
    /// against its own Free position (every other active reservation deducted); one without pieces never gains them.
    /// </summary>
    private async Task<PieceCount?> ResizedPiecesAsync(AllocationRow row, StockAllocation allocation, Kilograms quantity,
        int? requestedPieces, CancellationToken cancellationToken)
    {
        if (!allocation.TracksPieces)
        {
            return requestedPieces is null
                ? null
                : throw AllocationError(422, InventoryAllocationCodesV1.PieceCountNotTracked);
        }

        if (requestedPieces is null)
        {
            throw AllocationError(422, InventoryAllocationCodesV1.PieceCountRequired);
        }

        var free = await dbContext.Balances
            .Where(candidate => candidate.LabelId == row.LabelId && candidate.Block == (int)BlockState.Free
                && candidate.OwnerKind == row.OwnerKind && candidate.OwnerId == row.OwnerId
                && candidate.CustodianKind == row.CustodianKind && candidate.CustodianId == row.CustodianId
                && candidate.LocationId == row.LocationId)
            .ToListAsync(cancellationToken);
        if (free.Count != 1 || free[0].PieceCount is null)
        {
            // The allocation holds pieces its position no longer counts: nothing can be re-stated safely.
            throw AllocationError(free.Count > 1 ? 503 : 409,
                free.Count > 1 ? InventoryAllocationCodesV1.InvalidReference : InventoryAllocationCodesV1.InsufficientFreePieces);
        }

        var others = await ActiveAllocatedAtAsync(free[0], row.Id, cancellationToken);
        return ReservablePieces(quantity, requestedPieces, free[0], others);
    }

    /// <summary>An in-place increase: the allocation's own Free position must still be the company's and cover the extra kg.</summary>
    private async Task RequireReservableIncreaseAsync(
        AllocationRow row,
        Guid companyId,
        decimal increase,
        CancellationToken cancellationToken)
    {
        var free = await dbContext.Balances
            .Where(candidate => candidate.LabelId == row.LabelId && candidate.Block == (int)BlockState.Free
                && candidate.OwnerKind == row.OwnerKind && candidate.OwnerId == row.OwnerId
                && candidate.CustodianKind == row.CustodianKind && candidate.CustodianId == row.CustodianId
                && candidate.LocationId == row.LocationId)
            .ToListAsync(cancellationToken);
        if (free.Count > 1)
        {
            throw AllocationError(503, InventoryAllocationCodesV1.InvalidReference);
        }

        if (!InventorySalesOwnershipGuardV1.IsCompanyOwnedAndHeld((StockPartyKind)row.OwnerKind, row.OwnerId,
                (StockPartyKind)row.CustodianKind, row.CustodianId, companyId))
        {
            throw AllocationError(422, InventoryAllocationCodesV1.OwnerNotCompany);
        }

        var physical = free.Count == 0 ? 0m : free[0].QuantityKg;
        var others = (await ActiveAllocatedAtAsync(row.LabelId, row.OwnerKind, row.OwnerId, row.CustodianKind,
            row.CustodianId, row.LocationId, row.Id, cancellationToken)).Kilograms;
        // The position must hold this allocation's current remainder, every other active remainder AND the increase.
        if (row.RemainingKg + increase > physical - others)
        {
            throw AllocationError(409, InventoryAllocationCodesV1.InsufficientFreeStock);
        }
    }

    private async Task<ActiveAllocated> ActiveAllocatedAtAsync(
        Guid labelId,
        int ownerKind,
        Guid ownerId,
        int custodianKind,
        Guid custodianId,
        Guid locationId,
        Guid? exceptAllocationId,
        CancellationToken cancellationToken)
    {
        var held = await dbContext.Allocations.AsNoTracking()
            .Where(candidate => candidate.LabelId == labelId
                && candidate.OwnerKind == ownerKind && candidate.OwnerId == ownerId
                && candidate.CustodianKind == custodianKind && candidate.CustodianId == custodianId
                && candidate.LocationId == locationId && candidate.Block == (int)BlockState.Free
                && candidate.Status == (int)StockAllocationStatus.Active
                && (exceptAllocationId == null || candidate.Id != exceptAllocationId))
            .Select(candidate => new { candidate.RemainingKg, candidate.RemainingPieces })
            .ToListAsync(cancellationToken);
        return new ActiveAllocated(held.Sum(candidate => candidate.RemainingKg),
            held.Sum(candidate => candidate.RemainingPieces ?? 0), held.All(candidate => candidate.RemainingPieces is not null));
    }

    private static StockPosition PositionOf(AllocationRow row, LabelRow label, Guid companyId) =>
        new StockPosition(companyId,
            new StockOwner((StockPartyKind)row.OwnerKind, row.OwnerId),
            new StockCustodian((StockPartyKind)row.CustodianKind, row.CustodianId),
            row.LocationId,
            new MaterialLabel(label.Id, label.CompanyId, label.Code, label.ProductId, label.LotId,
                label.ProductVersion),
            (BlockState)row.Block);

    private static void BumpLabel(LabelRow label, DateTimeOffset now)
    {
        label.Version = checked(label.Version + 1);
        label.UpdatedAtUtc = now;
    }

    private static AllocationChangeRow ChangeRow(
        Guid companyId,
        int kind,
        AllocationRow source,
        long sourceVersion,
        decimal sourceRemainingKg,
        Guid targetAllocationId,
        Guid targetLabelId,
        decimal targetRemainingKg,
        string reason,
        InventoryCommandMetadata metadata,
        DateTimeOffset now) => new AllocationChangeRow
        {
            Id = Guid.NewGuid(),
            CompanyId = companyId,
            Kind = kind,
            SourceAllocationId = source.Id,
            SourceVersion = sourceVersion,
            SourceLabelId = source.LabelId,
            SourceRemainingKg = sourceRemainingKg,
            TargetAllocationId = targetAllocationId,
            TargetLabelId = targetLabelId,
            TargetRemainingKg = targetRemainingKg,
            Reason = reason,
            ChangedBy = metadata.ActorId,
            CorrelationId = metadata.CorrelationId.Trim(),
            ChangedAtUtc = now,
        };

    private async Task<InventoryPhysicalAllocationChangeResultV1?> FindChangeReplayAsync(
        string keyHash,
        string payloadHash,
        string actorId,
        CancellationToken cancellationToken)
    {
        var prior = await dbContext.Commands.AsNoTracking()
            .SingleOrDefaultAsync(candidate => candidate.CommandName == ChangePhysicalAllocationCommand
                && candidate.IdempotencyKeyHash == keyHash, cancellationToken);
        if (prior is null)
        {
            return null;
        }

        if (!string.Equals(prior.PayloadHash, payloadHash, StringComparison.Ordinal)
            || !string.Equals(prior.ActorId, actorId, StringComparison.Ordinal))
        {
            throw AllocationError(409, InventoryAllocationCodesV1.IdempotencyConflict);
        }

        var stored = JsonSerializer.Deserialize<AllocationChangeCommandResult>(prior.ResultJson, Json)
            ?? throw new InvalidOperationException("Kayıtlı tahsis değişikliği sonucu okunamadı.");
        return new InventoryPhysicalAllocationChangeResultV1(stored.Change, true, prior.CorrelationId);
    }

    private sealed record AllocationChangeCommandResult(InventoryPhysicalAllocationChangeViewV1 Change);
}
