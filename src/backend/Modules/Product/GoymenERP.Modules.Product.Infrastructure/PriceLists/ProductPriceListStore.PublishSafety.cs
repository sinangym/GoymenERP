using System.Data;
using GoymenERP.Modules.Audit.Application.Contracts;
using GoymenERP.Modules.Product.Application.Contracts;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace GoymenERP.Modules.Product.Infrastructure.PriceLists;

internal sealed partial class ProductPriceListStore
{
    private OwnedPublish? ownedPublish;
    internal bool IsPublishReplayOnly => ownedPublish is { ReplayOnly: true };

    internal async Task<PriceListResultV1> RunOwnedPublishAsync(IAuditTransactionWriterV1? writer,
        Func<CancellationToken, Task> fence, Func<CancellationToken, Task<PriceListResultV1>> execute, CancellationToken token)
    {
        try { return await Run(false); }
        catch (PublishSaveRace) { return await Run(true); }

        async Task<PriceListResultV1> Run(bool replayOnly)
        {
            token.ThrowIfCancellationRequested();
            // Outside owned cleanup: a refused entry must preserve the caller's tracked work, collector and transaction.
            Preflight();
            // Initial authentication/company refusal still owns nothing: do not clear or discard caller state.
            await fence(token);
            // The await may have admitted another same-scope caller. Never overwrite its lease or clear its new work.
            Preflight();
            var state = new OwnedPublish(replayOnly, writer!, fence);
            IDbContextTransaction? transaction = null; var committed = false;
            try
            {
                ownedPublish = state;
                transaction = await dbContext.Database.BeginTransactionAsync(IsolationLevel.Serializable, token);
                await fence(token);
                PriceListResultV1? answer = null; ProductPriceListException? refusal = null;
                try { answer = await execute(token); }
                catch (ProductPriceListException error) when (state.RefusalSaved || state.ReplayProven) { refusal = error; }
                await fence(token);
                if (answer is not null && !(answer.IsReplay ? state.ReplayProven : state.Saved))
                    throw new InvalidOperationException("Publish success has no native stored receipt.");
                if (writer!.HasPendingDrafts || dbContext.ChangeTracker.HasChanges())
                    throw new InvalidOperationException("Publish has unflushed owner work or Audit.");
                await fence(token);
                await transaction.CommitAsync(token); committed = true;
                var completed = transaction; transaction = null; await completed.DisposeAsync();
                await fence(token);
                if (refusal is not null) throw refusal;
                return answer ?? throw new InvalidOperationException("Publish result missing.");
            }
            catch (Exception failure)
            {
                var faults = new List<Exception>();
                if (transaction is not null)
                {
                    if (!committed) try { await transaction.RollbackAsync(CancellationToken.None); } catch (Exception error) { faults.Add(error); }
                    try { await transaction.DisposeAsync(); } catch (Exception error) { faults.Add(error); }
                }
                try { dbContext.ChangeTracker.Clear(); } catch (Exception error) { faults.Add(error); }
                try { writer!.DiscardPending(); } catch (Exception error) { faults.Add(error); }
                if (faults.Count > 0) throw new AggregateException("Publish cleanup failed; replay is unsafe.", [failure, .. faults]);
                if (!replayOnly && !committed && state.SaveAttempted && !state.Saved && failure is DbUpdateException)
                    throw new PublishSaveRace();
                throw;
            }
            finally { ownedPublish = null; }

            void Preflight()
            {
                token.ThrowIfCancellationRequested();
                if (writer is null || ownedPublish is not null || !dbContext.Database.IsRelational()
                    || System.Transactions.Transaction.Current is not null || dbContext.Database.CurrentTransaction is not null
                    || dbContext.ChangeTracker.HasChanges() || writer.HasPendingDrafts)
                    throw new ProductPriceListException(503, ProductPriceListCodesV1.Unavailable);
            }
        }
    }

    private async Task SaveOwnedPublishAsync(CancellationToken token)
    {
        var state = ownedPublish ?? throw new InvalidOperationException("Publish owned boundary absent.");
        if (state.ReplayOnly) throw new InvalidOperationException("Read-only publish cannot save.");
        await state.Fence(token);
        if (state.Writer.HasPendingDrafts)
        {
            await state.Writer.WritePendingAsync(dbContext.Database.CurrentTransaction!.GetDbTransaction(), token);
            await state.Fence(token);
        }
        if (state.Writer.HasPendingDrafts) throw new InvalidOperationException("Publish Audit remains pending.");
        state.SaveAttempted = true;
        await dbContext.SaveChangesAsync(token); state.Saved = true;
        await state.Fence(token);
    }

    private sealed record OwnedPublish(bool ReplayOnly, IAuditTransactionWriterV1 Writer, Func<CancellationToken, Task> Fence)
    { public bool SaveAttempted, Saved, RefusalSaved, ReplayProven; }
    private sealed class PublishSaveRace : Exception;
}
