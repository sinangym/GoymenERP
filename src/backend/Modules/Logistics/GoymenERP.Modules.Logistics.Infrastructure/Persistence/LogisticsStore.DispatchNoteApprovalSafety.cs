using System.Data;
using System.Text.Json;
using GoymenERP.Modules.Logistics.Application.Contracts;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace GoymenERP.Modules.Logistics.Infrastructure.Persistence;

public sealed partial class LogisticsStore
{
    private Func<CancellationToken, Task>? dispatchApprovalFence;

    public Task<LogisticsReplay?> FindDispatchNoteApprovalReplayAsync(LogisticsWriteEnvelope envelope, string requestHash,
        Guid instructionId, Guid dispatchNoteId, long expectedVersion, Func<CancellationToken, Task> freshAuthority,
        CancellationToken cancellationToken = default) =>
        DispatchApprovalOwnedAsync<LogisticsReplay?>(async () =>
        {
            var replay = await FindReplayAsync(envelope, requestHash, LogisticsCommands.ApproveDispatchNoteV1, cancellationToken);
            await DispatchApprovalFenceAsync(cancellationToken);
            if (replay?.Instruction is null) return replay;
            var receipt = await DispatchApprovalReceiptAsync(envelope, requestHash, instructionId, dispatchNoteId, expectedVersion, cancellationToken);
            return replay with { DispatchNoteApprovalReceipt = receipt };
        }, freshAuthority, cancellationToken);

    public Task<LogisticsStoreResult?> ApproveDispatchNoteOwnedAsync(LogisticsWriteEnvelope envelope, string requestHash,
        Guid instructionId, Guid dispatchNoteId, long expectedVersion, LogisticsMutationAudit audit,
        Func<CancellationToken, Task> freshAuthority, CancellationToken cancellationToken = default) =>
        DispatchApprovalOwnedAsync<LogisticsStoreResult?>(async () =>
        {
            var result = await MutateInstructionAsync(envelope, requestHash, LogisticsCommands.ApproveDispatchNoteV1, instructionId,
                (instruction, now) => instruction.ApproveDispatchNote(expectedVersion, dispatchNoteId, envelope.ActorId, now), audit, cancellationToken);
            if (result is null) return null;
            var receipt = await DispatchApprovalReceiptAsync(envelope, requestHash, instructionId, dispatchNoteId, expectedVersion, cancellationToken);
            return result with { DispatchNoteApprovalReceipt = receipt };
        }, freshAuthority, cancellationToken);

    public async Task RecordDispatchNoteApprovalRefusalAsync(LogisticsWriteEnvelope envelope, string requestHash,
        LogisticsTerminalRefusal refusal, Func<CancellationToken, Task> freshAuthority, CancellationToken cancellationToken = default)
    {
        await DispatchApprovalOwnedAsync(async () =>
        {
            await RecordRefusalAsync(envelope, requestHash, LogisticsCommands.ApproveDispatchNoteV1, refusal, cancellationToken);
            return true;
        }, freshAuthority, cancellationToken);
    }

    private async Task<T> DispatchApprovalOwnedAsync<T>(Func<Task<T>> action, Func<CancellationToken, Task> freshAuthority,
        CancellationToken token)
    {
        ArgumentNullException.ThrowIfNull(freshAuthority);
        // Outside owned cleanup: refused entry preserves the caller's transaction, tracked changes and audit collector.
        if (dispatchApprovalFence is not null || dispatchNoteAuditWriter is null || System.Transactions.Transaction.Current is not null
            || !dbContext.Database.IsRelational() || dbContext.Database.CurrentTransaction is not null
            || dbContext.ChangeTracker.HasChanges() || dispatchNoteAuditWriter.HasPendingDrafts)
            throw Refused("logistics_dispatch_note_approval_unavailable", "Dispatch approval requires a clean owned transaction.");
        var companyId = RequireResolvedCompany();
        await using var transaction = await dbContext.Database.BeginTransactionAsync(IsolationLevel.Serializable, token);
        async Task Fence(CancellationToken ct)
        {
            if (RequireResolvedCompany() != companyId) throw Refused(LogisticsStoreCodes.CompanyContextMismatch, "Company changed.");
            await freshAuthority(ct);
            if (RequireResolvedCompany() != companyId) throw Refused(LogisticsStoreCodes.CompanyContextMismatch, "Company changed.");
        }
        dispatchApprovalFence = Fence;
        try
        {
            await Fence(token);
            var result = await action();
            await Fence(token);
            if (dispatchNoteAuditWriter.HasPendingDrafts)
            {
                await dispatchNoteAuditWriter.WritePendingAsync(transaction.GetDbTransaction(), token);
                await Fence(token);
            }
            if (dispatchNoteAuditWriter.HasPendingDrafts) throw new InvalidOperationException("Dispatch audit is not flushed.");
            await Fence(token);
            await transaction.CommitAsync(token);
            return result;
        }
        catch (Exception exception)
        {
            try { await transaction.RollbackAsync(CancellationToken.None); }
            finally
            {
                try { await transaction.DisposeAsync(); }
                finally { dbContext.ChangeTracker.Clear(); dispatchNoteAuditWriter.DiscardPending(); }
            }
            if (IsDeadlockVictim(exception)) throw LostAggregateRead(exception);
            throw;
        }
        finally { dispatchApprovalFence = null; }
    }

    private Task DispatchApprovalFenceAsync(CancellationToken token) => dispatchApprovalFence?.Invoke(token) ?? Task.CompletedTask;

    private async Task FlushDispatchApprovalAuditAsync(CancellationToken token)
    {
        if (dispatchApprovalFence is null) return; // All legacy participant commands remain unchanged.
        await DispatchApprovalFenceAsync(token);
        if (dispatchNoteAuditWriter!.HasPendingDrafts)
        {
            await dispatchNoteAuditWriter.WritePendingAsync(dbContext.Database.CurrentTransaction!.GetDbTransaction(), token);
            await DispatchApprovalFenceAsync(token);
        }
        if (dispatchNoteAuditWriter.HasPendingDrafts) throw new InvalidOperationException("Dispatch audit is not flushed.");
    }

    private async Task<LogisticsDispatchNoteApprovalReceiptV1?> DispatchApprovalReceiptAsync(LogisticsWriteEnvelope envelope,
        string requestHash, Guid instructionId, Guid dispatchNoteId, long expectedVersion, CancellationToken token)
    {
        var attempt = await FindAttemptAsync(LogisticsCommands.ApproveDispatchNoteV1, IdempotencyKeyHash(envelope.IdempotencyKey), token);
        await DispatchApprovalFenceAsync(token);
        if (attempt is null || attempt.Outcome != AttemptOutcomeRecorded) throw new InvalidOperationException("Approval attempt missing.");
        RequireSameContent(attempt, envelope.ActorId.Trim(), requestHash);
        // Old native receipts remain replayable, but are not promoted to private Work approval proof from a mutable current view.
        if (attempt.ResultJson is null) return null;
        using var document = JsonDocument.Parse(attempt.ResultJson);
        string[] receiptKeys = ["companyId", "commandName", "instructionId", "dispatchNoteId", "expectedVersion", "approvedVersion",
            "approvedBy", "approvedAtUtc", "originalCorrelationId", "requestHash"];
        if (document.RootElement.ValueKind != JsonValueKind.Object
            || document.RootElement.EnumerateObject().Select(p => p.Name).Order(StringComparer.Ordinal)
                .SequenceEqual(receiptKeys.Order(StringComparer.Ordinal)) is false)
            throw new InvalidOperationException("Immutable approval receipt shape is invalid.");
        var receipt = JsonSerializer.Deserialize<LogisticsDispatchNoteApprovalReceiptV1>(attempt.ResultJson, Json);
        if (receipt is null || receipt.CompanyId != RequireResolvedCompany() || receipt.CommandName != LogisticsCommands.ApproveDispatchNoteV1
            || receipt.InstructionId != instructionId || attempt.InstructionId != instructionId || receipt.DispatchNoteId != dispatchNoteId
            || receipt.ExpectedVersion != expectedVersion || expectedVersion < 1 || expectedVersion == long.MaxValue
            || receipt.ApprovedVersion != expectedVersion + 1 || receipt.ApprovedBy != envelope.ActorId.Trim()
            || receipt.ApprovedAtUtc != attempt.RecordedAtUtc || receipt.OriginalCorrelationId != attempt.CorrelationId
            || receipt.RequestHash != requestHash)
            throw new InvalidOperationException("Immutable approval receipt does not match its original command.");
        return receipt;
    }
}
