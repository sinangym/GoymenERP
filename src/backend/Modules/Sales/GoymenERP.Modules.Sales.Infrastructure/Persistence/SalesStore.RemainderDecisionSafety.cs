using System.Data;
using GoymenERP.Modules.Sales.Application.Contracts;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace GoymenERP.Modules.Sales.Infrastructure.Persistence;

public sealed partial class SalesStore : ISalesRemainderDecisionSafetyStoreV1
{
    private OwnedRemainderDecision? ownedRemainderDecision;

    public bool RemainderDecisionEntryIsClean => officialCancellationAuditWriter is not null
        && ownedRemainderDecision is null && ownedCustomerReturnDecision is null && ownedPriceFloorApproval is null && ownedDiscountApproval is null && ownedOverDeliveryRequestApproval is null && ownedRiskExceptionDecision is null && ownedContractDiscountDecision is null && overDeliveryRequestDecisionReadFence is null && riskExceptionDecisionReadFence is null && contractDiscountDecisionReadFence is null && remainderDecisionReadFence is null && ownedRequestApproval is null && ownedQuoteApproval is null && quoteDecisionReadFence is null && dbContext.Database.IsRelational()
        && System.Transactions.Transaction.Current is null && dbContext.Database.CurrentTransaction is null
        && !dbContext.ChangeTracker.HasChanges() && !officialCancellationAuditWriter.HasPendingDrafts;

    public async Task<SalesOrderDraftResponse> RunRemainderDecisionAsync(SalesDraftWriteEnvelope envelope, string requestHash, string commandName,
        bool cleanBeforeAuthority, Func<CancellationToken, Task<SalesOrderDraftResponse>> execute,
        Func<CancellationToken, Task<SalesOrderDraftResponse?>> readOriginalKey,
        Func<CancellationToken, Task> freshFence, CancellationToken cancellationToken)
    {
        RequireEnvelope(envelope); RequireHash(requestHash);
        if (commandName is not (SalesDraftCommands.ApproveRemainderChangeV1 or SalesDraftCommands.RejectRemainderChangeV1))
            throw new ArgumentException("Only native remainder decisions own this boundary.", nameof(commandName));
        try { return await AttemptAsync(false); }
        catch (RemainderDecisionLostRace lost)
        {
            // The first transaction has been fully aborted and disposed. This pass can ONLY read this original key.
            try { return await AttemptAsync(true); }
            catch (RemainderDecisionNoWinner)
            {
                if (lost.NativeConcurrentChange)
                    throw Refused(SalesDraftStoreCodes.ConcurrentChange, "Satır kalanı kararı eşzamanlı değişti.");
                throw RemainderDecisionUnavailable();
            }
        }

        async Task<SalesOrderDraftResponse> AttemptAsync(bool replayOnly)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var cleanBefore = RemainderDecisionEntryIsClean;
            await freshFence(cancellationToken); cancellationToken.ThrowIfCancellationRequested();
            if (!cleanBeforeAuthority || !cleanBefore || !RemainderDecisionEntryIsClean) throw RemainderDecisionUnavailable();
            var state = new OwnedRemainderDecision(RequireResolvedCompany(), envelope, requestHash, commandName, freshFence, replayOnly);
            IDbContextTransaction? transaction = null;
            var committed = false;
            var commitStarted = false;
            var disposing = false;
            ownedRemainderDecision = state;
            try
            {
                transaction = await dbContext.Database.BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken);
                await RemainderDecisionFenceAsync(cancellationToken);
                SalesOrderDraftResponse? result = null;
                SalesDraftServiceException? terminal = null;
                try
                {
                    result = replayOnly ? await readOriginalKey(cancellationToken) : await execute(cancellationToken);
                    if (result is null) throw new RemainderDecisionNoWinner();
                }
                catch (SalesDraftServiceException refusal) when (refusal.IsReplay
                    || state.Saved && state.Terminal is { } recorded && recorded.Status == refusal.Status && recorded.Code == refusal.Code)
                {
                    // A recorded terminal native refusal is itself this command's durable answer.
                    terminal = refusal;
                }
                await RemainderDecisionFenceAsync(cancellationToken); RequireFlushed();
                if (terminal is null && result is { IsReplay: false } && !state.Saved)
                    throw new InvalidOperationException("Satır kalanı kararı kaydedilmiş bir deneme taşımıyor.");
                await RemainderDecisionFenceAsync(cancellationToken); RequireFlushed();
                commitStarted = true;
                await transaction.CommitAsync(cancellationToken); committed = true;
                var completed = transaction; transaction = null;
                disposing = true; await completed.DisposeAsync(); disposing = false;
                // A release denial suppresses the response, not an already committed native decision.
                await RemainderDecisionFenceAsync(cancellationToken); RequireFlushed();
                if (terminal is not null) throw terminal;
                return result!;
            }
            catch (Exception failure)
            {
                var cause = failure is RemainderDecisionPersistenceFailure save ? save.Cause : failure;
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
                if (cleanup.Count != 0 || disposing) throw new AggregateException("Satır kalanı kararı işlem temizliği başarısız; tekrar okuma güvenli değil.", [failure, .. cleanup]);
                await RemainderDecisionFenceAsync(cancellationToken);
                if (!replayOnly && !commitStarted && !committed && (transientRace || nativeRace)) throw new RemainderDecisionLostRace(nativeRace);
                if (failure is RemainderDecisionPersistenceFailure) throw RemainderDecisionUnavailable();
                if (replayOnly && (transientRace || nativeRace)) throw RemainderDecisionUnavailable();
                if (failure is not (SalesDraftServiceException or SalesDraftStoreException or OperationCanceledException or AggregateException or RemainderDecisionNoWinner))
                    throw RemainderDecisionUnavailable();
                throw;
            }
            finally { ownedRemainderDecision = null; }

            void RequireFlushed()
            {
                if (officialCancellationAuditWriter!.HasPendingDrafts || dbContext.ChangeTracker.HasChanges())
                    throw new InvalidOperationException("Satır kalanı kararı işleminin kaydedilmemiş işi veya denetim taslağı kaldı.");
            }
        }
    }

    private async Task RemainderDecisionFenceAsync(CancellationToken token)
    {
        if (ownedRemainderDecision is not { } state) return;
        token.ThrowIfCancellationRequested(); RequireCompany(state.CompanyId);
        await state.FreshFence(token);
        token.ThrowIfCancellationRequested(); RequireCompany(state.CompanyId);
    }

    private void RequireRemainderDecisionCommand(SalesDraftWriteEnvelope envelope, string hash, string command, bool writes)
    {
        if (writes && remainderDecisionReadFence is not null)
            throw new InvalidOperationException("Selected remainder read cannot dispatch a mutation.");
        if (ownedRemainderDecision is not { } state) return;
        if (state.Command != command || state.Hash != hash || state.Envelope.PermissionCode != envelope.PermissionCode || state.Envelope.ActorId != envelope.ActorId
            || state.Envelope.CorrelationId != envelope.CorrelationId
            || IdempotencyKeyHash(state.Envelope.IdempotencyKey) != IdempotencyKeyHash(envelope.IdempotencyKey)
            || writes && state.ReplayOnly)
            throw new InvalidOperationException("Satır kalanı kararı işlemi başka komuta veya ikinci yazma denemesine geçemez.");
    }

    private async Task SaveRemainderDecisionAsync(CancellationToken token, SalesDraftTerminalRefusal? terminal = null)
    {
        if (ownedRemainderDecision is not { } state) { await SaveAsync(token); return; }
        if (state.ReplayOnly) throw new InvalidOperationException("Kazanan denemesi yalnız okunabilir.");
        await RemainderDecisionFenceAsync(token);
        try
        {
            // A successful decision always enqueues one native Audit draft; a terminal refusal enqueues none.
            if (terminal is null && !officialCancellationAuditWriter!.HasPendingDrafts)
                throw new InvalidOperationException("Satır kalanı kararı ile aynı scoped denetim toplayıcısı gerekli.");
            if (officialCancellationAuditWriter!.HasPendingDrafts)
            {
                await officialCancellationAuditWriter.WritePendingAsync(dbContext.Database.CurrentTransaction!.GetDbTransaction(), token);
                await RemainderDecisionFenceAsync(token);
            }
            if (officialCancellationAuditWriter.HasPendingDrafts) throw new InvalidOperationException("Denetim taslakları boşaltılmadı.");
            await dbContext.SaveChangesAsync(token);
            state.Saved = true; state.Terminal = terminal;
        }
        catch (OperationCanceledException) { throw; }
        catch (AggregateException) { throw; }
        catch (SalesDraftServiceException refusal) when (refusal.Status is 401 or 403) { throw; }
        // Escape the native domain/refusal catches even if an interceptor throws after EF accepted the changes.
        catch (Exception failure) { throw new RemainderDecisionPersistenceFailure(failure); }
        await RemainderDecisionFenceAsync(token);
    }

    private static SalesDraftStoreException RemainderDecisionUnavailable() =>
        new(SalesDraftProblemCodesV1.OwnerReferenceUnavailable, "Satır kalanı kararı güvenli karar sınırı kullanılamıyor.");
    private sealed record OwnedRemainderDecision(Guid CompanyId, SalesDraftWriteEnvelope Envelope, string Hash, string Command,
        Func<CancellationToken, Task> FreshFence, bool ReplayOnly)
    {
        internal bool Saved;
        internal SalesDraftTerminalRefusal? Terminal;
    }
    private sealed class RemainderDecisionLostRace(bool nativeConcurrentChange) : Exception
    { internal bool NativeConcurrentChange { get; } = nativeConcurrentChange; }
    private sealed class RemainderDecisionNoWinner : Exception;
    private sealed class RemainderDecisionPersistenceFailure(Exception cause) : Exception("Remainder decision persistence failed.", cause)
    { internal Exception Cause { get; } = cause; }
}
