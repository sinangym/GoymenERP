using System.Data;
using GoymenERP.Modules.Sales.Application.Contracts;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace GoymenERP.Modules.Sales.Infrastructure.Persistence;

public sealed partial class SalesStore : ISalesContractDiscountDecisionSafetyStoreV1
{
    private OwnedContractDiscountDecision? ownedContractDiscountDecision;

    public bool ContractDiscountDecisionEntryIsClean => officialCancellationAuditWriter is not null
        && ownedContractDiscountDecision is null && ownedRemainderDecision is null && ownedCustomerReturnDecision is null && ownedPriceFloorApproval is null && ownedDiscountApproval is null && ownedOverDeliveryRequestApproval is null && ownedRiskExceptionDecision is null && overDeliveryRequestDecisionReadFence is null && riskExceptionDecisionReadFence is null && contractDiscountDecisionReadFence is null && remainderDecisionReadFence is null && ownedRequestApproval is null && ownedQuoteApproval is null && quoteDecisionReadFence is null && dbContext.Database.IsRelational()
        && System.Transactions.Transaction.Current is null && dbContext.Database.CurrentTransaction is null
        && !dbContext.ChangeTracker.HasChanges() && !officialCancellationAuditWriter.HasPendingDrafts;

    public async Task<SalesContractCommandResult> RunContractDiscountDecisionAsync(SalesDraftWriteEnvelope envelope, string requestHash, string commandName,
        bool cleanBeforeAuthority, Func<CancellationToken, Task<SalesContractCommandResult>> execute,
        Func<CancellationToken, Task<SalesContractCommandResult?>> readOriginalKey,
        Func<CancellationToken, Task> freshFence, CancellationToken cancellationToken)
    {
        RequireEnvelope(envelope); RequireHash(requestHash);
        if (commandName is not (SalesContractCommands.ApproveDiscountV1 or SalesContractCommands.RejectDiscountV1))
            throw new ArgumentException("Only native contract discount decisions own this boundary.", nameof(commandName));
        try { return await AttemptAsync(false); }
        catch (ContractDiscountDecisionLostRace lost)
        {
            // The first transaction has been fully aborted and disposed. This pass can ONLY read this original key.
            try { return await AttemptAsync(true); }
            catch (ContractDiscountDecisionNoWinner)
            {
                if (lost.NativeConcurrentChange)
                    throw Refused(SalesDraftStoreCodes.ConcurrentChange, "Sözleşme iskontosu kararı eşzamanlı değişti.");
                throw ContractDiscountDecisionUnavailable();
            }
        }

        async Task<SalesContractCommandResult> AttemptAsync(bool replayOnly)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var cleanBefore = ContractDiscountDecisionEntryIsClean;
            await freshFence(cancellationToken); cancellationToken.ThrowIfCancellationRequested();
            if (!cleanBeforeAuthority || !cleanBefore || !ContractDiscountDecisionEntryIsClean) throw ContractDiscountDecisionUnavailable();
            var state = new OwnedContractDiscountDecision(RequireResolvedCompany(), envelope, requestHash, commandName, freshFence, replayOnly);
            IDbContextTransaction? transaction = null;
            var committed = false;
            var commitStarted = false;
            var disposing = false;
            ownedContractDiscountDecision = state;
            try
            {
                transaction = await dbContext.Database.BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken);
                await ContractDiscountDecisionFenceAsync(cancellationToken);
                SalesContractCommandResult? result = null;
                SalesDraftServiceException? terminal = null;
                try
                {
                    result = replayOnly ? await readOriginalKey(cancellationToken) : await execute(cancellationToken);
                    if (result is null) throw new ContractDiscountDecisionNoWinner();
                }
                catch (SalesDraftServiceException refusal) when (refusal.IsReplay
                    || state.Saved && state.Terminal is { } recorded && recorded.Status == refusal.Status && recorded.Code == refusal.Code)
                {
                    // A recorded terminal native refusal is itself this command's durable answer.
                    terminal = refusal;
                }
                await ContractDiscountDecisionFenceAsync(cancellationToken); RequireFlushed();
                if (terminal is null && result is { IsReplay: false } && !state.Saved)
                    throw new InvalidOperationException("Sözleşme iskontosu kararı kaydedilmiş bir deneme taşımıyor.");
                await ContractDiscountDecisionFenceAsync(cancellationToken); RequireFlushed();
                commitStarted = true;
                await transaction.CommitAsync(cancellationToken); committed = true;
                var completed = transaction; transaction = null;
                disposing = true; await completed.DisposeAsync(); disposing = false;
                // A release denial suppresses the response, not an already committed native decision.
                await ContractDiscountDecisionFenceAsync(cancellationToken); RequireFlushed();
                if (terminal is not null) throw terminal;
                return result!;
            }
            catch (Exception failure)
            {
                var cause = failure is ContractDiscountDecisionPersistenceFailure save ? save.Cause : failure;
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
                if (cleanup.Count != 0 || disposing) throw new AggregateException("Sözleşme iskontosu kararı işlem temizliği başarısız; tekrar okuma güvenli değil.", [failure, .. cleanup]);
                await ContractDiscountDecisionFenceAsync(cancellationToken);
                if (!replayOnly && !commitStarted && !committed && (transientRace || nativeRace)) throw new ContractDiscountDecisionLostRace(nativeRace);
                if (failure is ContractDiscountDecisionPersistenceFailure) throw ContractDiscountDecisionUnavailable();
                if (replayOnly && (transientRace || nativeRace)) throw ContractDiscountDecisionUnavailable();
                if (failure is not (SalesDraftServiceException or SalesDraftStoreException or OperationCanceledException or AggregateException or ContractDiscountDecisionNoWinner))
                    throw ContractDiscountDecisionUnavailable();
                throw;
            }
            finally { ownedContractDiscountDecision = null; }

            void RequireFlushed()
            {
                if (officialCancellationAuditWriter!.HasPendingDrafts || dbContext.ChangeTracker.HasChanges())
                    throw new InvalidOperationException("Sözleşme iskontosu kararı işleminin kaydedilmemiş işi veya denetim taslağı kaldı.");
            }
        }
    }

    private async Task<T> ContractDiscountDecisionReadAsync<T>(Func<Task<T>> read, CancellationToken token)
    {
        await ContractDiscountDecisionFenceAsync(token);
        var result = await read();
        await ContractDiscountDecisionFenceAsync(token);
        return result;
    }

    private async Task ContractDiscountDecisionFenceAsync(CancellationToken token)
    {
        if (ownedContractDiscountDecision is not { } state) { await ContractDiscountDecisionSourceFenceAsync(token); await CustomerReturnDecisionFenceAsync(token); return; }
        token.ThrowIfCancellationRequested(); RequireCompany(state.CompanyId);
        await state.FreshFence(token);
        token.ThrowIfCancellationRequested(); RequireCompany(state.CompanyId);
    }

    private void RequireContractDiscountDecisionCommand(SalesDraftWriteEnvelope envelope, string hash, string command, bool writes)
    {
        if (writes && contractDiscountDecisionReadFence is not null)
            throw new InvalidOperationException("Selected contract discount read cannot dispatch a mutation.");
        if (ownedContractDiscountDecision is not { } state) return;
        if (state.Command != command || state.Hash != hash || state.Envelope.PermissionCode != envelope.PermissionCode || state.Envelope.ActorId != envelope.ActorId
            || state.Envelope.CorrelationId != envelope.CorrelationId
            || IdempotencyKeyHash(state.Envelope.IdempotencyKey) != IdempotencyKeyHash(envelope.IdempotencyKey)
            || writes && state.ReplayOnly)
            throw new InvalidOperationException("Sözleşme iskontosu kararı işlemi başka komuta veya ikinci yazma denemesine geçemez.");
    }

    private async Task SaveContractDiscountDecisionAsync(CancellationToken token, SalesDraftTerminalRefusal? terminal = null)
    {
        if (ownedContractDiscountDecision is not { } state) { await SaveAsync(token); return; }
        if (state.ReplayOnly) throw new InvalidOperationException("Kazanan denemesi yalnız okunabilir.");
        await ContractDiscountDecisionFenceAsync(token);
        try
        {
            // A successful decision always enqueues one native Audit draft; a terminal refusal enqueues none.
            if (terminal is null && !officialCancellationAuditWriter!.HasPendingDrafts)
                throw new InvalidOperationException("Sözleşme iskontosu kararı ile aynı scoped denetim toplayıcısı gerekli.");
            if (officialCancellationAuditWriter!.HasPendingDrafts)
            {
                await officialCancellationAuditWriter.WritePendingAsync(dbContext.Database.CurrentTransaction!.GetDbTransaction(), token);
                await ContractDiscountDecisionFenceAsync(token);
            }
            if (officialCancellationAuditWriter.HasPendingDrafts) throw new InvalidOperationException("Denetim taslakları boşaltılmadı.");
            await dbContext.SaveChangesAsync(token);
            state.Saved = true; state.Terminal = terminal;
        }
        catch (OperationCanceledException) { throw; }
        catch (AggregateException) { throw; }
        catch (SalesDraftServiceException refusal) when (refusal.Status is 401 or 403) { throw; }
        // Escape the native domain/refusal catches even if an interceptor throws after EF accepted the changes.
        catch (Exception failure) { throw new ContractDiscountDecisionPersistenceFailure(failure); }
        await ContractDiscountDecisionFenceAsync(token);
    }

    private static SalesDraftStoreException ContractDiscountDecisionUnavailable() =>
        new(SalesDraftProblemCodesV1.OwnerReferenceUnavailable, "Sözleşme iskontosu kararı güvenli karar sınırı kullanılamıyor.");
    private sealed record OwnedContractDiscountDecision(Guid CompanyId, SalesDraftWriteEnvelope Envelope, string Hash, string Command,
        Func<CancellationToken, Task> FreshFence, bool ReplayOnly)
    {
        internal bool Saved;
        internal SalesDraftTerminalRefusal? Terminal;
    }
    private sealed class ContractDiscountDecisionLostRace(bool nativeConcurrentChange) : Exception
    { internal bool NativeConcurrentChange { get; } = nativeConcurrentChange; }
    private sealed class ContractDiscountDecisionNoWinner : Exception;
    private sealed class ContractDiscountDecisionPersistenceFailure(Exception cause) : Exception("Contract discount decision persistence failed.", cause)
    { internal Exception Cause { get; } = cause; }
}
