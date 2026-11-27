using System.Data;
using GoymenERP.Modules.Audit.Application.Contracts;
using GoymenERP.Modules.Finance.Contracts;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace GoymenERP.Modules.Finance.Infrastructure;

public sealed partial class FinancePartyCreditLimitService
{
    private OwnedDecision? ownedDecision;

    // As with the native bank approval composition, entry refusal owns no caller work. The raw core remains
    // available to existing synthetic policy tests; runtime DI exposes only the guarded interface below.
    internal async Task<PartyCreditLimitCommandResult> RunOwnedDecisionAsync(FinanceContext context,
        CommandMetadata metadata, Guid proposalId, string command, IAuditTransactionWriterV1? writer,
        Func<CancellationToken, Task<PartyCreditLimitCommandResult>> execute, CancellationToken token)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(metadata);
        token.ThrowIfCancellationRequested();
        try { return await RunAsync(false); }
        catch (DecisionSaveRace) { return await RunAsync(true); }

        async Task<PartyCreditLimitCommandResult> RunAsync(bool replayOnly)
        {
            var cleanBefore = CleanEntry();
            // Keep the native context/permission/key refusal order, even when persistence is unavailable.
            if (await StartAsync(context, metadata, FinanceAction.ApproveCreditLimit, proposalId, token) is { } entry)
                return entry;
            if (!cleanBefore || !CleanEntry()) return Unavailable();
            if (command is not (ApproveCommand or RejectCommand)) throw new InvalidOperationException("Unknown credit limit decision.");
            var state = new OwnedDecision(command, writer!, replayOnly);
            IDbContextTransaction? transaction = null;
            var committed = false;
            ownedDecision = state;
            try
            {
                transaction = await dbContext.Database.BeginTransactionAsync(IsolationLevel.Serializable, token);
                await RequireFence();
                var result = await execute(token);
                if (result.Outcome is CommandOutcome.Denied or CommandOutcome.Unavailable)
                    throw new DecisionRefusal(result);
                if (result.Outcome == CommandOutcome.Succeeded)
                    result = result with { DecisionReceipt = await ReadDecisionReceiptAsync(context, metadata, proposalId, command, result, token) };
                await RequireFence();
                RequireFlushed();
                if (result.Outcome == CommandOutcome.Succeeded && !result.IsReplay && !state.Saved)
                    throw new InvalidOperationException("Credit limit success has no saved native receipt.");
                await RequireFence();
                RequireFlushed();
                await transaction.CommitAsync(token);
                committed = true;
                var completed = transaction; transaction = null;
                await completed.DisposeAsync();
                // A failure here suppresses release; the already committed decision is not rolled back.
                await RequireFence();
                RequireFlushed();
                return result;
            }
            catch (Exception failure)
            {
                var deadlock = IsDecisionDeadlock(failure);
                var faults = new List<Exception>();
                if (transaction is not null)
                {
                    if (!committed) try
                    {
                        // SQL Server already rolls back a 1205 victim. SqlClient exposes a completed transaction with
                        // Connection=null; a second Rollback would itself fail. Disposal/owner cleanup still follow.
                        if (!(deadlock && transaction.GetDbTransaction() is SqlTransaction { Connection: null }))
                            await transaction.RollbackAsync(CancellationToken.None);
                    }
                    catch (Exception error) { faults.Add(error); }
                    try { await transaction.DisposeAsync(); } catch (Exception error) { faults.Add(error); }
                }
                try { dbContext.ChangeTracker.Clear(); } catch (Exception error) { faults.Add(error); }
                try { state.Writer.DiscardPending(); } catch (Exception error) { faults.Add(error); }
                if (faults.Count > 0)
                    throw new AggregateException("Credit limit cleanup failed; winner replay is unsafe.", [failure, .. faults]);
                if (failure is DecisionRefusal refusal) return refusal.Result;
                // Serializable 1205 can precede Save (including Audit) or follow Save before Commit.
                // Only a successful owned abort above may enter one original-key read; never repeat mutation.
                if (!replayOnly && !committed && (deadlock
                    || state.SaveAttempted && !state.Saved && failure is DbUpdateException update
                        && (update is DbUpdateConcurrencyException || update.InnerException is SqlException { Number: 2601 or 2627 })))
                    throw new DecisionSaveRace();
                throw;
            }
            finally { ownedDecision = null; }

            async Task RequireFence()
            {
                if (await FenceAsync(context, FinanceAction.ApproveCreditLimit, proposalId, token) is { } refusal)
                    throw new DecisionRefusal(refusal);
            }
            void RequireFlushed()
            {
                if (state.Writer.HasPendingDrafts || dbContext.ChangeTracker.HasChanges())
                    throw new InvalidOperationException("Credit limit decision has unflushed owner work.");
            }
        }

        bool CleanEntry() => writer is not null && ownedDecision is null && dbContext.Database.IsRelational()
            && System.Transactions.Transaction.Current is null && dbContext.Database.CurrentTransaction is null
            && !dbContext.ChangeTracker.HasChanges() && !writer.HasPendingDrafts;
        PartyCreditLimitCommandResult Unavailable() => Refused(context, CommandOutcome.Unavailable,
            Issue("request", "credit_limit_approval_unavailable"));
    }

    private async Task<PartyCreditLimitCommandResult> SaveOwnedDecisionAsync(FinanceContext context,
        FinanceAction action, string command, PartyCreditLimitCommandResult result, CancellationToken token)
    {
        var state = ownedDecision ?? throw new InvalidOperationException("Credit limit decision boundary is absent.");
        if (state.ReplayOnly || command != state.Command || action != FinanceAction.ApproveCreditLimit)
            throw new InvalidOperationException("Credit limit decision cannot write another command or retry mutation.");
        await RequireFence();
        if (state.Writer.HasPendingDrafts)
        {
            await state.Writer.WritePendingAsync(dbContext.Database.CurrentTransaction!.GetDbTransaction(), token);
            await RequireFence();
        }
        if (state.Writer.HasPendingDrafts) throw new InvalidOperationException("Credit limit Audit remains pending.");
        state.SaveAttempted = true;
        await dbContext.SaveChangesAsync(token);
        state.Saved = true;
        await RequireFence();
        return result;

        async Task RequireFence()
        {
            if (await FenceAsync(context, action, result.Resource?.Id, token) is { } refusal)
                throw new DecisionRefusal(refusal);
        }
    }

    private static bool IsDecisionDeadlock(Exception error) => error is SqlException { Number: 1205 }
        or DbUpdateException { InnerException: SqlException { Number: 1205 } };

    private sealed record OwnedDecision(string Command, IAuditTransactionWriterV1 Writer, bool ReplayOnly)
    { internal bool SaveAttempted; internal bool Saved; }
    private sealed class DecisionRefusal(PartyCreditLimitCommandResult result) : Exception
    { internal PartyCreditLimitCommandResult Result { get; } = result; }
    private sealed class DecisionSaveRace : Exception;
}

internal sealed class GuardedFinancePartyCreditLimitService(FinancePartyCreditLimitService core,
    IAuditTransactionWriterV1? writer) : IFinancePartyCreditLimitMaster
{
    public Task<PartyCreditLimitCommandResult> ApproveAsync(FinanceContext context, CommandMetadata metadata,
        PartyCreditLimitApproveRequest request, CancellationToken token)
    {
        ArgumentNullException.ThrowIfNull(request);
        return core.RunOwnedDecisionAsync(context, metadata, request.ProposalId, FinancePartyCreditLimitService.ApproveCommand,
            writer, current => core.ApproveAsync(context, metadata, request, current), token);
    }
    public Task<PartyCreditLimitCommandResult> RejectAsync(FinanceContext context, CommandMetadata metadata,
        PartyCreditLimitRejectRequest request, CancellationToken token)
    {
        ArgumentNullException.ThrowIfNull(request);
        return core.RunOwnedDecisionAsync(context, metadata, request.ProposalId, FinancePartyCreditLimitService.RejectCommand,
            writer, current => core.RejectAsync(context, metadata, request, current), token);
    }
    public Task<PartyCreditLimitCommandResult> SetAsync(FinanceContext c, CommandMetadata m, PartyCreditLimitSetRequest r, CancellationToken t) => core.SetAsync(c, m, r, t);
    public Task<PartyCreditLimitCommandResult> RemoveAsync(FinanceContext c, CommandMetadata m, PartyCreditLimitRemoveRequest r, CancellationToken t) => core.RemoveAsync(c, m, r, t);
    public Task<PartyCreditLimitCommandResult> ProposeAsync(FinanceContext c, CommandMetadata m, PartyCreditLimitProposeRequest r, CancellationToken t) => core.ProposeAsync(c, m, r, t);
    public Task<ReadResult<PartyCreditLimitListPage>> ListAsync(FinanceContext c, CancellationToken t) => core.ListAsync(c, t);
    public Task<ReadResult<PartyCreditLimitAccountView>> GetAccountAsync(FinanceContext c, Guid id, CancellationToken t) => core.GetAccountAsync(c, id, t);
    public Task<ReadResult<PartyCreditLimitHistoryPage>> HistoryAsync(FinanceContext c, Guid id, CancellationToken t) => core.HistoryAsync(c, id, t);
    public Task<ReadResult<PartyCreditLimitCurrencyCandidates>> CurrencyCandidatesAsync(FinanceContext c, CancellationToken t) => core.CurrencyCandidatesAsync(c, t);
}
