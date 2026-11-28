using System.Data;
using GoymenERP.Modules.Sales.Application.Contracts;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace GoymenERP.Modules.Sales.Infrastructure.Persistence;

public sealed partial class SalesStore : ISalesQuoteApprovalStoreV1
{
    private OwnedQuoteApproval? ownedQuoteApproval;
    public bool QuoteApprovalEntryIsClean => officialCancellationAuditWriter is not null && ownedQuoteApproval is null && quoteDecisionReadFence is null
        && ownedRequestApproval is null && ownedPriceFloorApproval is null && ownedDiscountApproval is null && ownedOverDeliveryRequestApproval is null && ownedRiskExceptionDecision is null && ownedContractDiscountDecision is null && ownedRemainderDecision is null && overDeliveryRequestDecisionReadFence is null && riskExceptionDecisionReadFence is null && dbContext.Database.IsRelational() && System.Transactions.Transaction.Current is null
        && dbContext.Database.CurrentTransaction is null && !dbContext.ChangeTracker.HasChanges() && !officialCancellationAuditWriter.HasPendingDrafts;

    public async Task<T> RunQuoteApprovalAsync<T>(SalesDraftWriteEnvelope envelope, string requestHash, string commandName,
        bool cleanBeforeAuthority, Func<CancellationToken, Task<T>> execute, Func<CancellationToken, Task<T?>> readOriginalKey,
        Func<T, bool> isReplay, Func<CancellationToken, Task> freshFence, CancellationToken cancellationToken, SalesQuoteDecisionCommandV1? decision = null) where T : class
    {
        RequireEnvelope(envelope); RequireHash(requestHash);
        if (commandName is not (SalesQuoteCommands.ApproveExpiredConversionV1 or SalesApprovalRequestCommands.RejectQuoteConversionV1))
            throw new ArgumentException("Only the native expired-quote decisions own this boundary.", nameof(commandName));
        try { return await AttemptAsync(false); }
        catch (QuoteApprovalLostRace lost)
        {
            try { return await AttemptAsync(true); }
            catch (QuoteApprovalNoWinner)
            {
                if (lost.NativeConcurrentChange) throw Refused(SalesDraftStoreCodes.ConcurrentChange, "Teklif kararı eşzamanlı değişti.");
                throw QuoteApprovalUnavailable();
            }
        }

        async Task<T> AttemptAsync(bool replayOnly)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var cleanBefore = QuoteApprovalEntryIsClean;
            await freshFence(cancellationToken); cancellationToken.ThrowIfCancellationRequested();
            if (!cleanBeforeAuthority || !cleanBefore || !QuoteApprovalEntryIsClean) throw QuoteApprovalUnavailable();
            var state = new OwnedQuoteApproval(RequireResolvedCompany(), envelope, requestHash, commandName, freshFence, replayOnly) { Decision = decision };
            IDbContextTransaction? transaction = null;
            var committed = false; var disposing = false;
            ownedQuoteApproval = state;
            try
            {
                transaction = await dbContext.Database.BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken);
                await QuoteApprovalFenceAsync(cancellationToken);
                T? result = null; SalesDraftServiceException? terminal = null;
                try
                {
                    result = replayOnly ? await readOriginalKey(cancellationToken) : await execute(cancellationToken);
                    if (result is null) throw new QuoteApprovalNoWinner();
                }
                catch (SalesDraftServiceException refusal) when (refusal.IsReplay
                    || state.Saved && state.Terminal is { } recorded && recorded.Status == refusal.Status && recorded.Code == refusal.Code)
                { terminal = refusal; }
                if (terminal is null && result is not null && decision is not null)
                    result = await AttachQuoteDecisionReceiptAsync(result, state, cancellationToken);
                await QuoteApprovalFenceAsync(cancellationToken); RequireFlushed();
                if (terminal is null && result is not null && !isReplay(result) && !state.Saved)
                    throw new InvalidOperationException("Native quote decision has no saved attempt.");
                await QuoteApprovalFenceAsync(cancellationToken); RequireFlushed();
                await transaction.CommitAsync(cancellationToken); committed = true;
                disposing = true; await transaction.DisposeAsync(); transaction = null; disposing = false;
                await QuoteApprovalFenceAsync(cancellationToken); RequireFlushed();
                // A release refusal does not undo an already committed native decision.
                if (terminal is not null) throw terminal;
                return result!;
            }
            catch (Exception failure)
            {
                var cause = failure is QuoteApprovalPersistenceFailure save ? save.Cause : failure;
                // Reuse the existing provider classifier; no localized-message or reflection classification.
                var deadlock = SqlNumber(cause) == 1205;
                var transientRace = deadlock || SqlNumber(cause) == 1222;
                var nativeRace = cause is DbUpdateConcurrencyException || SqlNumber(cause) is 2601 or 2627;
                var cleanup = new List<Exception>();
                if (transaction is not null)
                {
                    if (!committed) try
                    {
                        if (!deadlock || transaction.GetDbTransaction() is not SqlTransaction { Connection: null })
                            await transaction.RollbackAsync(CancellationToken.None);
                    }
                    catch (Exception error) { cleanup.Add(error); }
                    try { await transaction.DisposeAsync(); } catch (Exception error) { cleanup.Add(error); }
                }
                try { dbContext.ChangeTracker.Clear(); } catch (Exception error) { cleanup.Add(error); }
                try { officialCancellationAuditWriter!.DiscardPending(); } catch (Exception error) { cleanup.Add(error); }
                if (cleanup.Count != 0 || disposing)
                    throw new AggregateException("Quote approval cleanup failed; reconciliation is unsafe.", [failure, .. cleanup]);
                // Includes non-recorded 404/422 after successful abort; caller state was rejected before ownership.
                await QuoteApprovalFenceAsync(cancellationToken);
                if (!replayOnly && !committed && (transientRace || nativeRace)) throw new QuoteApprovalLostRace(nativeRace);
                if (failure is QuoteApprovalPersistenceFailure || replayOnly && (transientRace || nativeRace)) throw QuoteApprovalUnavailable();
                if (failure is not (SalesDraftServiceException or SalesDraftStoreException or OperationCanceledException or AggregateException or QuoteApprovalNoWinner))
                    throw QuoteApprovalUnavailable();
                throw;
            }
            finally { ownedQuoteApproval = null; }

            void RequireFlushed()
            {
                if (officialCancellationAuditWriter!.HasPendingDrafts || dbContext.ChangeTracker.HasChanges())
                    throw new InvalidOperationException("Quote decision left unflushed changes or audit drafts.");
            }
        }
    }

    private async Task<T> QuoteApprovalReadAsync<T>(Func<Task<T>> read, CancellationToken token)
    {
        await QuoteApprovalFenceAsync(token);
        var result = await read();
        await QuoteApprovalFenceAsync(token);
        return result;
    }
    private async Task QuoteApprovalFenceAsync(CancellationToken token)
    {
        if (ownedQuoteApproval is not { } state)
        {
            if (quoteDecisionReadFence is not null) await quoteDecisionReadFence(token);
            return;
        }
        token.ThrowIfCancellationRequested(); RequireCompany(state.CompanyId);
        await state.FreshFence(token);
        token.ThrowIfCancellationRequested(); RequireCompany(state.CompanyId);
    }
    private void RequireQuoteApprovalCommand(SalesDraftWriteEnvelope envelope, string hash, string command, bool writes)
    {
        if (ownedQuoteApproval is not { } state) return;
        if (state.Command != command || state.Hash != hash || state.Envelope.ActorId != envelope.ActorId
            || state.Envelope.CorrelationId != envelope.CorrelationId
            || IdempotencyKeyHash(state.Envelope.IdempotencyKey) != IdempotencyKeyHash(envelope.IdempotencyKey)
            || writes && state.ReplayOnly)
            throw new InvalidOperationException("Quote approval cannot change command or retry mutation.");
    }
    private async Task SaveQuoteApprovalAsync(CancellationToken token, SalesDraftTerminalRefusal? terminal = null)
    {
        if (ownedQuoteApproval is not { } state) { await SaveAsync(token); return; }
        if (state.ReplayOnly) throw new InvalidOperationException("Original-key recovery is read-only.");
        await QuoteApprovalFenceAsync(token);
        try
        {
            if (terminal is null && !officialCancellationAuditWriter!.HasPendingDrafts)
                throw new InvalidOperationException("Quote approval needs the same scoped actual audit collector.");
            if (officialCancellationAuditWriter!.HasPendingDrafts)
            {
                await officialCancellationAuditWriter.WritePendingAsync(dbContext.Database.CurrentTransaction!.GetDbTransaction(), token);
                await QuoteApprovalFenceAsync(token);
            }
            if (officialCancellationAuditWriter.HasPendingDrafts) throw new InvalidOperationException("Audit drafts were not drained.");
            await dbContext.SaveChangesAsync(token);
            state.Saved = true; state.Terminal = terminal;
        }
        catch (OperationCanceledException) { throw; }
        catch (AggregateException) { throw; }
        catch (SalesDraftServiceException refusal) when (refusal.Status is 401 or 403) { throw; }
        // Escape native refusal/race catches even when an interceptor throws after EF AcceptAllChanges.
        catch (Exception failure) { throw new QuoteApprovalPersistenceFailure(failure); }
        await QuoteApprovalFenceAsync(token);
    }
    private static SalesDraftStoreException QuoteApprovalUnavailable() =>
        new(SalesDraftProblemCodesV1.OwnerReferenceUnavailable, "Teklif güvenli karar sınırı kullanılamıyor.");
    private sealed record OwnedQuoteApproval(Guid CompanyId, SalesDraftWriteEnvelope Envelope, string Hash, string Command,
        Func<CancellationToken, Task> FreshFence, bool ReplayOnly)
    { internal bool Saved; internal SalesDraftTerminalRefusal? Terminal; internal SalesQuoteDecisionCommandV1? Decision; }
    private sealed class QuoteApprovalLostRace(bool nativeConcurrentChange) : Exception
    { internal bool NativeConcurrentChange { get; } = nativeConcurrentChange; }
    private sealed class QuoteApprovalNoWinner : Exception;
    private sealed class QuoteApprovalPersistenceFailure(Exception cause) : Exception("Quote persistence failed.", cause)
    { internal Exception Cause { get; } = cause; }
}
