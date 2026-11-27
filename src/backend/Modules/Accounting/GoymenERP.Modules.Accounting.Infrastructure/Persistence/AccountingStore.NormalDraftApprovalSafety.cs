using System.Data;
using System.Text.Json;
using GoymenERP.Modules.Accounting.Application;
using GoymenERP.Modules.Audit.Application.Contracts;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace GoymenERP.Modules.Accounting.Infrastructure.Persistence;

public sealed partial class AccountingStore
{
    private OwnedNormalApproval? ownedNormalApproval;
    public IAccountingNormalJournalDraftStore WithNormalDraftApprovalSafety() => new GuardedNormalDraftStore(this);

    private async Task<AccountingApiResult<NormalJournalDraftViewV1>> RunOwnedNormalApprovalAsync(
        Func<bool, Task<AccountingApiResult<NormalJournalDraftViewV1>>> nativeAttempt,
        Func<CancellationToken, Task> freshFence, CancellationToken token)
        => await RunOwnedAccountingApprovalAsync(NormalJournalCommandNamesV1.ApproveDraft, nativeAttempt, freshFence, token);

    private async Task<AccountingApiResult<T>> RunOwnedAccountingApprovalAsync<T>(string commandName,
        Func<bool, Task<AccountingApiResult<T>>> nativeAttempt,
        Func<CancellationToken, Task> freshFence, CancellationToken token) where T : class
    {
        ArgumentNullException.ThrowIfNull(nativeAttempt); ArgumentNullException.ThrowIfNull(freshFence);
        token.ThrowIfCancellationRequested();
        try { return await Run(false); }
        catch (NormalApprovalRace race) { return await Run(true, race.NoWinnerCode); }
        async Task<AccountingApiResult<T>> Run(bool replayOnly, string? noWinnerCode = null)
        {
            var cleanBefore = CleanEntry();
            try { await freshFence(token); token.ThrowIfCancellationRequested(); }
            catch (NormalJournalAuthorityDeniedException) { return Denied(); }
            if (!cleanBefore || !CleanEntry()) return Unavailable(); // Caller state stays outside cleanup ownership.
            var company = companyContext.IsResolved ? companyContext.CompanyId : Guid.Empty;
            var state = new OwnedNormalApproval(company, normalApprovalAuditWriter!, freshFence, replayOnly, commandName);
            IDbContextTransaction? transaction = null; var committed = false; ownedNormalApproval = state;
            AccountingApiResult<T>? nativeDenial = null;
            try
            {
                transaction = await dbContext.Database.BeginTransactionAsync(IsolationLevel.Serializable, token);
                await NormalApprovalFence(token);
                var result = await nativeAttempt(replayOnly);
                if (replayOnly && !result.IsReplay && result.Status == AccountingApiStatus.Conflict && (result.Code == AccountingStoreCodes.ConcurrentChange
                        || noWinnerCode == NormalJournalDraftStoreCodesV1.PostingConflict && result.Code == AccountingApiCodes.IdempotencyConflict))
                    result = result with { Code = noWinnerCode ?? AccountingStoreCodes.ConcurrentChange };
                if (result.Status == AccountingApiStatus.Denied)
                {
                    // Period maker refusals retain their native business code/field after owned cleanup.
                    // The existing normal-draft denial contract remains unchanged.
                    if (commandName != NormalJournalCommandNamesV1.ApproveDraft) nativeDenial = result;
                    throw new NormalJournalAuthorityDeniedException();
                }
                if (result.Status == AccountingApiStatus.Unavailable) throw new NormalApprovalUnavailable();
                await NormalApprovalFence(token); RequireFlushed();
                if (!result.IsReplay && (result.Status == AccountingApiStatus.Ok || state.Saved)
                    && (!state.Saved || state.Command is null || state.Command.ResultJson != JsonSerializer.Serialize(result with { IsReplay = false, OriginalCorrelationId = null }, Json)))
                    throw new InvalidOperationException("Normal approval has no saved native result.");
                await NormalApprovalFence(token); RequireFlushed();
                await transaction.CommitAsync(token); committed = true;
                var completed = transaction; transaction = null; await completed.DisposeAsync();
                await NormalApprovalFence(token); RequireFlushed();
                return result;
            }
            catch (Exception failure)
            {
                var deadlock = failure is SqlException { Number: 1205 } or DbUpdateException { InnerException: SqlException { Number: 1205 } }
                    || commandName != NormalJournalCommandNamesV1.ApproveDraft && IsPeriodTransactionDeadlock(failure);
                var faults = new List<Exception>();
                if (transaction is not null)
                {
                    if (!committed) try
                    {
                        if (!deadlock || transaction.GetDbTransaction() is not SqlTransaction { Connection: null })
                            await transaction.RollbackAsync(CancellationToken.None);
                    }
                    catch (Exception error) { faults.Add(error); }
                    try { await transaction.DisposeAsync(); } catch (Exception error) { faults.Add(error); }
                }
                try { dbContext.ChangeTracker.Clear(); } catch (Exception error) { faults.Add(error); }
                try { state.Writer.DiscardPending(); } catch (Exception error) { faults.Add(error); }
                if (faults.Count != 0) throw new AggregateException("Normal approval cleanup failed; winner read is unsafe.", [failure, .. faults]);
                if (failure is NormalJournalAuthorityDeniedException)
                {
                    if (nativeDenial is null) return Denied();
                    try { await freshFence(token); token.ThrowIfCancellationRequested(); }
                    catch (NormalJournalAuthorityDeniedException) { return Denied(); }
                    return nativeDenial;
                }
                if (!replayOnly && !committed && (deadlock || failure is NormalJournalApprovalRaceException)) throw new NormalApprovalRace(failure is NormalJournalApprovalRaceException lost ? lost.NoWinnerCode : AccountingStoreCodes.ConcurrentChange);
                if (failure is NormalApprovalUnavailable or NormalJournalApprovalRaceException || deadlock) return Unavailable();
                throw;
            }
            finally { ownedNormalApproval = null; }
            void RequireFlushed()
            {
                if (dbContext.ChangeTracker.HasChanges() || state.Writer.HasPendingDrafts)
                    throw new InvalidOperationException("Normal approval has unflushed owner work.");
            }
        }
        bool CleanEntry() => normalApprovalAuditWriter is not null && ownedNormalApproval is null
            && System.Transactions.Transaction.Current is null && dbContext.Database.IsRelational()
            && dbContext.Database.CurrentTransaction is null && !dbContext.ChangeTracker.HasChanges() && !normalApprovalAuditWriter.HasPendingDrafts;
        static AccountingApiResult<T> Denied() => new(AccountingApiStatus.Denied, null, false, AccountingApiCodes.AuthorityDenied);
        static AccountingApiResult<T> Unavailable() => new(AccountingApiStatus.Unavailable, null, false, null);
    }

    private async Task NormalApprovalFence(CancellationToken token)
    {
        token.ThrowIfCancellationRequested(); var state = ownedNormalApproval ?? throw new InvalidOperationException("Normal approval owner scope missing.");
        if (!companyContext.IsResolved || companyContext.CompanyId != state.CompanyId) throw new NormalJournalAuthorityDeniedException();
        await state.Fence(token); token.ThrowIfCancellationRequested();
        if (!companyContext.IsResolved || companyContext.CompanyId != state.CompanyId) throw new NormalJournalAuthorityDeniedException();
    }
    private async Task SaveOwnedNormalApprovalAsync(CancellationToken token)
    {
        var state = ownedNormalApproval ?? throw new InvalidOperationException("Normal approval owner scope missing.");
        if (state.ReplayOnly) throw new InvalidOperationException("Normal approval original-key winner read cannot write.");
        await NormalApprovalFence(token);
        if (state.Writer.HasPendingDrafts)
        {
            await state.Writer.WritePendingAsync(dbContext.Database.CurrentTransaction!.GetDbTransaction(), token);
            await NormalApprovalFence(token);
        }
        if (state.Writer.HasPendingDrafts) throw new InvalidOperationException("Normal approval Audit remains pending.");
        await dbContext.SaveChangesAsync(token); state.Saved = true;
        await NormalApprovalFence(token);
    }
    private sealed record OwnedNormalApproval(Guid CompanyId, IAuditTransactionWriterV1 Writer, Func<CancellationToken, Task> Fence, bool ReplayOnly, string CommandName)
    { internal bool Saved; internal AccountingCommandRecord? Command; }
    private sealed class NormalApprovalRace(string noWinnerCode) : Exception { internal string NoWinnerCode { get; } = noWinnerCode; }
    private sealed class NormalApprovalUnavailable : Exception;
    private sealed class GuardedNormalDraftStore(AccountingStore core) : IAccountingNormalJournalDraftStore, IAccountingNormalJournalDraftDecisionStoreV1
    {
        public Task<AccountingApiResult<NormalJournalDraftViewV1>> RunNormalApprovalAsync(Func<bool, Task<AccountingApiResult<NormalJournalDraftViewV1>>> attempt,
            Func<CancellationToken, Task> fence, CancellationToken token = default) => core.RunOwnedNormalApprovalAsync(attempt, fence, token);
        public Task<NormalJournalDraftRecordV1> ApproveAndPostNormalDraftAsync(Guid id, long version, Guid period, Guid actor, Func<CancellationToken, Task> fence,
            AccountingCommandMetadata metadata, CancellationToken token = default) => core.ownedNormalApproval is not null
                ? core.ApproveAndPostNormalDraftAsync(id, version, period, actor, fence, metadata, token)
                : throw new InvalidOperationException("Guarded normal approval requires its owned command boundary.");
        public Task<(NormalJournalDraftRecordV1 Record, AccountingWriteOutcome Outcome)> CreateNormalDraftAsync(NormalJournalDraftV1 draft,
            Func<CancellationToken, Task> fence, AccountingCommandMetadata metadata, CancellationToken token = default) => core.CreateNormalDraftAsync(draft, fence, metadata, token);
        public Task<NormalJournalDraftDecisionSourceV1?> ReadNormalDraftDecisionAsync(Guid id, Func<CancellationToken, Task> fence, CancellationToken token = default) => core.ReadNormalDraftDecisionAsync(id, fence, token);
        public Task<NormalJournalDraftRecordV1?> FindNormalDraftAsync(Guid id, CancellationToken token = default) => core.FindNormalDraftAsync(id, token);
    }
}
