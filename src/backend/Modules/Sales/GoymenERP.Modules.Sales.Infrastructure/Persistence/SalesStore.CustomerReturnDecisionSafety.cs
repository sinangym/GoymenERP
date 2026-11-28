using System.Data;
using GoymenERP.Modules.Sales.Application.Contracts;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace GoymenERP.Modules.Sales.Infrastructure.Persistence;

public sealed partial class SalesStore : ISalesCustomerReturnDecisionSafetyStoreV1
{
    private OwnedCustomerReturnDecision? ownedCustomerReturnDecision;

    public bool CustomerReturnDecisionEntryIsClean => officialCancellationAuditWriter is not null
        && customerReturnDecisionReadFence is null && ownedCustomerReturnDecision is null && ownedContractDiscountDecision is null && ownedRemainderDecision is null && ownedPriceFloorApproval is null && ownedDiscountApproval is null && ownedOverDeliveryRequestApproval is null && ownedRiskExceptionDecision is null && overDeliveryRequestDecisionReadFence is null && riskExceptionDecisionReadFence is null && contractDiscountDecisionReadFence is null && remainderDecisionReadFence is null && ownedRequestApproval is null && ownedQuoteApproval is null && quoteDecisionReadFence is null && dbContext.Database.IsRelational()
        && System.Transactions.Transaction.Current is null && dbContext.Database.CurrentTransaction is null
        && !dbContext.ChangeTracker.HasChanges() && !officialCancellationAuditWriter.HasPendingDrafts;

    public async Task<SalesCustomerReturnCommandResult> RunCustomerReturnDecisionAsync(SalesDraftWriteEnvelope envelope, string requestHash, string commandName,
        bool cleanBeforeAuthority, Func<CancellationToken, Task<SalesCustomerReturnCommandResult>> execute,
        Func<CancellationToken, Task<SalesCustomerReturnCommandResult?>> readOriginalKey,
        Func<CancellationToken, Task> freshFence, CancellationToken cancellationToken)
    {
        RequireEnvelope(envelope); RequireHash(requestHash);
        if (commandName is not (SalesCustomerReturnCommands.ApproveV1 or SalesCustomerReturnCommands.RejectV1))
            throw new ArgumentException("Only native customer-return decisions own this boundary.", nameof(commandName));
        try { return await AttemptAsync(false); }
        catch (CustomerReturnDecisionLostRace lost)
        {
            // The first transaction has been fully aborted and disposed. This pass can ONLY read this original key.
            try { return await AttemptAsync(true); }
            catch (CustomerReturnDecisionNoWinner)
            {
                if (lost.NativeConcurrentChange)
                    throw Refused(SalesDraftStoreCodes.ConcurrentChange, "Müşteri iadesi kararı eşzamanlı değişti.");
                throw CustomerReturnDecisionUnavailable();
            }
        }

        async Task<SalesCustomerReturnCommandResult> AttemptAsync(bool replayOnly)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var cleanBefore = CustomerReturnDecisionEntryIsClean;
            await freshFence(cancellationToken); cancellationToken.ThrowIfCancellationRequested();
            if (!cleanBeforeAuthority || !cleanBefore || !CustomerReturnDecisionEntryIsClean) throw CustomerReturnDecisionUnavailable();
            var state = new OwnedCustomerReturnDecision(RequireResolvedCompany(), envelope, requestHash, commandName, freshFence, replayOnly);
            IDbContextTransaction? transaction = null;
            var committed = false;
            var commitStarted = false;
            var disposing = false;
            ownedCustomerReturnDecision = state;
            try
            {
                transaction = await dbContext.Database.BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken);
                await CustomerReturnDecisionFenceAsync(cancellationToken);
                SalesCustomerReturnCommandResult? result = null;
                SalesDraftServiceException? terminal = null;
                try
                {
                    result = replayOnly ? await readOriginalKey(cancellationToken) : await execute(cancellationToken);
                    if (result is null) throw new CustomerReturnDecisionNoWinner();
                }
                catch (SalesDraftServiceException refusal) when (refusal.IsReplay
                    || state.Saved && state.Terminal is { } recorded && recorded.Status == refusal.Status && recorded.Code == refusal.Code)
                {
                    // A recorded terminal native refusal is itself this command's durable answer.
                    terminal = refusal;
                }
                await CustomerReturnDecisionFenceAsync(cancellationToken); RequireFlushed();
                if (terminal is null && result is { IsReplay: false } && !state.Saved)
                    throw new InvalidOperationException("Müşteri iadesi kararı kaydedilmiş bir deneme taşımıyor.");
                await CustomerReturnDecisionFenceAsync(cancellationToken); RequireFlushed();
                commitStarted = true;
                await transaction.CommitAsync(cancellationToken); committed = true;
                var completed = transaction; transaction = null;
                disposing = true; await completed.DisposeAsync(); disposing = false;
                // A release denial suppresses the response, not an already committed native decision.
                await CustomerReturnDecisionFenceAsync(cancellationToken); RequireFlushed();
                if (terminal is not null) throw terminal;
                return result!;
            }
            catch (Exception failure)
            {
                var cause = failure is CustomerReturnDecisionPersistenceFailure save ? save.Cause : failure;
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
                if (cleanup.Count != 0 || disposing) throw new AggregateException("Müşteri iadesi kararı işlem temizliği başarısız; tekrar okuma güvenli değil.", [failure, .. cleanup]);
                await CustomerReturnDecisionFenceAsync(cancellationToken);
                if (!replayOnly && !commitStarted && !committed && (transientRace || nativeRace)) throw new CustomerReturnDecisionLostRace(nativeRace);
                if (failure is CustomerReturnDecisionPersistenceFailure) throw CustomerReturnDecisionUnavailable();
                if (replayOnly && (transientRace || nativeRace)) throw CustomerReturnDecisionUnavailable();
                if (failure is not (SalesDraftServiceException or SalesDraftStoreException or OperationCanceledException or AggregateException or CustomerReturnDecisionNoWinner))
                    throw CustomerReturnDecisionUnavailable();
                throw;
            }
            finally { ownedCustomerReturnDecision = null; }

            void RequireFlushed()
            {
                if (officialCancellationAuditWriter!.HasPendingDrafts || dbContext.ChangeTracker.HasChanges())
                    throw new InvalidOperationException("Müşteri iadesi kararı işleminin kaydedilmemiş işi veya denetim taslağı kaldı.");
            }
        }
    }

    private async Task<T> CustomerReturnDecisionReadAsync<T>(Func<Task<T>> read, CancellationToken token)
    {
        await CustomerReturnDecisionFenceAsync(token);
        var result = await read();
        await CustomerReturnDecisionFenceAsync(token);
        return result;
    }

    private async Task CustomerReturnDecisionFenceAsync(CancellationToken token)
    {
        if (ownedCustomerReturnDecision is not { } state)
        {
            if (customerReturnDecisionReadFence is { } read) { token.ThrowIfCancellationRequested(); await read(token); token.ThrowIfCancellationRequested(); }
            return;
        }
        token.ThrowIfCancellationRequested(); RequireCompany(state.CompanyId);
        await state.FreshFence(token);
        token.ThrowIfCancellationRequested(); RequireCompany(state.CompanyId);
    }

    private void RequireCustomerReturnDecisionCommand(SalesDraftWriteEnvelope envelope, string hash, string command, bool writes)
    {
        // A different owned decision or a purpose read cannot smuggle a legacy return write into its transaction.
        // With no approval/read marker this remains the unchanged native nonapproval path.
        if (writes && (customerReturnDecisionReadFence is not null || ownedContractDiscountDecision is not null || ownedRemainderDecision is not null || ownedPriceFloorApproval is not null
            || ownedDiscountApproval is not null || ownedOverDeliveryRequestApproval is not null || ownedRiskExceptionDecision is not null
            || ownedRequestApproval is not null || ownedQuoteApproval is not null || overDeliveryRequestDecisionReadFence is not null
            || riskExceptionDecisionReadFence is not null || remainderDecisionReadFence is not null || contractDiscountDecisionReadFence is not null || quoteDecisionReadFence is not null))
            throw new InvalidOperationException("Başka karar veya salt okuma kapsamı müşteri iadesi komutu yazamaz.");
        if (ownedCustomerReturnDecision is not { } state) return;
        if (state.Command != command || state.Hash != hash || state.Envelope.PermissionCode != envelope.PermissionCode || state.Envelope.ActorId != envelope.ActorId
            || state.Envelope.CorrelationId != envelope.CorrelationId
            || IdempotencyKeyHash(state.Envelope.IdempotencyKey) != IdempotencyKeyHash(envelope.IdempotencyKey)
            || writes && state.ReplayOnly)
            throw new InvalidOperationException("Müşteri iadesi kararı işlemi başka komuta veya ikinci yazma denemesine geçemez.");
    }

    private async Task SaveCustomerReturnDecisionAsync(CancellationToken token, SalesDraftTerminalRefusal? terminal = null)
    {
        if (ownedCustomerReturnDecision is not { } state) { await SaveAsync(token); return; }
        if (state.ReplayOnly) throw new InvalidOperationException("Kazanan denemesi yalnız okunabilir.");
        await CustomerReturnDecisionFenceAsync(token);
        try
        {
            // A successful decision always enqueues one native Audit draft; a terminal refusal enqueues none.
            if (terminal is null && !officialCancellationAuditWriter!.HasPendingDrafts)
                throw new InvalidOperationException("Müşteri iadesi kararı ile aynı scoped denetim toplayıcısı gerekli.");
            if (officialCancellationAuditWriter!.HasPendingDrafts)
            {
                await officialCancellationAuditWriter.WritePendingAsync(dbContext.Database.CurrentTransaction!.GetDbTransaction(), token);
                await CustomerReturnDecisionFenceAsync(token);
            }
            if (officialCancellationAuditWriter.HasPendingDrafts) throw new InvalidOperationException("Denetim taslakları boşaltılmadı.");
            await dbContext.SaveChangesAsync(token);
            state.Saved = true; state.Terminal = terminal;
        }
        catch (OperationCanceledException) { throw; }
        catch (AggregateException) { throw; }
        catch (SalesDraftServiceException refusal) when (refusal.Status is 401 or 403) { throw; }
        // Escape the native domain/refusal catches even if an interceptor throws after EF accepted the changes.
        catch (Exception failure) { throw new CustomerReturnDecisionPersistenceFailure(failure); }
        await CustomerReturnDecisionFenceAsync(token);
    }

    private static SalesDraftStoreException CustomerReturnDecisionUnavailable() =>
        new(SalesDraftProblemCodesV1.OwnerReferenceUnavailable, "Müşteri iadesi kararı güvenli karar sınırı kullanılamıyor.");
    private sealed record OwnedCustomerReturnDecision(Guid CompanyId, SalesDraftWriteEnvelope Envelope, string Hash, string Command,
        Func<CancellationToken, Task> FreshFence, bool ReplayOnly)
    {
        internal bool Saved;
        internal SalesDraftTerminalRefusal? Terminal;
    }
    private sealed class CustomerReturnDecisionLostRace(bool nativeConcurrentChange) : Exception
    { internal bool NativeConcurrentChange { get; } = nativeConcurrentChange; }
    private sealed class CustomerReturnDecisionNoWinner : Exception;
    private sealed class CustomerReturnDecisionPersistenceFailure(Exception cause) : Exception("Customer-return decision persistence failed.", cause)
    { internal Exception Cause { get; } = cause; }
}
