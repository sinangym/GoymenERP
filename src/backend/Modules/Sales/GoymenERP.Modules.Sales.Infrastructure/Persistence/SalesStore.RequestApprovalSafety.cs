using System.Data;
using GoymenERP.Modules.Sales.Application.Contracts;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace GoymenERP.Modules.Sales.Infrastructure.Persistence;

public sealed partial class SalesStore : ISalesRequestApprovalStoreV1
{
    private OwnedRequestApproval? ownedRequestApproval;

    public bool RequestApprovalEntryIsClean => officialCancellationAuditWriter is not null
        && ownedRequestApproval is null && ownedQuoteApproval is null && ownedPriceFloorApproval is null && ownedDiscountApproval is null && ownedOverDeliveryRequestApproval is null && quoteDecisionReadFence is null
        && dbContext.Database.IsRelational()
        && System.Transactions.Transaction.Current is null && dbContext.Database.CurrentTransaction is null
        && !dbContext.ChangeTracker.HasChanges() && !officialCancellationAuditWriter.HasPendingDrafts;

    public async Task<SalesRequestResponse> RunRequestApprovalAsync(SalesDraftWriteEnvelope envelope, string requestHash,
        string commandName, bool cleanBeforeAuthority, Func<CancellationToken, Task<SalesRequestResponse>> execute,
        Func<CancellationToken, Task<SalesRequestResponse?>> readOriginalKey,
        Func<CancellationToken, Task> freshFence, CancellationToken cancellationToken)
    {
        RequireEnvelope(envelope); RequireHash(requestHash);
        if (commandName is not (SalesRequestCommands.ApproveV1 or SalesRequestCommands.RejectV1))
            throw new ArgumentException("Yalnız satış talebi kararının işlem sınırı kullanılabilir.", nameof(commandName));
        try { return await AttemptAsync(false); }
        catch (RequestApprovalLostRace lost)
        {
            // The first transaction has been fully aborted and disposed. This pass can ONLY read this original key.
            try { return await AttemptAsync(true); }
            catch (RequestApprovalNoWinner)
            {
                if (lost.NativeConcurrentChange)
                    throw Refused(SalesDraftStoreCodes.ConcurrentChange, "Satış talebi eşzamanlı değişti.");
                throw RequestApprovalUnavailable();
            }
        }

        async Task<SalesRequestResponse> AttemptAsync(bool replayOnly)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var cleanBefore = RequestApprovalEntryIsClean;
            await freshFence(cancellationToken); cancellationToken.ThrowIfCancellationRequested();
            if (!cleanBeforeAuthority || !cleanBefore || !RequestApprovalEntryIsClean) throw RequestApprovalUnavailable();
            var state = new OwnedRequestApproval(RequireResolvedCompany(), envelope, requestHash, commandName, freshFence, replayOnly);
            IDbContextTransaction? transaction = null;
            var committed = false;
            var disposing = false;
            ownedRequestApproval = state;
            try
            {
                transaction = await dbContext.Database.BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken);
                await RequestApprovalFenceAsync(cancellationToken);
                SalesRequestResponse? result = null;
                SalesDraftServiceException? terminal = null;
                try
                {
                    result = replayOnly ? await readOriginalKey(cancellationToken) : await execute(cancellationToken);
                    if (result is null) throw new RequestApprovalNoWinner();
                }
                catch (SalesDraftServiceException refusal) when (refusal.IsReplay
                    || state.Saved && state.Terminal is { } recorded && recorded.Status == refusal.Status && recorded.Code == refusal.Code)
                {
                    // A recorded terminal native refusal is itself this command's durable answer.
                    terminal = refusal;
                }
                if (result is not null)
                    result = result with { OriginalDecisionReceipt = await ReadOriginalRequestDecisionReceiptAsync(
                        envelope, requestHash, commandName, result, cancellationToken) };
                await RequestApprovalFenceAsync(cancellationToken); RequireFlushed();
                if (terminal is null && result is { IsReplay: false } && !state.Saved)
                    throw new InvalidOperationException("Satış talebi kararı kaydedilmiş bir deneme taşımıyor.");
                await RequestApprovalFenceAsync(cancellationToken); RequireFlushed();
                await transaction.CommitAsync(cancellationToken); committed = true;
                var completed = transaction; transaction = null;
                disposing = true; await completed.DisposeAsync(); disposing = false;
                // A release denial suppresses the response, not an already committed native decision.
                await RequestApprovalFenceAsync(cancellationToken); RequireFlushed();
                if (terminal is not null) throw terminal;
                return result!;
            }
            catch (Exception failure)
            {
                var cause = failure is RequestApprovalPersistenceFailure save ? save.Cause : failure;
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
                if (cleanup.Count != 0 || disposing) throw new AggregateException("Satış talebi işlem temizliği başarısız; tekrar okuma güvenli değil.", [failure, .. cleanup]);
                await RequestApprovalFenceAsync(cancellationToken);
                if (!replayOnly && !committed && (transientRace || nativeRace)) throw new RequestApprovalLostRace(nativeRace);
                if (failure is RequestApprovalPersistenceFailure) throw RequestApprovalUnavailable();
                if (replayOnly && (transientRace || nativeRace)) throw RequestApprovalUnavailable();
                if (failure is not (SalesDraftServiceException or SalesDraftStoreException or OperationCanceledException or AggregateException or RequestApprovalNoWinner))
                    throw RequestApprovalUnavailable();
                throw;
            }
            finally { ownedRequestApproval = null; }

            void RequireFlushed()
            {
                if (officialCancellationAuditWriter!.HasPendingDrafts || dbContext.ChangeTracker.HasChanges())
                    throw new InvalidOperationException("Satış talebi işleminin kaydedilmemiş işi veya denetim taslağı kaldı.");
            }
        }
    }

    private async Task<T> RequestApprovalReadAsync<T>(Func<Task<T>> read, CancellationToken token)
    {
        await RequestApprovalFenceAsync(token);
        var result = await read();
        await RequestApprovalFenceAsync(token);
        return result;
    }

    private async Task RequestApprovalFenceAsync(CancellationToken token)
    {
        if (ownedRequestApproval is not { } state) return;
        token.ThrowIfCancellationRequested(); RequireCompany(state.CompanyId);
        await state.FreshFence(token);
        token.ThrowIfCancellationRequested(); RequireCompany(state.CompanyId);
    }

    private void RequireRequestApprovalCommand(SalesDraftWriteEnvelope envelope, string hash, string command, bool writes)
    {
        if (ownedRequestApproval is not { } state) return;
        if (state.Command != command || state.Hash != hash || state.Envelope.ActorId != envelope.ActorId
            || state.Envelope.CorrelationId != envelope.CorrelationId
            || IdempotencyKeyHash(state.Envelope.IdempotencyKey) != IdempotencyKeyHash(envelope.IdempotencyKey)
            || writes && state.ReplayOnly)
            throw new InvalidOperationException("Satış talebi işlemi başka komuta veya ikinci yazma denemesine geçemez.");
    }

    private async Task SaveRequestApprovalAsync(CancellationToken token, SalesDraftTerminalRefusal? terminal = null)
    {
        if (ownedRequestApproval is not { } state) { await SaveAsync(token); return; }
        if (state.ReplayOnly) throw new InvalidOperationException("Kazanan denemesi yalnız okunabilir.");
        await RequestApprovalFenceAsync(token);
        try
        {
            // A successful decision always enqueues one native Audit draft; a terminal refusal enqueues none.
            if (terminal is null && !officialCancellationAuditWriter!.HasPendingDrafts)
                throw new InvalidOperationException("Satış talebi ile aynı scoped denetim toplayıcısı gerekli.");
            if (officialCancellationAuditWriter!.HasPendingDrafts)
            {
                await officialCancellationAuditWriter.WritePendingAsync(dbContext.Database.CurrentTransaction!.GetDbTransaction(), token);
                await RequestApprovalFenceAsync(token);
            }
            if (officialCancellationAuditWriter.HasPendingDrafts) throw new InvalidOperationException("Denetim taslakları boşaltılmadı.");
            await dbContext.SaveChangesAsync(token);
            state.Saved = true; state.Terminal = terminal;
        }
        catch (OperationCanceledException) { throw; }
        catch (AggregateException) { throw; }
        catch (SalesDraftServiceException) { throw; }
        catch (SalesDraftStoreException) { throw; }
        // Escape the native domain/refusal catches even if an interceptor throws after EF accepted the changes.
        catch (Exception failure) { throw new RequestApprovalPersistenceFailure(failure); }
        await RequestApprovalFenceAsync(token);
    }

    private static int? SqlNumber(Exception failure) => failure switch
    {
        SqlException sql => sql.Number,
        DbUpdateException { InnerException: SqlException sql } => sql.Number,
        _ => null,
    };
    private static SalesDraftStoreException RequestApprovalUnavailable() =>
        new(SalesDraftProblemCodesV1.OwnerReferenceUnavailable, "Satış talebi güvenli karar sınırı kullanılamıyor.");
    private sealed record OwnedRequestApproval(Guid CompanyId, SalesDraftWriteEnvelope Envelope, string Hash, string Command,
        Func<CancellationToken, Task> FreshFence, bool ReplayOnly)
    {
        internal bool Saved;
        internal SalesDraftTerminalRefusal? Terminal;
    }
    private sealed class RequestApprovalLostRace(bool nativeConcurrentChange) : Exception
    { internal bool NativeConcurrentChange { get; } = nativeConcurrentChange; }
    private sealed class RequestApprovalNoWinner : Exception;
    private sealed class RequestApprovalPersistenceFailure(Exception cause) : Exception("Request approval persistence failed.", cause)
    { internal Exception Cause { get; } = cause; }
}
