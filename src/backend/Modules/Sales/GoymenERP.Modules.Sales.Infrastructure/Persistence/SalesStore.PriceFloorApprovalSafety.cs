using System.Data;
using GoymenERP.Modules.Sales.Application.Contracts;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace GoymenERP.Modules.Sales.Infrastructure.Persistence;

public sealed partial class SalesStore : ISalesPriceFloorApprovalStoreV1
{
    private OwnedPriceFloorApproval? ownedPriceFloorApproval;

    public bool PriceFloorApprovalEntryIsClean => officialCancellationAuditWriter is not null
        && ownedPriceFloorApproval is null && ownedDiscountApproval is null && ownedOverDeliveryRequestApproval is null && ownedRiskExceptionDecision is null && ownedContractDiscountDecision is null && ownedRemainderDecision is null && ownedCustomerReturnDecision is null && overDeliveryRequestDecisionReadFence is null && riskExceptionDecisionReadFence is null && remainderDecisionReadFence is null && ownedRequestApproval is null && ownedQuoteApproval is null && quoteDecisionReadFence is null && dbContext.Database.IsRelational()
        && System.Transactions.Transaction.Current is null && dbContext.Database.CurrentTransaction is null
        && !dbContext.ChangeTracker.HasChanges() && !officialCancellationAuditWriter.HasPendingDrafts;

    public async Task<SalesOrderDraftResponse> RunPriceFloorApprovalAsync(SalesDraftWriteEnvelope envelope, string requestHash,
        bool cleanBeforeAuthority, Func<CancellationToken, Task<SalesOrderDraftResponse>> execute,
        Func<CancellationToken, Task<SalesOrderDraftResponse?>> readOriginalKey,
        Func<CancellationToken, Task> freshFence, CancellationToken cancellationToken)
    {
        RequireEnvelope(envelope); RequireHash(requestHash);
        const string commandName = SalesDraftCommands.ApprovePriceFloorExceptionsV1;
        try { return await AttemptAsync(false); }
        catch (PriceFloorApprovalLostRace lost)
        {
            // The first transaction has been fully aborted and disposed. This pass can ONLY read this original key.
            try { return await AttemptAsync(true); }
            catch (PriceFloorApprovalNoWinner)
            {
                if (lost.NativeConcurrentChange)
                    throw Refused(SalesDraftStoreCodes.ConcurrentChange, "Taban fiyat istisnası eşzamanlı değişti.");
                throw PriceFloorApprovalUnavailable();
            }
        }

        async Task<SalesOrderDraftResponse> AttemptAsync(bool replayOnly)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var cleanBefore = PriceFloorApprovalEntryIsClean;
            await freshFence(cancellationToken); cancellationToken.ThrowIfCancellationRequested();
            if (!cleanBeforeAuthority || !cleanBefore || !PriceFloorApprovalEntryIsClean) throw PriceFloorApprovalUnavailable();
            var state = new OwnedPriceFloorApproval(RequireResolvedCompany(), envelope, requestHash, commandName, freshFence, replayOnly);
            IDbContextTransaction? transaction = null;
            var committed = false;
            var commitStarted = false;
            var disposing = false;
            ownedPriceFloorApproval = state;
            try
            {
                transaction = await dbContext.Database.BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken);
                await PriceFloorApprovalFenceAsync(cancellationToken);
                SalesOrderDraftResponse? result = null;
                SalesDraftServiceException? terminal = null;
                try
                {
                    result = replayOnly ? await readOriginalKey(cancellationToken) : await execute(cancellationToken);
                    if (result is null) throw new PriceFloorApprovalNoWinner();
                }
                catch (SalesDraftServiceException refusal) when (refusal.IsReplay
                    || state.Saved && state.Terminal is { } recorded && recorded.Status == refusal.Status && recorded.Code == refusal.Code)
                {
                    // A recorded terminal native refusal is itself this command's durable answer.
                    terminal = refusal;
                }
                if (result is not null)
                    result = result with { OriginalFloorApprovalReceipt = await ReadOriginalPriceFloorReceiptAsync(
                        envelope, requestHash, result, cancellationToken) };
                await PriceFloorApprovalFenceAsync(cancellationToken); RequireFlushed();
                if (terminal is null && result is { IsReplay: false } && !state.Saved)
                    throw new InvalidOperationException("Taban fiyat istisnası kararı kaydedilmiş bir deneme taşımıyor.");
                await PriceFloorApprovalFenceAsync(cancellationToken); RequireFlushed();
                commitStarted = true;
                await transaction.CommitAsync(cancellationToken); committed = true;
                var completed = transaction; transaction = null;
                disposing = true; await completed.DisposeAsync(); disposing = false;
                // A release denial suppresses the response, not an already committed native decision.
                await PriceFloorApprovalFenceAsync(cancellationToken); RequireFlushed();
                if (terminal is not null) throw terminal;
                return result!;
            }
            catch (Exception failure)
            {
                var cause = failure is PriceFloorApprovalPersistenceFailure save ? save.Cause : failure;
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
                if (cleanup.Count != 0 || disposing) throw new AggregateException("Taban fiyat istisnası işlem temizliği başarısız; tekrar okuma güvenli değil.", [failure, .. cleanup]);
                await PriceFloorApprovalFenceAsync(cancellationToken);
                if (!replayOnly && !commitStarted && !committed && (transientRace || nativeRace)) throw new PriceFloorApprovalLostRace(nativeRace);
                if (failure is PriceFloorApprovalPersistenceFailure) throw PriceFloorApprovalUnavailable();
                if (replayOnly && (transientRace || nativeRace)) throw PriceFloorApprovalUnavailable();
                if (failure is not (SalesDraftServiceException or SalesDraftStoreException or OperationCanceledException or AggregateException or PriceFloorApprovalNoWinner))
                    throw PriceFloorApprovalUnavailable();
                throw;
            }
            finally { ownedPriceFloorApproval = null; }

            void RequireFlushed()
            {
                if (officialCancellationAuditWriter!.HasPendingDrafts || dbContext.ChangeTracker.HasChanges())
                    throw new InvalidOperationException("Taban fiyat istisnası işleminin kaydedilmemiş işi veya denetim taslağı kaldı.");
            }
        }
    }

    private async Task PriceFloorApprovalFenceAsync(CancellationToken token)
    {
        if (ownedPriceFloorApproval is not { } state) return;
        token.ThrowIfCancellationRequested(); RequireCompany(state.CompanyId);
        await state.FreshFence(token);
        token.ThrowIfCancellationRequested(); RequireCompany(state.CompanyId);
    }

    private void RequirePriceFloorApprovalCommand(SalesDraftWriteEnvelope envelope, string hash, string command, bool writes)
    {
        if (ownedPriceFloorApproval is not { } state) return;
        if (state.Command != command || state.Hash != hash || state.Envelope.ActorId != envelope.ActorId
            || state.Envelope.CorrelationId != envelope.CorrelationId
            || IdempotencyKeyHash(state.Envelope.IdempotencyKey) != IdempotencyKeyHash(envelope.IdempotencyKey)
            || writes && state.ReplayOnly)
            throw new InvalidOperationException("Taban fiyat istisnası işlemi başka komuta veya ikinci yazma denemesine geçemez.");
    }

    private async Task SavePriceFloorApprovalAsync(CancellationToken token, SalesDraftTerminalRefusal? terminal = null)
    {
        if (ownedPriceFloorApproval is not { } state) { await SaveAsync(token); return; }
        if (state.ReplayOnly) throw new InvalidOperationException("Kazanan denemesi yalnız okunabilir.");
        await PriceFloorApprovalFenceAsync(token);
        try
        {
            // A successful decision always enqueues one native Audit draft; a terminal refusal enqueues none.
            if (terminal is null && !officialCancellationAuditWriter!.HasPendingDrafts)
                throw new InvalidOperationException("Taban fiyat istisnası ile aynı scoped denetim toplayıcısı gerekli.");
            if (officialCancellationAuditWriter!.HasPendingDrafts)
            {
                await officialCancellationAuditWriter.WritePendingAsync(dbContext.Database.CurrentTransaction!.GetDbTransaction(), token);
                await PriceFloorApprovalFenceAsync(token);
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
        catch (Exception failure) { throw new PriceFloorApprovalPersistenceFailure(failure); }
        await PriceFloorApprovalFenceAsync(token);
    }

    private static SalesDraftStoreException PriceFloorApprovalUnavailable() =>
        new(SalesDraftProblemCodesV1.OwnerReferenceUnavailable, "Taban fiyat istisnası güvenli karar sınırı kullanılamıyor.");
    private sealed record OwnedPriceFloorApproval(Guid CompanyId, SalesDraftWriteEnvelope Envelope, string Hash, string Command,
        Func<CancellationToken, Task> FreshFence, bool ReplayOnly)
    {
        internal bool Saved;
        internal SalesDraftTerminalRefusal? Terminal;
    }
    private sealed class PriceFloorApprovalLostRace(bool nativeConcurrentChange) : Exception
    { internal bool NativeConcurrentChange { get; } = nativeConcurrentChange; }
    private sealed class PriceFloorApprovalNoWinner : Exception;
    private sealed class PriceFloorApprovalPersistenceFailure(Exception cause) : Exception("Price-floor approval persistence failed.", cause)
    { internal Exception Cause { get; } = cause; }
}
