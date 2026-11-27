using System.Data;
using GoymenERP.Modules.Inventory.Application;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace GoymenERP.Modules.Inventory.Infrastructure.Persistence;

public sealed partial class InventoryStore
{
    private bool stockScrapCommandActive;

    private async Task SaveStockScrapAsync(Func<CancellationToken, Task> fence, CancellationToken token)
    {
        await fence(token);
        // Flush this command's real scoped collector in its existing TX before SaveChanges. A later
        // SavingChanges interceptor refusal must not leave an audit interceptor's attached batch for a retry.
        await lossDecisionAuditWriter!.WritePendingAsync(dbContext.Database.CurrentTransaction!.GetDbTransaction(), token);
        await fence(token);
        if (lossDecisionAuditWriter.HasPendingDrafts) throw StockScrapError(503, InventoryStockScrapCodesV1.Unavailable);
        await dbContext.SaveChangesAsync(token);
        await fence(token);
    }

    private async Task<InventoryStockScrapResultV1> RunStockScrapCommandAsync(
        Func<Func<CancellationToken, Task>, bool, Task<InventoryStockScrapResultV1>> command,
        Func<CancellationToken, Task> freshAuthorityFence, bool decision, CancellationToken token)
    {
        ArgumentNullException.ThrowIfNull(freshAuthorityFence);
        try
        {
            return await RunOwnedStockScrapAsync(command, freshAuthorityFence, decision, false, token);
        }
        catch (StockScrapSaveRace)
        {
            // Dispose and discard the failed mutation before reading a winner in one fresh, read-only transaction.
            // No mutation retry or durable concurrency refusal is produced here.
            return await RunOwnedStockScrapAsync(command, freshAuthorityFence, decision, true, token);
        }
    }

    private async Task<InventoryStockScrapResultV1> RunOwnedStockScrapAsync(
        Func<Func<CancellationToken, Task>, bool, Task<InventoryStockScrapResultV1>> command,
        Func<CancellationToken, Task> freshAuthorityFence, bool decision, bool replayOnly, CancellationToken token)
    {
        // Refuse entry outside cleanup: another caller's transaction, tracker and audit drafts belong to that caller.
        if (lossDecisionAuditWriter is null || System.Transactions.Transaction.Current is not null
            || !dbContext.Database.IsRelational() || dbContext.Database.CurrentTransaction is not null
            || dbContext.ChangeTracker.HasChanges() || lossDecisionAuditWriter.HasPendingDrafts
            || stockScrapCommandActive)
            throw StockScrapError(503, InventoryStockScrapCodesV1.Unavailable);
        RequireResolvedCompany();
        var companyId = companyContext.CompanyId;
        async Task Fence(CancellationToken current)
        {
            if (!companyContext.IsResolved || companyContext.CompanyId != companyId)
                throw StockScrapError(403, InventoryStockScrapCodesV1.Forbidden);
            await freshAuthorityFence(current);
            if (!companyContext.IsResolved || companyContext.CompanyId != companyId)
                throw StockScrapError(403, InventoryStockScrapCodesV1.Forbidden);
        }

        stockScrapCommandActive = true;
        try
        {
            await using var owned = await dbContext.Database.BeginTransactionAsync(
                decision ? IsolationLevel.Serializable : IsolationLevel.ReadCommitted, token);
            try
            {
                await Fence(token);
                var result = await command(Fence, replayOnly);
                await Fence(token);
                // AuditSaveChangesInterceptor may have flushed already; without it use the same scoped writer/TX.
                if (lossDecisionAuditWriter.HasPendingDrafts)
                {
                    await lossDecisionAuditWriter.WritePendingAsync(owned.GetDbTransaction(), token);
                    await Fence(token);
                }
                if (lossDecisionAuditWriter.HasPendingDrafts)
                    throw StockScrapError(503, InventoryStockScrapCodesV1.Unavailable);
                await Fence(token);
                await owned.CommitAsync(token);
                return result;
            }
            catch (Exception exception)
            {
                // Even an unconfirmed rollback or failed dispose must not retain this owned attempt's
                // tracked mutation/audit drafts. Cleanup errors escape here, before any winner replay.
                try
                {
                    await owned.RollbackAsync(CancellationToken.None);
                }
                finally
                {
                    try
                    {
                        await owned.DisposeAsync();
                    }
                    finally
                    {
                        try { dbContext.ChangeTracker.Clear(); }
                        finally { lossDecisionAuditWriter.DiscardPending(); }
                    }
                }
                if (exception is DbUpdateException update
                    && (update is DbUpdateConcurrencyException || IsUniqueConstraintViolation(update)))
                {
                    if (replayOnly) throw StockScrapError(409, InventoryStockScrapCodesV1.ConcurrentChange, update);
                    throw new StockScrapSaveRace(update);
                }
                await RecordPhysicalRefusalAfterRollbackAsync(exception);
                throw;
            }
        }
        finally
        {
            stockScrapCommandActive = false;
            negativeValidationTransaction = null;
            negativeCountBoundaries.Clear();
        }
    }

    private sealed class StockScrapSaveRace(Exception inner) : Exception("Stock scrap save race.", inner);
}
