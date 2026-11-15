using System.Globalization;
using System.Text.Json;
using GoymenERP.Modules.Inventory.Application;
using GoymenERP.Modules.Inventory.Domain;
using GoymenERP.Modules.Inventory.Domain.Transfers;
using Microsoft.EntityFrameworkCore;

namespace GoymenERP.Modules.Inventory.Infrastructure.Persistence;

/// <summary>
/// B-22b (KLN-D20/D25/D27) dispatch cancellation persistence. The window decision and the owner confirmations are made
/// by the command service; the store keeps the final closure (moment and basis, insert-only), records the terminal
/// "window closed" refusal under the key, and writes the reverse movement, the cancellation row, the transfer state,
/// command replay and audit in one Inventory save. The dispatch row and its movement are never changed.
/// </summary>
public sealed partial class InventoryStore
{
    public async Task<InventoryTransferResultV1?> ReplayCancelDispatchAsync(
        Guid transferId,
        CancelInventoryTransferDispatchV1 request,
        InventoryCommandMetadata metadata,
        Func<CancellationToken, Task> freshAuthorityFence,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(freshAuthorityFence);
        RequireTransferCall(metadata);
        var (_, _, payloadHash) = CancelDispatchPayload(transferId, request);
        return await FindTransferKeyReplayAsync(CancelDispatchTransferCommand,
            GenealogyHash(metadata.IdempotencyKey.Trim()), payloadHash, metadata.ActorId, freshAuthorityFence,
            cancellationToken);
    }

    public async Task<InventoryTransferDispatchCancelContextV1?> ReadDispatchCancelContextAsync(
        Guid transferId,
        CancellationToken cancellationToken = default)
    {
        RequireResolvedCompany();
        if (transferId == Guid.Empty)
        {
            throw TransferError(400, InventoryTransferCodesV1.ValidationFailed);
        }

        var row = await dbContext.Transfers.AsNoTracking()
            .SingleOrDefaultAsync(candidate => candidate.Id == transferId, cancellationToken);
        if (row is null)
        {
            return null;
        }

        var dispatch = await dbContext.TransferDispatches.AsNoTracking()
            .SingleOrDefaultAsync(candidate => candidate.TransferId == transferId, cancellationToken);
        var closure = await dbContext.TransferDispatchCancelClosures.AsNoTracking()
            .SingleOrDefaultAsync(candidate => candidate.TransferId == transferId, cancellationToken);
        InventoryTransferSourceScaleSetting? storedSetting = dispatch is null
            ? null
            : (InventoryTransferSourceScaleSetting)dispatch.SourceScaleSettingAtDispatch;
        InventoryTransferCancelClosureBasis? closureBasis = closure is null
            ? null
            : (InventoryTransferCancelClosureBasis)closure.Basis;
        DateTimeOffset? closedAtUtc = closure?.ClosedAtUtc;
        return new InventoryTransferDispatchCancelContextV1(row.Id, row.CompanyId, row.Version,
            (InventoryTransferStatus)row.Status, row.SourceLocationId, dispatch?.DispatchedAtUtc, storedSetting,
            closureBasis, closedAtUtc);
    }

    public async Task<InventoryTransferResultV1?> RecordDispatchCancelClosureAsync(
        Guid transferId,
        CancelInventoryTransferDispatchV1 request,
        InventoryTransferCancelClosureBasis basis,
        DateTimeOffset closedAtUtc,
        InventoryCommandMetadata metadata,
        Func<CancellationToken, Task> freshAuthorityFence,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(freshAuthorityFence);
        RequireTransferCall(metadata);
        if (!Enum.IsDefined(basis) || closedAtUtc.Offset != TimeSpan.Zero)
        {
            // A closure comes only from the evaluated window; an undefined basis is a composition fault.
            throw TransferError(503, InventoryTransferCodesV1.Unavailable);
        }

        var (_, _, payloadHash) = CancelDispatchPayload(transferId, request);
        var keyHash = GenealogyHash(metadata.IdempotencyKey.Trim());
        if (await FindTransferKeyReplayAsync(CancelDispatchTransferCommand, keyHash, payloadHash, metadata.ActorId,
                freshAuthorityFence, cancellationToken) is { } winner)
        {
            return winner;
        }

        var row = await dbContext.Transfers.AsNoTracking()
            .SingleOrDefaultAsync(candidate => candidate.Id == transferId, cancellationToken);
        var dispatch = row is null ? null : await dbContext.TransferDispatches.AsNoTracking()
            .SingleOrDefaultAsync(candidate => candidate.TransferId == transferId, cancellationToken);
        var existing = row is null ? null : await dbContext.TransferDispatchCancelClosures.AsNoTracking()
            .SingleOrDefaultAsync(candidate => candidate.TransferId == transferId, cancellationToken);
        await RequireFreshAuthorityAsync(freshAuthorityFence, cancellationToken);
        if (row is null)
        {
            throw TransferError(404, InventoryTransferCodesV1.TransferNotFound);
        }

        if (dispatch is null)
        {
            throw TransferError(409, InventoryTransferCodesV1.TransferStateConflict);
        }

        var now = timeProvider.GetUtcNow();
        // KLN-D25 (7)(g) / KLN-D27 (5): the first closure is final; a later reading never moves its moment or basis.
        var kept = existing is null ? basis : (InventoryTransferCancelClosureBasis)existing.Basis;
        var keptAtUtc = existing?.ClosedAtUtc ?? closedAtUtc;
        if (existing is null)
        {
            dbContext.TransferDispatchCancelClosures.Add(new InventoryTransferDispatchCancelClosureRow
            {
                CompanyId = row.CompanyId,
                TransferId = row.Id,
                Basis = (int)basis,
                ClosedAtUtc = closedAtUtc,
                RecordedAtUtc = now,
                RecordedBy = metadata.ActorId.Trim(),
                CorrelationId = metadata.CorrelationId.Trim(),
            });
        }

        dbContext.Commands.Add(new InventoryCommandRow
        {
            CompanyId = companyContext.CompanyId,
            CommandName = CancelDispatchTransferCommand,
            IdempotencyKeyHash = keyHash,
            ActorId = metadata.ActorId.Trim(),
            PayloadHash = payloadHash,
            TargetId = transferId,
            ResultJson = JsonSerializer.Serialize(
                new TransferCommandResult(null, 409, InventoryTransferCodesV1.CancelWindowClosed), Json),
            CorrelationId = metadata.CorrelationId.Trim(),
            RecordedAtUtc = now,
        });
        var state = JsonSerializer.Serialize(new { transferId, version = row.Version, status = row.Status }, Json);
        Audit(metadata, row.CompanyId, TransferEntityType, row.Id, "inventory.transfer.dispatch-cancel-refused.v1",
            "DispatchCancelRefused", row.Version, row.Version, state,
            JsonSerializer.Serialize(new
            {
                status = 409,
                code = InventoryTransferCodesV1.CancelWindowClosed,
                closureBasis = kept.ToString(),
                closedAtUtc = keptAtUtc.UtcDateTime.ToString("O", CultureInfo.InvariantCulture),
                closureRecordedNow = existing is null,
            }, Json),
            "Transfer çıkış iptali reddedildi");
        await RequireFreshAuthorityAsync(freshAuthorityFence, cancellationToken);
        try
        {
            await dbContext.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException exception) when (exception is DbUpdateConcurrencyException
            || IsUniqueConstraintViolation(exception))
        {
            dbContext.ChangeTracker.Clear();
            // Same key raced (its answer wins), or another key closed the window first (that closure is final, and a
            // retry of this key records only its own refusal over it).
            if (await FindTransferKeyReplayAsync(CancelDispatchTransferCommand, keyHash, payloadHash, metadata.ActorId,
                    freshAuthorityFence, cancellationToken) is { } raced)
            {
                return raced;
            }

            await RequireFreshAuthorityAsync(freshAuthorityFence, cancellationToken);
            throw TransferError(409, InventoryTransferCodesV1.ConcurrentChange, exception);
        }

        return null;
    }

    public async Task<InventoryTransferResultV1> CancelDispatchAsync(
        Guid transferId,
        CancelInventoryTransferDispatchV1 request,
        long? logisticsNoExitConfirmationVersion,
        long? eDispatchCancelConfirmationVersion,
        InventoryCommandMetadata metadata,
        Func<CancellationToken, Task> freshAuthorityFence,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(freshAuthorityFence);
        RequireTransferCall(metadata);
        if (logisticsNoExitConfirmationVersion is < 1 || eDispatchCancelConfirmationVersion is < 1)
        {
            // Only a positive owner confirmation version is stored; anything else is a composition fault.
            throw TransferError(503, InventoryTransferCodesV1.Unavailable);
        }

        var (expectedVersion, reason, payloadHash) = CancelDispatchPayload(transferId, request);
        var keyHash = GenealogyHash(metadata.IdempotencyKey.Trim());
        if (await FindTransferKeyReplayAsync(CancelDispatchTransferCommand, keyHash, payloadHash, metadata.ActorId,
                freshAuthorityFence, cancellationToken) is { } keyReplay)
        {
            return keyReplay;
        }

        if (await dbContext.TransferDispatchCancellations.AsNoTracking()
                .SingleOrDefaultAsync(candidate => candidate.TransferId == transferId, cancellationToken) is { } prior)
        {
            var priorView = await TransferViewAsync(transferId, cancellationToken);
            await RequireFreshAuthorityAsync(freshAuthorityFence, cancellationToken);
            if (!string.Equals(prior.PayloadHash, payloadHash, StringComparison.Ordinal))
            {
                throw TransferError(409, InventoryTransferCodesV1.TransferStateConflict);
            }

            return new(priorView, true, prior.CorrelationId);
        }

        var row = await dbContext.Transfers.SingleOrDefaultAsync(candidate => candidate.Id == transferId, cancellationToken);
        if (row is null)
        {
            await RequireFreshAuthorityAsync(freshAuthorityFence, cancellationToken);
            throw TransferError(404, InventoryTransferCodesV1.TransferNotFound);
        }
        await RequireFreshAuthorityAsync(freshAuthorityFence, cancellationToken);
        if (row.Version != expectedVersion)
        {
            throw TransferError(409, InventoryTransferCodesV1.TransferVersionConflict);
        }

        var closed = await dbContext.TransferDispatchCancelClosures.AsNoTracking()
            .AnyAsync(candidate => candidate.TransferId == transferId, cancellationToken);
        var dispatch = await dbContext.TransferDispatches.AsNoTracking()
            .SingleOrDefaultAsync(candidate => candidate.TransferId == transferId, cancellationToken);
        await RequireFreshAuthorityAsync(freshAuthorityFence, cancellationToken);
        if (closed)
        {
            // A persisted closure is final (KLN-D27 (5)); nothing reopens it, whatever the service read meanwhile.
            throw TransferError(409, InventoryTransferCodesV1.CancelWindowClosed);
        }

        if (dispatch is null || row.Status != (int)InventoryTransferStatus.InTransit)
        {
            throw TransferError(409, InventoryTransferCodesV1.TransferStateConflict);
        }

        var lines = await dbContext.TransferLines.AsNoTracking().Where(line => line.TransferId == transferId)
            .OrderBy(line => line.Sequence).ToListAsync(cancellationToken);
        var dispatchLines = await dbContext.TransferDispatchLines.AsNoTracking()
            .Where(candidate => candidate.TransferId == transferId).ToListAsync(cancellationToken);
        var buckets = await dbContext.TransferBuckets.AsNoTracking()
            .Where(candidate => candidate.TransferId == transferId).OrderBy(candidate => candidate.Sequence)
            .ToListAsync(cancellationToken);
        var anyAccepted = await dbContext.TransferAcceptances.AsNoTracking()
            .AnyAsync(candidate => candidate.TransferId == transferId, cancellationToken);
        await RequireFreshAuthorityAsync(freshAuthorityFence, cancellationToken);
        if (anyAccepted)
        {
            // KLN-D20: an accepted line is never reversed by a dispatch cancel.
            throw TransferError(409, InventoryTransferCodesV1.TransferStateConflict);
        }

        if (await HasOpenOrApprovedLossRequestAsync(transferId, cancellationToken))
        {
            // KLN-D23 (c): a written-off line is terminal, and a loss request still waiting for its decision is not
            // overtaken by a dispatch cancel (an implementation reading; a rejected request does not block).
            await RequireFreshAuthorityAsync(freshAuthorityFence, cancellationToken);
            throw TransferError(409, InventoryTransferCodesV1.TransferStateConflict);
        }

        var labelIds = lines.Select(line => line.LabelId).ToArray();
        var labels = await dbContext.Labels.Where(candidate => labelIds.Contains(candidate.Id))
            .ToDictionaryAsync(candidate => candidate.Id, cancellationToken);
        await RequireFreshAuthorityAsync(freshAuthorityFence, cancellationToken);
        if (labels.Count != labelIds.Length || dispatchLines.Count != lines.Count)
        {
            throw TransferError(503, InventoryTransferCodesV1.Unavailable);
        }

        foreach (var line in lines)
        {
            var dispatchLine = dispatchLines.SingleOrDefault(candidate => candidate.LineId == line.LineId);
            if (dispatchLine is null)
            {
                throw TransferError(503, InventoryTransferCodesV1.Unavailable);
            }

            if (labels[line.LabelId].Version != dispatchLine.LabelVersionAfterDispatch)
            {
                // The label moved after dispatch: the stored buckets no longer describe it, so nothing is restored.
                throw TransferError(409, InventoryTransferCodesV1.LabelVersionConflict);
            }
        }

        var aggregate = RestoreTransfer(row, lines, dispatch, dispatchLines, [], buckets, labels);
        InventoryTransferDispatchCancellationEffect effect;
        try
        {
            effect = aggregate.CancelDispatch(expectedVersion, request.CancelledAtUtc);
        }
        catch (InventoryRuleException exception)
        {
            throw TransferError(409, InventoryTransferCodesV1.TransferStateConflict, exception);
        }

        var movement = effect.Movement(Guid.NewGuid(), row.CompanyId);
        await StageTransferMovementAsync(movement, metadata, freshAuthorityFence, cancellationToken);
        // KLN-D64 (2): the dispatch posted nothing to the average-cost ledger, so the cancellation posts nothing either.
        // Once transfer cost posting starts, this save stages AverageCostCalculator.ApplyTransferDispatchCancel at the
        // dispatch unit cost (KLN-D64 (1)) through the ledger writer.
        var now = timeProvider.GetUtcNow();
        var cancellation = new InventoryTransferDispatchCancellationRow
        {
            CompanyId = row.CompanyId,
            TransferId = row.Id,
            MovementId = movement.Id,
            CancelledAtUtc = request.CancelledAtUtc,
            Reason = reason,
            LogisticsConfirmationVersion = logisticsNoExitConfirmationVersion,
            EDispatchConfirmationVersion = eDispatchCancelConfirmationVersion,
            PayloadHash = payloadHash,
            RecordedAtUtc = now,
            RecordedBy = metadata.ActorId.Trim(),
            CorrelationId = metadata.CorrelationId.Trim(),
        };
        dbContext.TransferDispatchCancellations.Add(cancellation);
        var before = TransferView(row, lines, dispatch, dispatchLines, []);
        row.Version = effect.TransferVersion;
        row.Status = (int)aggregate.Status;
        row.UpdatedAtUtc = now;
        var view = TransferView(row, lines, dispatch, dispatchLines, []);
        dbContext.Commands.Add(TransferCommandRow(CancelDispatchTransferCommand, keyHash, payloadHash, metadata, row.Id,
            view));
        Audit(metadata, row.CompanyId, TransferEntityType, row.Id, "inventory.transfer.dispatch-cancelled.v1",
            "CancelDispatch", expectedVersion, row.Version, JsonSerializer.Serialize(before, Json),
            JsonSerializer.Serialize(new
            {
                transfer = view,
                movementId = movement.Id,
                reason,
                logisticsNoExitConfirmationVersion,
                eDispatchCancelConfirmationVersion,
            }, Json),
            "Transfer çıkışı iptal edildi");
        return await SaveTransferAsync(view, cancellation.CorrelationId, metadata, CancelDispatchTransferCommand,
            keyHash, payloadHash, freshAuthorityFence, cancellationToken);
    }

    private static (long ExpectedVersion, string Reason, string PayloadHash) CancelDispatchPayload(
        Guid transferId,
        CancelInventoryTransferDispatchV1 request)
    {
        var reason = CancelInventoryTransferDispatchV1.NormalizeReason(request.Reason);
        if (transferId == Guid.Empty || !TryVersion(request.ExpectedTransferVersion, out var expectedVersion)
            || request.CancelledAtUtc.Offset != TimeSpan.Zero || reason is null)
        {
            throw TransferError(400, InventoryTransferCodesV1.ValidationFailed);
        }

        var payloadHash = GenealogyPayloadHash(new
        {
            transferId,
            expectedVersion,
            cancelledAtUtc = request.CancelledAtUtc.UtcDateTime.ToString("O", CultureInfo.InvariantCulture),
            reason,
        });
        return (expectedVersion, reason, payloadHash);
    }
}
