using System.Globalization;
using System.Text.Json;
using GoymenERP.Modules.Inventory.Application;
using GoymenERP.Modules.Inventory.Domain;
using GoymenERP.Modules.Inventory.Domain.Quantities;
using GoymenERP.Modules.Inventory.Domain.Transfers;
using Microsoft.EntityFrameworkCore;

namespace GoymenERP.Modules.Inventory.Infrastructure.Persistence;

/// <summary>
/// B-22c receipt persistence (KLN-D21/KLN-D23/KLN-D24). A receipt accepts the whole dispatched label at the
/// destination and, when short, stages the separate automatic fire movement in the same save. An excess and a transit
/// loss each wait for a decision by a person other than the recorder; the maker≠approver comparison uses the stored
/// recorder and the freshly authorized actor. Rows, movements, balances, label versions, the transfer row, command
/// replay and audit share one Inventory save.
/// </summary>
public sealed partial class InventoryStore : IInventoryTransferReceiptWriterV1, IInventoryTransferReceiptReaderV1
{
    internal const string ReceiveTransferLineCommand = "inventory.transfer.receive-line.v1";
    internal const string DecideTransferOverageCommand = "inventory.transfer.overage-decision.v1";
    internal const string RequestTransferLossCommand = "inventory.transfer.loss-request.v1";
    internal const string DecideTransferLossCommand = "inventory.transfer.loss-decision.v1";
    private const int ApproveDecision = (int)InventoryTransferDecisionKindV1.Approve;
    private const int RejectDecision = (int)InventoryTransferDecisionKindV1.Reject;
    /// <summary>The first loss request of a transfer line (KLN-D97); a renewal after a rejection takes the next one.</summary>
    internal const int FirstLossRequestNumber = 1;

    /// <summary>KLN-D107: the receipt's own declared excess is request 1; a corrected re-request takes the next number.</summary>
    internal const int FirstOverageRequestNumber = 1;

    public async Task<InventoryTransferReceiptResultV1?> ReplayReceiveLineAsync(
        Guid transferId,
        Guid lineId,
        ReceiveInventoryTransferLineV1 request,
        InventoryCommandMetadata metadata,
        Func<CancellationToken, Task> freshAuthorityFence,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(freshAuthorityFence);
        RequireTransferCall(metadata);
        var parsed = ReceivePayload(transferId, lineId, request);
        return await FindReceiptKeyReplayAsync(ReceiveTransferLineCommand,
            GenealogyHash(metadata.IdempotencyKey.Trim()), parsed.PayloadHash, metadata.ActorId,
            freshAuthorityFence, cancellationToken);
    }

    public Task<InventoryTransferReceiptResultV1> ReceiveLineAsync(
        Guid transferId,
        Guid lineId,
        ReceiveInventoryTransferLineV1 request,
        InventoryCommandMetadata metadata,
        Func<CancellationToken, Task> freshAuthorityFence,
        CancellationToken cancellationToken = default) =>
        WithinPhysicalValidationTransactionAsync(() => ReceiveLineCoreAsync(transferId, lineId, request,
            metadata, freshAuthorityFence, cancellationToken), cancellationToken);

    private async Task<InventoryTransferReceiptResultV1> ReceiveLineCoreAsync(
        Guid transferId,
        Guid lineId,
        ReceiveInventoryTransferLineV1 request,
        InventoryCommandMetadata metadata,
        Func<CancellationToken, Task> freshAuthorityFence,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(freshAuthorityFence);
        RequireTransferCall(metadata);
        var parsed = ReceivePayload(transferId, lineId, request);
        var payloadHash = parsed.PayloadHash;
        var keyHash = GenealogyHash(metadata.IdempotencyKey.Trim());
        if (await FindReceiptKeyReplayAsync(ReceiveTransferLineCommand, keyHash, payloadHash, metadata.ActorId,
                freshAuthorityFence, cancellationToken) is { } keyReplay)
        {
            return keyReplay;
        }

        if (await dbContext.TransferReceipts.AsNoTracking()
                .SingleOrDefaultAsync(row => row.TransferId == transferId && row.LineId == lineId,
                    cancellationToken) is { } prior)
        {
            var priorResult = await ReceiptResultAsync(transferId, prior.CorrelationId, cancellationToken);
            await RequireFreshAuthorityAsync(freshAuthorityFence, cancellationToken);
            if (!string.Equals(prior.PayloadHash, payloadHash, StringComparison.Ordinal))
            {
                throw TransferError(409, InventoryTransferCodesV1.TransferStateConflict);
            }

            return priorResult;
        }

        var row = await dbContext.Transfers.SingleOrDefaultAsync(candidate => candidate.Id == transferId, cancellationToken);
        if (row is null)
        {
            await RequireFreshAuthorityAsync(freshAuthorityFence, cancellationToken);
            throw TransferError(404, InventoryTransferCodesV1.TransferNotFound);
        }
        await RequireFreshAuthorityAsync(freshAuthorityFence, cancellationToken);
        if (row.Version != parsed.ExpectedVersion)
        {
            throw TransferError(409, InventoryTransferCodesV1.TransferVersionConflict);
        }

        var state = await LoadReceiptStateAsync(row, lineId, freshAuthorityFence, cancellationToken);
        if (await LossPendingAsync(transferId, lineId, cancellationToken))
        {
            await RequireFreshAuthorityAsync(freshAuthorityFence, cancellationToken);
            throw TransferError(409, InventoryTransferReceiptCodesV1.LossPending);
        }

        if (state.WrittenOff.Any(candidate => candidate.LineId == lineId)
            || state.Accepted.Any(candidate => candidate.LineId == lineId))
        {
            throw TransferError(409, InventoryTransferCodesV1.TransferStateConflict);
        }

        if (state.Label.Version != state.DispatchLine.LabelVersionAfterDispatch)
        {
            throw TransferError(409, InventoryTransferCodesV1.LabelVersionConflict);
        }

        var dispatchedKg = state.DispatchLine.ActualKg;
        var receivedKg = parsed.Received.Value;
        await RequireReceiptDocumentProofAsync(transferId, lineId, parsed.Document, request.ExpectedIncomingDocumentVersion,
            request.ReceivedAtUtc, receivedKg, parsed.OverageReason, freshAuthorityFence, cancellationToken);
        if (receivedKg != dispatchedKg && state.Buckets.Count(bucket => bucket.LineId == lineId) != 1)
        {
            // KLN-D91: a label dispatched in more than one block/lot bucket is received only with its full dispatched
            // kilograms. The difference is refused before any excess reason is weighed, and never split over buckets.
            throw TransferError(409, InventoryTransferReceiptCodesV1.MultiBlockFullReceiptOnly);
        }

        if (receivedKg > dispatchedKg && parsed.OverageReason is null)
        {
            throw TransferError(422, InventoryTransferReceiptCodesV1.OverageReasonRequired);
        }

        if (receivedKg <= dispatchedKg && parsed.OverageReason is not null)
        {
            throw TransferError(422, InventoryTransferReceiptCodesV1.OverageReasonNotAllowed);
        }

        var aggregate = RestoreTransfer(row, state.Lines, state.Dispatch, state.DispatchLines, state.Accepted,
            state.Buckets, state.Labels, writtenOff: state.WrittenOff);
        InventoryTransferReceiptEffect effect;
        try
        {
            effect = aggregate.ReceiveLine(parsed.ExpectedVersion, lineId, request.ReceivedAtUtc, parsed.Document,
                parsed.Received);
        }
        catch (InventoryRuleException exception)
        {
            throw TransferError(409, InventoryTransferCodesV1.TransferStateConflict, exception);
        }

        var acceptanceMovement = effect.Acceptance.Movement(Guid.NewGuid(), row.CompanyId);
        await StageTransferMovementAsync(acceptanceMovement, metadata, freshAuthorityFence, cancellationToken);
        var shortageMovement = effect.Shortage?.Movement(Guid.NewGuid(), row.CompanyId);
        if (shortageMovement is not null)
        {
            // KLN-D21: the fire is its own movement right after the acceptance, with no tolerance and no approval.
            // KLN-D90: its cost is the transfer dispatch unit cost (AverageCostCalculator.ValueTransferReceiptShortage),
            // never the moving average at receipt. Transfers post nothing to the cost ledger yet (KLN-D64 (2)) and no
            // accounting entry is written until ledger posting opens (account mapping: Finance spec), so nothing is
            // valued here today.
            await StageTransferMovementAsync(shortageMovement, metadata, freshAuthorityFence, cancellationToken);
        }

        var now = timeProvider.GetUtcNow();
        var acceptance = new InventoryTransferAcceptanceRow
        {
            CompanyId = row.CompanyId,
            TransferId = row.Id,
            LineId = lineId,
            MovementId = acceptanceMovement.Id,
            DocumentKind = parsed.Document.Kind,
            DocumentId = parsed.Document.Id,
            DocumentLineId = parsed.Document.LineId,
            AcceptedAtUtc = request.ReceivedAtUtc,
            PayloadHash = payloadHash,
            RecordedAtUtc = now,
            RecordedBy = metadata.ActorId.Trim(),
            CorrelationId = metadata.CorrelationId.Trim(),
        };
        var receipt = new InventoryTransferReceiptRow
        {
            CompanyId = row.CompanyId,
            TransferId = row.Id,
            LineId = lineId,
            DispatchedKg = effect.DispatchedQuantity.Value,
            ReceivedKg = effect.ReceivedQuantity.Value,
            ShortageKg = effect.ShortageQuantity.Value,
            OverageKg = effect.OverageQuantity.Value,
            ShortageMovementId = shortageMovement?.Id,
            LabelVersionAfterReceipt = state.Label.Version,
            OverageReason = effect.OverageQuantity.Value > 0m ? parsed.OverageReason : null,
            ReceivedAtUtc = request.ReceivedAtUtc,
            PayloadHash = payloadHash,
            RecordedAtUtc = now,
            RecordedBy = metadata.ActorId.Trim(),
            CorrelationId = metadata.CorrelationId.Trim(),
        };
        dbContext.TransferAcceptances.Add(acceptance);
        dbContext.TransferReceipts.Add(receipt);
        var writtenOffIds = state.WrittenOff.Select(candidate => candidate.LineId).ToArray();
        var before = TransferView(row, state.Lines, state.Dispatch, state.DispatchLines, state.Accepted, writtenOffIds);
        row.Version = effect.Acceptance.TransferVersion;
        row.Status = (int)aggregate.Status;
        row.UpdatedAtUtc = now;
        var view = TransferView(row, state.Lines, state.Dispatch, state.DispatchLines,
            state.Accepted.Append(acceptance).ToArray(), writtenOffIds);
        var facts = await ReceiptFactsAsync(row, cancellationToken, addedReceipt: receipt);
        dbContext.Commands.Add(ReceiptCommandRow(ReceiveTransferLineCommand, keyHash, payloadHash, metadata, row.Id,
            view, facts));
        Audit(metadata, row.CompanyId, TransferEntityType, row.Id, "inventory.transfer.line-received.v1", "ReceiveLine",
            parsed.ExpectedVersion, row.Version, JsonSerializer.Serialize(before, Json),
            JsonSerializer.Serialize(new
            {
                transfer = view,
                lineId,
                acceptanceMovementId = acceptanceMovement.Id,
                dispatchedKg = effect.DispatchedQuantity.ToString(),
                receivedKg = effect.ReceivedQuantity.ToString(),
                shortageKg = effect.ShortageQuantity.ToString(),
                overageKg = effect.OverageQuantity.ToString(),
                shortageMovementId = shortageMovement?.Id,
                overageReason = receipt.OverageReason,
            }, Json),
            "Transfer satırı hedefte tartılarak teslim alındı");
        return await SaveReceiptAsync(view, facts, receipt.CorrelationId, metadata, ReceiveTransferLineCommand, keyHash,
            payloadHash, freshAuthorityFence, cancellationToken);
    }

    public async Task<InventoryTransferReceiptResultV1?> ReplayDecideOverageAsync(
        Guid transferId,
        Guid lineId,
        DecideInventoryTransferOverageV1 request,
        InventoryCommandMetadata metadata,
        Func<CancellationToken, Task> freshAuthorityFence,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(freshAuthorityFence);
        RequireTransferCall(metadata);
        var parsed = OverageDecisionPayload(transferId, lineId, request);
        return await FindReceiptKeyReplayAsync(DecideTransferOverageCommand,
            GenealogyHash(metadata.IdempotencyKey.Trim()), parsed.PayloadHash, metadata.ActorId,
            freshAuthorityFence, cancellationToken);
    }

    public async Task<InventoryTransferReceiptResultV1> DecideOverageAsync(
        Guid transferId,
        Guid lineId,
        DecideInventoryTransferOverageV1 request,
        InventoryCommandMetadata metadata,
        Func<CancellationToken, Task> freshAuthorityFence,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(freshAuthorityFence);
        RequireTransferCall(metadata);
        var parsed = OverageDecisionPayload(transferId, lineId, request);
        var payloadHash = parsed.PayloadHash;
        var keyHash = GenealogyHash(metadata.IdempotencyKey.Trim());
        if (await FindReceiptKeyReplayAsync(DecideTransferOverageCommand, keyHash, payloadHash, metadata.ActorId,
                freshAuthorityFence, cancellationToken) is { } keyReplay)
        {
            return keyReplay;
        }

        var overageRequestNumber = parsed.RequestNumber;
        if (await dbContext.TransferOverageDecisions.AsNoTracking()
                .SingleOrDefaultAsync(row => row.TransferId == transferId && row.LineId == lineId
                    && row.RequestNumber == overageRequestNumber, cancellationToken) is { } prior)
        {
            var priorResult = await ReceiptResultAsync(transferId, prior.CorrelationId, cancellationToken);
            await RequireFreshAuthorityAsync(freshAuthorityFence, cancellationToken);
            if (!string.Equals(prior.PayloadHash, payloadHash, StringComparison.Ordinal))
            {
                throw TransferError(409, InventoryTransferReceiptCodesV1.AlreadyDecided);
            }

            return priorResult;
        }

        var row = await dbContext.Transfers.SingleOrDefaultAsync(candidate => candidate.Id == transferId, cancellationToken);
        var receipt = row is null
            ? null
            : await dbContext.TransferReceipts.AsNoTracking()
                .SingleOrDefaultAsync(candidate => candidate.TransferId == transferId && candidate.LineId == lineId,
                    cancellationToken);
        var lineExists = row is not null && await dbContext.TransferLines.AsNoTracking()
            .AnyAsync(candidate => candidate.TransferId == transferId && candidate.LineId == lineId, cancellationToken);
        var renewal = row is null || overageRequestNumber == FirstOverageRequestNumber
            ? null
            : await dbContext.TransferOverageRenewals.AsNoTracking()
                .SingleOrDefaultAsync(candidate => candidate.TransferId == transferId && candidate.LineId == lineId
                    && candidate.RequestNumber == overageRequestNumber, cancellationToken);
        await RequireFreshAuthorityAsync(freshAuthorityFence, cancellationToken);
        if (row is null || !lineExists)
        {
            throw TransferError(404, InventoryTransferCodesV1.TransferNotFound);
        }

        if (receipt is null || receipt.OverageKg <= 0m
            || (overageRequestNumber != FirstOverageRequestNumber && renewal is null))
        {
            throw TransferError(409, InventoryTransferReceiptCodesV1.OverageNotPending);
        }

        var actor = metadata.ActorId.Trim();
        if (string.Equals(receipt.RecordedBy, actor, StringComparison.Ordinal)
            || (renewal is not null && string.Equals(renewal.RecordedBy, actor, StringComparison.Ordinal)))
        {
            // KLN-D24, KLN-D107 maker≠approver: neither the receiver nor the re-requester decides the excess.
            throw TransferError(409, InventoryTransferReceiptCodesV1.SelfApproval);
        }

        if (request.DecidedAtUtc < receipt.ReceivedAtUtc
            || (renewal is not null && request.DecidedAtUtc < renewal.RequestedAtUtc))
        {
            throw TransferError(409, InventoryTransferCodesV1.TransferStateConflict);
        }

        // KLN-D107: a re-request is decided for its own kilograms, never above the receipt's first declared excess.
        var decidedOverageKg = renewal?.OverageKg ?? receipt.OverageKg;

        Guid? movementId = null;
        if (parsed.Decision == ApproveDecision)
        {
            var state = await LoadReceiptStateAsync(row, lineId, freshAuthorityFence, cancellationToken);
            if (state.Label.Version != receipt.LabelVersionAfterReceipt)
            {
                // The received label moved after the receipt: the received bucket no longer describes it.
                throw TransferError(409, InventoryTransferCodesV1.LabelVersionConflict);
            }

            var aggregate = RestoreTransfer(row, state.Lines, state.Dispatch, state.DispatchLines, state.Accepted,
                state.Buckets, state.Labels, writtenOff: state.WrittenOff);
            InventoryTransferReceiptOverageEffect effect;
            try
            {
                effect = aggregate.ReceiptOverage(lineId, Kilograms.Positive(decidedOverageKg), request.DecidedAtUtc);
            }
            catch (InventoryRuleException exception)
            {
                throw TransferError(409, InventoryTransferCodesV1.TransferStateConflict, exception);
            }

            var movement = effect.Movement(Guid.NewGuid(), row.CompanyId);
            await StageTransferMovementAsync(movement, metadata, freshAuthorityFence, cancellationToken);
            movementId = movement.Id;
        }

        var now = timeProvider.GetUtcNow();
        var decision = new InventoryTransferOverageDecisionRow
        {
            CompanyId = row.CompanyId,
            TransferId = row.Id,
            LineId = lineId,
            RequestNumber = overageRequestNumber,
            Decision = parsed.Decision,
            Reason = parsed.Reason,
            MovementId = movementId,
            DecidedAtUtc = request.DecidedAtUtc,
            PayloadHash = payloadHash,
            RecordedAtUtc = now,
            DecidedBy = metadata.ActorId.Trim(),
            CorrelationId = metadata.CorrelationId.Trim(),
        };
        dbContext.TransferOverageDecisions.Add(decision);
        // The decision changes no transfer state; touching the row makes a racing transfer write a concurrency conflict.
        row.UpdatedAtUtc = now;
        var view = await TransferViewAsync(row.Id, cancellationToken);
        var facts = await ReceiptFactsAsync(row, cancellationToken, addedOverageDecision: decision);
        dbContext.Commands.Add(ReceiptCommandRow(DecideTransferOverageCommand, keyHash, payloadHash, metadata, row.Id,
            view, facts));
        Audit(metadata, row.CompanyId, TransferEntityType, row.Id, "inventory.transfer.overage-decided.v1",
            "DecideOverage", row.Version, row.Version, "{}",
            JsonSerializer.Serialize(new
            {
                lineId,
                requestNumber = overageRequestNumber,
                decision = ((InventoryTransferDecisionKindV1)parsed.Decision).ToString(),
                reason = parsed.Reason,
                overageKg = Kilograms.Positive(decidedOverageKg).ToString(),
                receivedBy = receipt.RecordedBy,
                requestedBy = renewal?.RecordedBy ?? receipt.RecordedBy,
                movementId,
            }, Json),
            parsed.Decision == ApproveDecision ? "Transfer fazla teslimi onaylandı" : "Transfer fazla teslimi reddedildi");
        return await SaveReceiptAsync(view, facts, decision.CorrelationId, metadata, DecideTransferOverageCommand,
            keyHash, payloadHash, freshAuthorityFence, cancellationToken);
    }

    public async Task<InventoryTransferReceiptResultV1?> ReplayRequestLossAsync(
        Guid transferId,
        Guid lineId,
        RequestInventoryTransferLossV1 request,
        InventoryCommandMetadata metadata,
        Func<CancellationToken, Task> freshAuthorityFence,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(freshAuthorityFence);
        RequireTransferCall(metadata);
        var parsed = LossRequestPayload(transferId, lineId, request);
        return await FindReceiptKeyReplayAsync(RequestTransferLossCommand,
            GenealogyHash(metadata.IdempotencyKey.Trim()), parsed.PayloadHash, metadata.ActorId,
            freshAuthorityFence, cancellationToken);
    }

    public async Task<InventoryTransferReceiptResultV1> RequestLossAsync(
        Guid transferId,
        Guid lineId,
        RequestInventoryTransferLossV1 request,
        InventoryCommandMetadata metadata,
        Func<CancellationToken, Task> freshAuthorityFence,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(freshAuthorityFence);
        RequireTransferCall(metadata);
        var parsed = LossRequestPayload(transferId, lineId, request);
        var payloadHash = parsed.PayloadHash;
        var keyHash = GenealogyHash(metadata.IdempotencyKey.Trim());
        if (await FindReceiptKeyReplayAsync(RequestTransferLossCommand, keyHash, payloadHash, metadata.ActorId,
                freshAuthorityFence, cancellationToken) is { } keyReplay)
        {
            return keyReplay;
        }

        // KLN-D97: requests are numbered per line. A rejected request stays as it was; a new one takes the next number.
        var prior = await dbContext.TransferLossRequests.AsNoTracking()
            .Where(candidate => candidate.TransferId == transferId && candidate.LineId == lineId)
            .OrderByDescending(candidate => candidate.RequestNumber)
            .FirstOrDefaultAsync(cancellationToken);
        InventoryTransferLossDecisionRow? priorDecision = null;
        InventoryTransferLossWithdrawalRow? priorWithdrawal = null;
        if (prior is not null)
        {
            if (string.Equals(prior.PayloadHash, payloadHash, StringComparison.Ordinal))
            {
                var priorResult = await ReceiptResultAsync(transferId, prior.CorrelationId, cancellationToken);
                await RequireFreshAuthorityAsync(freshAuthorityFence, cancellationToken);
                return priorResult;
            }

            priorDecision = await dbContext.TransferLossDecisions.AsNoTracking()
                .SingleOrDefaultAsync(candidate => candidate.TransferId == transferId && candidate.LineId == lineId
                    && candidate.RequestNumber == prior.RequestNumber, cancellationToken);
            priorWithdrawal = priorDecision is not null
                ? null
                : await dbContext.TransferLossWithdrawals.AsNoTracking()
                    .SingleOrDefaultAsync(candidate => candidate.TransferId == transferId && candidate.LineId == lineId
                        && candidate.RequestNumber == prior.RequestNumber, cancellationToken);
            if (priorDecision is null && priorWithdrawal is null)
            {
                // ASSUMPTION (KLN-D97, reversible implementation detail): one undecided request per line at a time.
                await RequireFreshAuthorityAsync(freshAuthorityFence, cancellationToken);
                throw TransferError(409, InventoryTransferReceiptCodesV1.LossAlreadyRequested);
            }
        }

        var row = await dbContext.Transfers.SingleOrDefaultAsync(candidate => candidate.Id == transferId, cancellationToken);
        if (row is null)
        {
            await RequireFreshAuthorityAsync(freshAuthorityFence, cancellationToken);
            throw TransferError(404, InventoryTransferCodesV1.TransferNotFound);
        }
        await RequireFreshAuthorityAsync(freshAuthorityFence, cancellationToken);
        if (row.Version != parsed.ExpectedVersion)
        {
            throw TransferError(409, InventoryTransferCodesV1.TransferVersionConflict);
        }

        // An approved request wrote the line off (terminal); a new request may only follow a rejection, and never
        // predates that rejection.
        if (priorDecision is not null
            && (priorDecision.Decision != RejectDecision || request.RequestedAtUtc < priorDecision.DecidedAtUtc))
        {
            throw TransferError(409, InventoryTransferCodesV1.TransferStateConflict);
        }

        // ASSUMPTION (KLN-D108, reversible): a withdrawn request is not decided, so like a rejection it may be followed by
        // a new request, which never predates the withdrawal.
        if (priorWithdrawal is not null && request.RequestedAtUtc < priorWithdrawal.WithdrawnAtUtc)
        {
            throw TransferError(409, InventoryTransferCodesV1.TransferStateConflict);
        }

        var requestNumber = prior is null ? FirstLossRequestNumber : prior.RequestNumber + 1;

        var lineExists = await dbContext.TransferLines.AsNoTracking()
            .AnyAsync(candidate => candidate.TransferId == transferId && candidate.LineId == lineId, cancellationToken);
        var dispatch = await dbContext.TransferDispatches.AsNoTracking()
            .SingleOrDefaultAsync(candidate => candidate.TransferId == transferId, cancellationToken);
        var accepted = await dbContext.TransferAcceptances.AsNoTracking()
            .AnyAsync(candidate => candidate.TransferId == transferId && candidate.LineId == lineId, cancellationToken);
        await RequireFreshAuthorityAsync(freshAuthorityFence, cancellationToken);
        if (!lineExists)
        {
            throw TransferError(404, InventoryTransferCodesV1.TransferNotFound);
        }

        // KLN-D23: only a pending label (dispatched, not received, not cancelled) can be reported lost.
        if (dispatch is null || accepted
            || row.Status is not ((int)InventoryTransferStatus.InTransit or (int)InventoryTransferStatus.PartiallyAccepted)
            || request.RequestedAtUtc < dispatch.DispatchedAtUtc)
        {
            throw TransferError(409, InventoryTransferCodesV1.TransferStateConflict);
        }

        var now = timeProvider.GetUtcNow();
        var lossRequest = new InventoryTransferLossRequestRow
        {
            CompanyId = row.CompanyId,
            TransferId = row.Id,
            LineId = lineId,
            RequestNumber = requestNumber,
            Explanation = parsed.Explanation,
            RequestedAtUtc = request.RequestedAtUtc,
            PayloadHash = payloadHash,
            RecordedAtUtc = now,
            RecordedBy = metadata.ActorId.Trim(),
            CorrelationId = metadata.CorrelationId.Trim(),
        };
        dbContext.TransferLossRequests.Add(lossRequest);
        // A request changes no stock and no transfer version; touching the row serializes it with other transfer writes.
        row.UpdatedAtUtc = now;
        var view = await TransferViewAsync(row.Id, cancellationToken);
        var facts = await ReceiptFactsAsync(row, cancellationToken, addedLossRequest: lossRequest);
        dbContext.Commands.Add(ReceiptCommandRow(RequestTransferLossCommand, keyHash, payloadHash, metadata, row.Id,
            view, facts));
        Audit(metadata, row.CompanyId, TransferEntityType, row.Id, "inventory.transfer.loss-requested.v1",
            "RequestLoss", parsed.ExpectedVersion, row.Version, "{}",
            JsonSerializer.Serialize(new { lineId, requestNumber, explanation = parsed.Explanation }, Json),
            requestNumber == FirstLossRequestNumber
                ? "Transfer satırı için yolda kayıp talebi kaydedildi"
                : priorWithdrawal is not null
                    ? "Geri çekilen talebin ardından transfer satırı için yeni yolda kayıp talebi kaydedildi"
                    : "Reddedilen talebin ardından transfer satırı için yeni yolda kayıp talebi kaydedildi");
        return await SaveReceiptAsync(view, facts, lossRequest.CorrelationId, metadata, RequestTransferLossCommand,
            keyHash, payloadHash, freshAuthorityFence, cancellationToken);
    }

    public async Task<InventoryTransferReceiptResultV1?> ReplayDecideLossAsync(
        Guid transferId,
        Guid lineId,
        DecideInventoryTransferLossV1 request,
        InventoryCommandMetadata metadata,
        Func<CancellationToken, Task> freshAuthorityFence,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(freshAuthorityFence);
        RequireTransferCall(metadata);
        var parsed = LossDecisionPayload(transferId, lineId, request);
        return await FindReceiptKeyReplayAsync(DecideTransferLossCommand,
            GenealogyHash(metadata.IdempotencyKey.Trim()), parsed.PayloadHash, metadata.ActorId,
            freshAuthorityFence, cancellationToken);
    }

    public async Task<InventoryTransferReceiptResultV1> DecideLossAsync(
        Guid transferId,
        Guid lineId,
        DecideInventoryTransferLossV1 request,
        InventoryCommandMetadata metadata,
        Func<CancellationToken, Task> freshAuthorityFence,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(freshAuthorityFence);
        RequireTransferCall(metadata);
        var parsed = LossDecisionPayload(transferId, lineId, request);
        var payloadHash = parsed.PayloadHash;
        var keyHash = GenealogyHash(metadata.IdempotencyKey.Trim());
        if (await FindReceiptKeyReplayAsync(DecideTransferLossCommand, keyHash, payloadHash, metadata.ActorId,
                freshAuthorityFence, cancellationToken) is { } keyReplay)
        {
            return keyReplay;
        }

        var requestNumber = parsed.RequestNumber;
        if (await dbContext.TransferLossDecisions.AsNoTracking()
                .SingleOrDefaultAsync(row => row.TransferId == transferId && row.LineId == lineId
                    && row.RequestNumber == requestNumber, cancellationToken) is { } prior)
        {
            var priorResult = await ReceiptResultAsync(transferId, prior.CorrelationId, cancellationToken);
            await RequireFreshAuthorityAsync(freshAuthorityFence, cancellationToken);
            if (!string.Equals(prior.PayloadHash, payloadHash, StringComparison.Ordinal))
            {
                throw TransferError(409, InventoryTransferReceiptCodesV1.AlreadyDecided);
            }

            return priorResult;
        }

        var row = await dbContext.Transfers.SingleOrDefaultAsync(candidate => candidate.Id == transferId, cancellationToken);
        if (row is null)
        {
            await RequireFreshAuthorityAsync(freshAuthorityFence, cancellationToken);
            throw TransferError(404, InventoryTransferCodesV1.TransferNotFound);
        }
        await RequireFreshAuthorityAsync(freshAuthorityFence, cancellationToken);
        if (row.Version != parsed.ExpectedVersion)
        {
            throw TransferError(409, InventoryTransferCodesV1.TransferVersionConflict);
        }

        var lineExists = await dbContext.TransferLines.AsNoTracking()
            .AnyAsync(candidate => candidate.TransferId == transferId && candidate.LineId == lineId, cancellationToken);
        var lossRequest = await dbContext.TransferLossRequests.AsNoTracking()
            .SingleOrDefaultAsync(candidate => candidate.TransferId == transferId && candidate.LineId == lineId
                && candidate.RequestNumber == requestNumber, cancellationToken);
        var withdrawn = lossRequest is not null && await dbContext.TransferLossWithdrawals.AsNoTracking()
            .AnyAsync(candidate => candidate.TransferId == transferId && candidate.LineId == lineId
                && candidate.RequestNumber == requestNumber, cancellationToken);
        await RequireFreshAuthorityAsync(freshAuthorityFence, cancellationToken);
        if (!lineExists)
        {
            throw TransferError(404, InventoryTransferCodesV1.TransferNotFound);
        }

        if (withdrawn)
        {
            // KLN-D108: a withdrawn request is closed without a decision.
            throw TransferError(409, InventoryTransferReceiptCodesV1.LossWithdrawn);
        }

        if (lossRequest is null || request.DecidedAtUtc < lossRequest.RequestedAtUtc)
        {
            throw TransferError(409, InventoryTransferCodesV1.TransferStateConflict);
        }

        if (string.Equals(lossRequest.RecordedBy, metadata.ActorId.Trim(), StringComparison.Ordinal))
        {
            // KLN-D23 (c), KLN-D92, KLN-D97 maker≠approver: the requester never approves or rejects their own loss
            // request, a renewed one included.
            throw TransferError(409, InventoryTransferReceiptCodesV1.SelfApproval);
        }

        Guid? movementId = null;
        var now = timeProvider.GetUtcNow();
        var before = await TransferViewAsync(row.Id, cancellationToken);
        InventoryTransferViewV1 view;
        InventoryTransferLossDecisionRow decision = new()
        {
            CompanyId = row.CompanyId,
            TransferId = row.Id,
            LineId = lineId,
            RequestNumber = requestNumber,
            Decision = parsed.Decision,
            Reason = parsed.Reason,
            DecidedAtUtc = request.DecidedAtUtc,
            PayloadHash = payloadHash,
            RecordedAtUtc = now,
            DecidedBy = metadata.ActorId.Trim(),
            CorrelationId = metadata.CorrelationId.Trim(),
        };
        if (parsed.Decision == ApproveDecision)
        {
            var state = await LoadReceiptStateAsync(row, lineId, freshAuthorityFence, cancellationToken);
            if (state.Label.Version != state.DispatchLine.LabelVersionAfterDispatch)
            {
                throw TransferError(409, InventoryTransferCodesV1.LabelVersionConflict);
            }

            var aggregate = RestoreTransfer(row, state.Lines, state.Dispatch, state.DispatchLines, state.Accepted,
                state.Buckets, state.Labels, writtenOff: state.WrittenOff);
            InventoryTransferTransitLossEffect effect;
            try
            {
                effect = aggregate.WriteOffLine(parsed.ExpectedVersion, lineId, request.DecidedAtUtc);
            }
            catch (InventoryRuleException exception)
            {
                throw TransferError(409, InventoryTransferCodesV1.TransferStateConflict, exception);
            }

            var movement = effect.Movement(Guid.NewGuid(), row.CompanyId);
            await StageTransferMovementAsync(movement, metadata, freshAuthorityFence, cancellationToken);
            movementId = movement.Id;
            decision.MovementId = movementId;
            row.Version = effect.TransferVersion;
            row.Status = (int)aggregate.Status;
            row.UpdatedAtUtc = now;
            view = TransferView(row, state.Lines, state.Dispatch, state.DispatchLines, state.Accepted,
                state.WrittenOff.Select(candidate => candidate.LineId).Append(lineId).ToArray());
        }
        else
        {
            // A rejection leaves the line pending (KLN-D23 (a)/(b) stay open); only the row is touched.
            row.UpdatedAtUtc = now;
            view = before;
        }

        dbContext.TransferLossDecisions.Add(decision);
        var facts = await ReceiptFactsAsync(row, cancellationToken, addedLossDecision: decision);
        dbContext.Commands.Add(ReceiptCommandRow(DecideTransferLossCommand, keyHash, payloadHash, metadata, row.Id,
            view, facts));
        Audit(metadata, row.CompanyId, TransferEntityType, row.Id, "inventory.transfer.loss-decided.v1",
            "DecideLoss", parsed.ExpectedVersion, row.Version, JsonSerializer.Serialize(before, Json),
            JsonSerializer.Serialize(new
            {
                transfer = view,
                lineId,
                requestNumber,
                decision = ((InventoryTransferDecisionKindV1)parsed.Decision).ToString(),
                reason = parsed.Reason,
                requestedBy = lossRequest.RecordedBy,
                movementId,
            }, Json),
            parsed.Decision == ApproveDecision
                ? "Transfer satırı yolda kayıp olarak kapatıldı"
                : "Transfer satırı yolda kayıp talebi reddedildi");
        return await SaveReceiptAsync(view, facts, decision.CorrelationId, metadata, DecideTransferLossCommand,
            keyHash, payloadHash, freshAuthorityFence, cancellationToken);
    }

    async Task<InventoryTransferReceiptFactsViewV1?> IInventoryTransferReceiptReaderV1.ReadReceiptFactsAsync(
        Guid transferId,
        CancellationToken cancellationToken)
    {
        RequireResolvedCompany();
        if (transferId == Guid.Empty)
        {
            throw TransferError(400, InventoryTransferCodesV1.ValidationFailed);
        }

        var row = await dbContext.Transfers.AsNoTracking()
            .SingleOrDefaultAsync(candidate => candidate.Id == transferId, cancellationToken);
        return row is null ? null : await ReceiptFactsAsync(row, cancellationToken);
    }

    /// <summary>The approved loss decisions of one transfer: the written-off lines.</summary>
    private async Task<List<InventoryTransferLossDecisionRow>> ApprovedLossDecisionsAsync(
        Guid transferId,
        CancellationToken cancellationToken) =>
        await dbContext.TransferLossDecisions.AsNoTracking()
            .Where(candidate => candidate.TransferId == transferId && candidate.Decision == ApproveDecision)
            .ToListAsync(cancellationToken);

    /// <summary>
    /// True when the line carries a loss request that has no decision yet (KLN-D23 (c)); a rejected request does not
    /// count, its renewal (KLN-D97) does until it is decided; a withdrawn request (KLN-D108) does not count.
    /// </summary>
    private Task<bool> LossPendingAsync(Guid transferId, Guid lineId, CancellationToken cancellationToken) =>
        dbContext.TransferLossRequests.AsNoTracking()
            .AnyAsync(request => request.TransferId == transferId && request.LineId == lineId
                && !dbContext.TransferLossDecisions.Any(decision => decision.TransferId == request.TransferId
                    && decision.LineId == request.LineId && decision.RequestNumber == request.RequestNumber)
                && !dbContext.TransferLossWithdrawals.Any(withdrawal => withdrawal.TransferId == request.TransferId
                    && withdrawal.LineId == request.LineId && withdrawal.RequestNumber == request.RequestNumber),
                cancellationToken);

    /// <summary>True when any line of the transfer carries an undecided, not withdrawn loss request (KLN-D98, KLN-D108).</summary>
    private Task<bool> HasPendingLossRequestAsync(Guid transferId, CancellationToken cancellationToken) =>
        dbContext.TransferLossRequests.AsNoTracking()
            .AnyAsync(request => request.TransferId == transferId
                && !dbContext.TransferLossDecisions.Any(decision => decision.TransferId == request.TransferId
                    && decision.LineId == request.LineId && decision.RequestNumber == request.RequestNumber)
                && !dbContext.TransferLossWithdrawals.Any(withdrawal => withdrawal.TransferId == request.TransferId
                    && withdrawal.LineId == request.LineId && withdrawal.RequestNumber == request.RequestNumber),
                cancellationToken);

    private async Task<ReceiptState> LoadReceiptStateAsync(
        InventoryTransferRow row,
        Guid lineId,
        Func<CancellationToken, Task> freshAuthorityFence,
        CancellationToken cancellationToken)
    {
        var transferId = row.Id;
        var lines = await dbContext.TransferLines.AsNoTracking().Where(line => line.TransferId == transferId)
            .OrderBy(line => line.Sequence).ToListAsync(cancellationToken);
        await RequireFreshAuthorityAsync(freshAuthorityFence, cancellationToken);
        var planned = lines.SingleOrDefault(line => line.LineId == lineId)
            ?? throw TransferError(404, InventoryTransferCodesV1.TransferNotFound);
        if (row.Status == (int)InventoryTransferStatus.DispatchCancelled)
        {
            // KLN-D20: a cancelled dispatch is terminal; its stock is back at the source and nothing is received.
            throw TransferError(409, InventoryTransferCodesV1.TransferStateConflict);
        }

        var dispatch = await dbContext.TransferDispatches.AsNoTracking()
            .SingleOrDefaultAsync(candidate => candidate.TransferId == transferId, cancellationToken);
        if (dispatch is null)
        {
            await RequireFreshAuthorityAsync(freshAuthorityFence, cancellationToken);
            throw TransferError(409, InventoryTransferCodesV1.TransferStateConflict);
        }

        var dispatchLines = await dbContext.TransferDispatchLines.AsNoTracking()
            .Where(candidate => candidate.TransferId == transferId).ToListAsync(cancellationToken);
        var buckets = await dbContext.TransferBuckets.AsNoTracking()
            .Where(candidate => candidate.TransferId == transferId).OrderBy(candidate => candidate.Sequence)
            .ToListAsync(cancellationToken);
        var accepted = await dbContext.TransferAcceptances.AsNoTracking()
            .Where(candidate => candidate.TransferId == transferId).ToListAsync(cancellationToken);
        var writtenOff = await ApprovedLossDecisionsAsync(transferId, cancellationToken);
        var labelIds = lines.Select(line => line.LabelId).ToArray();
        var labels = await dbContext.Labels.Where(candidate => labelIds.Contains(candidate.Id))
            .ToDictionaryAsync(candidate => candidate.Id, cancellationToken);
        await RequireFreshAuthorityAsync(freshAuthorityFence, cancellationToken);
        var dispatchLine = dispatchLines.SingleOrDefault(candidate => candidate.LineId == lineId);
        if (dispatchLine is null || labels.Count != labelIds.Length
            || !labels.TryGetValue(planned.LabelId, out var label))
        {
            throw TransferError(503, InventoryTransferCodesV1.Unavailable);
        }

        return new ReceiptState(lines, dispatch, dispatchLines, buckets, accepted, writtenOff, labels, dispatchLine,
            label);
    }

    private async Task<InventoryTransferReceiptResultV1> ReceiptResultAsync(
        Guid transferId,
        string correlationId,
        CancellationToken cancellationToken)
    {
        var row = await dbContext.Transfers.AsNoTracking().SingleAsync(candidate => candidate.Id == transferId,
            cancellationToken);
        var view = await TransferViewAsync(transferId, cancellationToken);
        var facts = await ReceiptFactsAsync(row, cancellationToken);
        return new(view, facts, true, correlationId);
    }

    /// <summary>
    /// The stored receipt facts of one transfer, plus the one row this command adds (not yet saved), so a new answer
    /// and its later replay carry the same facts.
    /// </summary>
    private async Task<InventoryTransferReceiptFactsViewV1> ReceiptFactsAsync(
        InventoryTransferRow row,
        CancellationToken cancellationToken,
        InventoryTransferReceiptRow? addedReceipt = null,
        InventoryTransferOverageDecisionRow? addedOverageDecision = null,
        InventoryTransferLossRequestRow? addedLossRequest = null,
        InventoryTransferLossDecisionRow? addedLossDecision = null,
        InventoryTransferOverageRenewalRow? addedOverageRenewal = null,
        InventoryTransferLossWithdrawalRow? addedLossWithdrawal = null)
    {
        var transferId = row.Id;
        var receipts = await dbContext.TransferReceipts.AsNoTracking()
            .Where(candidate => candidate.TransferId == transferId).ToListAsync(cancellationToken);
        var overageDecisions = await dbContext.TransferOverageDecisions.AsNoTracking()
            .Where(candidate => candidate.TransferId == transferId).ToListAsync(cancellationToken);
        var lossRequests = await dbContext.TransferLossRequests.AsNoTracking()
            .Where(candidate => candidate.TransferId == transferId).ToListAsync(cancellationToken);
        var lossDecisions = await dbContext.TransferLossDecisions.AsNoTracking()
            .Where(candidate => candidate.TransferId == transferId).ToListAsync(cancellationToken);
        var overageRenewals = await dbContext.TransferOverageRenewals.AsNoTracking()
            .Where(candidate => candidate.TransferId == transferId).ToListAsync(cancellationToken);
        var lossWithdrawals = await dbContext.TransferLossWithdrawals.AsNoTracking()
            .Where(candidate => candidate.TransferId == transferId).ToListAsync(cancellationToken);
        if (addedOverageRenewal is not null) overageRenewals.Add(addedOverageRenewal);
        if (addedLossWithdrawal is not null) lossWithdrawals.Add(addedLossWithdrawal);
        if (addedReceipt is not null) receipts.Add(addedReceipt);
        if (addedOverageDecision is not null) overageDecisions.Add(addedOverageDecision);
        if (addedLossRequest is not null) lossRequests.Add(addedLossRequest);
        if (addedLossDecision is not null) lossDecisions.Add(addedLossDecision);
        var sequence = (await dbContext.TransferLines.AsNoTracking().Where(line => line.TransferId == transferId)
                .Select(line => new { line.LineId, line.Sequence }).ToListAsync(cancellationToken))
            .ToDictionary(line => line.LineId, line => line.Sequence);
        // KLN-D107: the receipt view describes the receipt's own request (1); re-requests are listed separately.
        var overageByRequest = overageDecisions.ToDictionary(decision => (decision.LineId, decision.RequestNumber));
        var withdrawalByRequest = lossWithdrawals.ToDictionary(withdrawal => (withdrawal.LineId, withdrawal.RequestNumber));
        var lossDecisionByRequest = lossDecisions.ToDictionary(decision => (decision.LineId, decision.RequestNumber));
        return new InventoryTransferReceiptFactsViewV1(row.Id, row.CompanyId,
            row.Version.ToString(CultureInfo.InvariantCulture), ((InventoryTransferStatus)row.Status).ToString(),
            [.. receipts.OrderBy(receipt => sequence.GetValueOrDefault(receipt.LineId)).Select(receipt =>
            {
                overageByRequest.TryGetValue((receipt.LineId, FirstOverageRequestNumber), out var decision);
                return new InventoryTransferReceiptViewV1(receipt.LineId,
                    Kilograms.Positive(receipt.DispatchedKg).ToString(),
                    Kilograms.Positive(receipt.ReceivedKg).ToString(),
                    Kilograms.Of(receipt.ShortageKg).ToString(),
                    Kilograms.Of(receipt.OverageKg).ToString(),
                    receipt.ShortageMovementId,
                    receipt.OverageReason,
                    receipt.ReceivedAtUtc,
                    receipt.RecordedBy,
                    receipt.OverageKg <= 0m ? "None" : DecisionStatus(decision?.Decision),
                    decision?.Reason,
                    decision?.DecidedAtUtc,
                    decision?.DecidedBy,
                    decision?.MovementId);
            })],
            [.. lossRequests.OrderBy(request => sequence.GetValueOrDefault(request.LineId))
                .ThenBy(request => request.RequestNumber).Select(request =>
            {
                lossDecisionByRequest.TryGetValue((request.LineId, request.RequestNumber), out var decision);
                if (decision is null
                    && withdrawalByRequest.TryGetValue((request.LineId, request.RequestNumber), out var withdrawal))
                {
                    return new InventoryTransferLossViewV1(request.LineId, request.RequestNumber, request.Explanation,
                        request.RequestedAtUtc, request.RecordedBy, WithdrawnStatus, withdrawal.Reason,
                        withdrawal.WithdrawnAtUtc, withdrawal.WithdrawnBy, null);
                }

                return new InventoryTransferLossViewV1(request.LineId, request.RequestNumber, request.Explanation,
                    request.RequestedAtUtc,
                    request.RecordedBy, DecisionStatus(decision?.Decision), decision?.Reason, decision?.DecidedAtUtc,
                    decision?.DecidedBy, decision?.MovementId);
            })],
            [.. overageRenewals.OrderBy(renewal => sequence.GetValueOrDefault(renewal.LineId))
                .ThenBy(renewal => renewal.RequestNumber).Select(renewal =>
            {
                overageByRequest.TryGetValue((renewal.LineId, renewal.RequestNumber), out var decision);
                return new InventoryTransferOverageRenewalViewV1(renewal.LineId, renewal.RequestNumber,
                    Kilograms.Positive(renewal.OverageKg).ToString(), renewal.Reason, renewal.RequestedAtUtc,
                    renewal.RecordedBy, DecisionStatus(decision?.Decision), decision?.Reason, decision?.DecidedAtUtc,
                    decision?.DecidedBy, decision?.MovementId);
            })]);
    }

    /// <summary>KLN-D108: the loss view status of a withdrawn request.</summary>
    private const string WithdrawnStatus = "Withdrawn";

    private static string DecisionStatus(int? decision) => decision switch
    {
        null => "Pending",
        ApproveDecision => "Approved",
        RejectDecision => "Rejected",
        _ => throw TransferError(503, InventoryTransferCodesV1.Unavailable),
    };

    private async Task<InventoryTransferReceiptResultV1?> FindReceiptKeyReplayAsync(
        string command,
        string keyHash,
        string payloadHash,
        string actorId,
        Func<CancellationToken, Task> freshAuthorityFence,
        CancellationToken cancellationToken)
    {
        var prior = await dbContext.Commands.AsNoTracking()
            .SingleOrDefaultAsync(row => row.CommandName == command && row.IdempotencyKeyHash == keyHash,
                cancellationToken);
        if (prior is null) return null;
        await RequireFreshAuthorityAsync(freshAuthorityFence, cancellationToken);
        if (!string.Equals(prior.PayloadHash, payloadHash, StringComparison.Ordinal)
            || !string.Equals(prior.ActorId, actorId.Trim(), StringComparison.Ordinal))
        {
            throw TransferError(409, InventoryTransferCodesV1.IdempotencyConflict);
        }

        var stored = JsonSerializer.Deserialize<ReceiptCommandResult>(prior.ResultJson, Json);
        // A result stored before KLN-D107 has no re-request list; it replays with an empty one.
        return stored is { Transfer: { } transfer, Facts: { } facts }
            ? new(transfer, facts.OverageRenewals is null ? facts with { OverageRenewals = [] } : facts, true,
                prior.CorrelationId)
            : throw TransferError(503, InventoryTransferCodesV1.Unavailable);
    }

    private async Task<InventoryTransferReceiptResultV1> SaveReceiptAsync(
        InventoryTransferViewV1 view,
        InventoryTransferReceiptFactsViewV1 facts,
        string correlationId,
        InventoryCommandMetadata metadata,
        string command,
        string keyHash,
        string payloadHash,
        Func<CancellationToken, Task> freshAuthorityFence,
        CancellationToken cancellationToken)
    {
        await RequireFreshAuthorityAsync(freshAuthorityFence, cancellationToken);

        try
        {
            await dbContext.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException exception) when (exception is DbUpdateConcurrencyException
            || IsUniqueConstraintViolation(exception))
        {
            dbContext.ChangeTracker.Clear();
            if (await FindReceiptKeyReplayAsync(command, keyHash, payloadHash, metadata.ActorId,
                    freshAuthorityFence, cancellationToken) is { } winner)
            {
                return winner;
            }

            await RequireFreshAuthorityAsync(freshAuthorityFence, cancellationToken);
            throw TransferError(409, InventoryTransferCodesV1.ConcurrentChange, exception);
        }

        return new(view, facts, false, correlationId);
    }

    private InventoryCommandRow ReceiptCommandRow(
        string command,
        string keyHash,
        string payloadHash,
        InventoryCommandMetadata metadata,
        Guid transferId,
        InventoryTransferViewV1 view,
        InventoryTransferReceiptFactsViewV1 facts) => new()
        {
            CompanyId = companyContext.CompanyId,
            CommandName = command,
            IdempotencyKeyHash = keyHash,
            ActorId = metadata.ActorId.Trim(),
            PayloadHash = payloadHash,
            TargetId = transferId,
            ResultJson = JsonSerializer.Serialize(new ReceiptCommandResult(view, facts), Json),
            CorrelationId = metadata.CorrelationId.Trim(),
            RecordedAtUtc = timeProvider.GetUtcNow(),
        };

    private static ParsedReceipt ReceivePayload(Guid transferId, Guid lineId, ReceiveInventoryTransferLineV1 request)
    {
        string? overageReason = null;
        if (transferId == Guid.Empty || lineId == Guid.Empty
            || !TryVersion(request.ExpectedTransferVersion, out var expectedVersion)
            || request.ReceivedAtUtc.Offset != TimeSpan.Zero
            || !TryQuantity(request.ReceivedKg, out var received)
            || (request.OverageReason is not null
                && (overageReason = InventoryTransferReceiptApiV1.NormalizeText(request.OverageReason)) is null))
        {
            throw TransferError(400, InventoryTransferCodesV1.ValidationFailed);
        }

        var document = ParseDocument(request.IncomingDocument);
        var payloadHash = request.ExpectedIncomingDocumentVersion is null ? GenealogyPayloadHash(new
        {
            transferId,
            lineId,
            expectedVersion,
            receivedAtUtc = request.ReceivedAtUtc.UtcDateTime.ToString("O", CultureInfo.InvariantCulture),
            document,
            receivedKg = received.ToString(),
            overageReason,
        }) : GenealogyPayloadHash(new
        {
            transferId,
            lineId,
            expectedVersion,
            receivedAtUtc = request.ReceivedAtUtc.UtcDateTime.ToString("O", CultureInfo.InvariantCulture),
            document,
            receivedKg = received.ToString(),
            overageReason,
            expectedIncomingDocumentVersion = request.ExpectedIncomingDocumentVersion,
        });
        return new(expectedVersion, document, received, overageReason, payloadHash);
    }

    private static ParsedDecision OverageDecisionPayload(
        Guid transferId,
        Guid lineId,
        DecideInventoryTransferOverageV1 request)
    {
        var reason = InventoryTransferReceiptApiV1.NormalizeText(request.Reason);
        if (transferId == Guid.Empty || lineId == Guid.Empty || !Enum.IsDefined(request.Decision)
            || request.DecidedAtUtc.Offset != TimeSpan.Zero || reason is null
            || request.RequestNumber is < FirstOverageRequestNumber)
        {
            throw TransferError(400, InventoryTransferCodesV1.ValidationFailed);
        }

        var decidedAtUtc = request.DecidedAtUtc.UtcDateTime.ToString("O", CultureInfo.InvariantCulture);
        // KLN-D107: the request number joins the hash only when it is sent, so a decision recorded before re-requests
        // existed still replays with its stored hash.
        var payloadHash = request.RequestNumber is { } sentNumber
            ? GenealogyPayloadHash(new
            {
                transferId,
                lineId,
                requestNumber = sentNumber,
                decision = (int)request.Decision,
                decidedAtUtc,
                reason,
            })
            : GenealogyPayloadHash(new
            {
                transferId,
                lineId,
                decision = (int)request.Decision,
                decidedAtUtc,
                reason,
            });
        return new(0, request.RequestNumber ?? FirstOverageRequestNumber, (int)request.Decision, reason, payloadHash);
    }

    private static (long ExpectedVersion, string Explanation, string PayloadHash) LossRequestPayload(
        Guid transferId,
        Guid lineId,
        RequestInventoryTransferLossV1 request)
    {
        var explanation = InventoryTransferReceiptApiV1.NormalizeText(request.Explanation);
        if (transferId == Guid.Empty || lineId == Guid.Empty
            || !TryVersion(request.ExpectedTransferVersion, out var expectedVersion)
            || request.RequestedAtUtc.Offset != TimeSpan.Zero || explanation is null)
        {
            throw TransferError(400, InventoryTransferCodesV1.ValidationFailed);
        }

        var payloadHash = GenealogyPayloadHash(new
        {
            transferId,
            lineId,
            expectedVersion,
            requestedAtUtc = request.RequestedAtUtc.UtcDateTime.ToString("O", CultureInfo.InvariantCulture),
            explanation,
        });
        return (expectedVersion, explanation, payloadHash);
    }

    private static ParsedLossDecision LossDecisionPayload(
        Guid transferId,
        Guid lineId,
        DecideInventoryTransferLossV1 request)
    {
        var reason = InventoryTransferReceiptApiV1.NormalizeText(request.Reason);
        if (transferId == Guid.Empty || lineId == Guid.Empty
            || !TryVersion(request.ExpectedTransferVersion, out var expectedVersion)
            || !Enum.IsDefined(request.Decision)
            || request.DecidedAtUtc.Offset != TimeSpan.Zero || reason is null
            || request.RequestNumber is < FirstLossRequestNumber)
        {
            throw TransferError(400, InventoryTransferCodesV1.ValidationFailed);
        }

        var decidedAtUtc = request.DecidedAtUtc.UtcDateTime.ToString("O", CultureInfo.InvariantCulture);
        // KLN-D97: the request number joins the hash only when it is sent, so a decision recorded before requests
        // were numbered still replays with its stored hash.
        var payloadHash = request.RequestNumber is { } sentNumber
            ? GenealogyPayloadHash(new
            {
                transferId,
                lineId,
                expectedVersion,
                requestNumber = sentNumber,
                decision = (int)request.Decision,
                decidedAtUtc,
                reason,
            })
            : GenealogyPayloadHash(new
            {
                transferId,
                lineId,
                expectedVersion,
                decision = (int)request.Decision,
                decidedAtUtc,
                reason,
            });
        return new(expectedVersion, request.RequestNumber ?? FirstLossRequestNumber, (int)request.Decision, reason,
            payloadHash);
    }

    private sealed record ParsedReceipt(
        long ExpectedVersion,
        InventoryTransferDocumentReference Document,
        Kilograms Received,
        string? OverageReason,
        string PayloadHash);

    private sealed record ParsedDecision(long ExpectedVersion, int RequestNumber, int Decision, string Reason, string PayloadHash);

    private sealed record ParsedLossDecision(
        long ExpectedVersion,
        int RequestNumber,
        int Decision,
        string Reason,
        string PayloadHash);

    private sealed record ReceiptState(
        IReadOnlyList<InventoryTransferLineRow> Lines,
        InventoryTransferDispatchRow Dispatch,
        IReadOnlyList<InventoryTransferDispatchLineRow> DispatchLines,
        IReadOnlyList<InventoryTransferBucketRow> Buckets,
        IReadOnlyList<InventoryTransferAcceptanceRow> Accepted,
        IReadOnlyList<InventoryTransferLossDecisionRow> WrittenOff,
        IReadOnlyDictionary<Guid, LabelRow> Labels,
        InventoryTransferDispatchLineRow DispatchLine,
        LabelRow Label);

    private sealed record ReceiptCommandResult(
        InventoryTransferViewV1? Transfer,
        InventoryTransferReceiptFactsViewV1? Facts);
}
