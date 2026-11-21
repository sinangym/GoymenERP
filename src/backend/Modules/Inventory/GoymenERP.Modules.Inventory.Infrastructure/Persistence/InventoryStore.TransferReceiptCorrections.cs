using System.Globalization;
using System.Text.Json;
using GoymenERP.Modules.Inventory.Application;
using GoymenERP.Modules.Inventory.Domain.Quantities;
using Microsoft.EntityFrameworkCore;

namespace GoymenERP.Modules.Inventory.Infrastructure.Persistence;

/// <summary>
/// STK-SC36-b: the two corrections of the B-22c receipt workflow. KLN-D107 records a corrected re-request of a rejected
/// receipt excess (under the receive permission, decided later under the overage approval); KLN-D108 withdraws an
/// undecided loss request (under the loss record permission). Neither changes stock or the transfer version; both touch the
/// transfer row so they serialize with every other transfer write, and both share the receipt replay, audit and save.
/// </summary>
public sealed partial class InventoryStore
{
    internal const string RenewTransferOverageCommand = "inventory.transfer.overage-renewal.v1";
    internal const string WithdrawTransferLossCommand = "inventory.transfer.loss-withdraw.v1";

    public async Task<InventoryTransferReceiptResultV1?> ReplayRenewOverageAsync(
        Guid transferId,
        Guid lineId,
        RenewInventoryTransferOverageV1 request,
        InventoryCommandMetadata metadata,
        Func<CancellationToken, Task> freshAuthorityFence,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(freshAuthorityFence);
        RequireTransferCall(metadata);
        var parsed = OverageRenewalPayload(transferId, lineId, request);
        return await FindReceiptKeyReplayAsync(RenewTransferOverageCommand,
            GenealogyHash(metadata.IdempotencyKey.Trim()), parsed.PayloadHash, metadata.ActorId,
            freshAuthorityFence, cancellationToken);
    }

    public async Task<InventoryTransferReceiptResultV1> RenewOverageAsync(
        Guid transferId,
        Guid lineId,
        RenewInventoryTransferOverageV1 request,
        InventoryCommandMetadata metadata,
        Func<CancellationToken, Task> freshAuthorityFence,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(freshAuthorityFence);
        RequireTransferCall(metadata);
        var parsed = OverageRenewalPayload(transferId, lineId, request);
        var payloadHash = parsed.PayloadHash;
        var keyHash = GenealogyHash(metadata.IdempotencyKey.Trim());
        if (await FindReceiptKeyReplayAsync(RenewTransferOverageCommand, keyHash, payloadHash, metadata.ActorId,
                freshAuthorityFence, cancellationToken) is { } keyReplay)
        {
            return keyReplay;
        }

        var priorRenewal = await dbContext.TransferOverageRenewals.AsNoTracking()
            .Where(candidate => candidate.TransferId == transferId && candidate.LineId == lineId)
            .OrderByDescending(candidate => candidate.RequestNumber)
            .FirstOrDefaultAsync(cancellationToken);
        if (priorRenewal is not null && string.Equals(priorRenewal.PayloadHash, payloadHash, StringComparison.Ordinal))
        {
            var priorResult = await ReceiptResultAsync(transferId, priorRenewal.CorrelationId, cancellationToken);
            await RequireFreshAuthorityAsync(freshAuthorityFence, cancellationToken);
            return priorResult;
        }

        var latestRequestNumber = priorRenewal?.RequestNumber ?? FirstOverageRequestNumber;
        var row = await dbContext.Transfers.SingleOrDefaultAsync(candidate => candidate.Id == transferId, cancellationToken);
        var lineExists = row is not null && await dbContext.TransferLines.AsNoTracking()
            .AnyAsync(candidate => candidate.TransferId == transferId && candidate.LineId == lineId, cancellationToken);
        var receipt = row is null
            ? null
            : await dbContext.TransferReceipts.AsNoTracking()
                .SingleOrDefaultAsync(candidate => candidate.TransferId == transferId && candidate.LineId == lineId,
                    cancellationToken);
        var latestDecision = row is null
            ? null
            : await dbContext.TransferOverageDecisions.AsNoTracking()
                .SingleOrDefaultAsync(candidate => candidate.TransferId == transferId && candidate.LineId == lineId
                    && candidate.RequestNumber == latestRequestNumber, cancellationToken);
        await RequireFreshAuthorityAsync(freshAuthorityFence, cancellationToken);
        if (row is null || !lineExists)
        {
            throw TransferError(404, InventoryTransferCodesV1.TransferNotFound);
        }

        if (row.Version != parsed.ExpectedVersion)
        {
            throw TransferError(409, InventoryTransferCodesV1.TransferVersionConflict);
        }

        if (receipt is null || receipt.OverageKg <= 0m)
        {
            throw TransferError(409, InventoryTransferReceiptCodesV1.OverageNotPending);
        }

        if (latestDecision is null)
        {
            // KLN-D107: the line's latest excess request is still undecided; one undecided request per line at a time.
            throw TransferError(409, InventoryTransferReceiptCodesV1.OverageAlreadyRequested);
        }

        if (latestDecision.Decision != RejectDecision || request.RequestedAtUtc < latestDecision.DecidedAtUtc)
        {
            // An approved excess is terminal; a re-request only corrects a rejection and never predates it.
            throw TransferError(409, InventoryTransferCodesV1.TransferStateConflict);
        }

        if (parsed.Overage.Value > receipt.OverageKg)
        {
            // KLN-D107: a corrected re-request never asks for more than the excess first declared on the receipt.
            throw TransferError(422, InventoryTransferReceiptCodesV1.OverageRenewalExceedsDeclared);
        }

        var now = timeProvider.GetUtcNow();
        var renewal = new InventoryTransferOverageRenewalRow
        {
            CompanyId = row.CompanyId,
            TransferId = row.Id,
            LineId = lineId,
            RequestNumber = latestRequestNumber + 1,
            OverageKg = parsed.Overage.Value,
            Reason = parsed.Reason,
            RequestedAtUtc = request.RequestedAtUtc,
            PayloadHash = payloadHash,
            RecordedAtUtc = now,
            RecordedBy = metadata.ActorId.Trim(),
            CorrelationId = metadata.CorrelationId.Trim(),
        };
        dbContext.TransferOverageRenewals.Add(renewal);
        // A re-request changes no stock and no transfer version; touching the row serializes it with other transfer writes.
        row.UpdatedAtUtc = now;
        var view = await TransferViewAsync(row.Id, cancellationToken);
        var facts = await ReceiptFactsAsync(row, cancellationToken, addedOverageRenewal: renewal);
        dbContext.Commands.Add(ReceiptCommandRow(RenewTransferOverageCommand, keyHash, payloadHash, metadata, row.Id,
            view, facts));
        Audit(metadata, row.CompanyId, TransferEntityType, row.Id, "inventory.transfer.overage-renewed.v1",
            "RenewOverage", parsed.ExpectedVersion, row.Version, "{}",
            JsonSerializer.Serialize(new
            {
                lineId,
                requestNumber = renewal.RequestNumber,
                overageKg = parsed.Overage.ToString(),
                declaredOverageKg = Kilograms.Positive(receipt.OverageKg).ToString(),
                reason = parsed.Reason,
                rejectedRequestNumber = latestRequestNumber,
            }, Json),
            "Reddedilen fazla teslim için düzeltilmiş yeni talep kaydedildi");
        return await SaveReceiptAsync(view, facts, renewal.CorrelationId, metadata, RenewTransferOverageCommand,
            keyHash, payloadHash, freshAuthorityFence, cancellationToken);
    }

    public async Task<InventoryTransferReceiptResultV1?> ReplayWithdrawLossAsync(
        Guid transferId,
        Guid lineId,
        WithdrawInventoryTransferLossV1 request,
        InventoryCommandMetadata metadata,
        Func<CancellationToken, Task> freshAuthorityFence,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(freshAuthorityFence);
        RequireTransferCall(metadata);
        var parsed = LossWithdrawalPayload(transferId, lineId, request);
        return await FindReceiptKeyReplayAsync(WithdrawTransferLossCommand,
            GenealogyHash(metadata.IdempotencyKey.Trim()), parsed.PayloadHash, metadata.ActorId,
            freshAuthorityFence, cancellationToken);
    }

    public async Task<InventoryTransferReceiptResultV1> WithdrawLossAsync(
        Guid transferId,
        Guid lineId,
        WithdrawInventoryTransferLossV1 request,
        InventoryCommandMetadata metadata,
        Func<CancellationToken, Task> freshAuthorityFence,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(freshAuthorityFence);
        RequireTransferCall(metadata);
        var parsed = LossWithdrawalPayload(transferId, lineId, request);
        var payloadHash = parsed.PayloadHash;
        var keyHash = GenealogyHash(metadata.IdempotencyKey.Trim());
        if (await FindReceiptKeyReplayAsync(WithdrawTransferLossCommand, keyHash, payloadHash, metadata.ActorId,
                freshAuthorityFence, cancellationToken) is { } keyReplay)
        {
            return keyReplay;
        }

        var requestNumber = request.RequestNumber;
        if (await dbContext.TransferLossWithdrawals.AsNoTracking()
                .SingleOrDefaultAsync(candidate => candidate.TransferId == transferId && candidate.LineId == lineId
                    && candidate.RequestNumber == requestNumber, cancellationToken) is { } prior)
        {
            var priorResult = await ReceiptResultAsync(transferId, prior.CorrelationId, cancellationToken);
            await RequireFreshAuthorityAsync(freshAuthorityFence, cancellationToken);
            if (!string.Equals(prior.PayloadHash, payloadHash, StringComparison.Ordinal))
            {
                throw TransferError(409, InventoryTransferReceiptCodesV1.LossWithdrawn);
            }

            return priorResult;
        }

        var row = await dbContext.Transfers.SingleOrDefaultAsync(candidate => candidate.Id == transferId, cancellationToken);
        var lineExists = row is not null && await dbContext.TransferLines.AsNoTracking()
            .AnyAsync(candidate => candidate.TransferId == transferId && candidate.LineId == lineId, cancellationToken);
        var lossRequest = row is null
            ? null
            : await dbContext.TransferLossRequests.AsNoTracking()
                .SingleOrDefaultAsync(candidate => candidate.TransferId == transferId && candidate.LineId == lineId
                    && candidate.RequestNumber == requestNumber, cancellationToken);
        var decided = lossRequest is not null && await dbContext.TransferLossDecisions.AsNoTracking()
            .AnyAsync(candidate => candidate.TransferId == transferId && candidate.LineId == lineId
                && candidate.RequestNumber == requestNumber, cancellationToken);
        await RequireFreshAuthorityAsync(freshAuthorityFence, cancellationToken);
        if (row is null || !lineExists)
        {
            throw TransferError(404, InventoryTransferCodesV1.TransferNotFound);
        }

        if (row.Version != parsed.ExpectedVersion)
        {
            throw TransferError(409, InventoryTransferCodesV1.TransferVersionConflict);
        }

        if (decided)
        {
            // KLN-D108: only an undecided request is withdrawn; a decision is never undone this way.
            throw TransferError(409, InventoryTransferReceiptCodesV1.AlreadyDecided);
        }

        if (lossRequest is null || request.WithdrawnAtUtc < lossRequest.RequestedAtUtc)
        {
            throw TransferError(409, InventoryTransferCodesV1.TransferStateConflict);
        }

        var now = timeProvider.GetUtcNow();
        var withdrawal = new InventoryTransferLossWithdrawalRow
        {
            CompanyId = row.CompanyId,
            TransferId = row.Id,
            LineId = lineId,
            RequestNumber = requestNumber,
            Reason = parsed.Reason,
            WithdrawnAtUtc = request.WithdrawnAtUtc,
            PayloadHash = payloadHash,
            RecordedAtUtc = now,
            WithdrawnBy = metadata.ActorId.Trim(),
            CorrelationId = metadata.CorrelationId.Trim(),
        };
        dbContext.TransferLossWithdrawals.Add(withdrawal);
        // A withdrawal changes no stock and no transfer version; touching the row serializes it with other transfer writes.
        row.UpdatedAtUtc = now;
        var view = await TransferViewAsync(row.Id, cancellationToken);
        var facts = await ReceiptFactsAsync(row, cancellationToken, addedLossWithdrawal: withdrawal);
        dbContext.Commands.Add(ReceiptCommandRow(WithdrawTransferLossCommand, keyHash, payloadHash, metadata, row.Id,
            view, facts));
        Audit(metadata, row.CompanyId, TransferEntityType, row.Id, "inventory.transfer.loss-withdrawn.v1",
            "WithdrawLoss", parsed.ExpectedVersion, row.Version, "{}",
            JsonSerializer.Serialize(new
            {
                lineId,
                requestNumber,
                reason = parsed.Reason,
                requestedBy = lossRequest.RecordedBy,
            }, Json),
            "Transfer satırı için karar bekleyen yolda kayıp talebi geri çekildi");
        return await SaveReceiptAsync(view, facts, withdrawal.CorrelationId, metadata, WithdrawTransferLossCommand,
            keyHash, payloadHash, freshAuthorityFence, cancellationToken);
    }

    private static ParsedOverageRenewal OverageRenewalPayload(
        Guid transferId,
        Guid lineId,
        RenewInventoryTransferOverageV1 request)
    {
        var reason = InventoryTransferReceiptApiV1.NormalizeText(request.Reason);
        if (transferId == Guid.Empty || lineId == Guid.Empty
            || !TryVersion(request.ExpectedTransferVersion, out var expectedVersion)
            || request.RequestedAtUtc.Offset != TimeSpan.Zero
            || !TryQuantity(request.OverageKg, out var overage)
            || reason is null)
        {
            throw TransferError(400, InventoryTransferCodesV1.ValidationFailed);
        }

        var payloadHash = GenealogyPayloadHash(new
        {
            transferId,
            lineId,
            expectedVersion,
            requestedAtUtc = request.RequestedAtUtc.UtcDateTime.ToString("O", CultureInfo.InvariantCulture),
            overageKg = overage.ToString(),
            reason,
        });
        return new(expectedVersion, overage, reason, payloadHash);
    }

    private static ParsedLossWithdrawal LossWithdrawalPayload(
        Guid transferId,
        Guid lineId,
        WithdrawInventoryTransferLossV1 request)
    {
        var reason = InventoryTransferReceiptApiV1.NormalizeText(request.Reason);
        if (transferId == Guid.Empty || lineId == Guid.Empty
            || !TryVersion(request.ExpectedTransferVersion, out var expectedVersion)
            || request.RequestNumber < FirstLossRequestNumber
            || request.WithdrawnAtUtc.Offset != TimeSpan.Zero || reason is null)
        {
            throw TransferError(400, InventoryTransferCodesV1.ValidationFailed);
        }

        var payloadHash = GenealogyPayloadHash(new
        {
            transferId,
            lineId,
            expectedVersion,
            requestNumber = request.RequestNumber,
            withdrawnAtUtc = request.WithdrawnAtUtc.UtcDateTime.ToString("O", CultureInfo.InvariantCulture),
            reason,
        });
        return new(expectedVersion, reason, payloadHash);
    }

    private sealed record ParsedOverageRenewal(long ExpectedVersion, Kilograms Overage, string Reason, string PayloadHash);

    private sealed record ParsedLossWithdrawal(long ExpectedVersion, string Reason, string PayloadHash);
}
