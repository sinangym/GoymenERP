using System.Data;
using GoymenERP.Modules.Sales.Application.Contracts;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace GoymenERP.Modules.Sales.Infrastructure.Persistence;

public sealed partial class SalesStore : ISalesOverDeliveryRequestApprovalStoreV1
{
    private OwnedOverDeliveryRequestApproval? ownedOverDeliveryRequestApproval;
    public bool OverDeliveryRequestApprovalEntryIsClean => officialCancellationAuditWriter is not null && ownedRiskExceptionDecision is null && ownedRemainderDecision is null && ownedOverDeliveryRequestApproval is null && overDeliveryRequestDecisionReadFence is null && riskExceptionDecisionReadFence is null && ownedQuoteApproval is null && quoteDecisionReadFence is null
        && ownedRequestApproval is null && ownedPriceFloorApproval is null && ownedDiscountApproval is null && dbContext.Database.IsRelational() && System.Transactions.Transaction.Current is null
        && dbContext.Database.CurrentTransaction is null && !dbContext.ChangeTracker.HasChanges() && !officialCancellationAuditWriter.HasPendingDrafts;

    public async Task<SalesApprovalRequestCommandResult> RunOverDeliveryRequestApprovalAsync(SalesDraftWriteEnvelope envelope, string requestHash, string commandName,
        bool cleanBeforeAuthority, Func<CancellationToken, Task<SalesApprovalRequestCommandResult>> execute, Func<CancellationToken, Task<SalesApprovalRequestCommandResult?>> readOriginalKey,
        Func<CancellationToken, Task> freshFence, CancellationToken cancellationToken,
        SalesDraftStateCommandRequest? nativeRequest = null, SalesOverDeliveryRequestDecisionCommandV1? decision = null)
    {
        RequireEnvelope(envelope); RequireHash(requestHash);
        if (commandName is not (SalesApprovalRequestCommands.ApproveOverDeliveryV1 or SalesApprovalRequestCommands.RejectOverDeliveryV1))
            throw new ArgumentException("Only the numbered native over-delivery decisions own this boundary.", nameof(commandName));
        try { return await AttemptAsync(false); }
        catch (OverDeliveryRequestApprovalLostRace lost)
        {
            try { return await AttemptAsync(true); }
            catch (OverDeliveryRequestApprovalNoWinner)
            {
                if (lost.NativeConcurrentChange) throw Refused(SalesDraftStoreCodes.ConcurrentChange, "Fazla teslim talebi kararı eşzamanlı değişti.");
                throw OverDeliveryRequestApprovalUnavailable();
            }
        }

        async Task<SalesApprovalRequestCommandResult> AttemptAsync(bool replayOnly)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var cleanBefore = OverDeliveryRequestApprovalEntryIsClean;
            await freshFence(cancellationToken); cancellationToken.ThrowIfCancellationRequested();
            if (!cleanBeforeAuthority || !cleanBefore || !OverDeliveryRequestApprovalEntryIsClean) throw OverDeliveryRequestApprovalUnavailable();
            var state = new OwnedOverDeliveryRequestApproval(RequireResolvedCompany(), envelope, requestHash, commandName, freshFence, replayOnly) { NativeRequest = nativeRequest, Decision = decision };
            IDbContextTransaction? transaction = null;
            var committed = false; var disposing = false;
            ownedOverDeliveryRequestApproval = state;
            try
            {
                transaction = await dbContext.Database.BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken);
                await OverDeliveryRequestApprovalFenceAsync(cancellationToken);
                SalesApprovalRequestCommandResult? result = null; SalesDraftServiceException? terminal = null;
                try
                {
                    result = replayOnly ? await readOriginalKey(cancellationToken) : await execute(cancellationToken);
                    if (result is null) throw new OverDeliveryRequestApprovalNoWinner();
                }
                catch (SalesDraftServiceException refusal) when (refusal.IsReplay
                    || state.Saved && state.Terminal is { } recorded && recorded.Status == refusal.Status && recorded.Code == refusal.Code)
                { terminal = refusal; }
                if (terminal is null && result is not null && nativeRequest is not null)
                    result = await AttachOverDeliveryRequestReceiptAsync(result, state, cancellationToken);
                await OverDeliveryRequestApprovalFenceAsync(cancellationToken); RequireFlushed();
                if (terminal is null && result is not null && !result.IsReplay && !state.Saved)
                    throw new InvalidOperationException("Native over-delivery request decision has no saved attempt.");
                await OverDeliveryRequestApprovalFenceAsync(cancellationToken); RequireFlushed();
                await transaction.CommitAsync(cancellationToken); committed = true;
                disposing = true; await transaction.DisposeAsync(); transaction = null; disposing = false;
                await OverDeliveryRequestApprovalFenceAsync(cancellationToken); RequireFlushed();
                // A release refusal does not undo an already committed native decision.
                if (terminal is not null) throw terminal;
                return result!;
            }
            catch (Exception failure)
            {
                var cause = failure is OverDeliveryRequestApprovalPersistenceFailure save ? save.Cause : failure;
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
                    throw new AggregateException("Over-delivery request approval cleanup failed; reconciliation is unsafe.", [failure, .. cleanup]);
                // Includes non-recorded 404/422 after successful abort; caller state was rejected before ownership.
                await OverDeliveryRequestApprovalFenceAsync(cancellationToken);
                if (!replayOnly && !committed && (transientRace || nativeRace)) throw new OverDeliveryRequestApprovalLostRace(nativeRace);
                if (failure is OverDeliveryRequestApprovalPersistenceFailure || replayOnly && (transientRace || nativeRace)) throw OverDeliveryRequestApprovalUnavailable();
                if (failure is not (SalesDraftServiceException or SalesDraftStoreException or OperationCanceledException or AggregateException or OverDeliveryRequestApprovalNoWinner))
                    throw OverDeliveryRequestApprovalUnavailable();
                throw;
            }
            finally { ownedOverDeliveryRequestApproval = null; }

            void RequireFlushed()
            {
                if (officialCancellationAuditWriter!.HasPendingDrafts || dbContext.ChangeTracker.HasChanges())
                    throw new InvalidOperationException("Over-delivery request decision left unflushed changes or audit drafts.");
            }
        }
    }

    private async Task<T> OverDeliveryRequestApprovalReadAsync<T>(Func<Task<T>> read, CancellationToken token)
    {
        await OverDeliveryRequestApprovalFenceAsync(token);
        var result = await read();
        await OverDeliveryRequestApprovalFenceAsync(token);
        return result;
    }
    private async Task OverDeliveryRequestApprovalFenceAsync(CancellationToken token)
    {
        if (ownedOverDeliveryRequestApproval is not { } state)
        {
            if (overDeliveryRequestDecisionReadFence is not null) await overDeliveryRequestDecisionReadFence(token);
            return;
        }
        token.ThrowIfCancellationRequested(); RequireCompany(state.CompanyId);
        await state.FreshFence(token);
        token.ThrowIfCancellationRequested(); RequireCompany(state.CompanyId);
    }
    private void RequireOverDeliveryRequestApprovalCommand(SalesDraftWriteEnvelope envelope, string hash, string command, bool writes)
    {
        if (ownedOverDeliveryRequestApproval is not { } state) return;
        if (state.Command != command || state.Hash != hash || state.Envelope.ActorId != envelope.ActorId
            || state.Envelope.CorrelationId != envelope.CorrelationId
            || IdempotencyKeyHash(state.Envelope.IdempotencyKey) != IdempotencyKeyHash(envelope.IdempotencyKey)
            || writes && state.ReplayOnly)
            throw new InvalidOperationException("Over-delivery request approval cannot change command or retry mutation.");
    }
    private Task SaveApprovalRequestDecisionAsync(CancellationToken token, SalesDraftTerminalRefusal? terminal = null) =>
        ownedOverDeliveryRequestApproval is null ? SaveQuoteApprovalAsync(token, terminal)
            : SaveOverDeliveryRequestApprovalAsync(token, terminal);

    private async Task SaveOverDeliveryRequestApprovalAsync(CancellationToken token, SalesDraftTerminalRefusal? terminal = null)
    {
        if (ownedOverDeliveryRequestApproval is not { } state) { await SaveAsync(token); return; }
        if (state.ReplayOnly) throw new InvalidOperationException("Original-key recovery is read-only.");
        await OverDeliveryRequestApprovalFenceAsync(token);
        try
        {
            if (terminal is null && !officialCancellationAuditWriter!.HasPendingDrafts)
                throw new InvalidOperationException("Over-delivery request approval needs the same scoped actual audit collector.");
            if (officialCancellationAuditWriter!.HasPendingDrafts)
            {
                await officialCancellationAuditWriter.WritePendingAsync(dbContext.Database.CurrentTransaction!.GetDbTransaction(), token);
                await OverDeliveryRequestApprovalFenceAsync(token);
            }
            if (officialCancellationAuditWriter.HasPendingDrafts) throw new InvalidOperationException("Audit drafts were not drained.");
            await dbContext.SaveChangesAsync(token);
            state.Saved = true; state.Terminal = terminal;
        }
        catch (OperationCanceledException) { throw; }
        catch (AggregateException) { throw; }
        catch (SalesDraftServiceException refusal) when (refusal.Status is 401 or 403) { throw; }
        // Escape native refusal/race catches even when an interceptor throws after EF AcceptAllChanges.
        catch (Exception failure) { throw new OverDeliveryRequestApprovalPersistenceFailure(failure); }
        await OverDeliveryRequestApprovalFenceAsync(token);
    }
    private static SalesDraftStoreException OverDeliveryRequestApprovalUnavailable() =>
        new(SalesDraftProblemCodesV1.OwnerReferenceUnavailable, "Fazla teslim güvenli karar sınırı kullanılamıyor.");
    private sealed record OwnedOverDeliveryRequestApproval(Guid CompanyId, SalesDraftWriteEnvelope Envelope, string Hash, string Command,
        Func<CancellationToken, Task> FreshFence, bool ReplayOnly)
    { internal bool Saved; internal SalesDraftTerminalRefusal? Terminal; internal SalesDraftStateCommandRequest? NativeRequest; internal SalesOverDeliveryRequestDecisionCommandV1? Decision; }
    private sealed class OverDeliveryRequestApprovalLostRace(bool nativeConcurrentChange) : Exception
    { internal bool NativeConcurrentChange { get; } = nativeConcurrentChange; }
    private sealed class OverDeliveryRequestApprovalNoWinner : Exception;
    private sealed class OverDeliveryRequestApprovalPersistenceFailure(Exception cause) : Exception("Over-delivery request persistence failed.", cause)
    { internal Exception Cause { get; } = cause; }
}
