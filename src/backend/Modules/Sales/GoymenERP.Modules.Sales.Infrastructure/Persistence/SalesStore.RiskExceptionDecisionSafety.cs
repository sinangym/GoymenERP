using System.Data;
using GoymenERP.Modules.Sales.Application.Contracts;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace GoymenERP.Modules.Sales.Infrastructure.Persistence;

public sealed partial class SalesStore : ISalesRiskExceptionDecisionSafetyStoreV1
{
    private OwnedRiskExceptionDecision? ownedRiskExceptionDecision;
    public bool RiskExceptionDecisionEntryIsClean => officialCancellationAuditWriter is not null && ownedRiskExceptionDecision is null && ownedOverDeliveryRequestApproval is null && ownedQuoteApproval is null && quoteDecisionReadFence is null
        && ownedRequestApproval is null && ownedPriceFloorApproval is null && ownedDiscountApproval is null && dbContext.Database.IsRelational() && System.Transactions.Transaction.Current is null
        && dbContext.Database.CurrentTransaction is null && !dbContext.ChangeTracker.HasChanges() && !officialCancellationAuditWriter.HasPendingDrafts;

    public async Task<SalesRiskExceptionCommandResult> RunRiskExceptionDecisionAsync(SalesDraftWriteEnvelope envelope, string requestHash, string commandName,
        bool cleanBeforeAuthority, Func<CancellationToken, Task<SalesRiskExceptionCommandResult>> execute, Func<CancellationToken, Task<SalesRiskExceptionCommandResult?>> readOriginalKey,
        Func<CancellationToken, Task> freshFence, CancellationToken cancellationToken)
    {
        RequireEnvelope(envelope); RequireHash(requestHash);
        if (commandName is not (SalesRiskExceptionCommands.ApproveV1 or SalesRiskExceptionCommands.RejectV1))
            throw new ArgumentException("Only the native risk exception decisions own this boundary.", nameof(commandName));
        try { return await AttemptAsync(false); }
        catch (RiskExceptionDecisionLostRace lost)
        {
            try { return await AttemptAsync(true); }
            catch (RiskExceptionDecisionNoWinner)
            {
                if (lost.NativeConcurrentChange) throw Refused(SalesDraftStoreCodes.ConcurrentChange, "Risk istisnası kararı eşzamanlı değişti.");
                throw RiskExceptionDecisionUnavailable();
            }
        }

        async Task<SalesRiskExceptionCommandResult> AttemptAsync(bool replayOnly)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var cleanBefore = RiskExceptionDecisionEntryIsClean;
            await freshFence(cancellationToken); cancellationToken.ThrowIfCancellationRequested();
            if (!cleanBeforeAuthority || !cleanBefore || !RiskExceptionDecisionEntryIsClean) throw RiskExceptionDecisionUnavailable();
            var state = new OwnedRiskExceptionDecision(RequireResolvedCompany(), envelope, requestHash, commandName, freshFence, replayOnly);
            IDbContextTransaction? transaction = null;
            var committed = false; var disposing = false;
            ownedRiskExceptionDecision = state;
            try
            {
                transaction = await dbContext.Database.BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken);
                await RiskExceptionDecisionFenceAsync(cancellationToken);
                SalesRiskExceptionCommandResult? result = null; SalesDraftServiceException? terminal = null;
                try
                {
                    result = replayOnly ? await readOriginalKey(cancellationToken) : await execute(cancellationToken);
                    if (result is null) throw new RiskExceptionDecisionNoWinner();
                }
                catch (SalesDraftServiceException refusal) when (refusal.IsReplay
                    || state.Saved && state.Terminal is { } recorded && recorded.Status == refusal.Status && recorded.Code == refusal.Code)
                { terminal = refusal; }
                await RiskExceptionDecisionFenceAsync(cancellationToken); RequireFlushed();
                if (terminal is null && result is not null && !result.IsReplay && !state.Saved)
                    throw new InvalidOperationException("Native risk exception decision has no saved attempt.");
                await RiskExceptionDecisionFenceAsync(cancellationToken); RequireFlushed();
                await transaction.CommitAsync(cancellationToken); committed = true;
                disposing = true; await transaction.DisposeAsync(); transaction = null; disposing = false;
                await RiskExceptionDecisionFenceAsync(cancellationToken); RequireFlushed();
                // A release refusal does not undo an already committed native decision.
                if (terminal is not null) throw terminal;
                return result!;
            }
            catch (Exception failure)
            {
                var cause = failure is RiskExceptionDecisionPersistenceFailure save ? save.Cause : failure;
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
                    throw new AggregateException("Risk exception approval cleanup failed; reconciliation is unsafe.", [failure, .. cleanup]);
                // Includes non-recorded 404/422 after successful abort; caller state was rejected before ownership.
                await RiskExceptionDecisionFenceAsync(cancellationToken);
                if (!replayOnly && !committed && (transientRace || nativeRace)) throw new RiskExceptionDecisionLostRace(nativeRace);
                if (failure is RiskExceptionDecisionPersistenceFailure || replayOnly && (transientRace || nativeRace)) throw RiskExceptionDecisionUnavailable();
                if (failure is not (SalesDraftServiceException or SalesDraftStoreException or OperationCanceledException or AggregateException or RiskExceptionDecisionNoWinner))
                    throw RiskExceptionDecisionUnavailable();
                throw;
            }
            finally { ownedRiskExceptionDecision = null; }

            void RequireFlushed()
            {
                if (officialCancellationAuditWriter!.HasPendingDrafts || dbContext.ChangeTracker.HasChanges())
                    throw new InvalidOperationException("Risk exception decision left unflushed changes or audit drafts.");
            }
        }
    }

    private async Task<T> RiskExceptionDecisionReadAsync<T>(Func<Task<T>> read, CancellationToken token)
    {
        await RiskExceptionDecisionFenceAsync(token);
        var result = await read();
        await RiskExceptionDecisionFenceAsync(token);
        return result;
    }
    // Root also relays shared FindDraft/Restore awaited reads here before enabling the service flag.
    private async Task RiskExceptionDecisionFenceAsync(CancellationToken token)
    {
        if (ownedRiskExceptionDecision is not { } state)
        {
            return;
        }
        token.ThrowIfCancellationRequested(); RequireCompany(state.CompanyId);
        await state.FreshFence(token);
        token.ThrowIfCancellationRequested(); RequireCompany(state.CompanyId);
    }
    private void RequireRiskExceptionDecisionCommand(SalesDraftWriteEnvelope envelope, string hash, string command, bool writes)
    {
        if (ownedRiskExceptionDecision is not { } state) return;
        if (state.Command != command || state.Hash != hash || state.Envelope.PermissionCode != envelope.PermissionCode
            || state.Envelope.ActorId != envelope.ActorId
            || state.Envelope.CorrelationId != envelope.CorrelationId
            || IdempotencyKeyHash(state.Envelope.IdempotencyKey) != IdempotencyKeyHash(envelope.IdempotencyKey)
            || writes && state.ReplayOnly)
            throw new InvalidOperationException("Risk exception approval cannot change command or retry mutation.");
    }
    private async Task SaveRiskExceptionDecisionAsync(CancellationToken token, SalesDraftTerminalRefusal? terminal = null)
    {
        if (ownedRiskExceptionDecision is not { } state) { await SaveAsync(token); return; }
        if (state.ReplayOnly) throw new InvalidOperationException("Original-key recovery is read-only.");
        await RiskExceptionDecisionFenceAsync(token);
        try
        {
            if (terminal is null && !officialCancellationAuditWriter!.HasPendingDrafts)
                throw new InvalidOperationException("Risk exception approval needs the same scoped actual audit collector.");
            if (officialCancellationAuditWriter!.HasPendingDrafts)
            {
                await officialCancellationAuditWriter.WritePendingAsync(dbContext.Database.CurrentTransaction!.GetDbTransaction(), token);
                await RiskExceptionDecisionFenceAsync(token);
            }
            if (officialCancellationAuditWriter.HasPendingDrafts) throw new InvalidOperationException("Audit drafts were not drained.");
            await dbContext.SaveChangesAsync(token);
            state.Saved = true; state.Terminal = terminal;
        }
        catch (OperationCanceledException) { throw; }
        catch (AggregateException) { throw; }
        catch (SalesDraftServiceException refusal) when (refusal.Status is 401 or 403) { throw; }
        // Escape native refusal/race catches even when an interceptor throws after EF AcceptAllChanges.
        catch (Exception failure) { throw new RiskExceptionDecisionPersistenceFailure(failure); }
        await RiskExceptionDecisionFenceAsync(token);
    }
    private static SalesDraftStoreException RiskExceptionDecisionUnavailable() =>
        new(SalesDraftProblemCodesV1.OwnerReferenceUnavailable, "Risk istisnası güvenli karar sınırı kullanılamıyor.");
    private sealed record OwnedRiskExceptionDecision(Guid CompanyId, SalesDraftWriteEnvelope Envelope, string Hash, string Command,
        Func<CancellationToken, Task> FreshFence, bool ReplayOnly)
    { internal bool Saved; internal SalesDraftTerminalRefusal? Terminal; }
    private sealed class RiskExceptionDecisionLostRace(bool nativeConcurrentChange) : Exception
    { internal bool NativeConcurrentChange { get; } = nativeConcurrentChange; }
    private sealed class RiskExceptionDecisionNoWinner : Exception;
    private sealed class RiskExceptionDecisionPersistenceFailure(Exception cause) : Exception("Risk exception persistence failed.", cause)
    { internal Exception Cause { get; } = cause; }
}
