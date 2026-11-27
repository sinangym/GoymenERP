using System.Data;
using GoymenERP.Modules.Audit.Application.Contracts;
using GoymenERP.Modules.Finance.Contracts;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace GoymenERP.Modules.Finance.Infrastructure;

public sealed partial class FinanceOpeningBalanceService
{
    private OwnedOpeningDecision? ownedOpeningDecision;

    // Runtime exposes the guarded interface. Raw core calls remain only the existing native policy-test seam.
    public IFinanceOpeningBalanceService WithApprovalSafety() => new GuardedOpeningBalanceService(this, auditOwner);

    private async Task<OpeningBalanceCommandResult> RunOwnedOpeningDecisionAsync(FinanceContext context,
        CommandMetadata metadata, Guid openingId, string expectedVersion, string? reason, string command,
        IAuditTransactionWriterV1? writer, Func<CancellationToken, Task<OpeningBalanceCommandResult>> execute,
        CancellationToken token, Guid? boundReversalId = null)
    {
        ArgumentNullException.ThrowIfNull(context); ArgumentNullException.ThrowIfNull(metadata);
        token.ThrowIfCancellationRequested();
        try { return await RunAsync(false); }
        catch (OpeningDecisionSaveRace) { return await RunAsync(true); }

        async Task<OpeningBalanceCommandResult> RunAsync(bool replayOnly)
        {
            // Observe both sides of the first authority await; entry refusals own no caller state.
            var cleanBefore = CleanEntry();
            if (await StartAsync(context, metadata, FinanceAction.ApproveOpeningBalance, token) is { } denied) return denied;
            if (expectedVersion is null)
                return Refused(context, CommandOutcome.ValidationFailed, Issue("expectedVersion", "identity_required"));
            if (command is RejectCommand or RejectReversalCommand && ReasonRefusal(context, reason!) is { } invalid) return invalid;
            if (boundReversalId == Guid.Empty)
                return Refused(context, CommandOutcome.ValidationFailed, Issue("reversalId", "identity_required"));
            if (!cleanBefore || !CleanEntry()) return Unavailable();
            if (command is not (ApproveCommand or RejectCommand or ApproveReversalCommand or RejectReversalCommand))
                throw new InvalidOperationException("Unknown opening balance decision.");
            var state = new OwnedOpeningDecision(command, writer!, replayOnly, boundReversalId);
            IDbContextTransaction? transaction = null;
            var committed = false;
            ownedOpeningDecision = state;
            try
            {
                transaction = await dbContext.Database.BeginTransactionAsync(IsolationLevel.Serializable, token);
                await RequireFence();
                var result = await execute(token);
                if (result.Outcome is CommandOutcome.Denied or CommandOutcome.Unavailable) throw new OpeningDecisionRefusal(result);
                if (result.Outcome == CommandOutcome.Succeeded && boundReversalId is { } bound && result.Resource?.Reversal?.Id != bound)
                    throw new OpeningDecisionRefusal(Unavailable());
                if (result.Outcome == CommandOutcome.Succeeded && (state.Saved || result.IsReplay))
                    result = result with { DecisionReceipt = await ReadDecisionReceiptAsync(context, metadata, openingId, expectedVersion, reason, command, result, token) };
                await RequireFence(); RequireFlushed();
                if (result.Outcome == CommandOutcome.Succeeded && !result.IsReplay && !state.Saved)
                    throw new InvalidOperationException("Opening balance decision has no saved native receipt.");
                await RequireFence(); RequireFlushed();
                await transaction.CommitAsync(token); committed = true;
                var completed = transaction; transaction = null;
                await completed.DisposeAsync();
                // A denied release does not undo an already committed opening movement or decision.
                await RequireFence(); RequireFlushed();
                return result;
            }
            catch (Exception failure)
            {
                var deadlock = failure is SqlException { Number: 1205 }
                    or DbUpdateException { InnerException: SqlException { Number: 1205 } };
                var faults = new List<Exception>();
                if (transaction is not null)
                {
                    if (!committed) try
                    {
                        // A known SQL Server deadlock victim can already have no live provider transaction.
                        if (!deadlock || transaction.GetDbTransaction() is not SqlTransaction { Connection: null })
                            await transaction.RollbackAsync(CancellationToken.None);
                    }
                    catch (Exception error) { faults.Add(error); }
                    try { await transaction.DisposeAsync(); } catch (Exception error) { faults.Add(error); }
                }
                try { dbContext.ChangeTracker.Clear(); } catch (Exception error) { faults.Add(error); }
                try { state.Writer.DiscardPending(); } catch (Exception error) { faults.Add(error); }
                if (faults.Count > 0) throw new AggregateException("Opening balance cleanup failed; replay is unsafe.", [failure, .. faults]);
                if (failure is OpeningDecisionRefusal refusal) return refusal.Result;
                if (!replayOnly && !committed && (deadlock || state.SaveAttempted && !state.Saved
                    && failure is DbUpdateException update && (update is DbUpdateConcurrencyException
                        || update.InnerException is SqlException { Number: 2601 or 2627 })))
                    throw new OpeningDecisionSaveRace();
                throw;
            }
            finally { ownedOpeningDecision = null; }

            async Task RequireFence()
            {
                if (await FenceAsync(context, FinanceAction.ApproveOpeningBalance, token) is { } refusal)
                    throw new OpeningDecisionRefusal(refusal);
            }
            void RequireFlushed()
            {
                if (state.Writer.HasPendingDrafts || dbContext.ChangeTracker.HasChanges())
                    throw new InvalidOperationException("Opening balance decision has unflushed owner work.");
            }
        }
        bool CleanEntry() => writer is not null && ownedOpeningDecision is null && dbContext.Database.IsRelational()
            && System.Transactions.Transaction.Current is null && dbContext.Database.CurrentTransaction is null
            && !dbContext.ChangeTracker.HasChanges() && !writer.HasPendingDrafts;
        OpeningBalanceCommandResult Unavailable() => Refused(context, CommandOutcome.Unavailable,
            Issue("request", TreasuryBalanceAnswer.UnavailableCode));
    }

    private async Task SaveOwnedOpeningDecisionAsync(FinanceContext context, string command, CancellationToken token)
    {
        var state = ownedOpeningDecision ?? throw new InvalidOperationException("Opening decision ownership is absent.");
        if (state.ReplayOnly || state.Command != command)
            throw new InvalidOperationException("Opening decision cannot write another command or retry mutation.");
        await RequireFence();
        if (state.Writer.HasPendingDrafts)
        {
            await state.Writer.WritePendingAsync(dbContext.Database.CurrentTransaction!.GetDbTransaction(), token);
            await RequireFence();
        }
        if (state.Writer.HasPendingDrafts) throw new InvalidOperationException("Opening balance Audit remains pending.");
        state.SaveAttempted = true; await dbContext.SaveChangesAsync(token); state.Saved = true;
        await RequireFence();
        async Task RequireFence()
        {
            if (await FenceAsync(context, FinanceAction.ApproveOpeningBalance, token) is { } refusal)
                throw new OpeningDecisionRefusal(refusal);
        }
    }

    private sealed record OwnedOpeningDecision(string Command, IAuditTransactionWriterV1 Writer, bool ReplayOnly, Guid? BoundReversalId)
    { internal bool SaveAttempted; internal bool Saved; }
    private sealed class OpeningDecisionRefusal(OpeningBalanceCommandResult result) : Exception
    { internal OpeningBalanceCommandResult Result { get; } = result; }
    private sealed class OpeningDecisionSaveRace : Exception;

    private sealed class GuardedOpeningBalanceService(FinanceOpeningBalanceService core, IAuditTransactionWriterV1? writer)
        : IFinanceOpeningBalanceService, IFinanceOpeningBalanceDecisionReadV1, IFinanceOpeningBalanceReversalDecisionV1
    {
        public Task<OpeningBalanceCommandResult> ApproveAsync(FinanceContext c, CommandMetadata m, OpeningBalanceApproveRequest r, CancellationToken t)
        {
            ArgumentNullException.ThrowIfNull(r);
            return core.RunOwnedOpeningDecisionAsync(c, m, r.OpeningBalanceId, r.ExpectedVersion, null, ApproveCommand, writer,
                token => core.ApproveAsync(c, m, r, token), t);
        }
        public Task<OpeningBalanceCommandResult> RejectAsync(FinanceContext c, CommandMetadata m, OpeningBalanceRejectRequest r, CancellationToken t)
        {
            ArgumentNullException.ThrowIfNull(r);
            return core.RunOwnedOpeningDecisionAsync(c, m, r.OpeningBalanceId, r.ExpectedVersion, r.Reason, RejectCommand, writer,
                token => core.RejectAsync(c, m, r, token), t);
        }
        public Task<OpeningBalanceCommandResult> ApproveReversalAsync(FinanceContext c, CommandMetadata m, OpeningBalanceReversalApproveRequest r, CancellationToken t)
        {
            ArgumentNullException.ThrowIfNull(r);
            return core.RunOwnedOpeningDecisionAsync(c, m, r.OpeningBalanceId, r.ExpectedVersion, null, ApproveReversalCommand, writer,
                token => core.ApproveReversalAsync(c, m, r, token), t);
        }
        public Task<OpeningBalanceCommandResult> RejectReversalAsync(FinanceContext c, CommandMetadata m, OpeningBalanceReversalRejectRequest r, CancellationToken t)
        {
            ArgumentNullException.ThrowIfNull(r);
            return core.RunOwnedOpeningDecisionAsync(c, m, r.OpeningBalanceId, r.ExpectedVersion, r.Reason, RejectReversalCommand, writer,
                token => core.RejectReversalAsync(c, m, r, token), t);
        }
        public Task<OpeningBalanceCommandResult> ApproveReversalDecisionAsync(FinanceContext c, CommandMetadata m, Guid reversalId, OpeningBalanceReversalApproveRequest r, CancellationToken t)
        {
            ArgumentNullException.ThrowIfNull(r);
            return core.RunOwnedOpeningDecisionAsync(c, m, r.OpeningBalanceId, r.ExpectedVersion, null, ApproveReversalCommand, writer,
                token => core.ApproveReversalAsync(c, m, r, token), t, reversalId);
        }
        public Task<OpeningBalanceCommandResult> RejectReversalDecisionAsync(FinanceContext c, CommandMetadata m, Guid reversalId, OpeningBalanceReversalRejectRequest r, CancellationToken t)
        {
            ArgumentNullException.ThrowIfNull(r);
            return core.RunOwnedOpeningDecisionAsync(c, m, r.OpeningBalanceId, r.ExpectedVersion, r.Reason, RejectReversalCommand, writer,
                token => core.RejectReversalAsync(c, m, r, token), t, reversalId);
        }
        public Task<ReadResult<OpeningBalanceDecisionDetailV1>> ReadOpeningDecisionAsync(FinanceContext c, Guid id, Func<CancellationToken, Task> fence, CancellationToken t) => core.ReadOpeningDecisionAsync(c, id, fence, t);
        public Task<ReadResult<OpeningBalanceDecisionDetailV1>> ReadReversalDecisionAsync(FinanceContext c, Guid id, Func<CancellationToken, Task> fence, CancellationToken t) => core.ReadReversalDecisionAsync(c, id, fence, t);
        public Task<OpeningBalanceCommandResult> ProposeAsync(FinanceContext c, CommandMetadata m, OpeningBalanceProposeRequest r, CancellationToken t) => core.ProposeAsync(c, m, r, t);
        public Task<OpeningBalanceCommandResult> ProposeReversalAsync(FinanceContext c, CommandMetadata m, OpeningBalanceReversalProposeRequest r, CancellationToken t) => core.ProposeReversalAsync(c, m, r, t);
        public Task<ReadResult<OpeningPeriodView>> GetPeriodAsync(FinanceContext c, CancellationToken t) => core.GetPeriodAsync(c, t);
        public Task<ReadResult<OpeningBalanceListPage>> ListAsync(FinanceContext c, CancellationToken t) => core.ListAsync(c, t);
        public Task<ReadResult<OpeningBalanceCurrencyCandidates>> CurrencyCandidatesAsync(FinanceContext c, CancellationToken t) => core.CurrencyCandidatesAsync(c, t);
    }
}
