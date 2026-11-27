using GoymenERP.Modules.Inventory.Application;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace GoymenERP.Modules.Inventory.Infrastructure.Persistence;

public sealed partial class InventoryStore
{
    private Func<CancellationToken, Task>? lossDecisionFence;
    private bool lossDecisionReceiptProven;

    private async Task<InventoryTransferReceiptResultV1?> RunOwnedLossDecisionAsync(Guid transferId, Guid lineId,
        DecideInventoryTransferLossV1 request, InventoryCommandMetadata metadata,
        Func<CancellationToken, Task> freshAuthorityFence, bool replayOnly, CancellationToken token)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(freshAuthorityFence);
        // Entry refusals cannot consume or clear another caller's handles, changes or audit drafts.
        if (lossDecisionAuditWriter is null) throw TransferError(503, InventoryTransferCodesV1.Unavailable);
        if (System.Transactions.Transaction.Current is not null || !dbContext.Database.IsRelational()
            || dbContext.Database.CurrentTransaction is not null || dbContext.ChangeTracker.HasChanges()
            || lossDecisionAuditWriter.HasPendingDrafts || lossDecisionFence is not null)
            throw new InvalidOperationException("A transfer loss decision requires its own clean Inventory transaction.");
        RequireTransferCall(metadata);
        var parsed = LossDecisionPayload(transferId, lineId, request);
        RequireResolvedCompany();
        var companyId = companyContext.CompanyId;
        async Task Fence(CancellationToken current)
        {
            RequireResolvedCompany();
            if (companyContext.CompanyId != companyId) throw TransferError(403, InventoryTransferCodesV1.Forbidden);
            await freshAuthorityFence(current);
            RequireResolvedCompany();
            if (companyContext.CompanyId != companyId) throw TransferError(403, InventoryTransferCodesV1.Forbidden);
        }
        try
        {
            lossDecisionFence = Fence;
            lossDecisionReceiptProven = false;
            await using var owned = await dbContext.Database.BeginTransactionAsync(token);
            try
            {
                await Fence(token);
                var result = replayOnly
                    ? await FindReceiptKeyReplayAsync(DecideTransferLossCommand, GenealogyHash(metadata.IdempotencyKey.Trim()),
                        parsed.PayloadHash, metadata.ActorId, Fence, token)
                    : await DecideLossCoreAsync(transferId, lineId, request, metadata, Fence, token);
                await Fence(token);
                // Only a successfully stored native command or a real immutable key replay can commit.
                // A different-key prior domain decision remains the existing read-only answer, never a new receipt.
                if (lossDecisionReceiptProven)
                {
                    if (lossDecisionAuditWriter.HasPendingDrafts)
                    {
                        await lossDecisionAuditWriter.WritePendingAsync(owned.GetDbTransaction(), token);
                        await Fence(token);
                    }
                    if (lossDecisionAuditWriter.HasPendingDrafts)
                        throw new InvalidOperationException("A loss decision cannot commit pending audit drafts.");
                    await Fence(token);
                    await owned.CommitAsync(token);
                }
                else
                {
                    await owned.RollbackAsync(CancellationToken.None);
                    dbContext.ChangeTracker.Clear();
                }
                return result is null ? null : result with { CommandReceiptProven = lossDecisionReceiptProven };
            }
            catch (Exception exception)
            {
                await owned.RollbackAsync(CancellationToken.None);
                await owned.DisposeAsync();
                dbContext.ChangeTracker.Clear();
                lossDecisionAuditWriter.DiscardPending();
                if (exception is DbUpdateException update
                    && (update is DbUpdateConcurrencyException || IsUniqueConstraintViolation(update)))
                    throw new LossDecisionSaveRace(update);
                if (negativeStockAttempts is not null) await RecordPhysicalRefusalAfterRollbackAsync(exception);
                throw;
            }
        }
        finally
        {
            lossDecisionFence = null;
            lossDecisionReceiptProven = false;
            negativeValidationTransaction = null;
            negativeCountBoundaries.Clear();
            lossDecisionAuditWriter.DiscardPending();
        }
    }

    private sealed class LossDecisionSaveRace(Exception inner) : Exception("Loss decision save race.", inner);
}
