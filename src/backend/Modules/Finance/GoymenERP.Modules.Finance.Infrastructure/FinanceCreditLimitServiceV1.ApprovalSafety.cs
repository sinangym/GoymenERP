using System.Data;
using GoymenERP.Modules.Audit.Application.Contracts;
using GoymenERP.Modules.Finance.Contracts;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace GoymenERP.Modules.Finance.Infrastructure;

public sealed partial class FinanceCreditLimitServiceV1
{
    private OwnedLimitDecision? ownedDecision;

    // Same native owner-composition seam as ManualBankPaymentService: only runtime Decide gains an owned boundary.
    // Core synthetic policy goldens retain their existing paths; they are not evidence of relational rollback.
    public IFinanceCreditLimitServiceV1 WithApprovalSafety() => new GuardedFinanceCreditLimitServiceV1(this);

    internal async Task<CreditLimitResultV1> RunOwnedDecisionAsync(FinanceContext context, CommandMetadata metadata,
        CreditLimitDecisionV1 request, CancellationToken token)
    {
        ArgumentNullException.ThrowIfNull(context); ArgumentNullException.ThrowIfNull(metadata); ArgumentNullException.ThrowIfNull(request);
        token.ThrowIfCancellationRequested();
        try { return await Run(false); }
        catch (LimitDecisionRace) { return await Run(true); }
        async Task<CreditLimitResultV1> Run(bool replayOnly)
        {
            // Capture before awaiting fresh authority; check again before ownership. Caller work is never cleanup-owned.
            var cleanBefore = CleanEntry();
            if (await Start(context, metadata, FinanceAction.ApproveCredit, token) is { } denied) return denied;
            if (DecisionShapeRefusal(context, request) is { } invalid) return invalid;
            if (!cleanBefore || !CleanEntry()) return Unavailable();
            var state = new OwnedLimitDecision(request.LimitId, auditOwner!, replayOnly);
            IDbContextTransaction? transaction = null; var committed = false; ownedDecision = state;
            try
            {
                transaction = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, token);
                await RequireFence();
                // Exact native authority, maker/checker, parent/current-limit versions, literal reason, attempt and Audit behavior.
                var result = await DecideAsync(context, metadata, request, token);
                if (result.Outcome is CommandOutcome.Denied or CommandOutcome.Unavailable) throw new LimitDecisionRefusal(result);
                if (result.Outcome == CommandOutcome.Succeeded && (state.Saved || result.IsReplay))
                    result = result with { DecisionReceipt = await ReadDecisionReceiptAsync(context, metadata, request, result, token) };
                await RequireFence(); RequireFlushed();
                if (result.Outcome == CommandOutcome.Succeeded && !result.IsReplay && !state.Saved)
                    throw new InvalidOperationException("Bank credit limit decision has no saved native command receipt.");
                await RequireFence(); RequireFlushed();
                await transaction.CommitAsync(token); committed = true;
                var completed = transaction; transaction = null; await completed.DisposeAsync();
                // Release refusal suppresses the response, without pretending to roll back an already committed receipt.
                await RequireFence(); RequireFlushed();
                return result;
            }
            catch (Exception failure)
            {
                var deadlock = failure is SqlException { Number: 1205 } or DbUpdateException { InnerException: SqlException { Number: 1205 } };
                var faults = new List<Exception>();
                if (transaction is not null)
                {
                    if (!committed) try
                    {
                        // Only a known 1205 with an already completed SqlClient transaction skips redundant rollback.
                        if (!deadlock || transaction.GetDbTransaction() is not SqlTransaction { Connection: null })
                            await transaction.RollbackAsync(CancellationToken.None);
                    }
                    catch (Exception error) { faults.Add(error); }
                    try { await transaction.DisposeAsync(); } catch (Exception error) { faults.Add(error); }
                }
                try { db.ChangeTracker.Clear(); } catch (Exception error) { faults.Add(error); }
                try { state.Writer.DiscardPending(); } catch (Exception error) { faults.Add(error); }
                if (faults.Count > 0) throw new AggregateException("Bank credit limit decision cleanup failed; replay is unsafe.", [failure, .. faults]);
                if (failure is LimitDecisionRefusal refusal) return refusal.Result;
                if (!replayOnly && !committed && (deadlock || state.SaveAttempted && !state.Saved
                    && failure is DbUpdateException update && (update is DbUpdateConcurrencyException || update.InnerException is SqlException { Number: 2601 or 2627 })))
                    throw new LimitDecisionRace();
                if (FinanceTreasuryTransaction.IsTreasuryDatabaseFailure(failure)) return Unavailable();
                throw;
            }
            finally { ownedDecision = null; }
            async Task RequireFence()
            {
                if (await Fence(context, FinanceAction.ApproveCredit, token) is { } refusal)
                    throw new LimitDecisionRefusal(refusal);
            }
            void RequireFlushed()
            {
                if (state.Writer.HasPendingDrafts || db.ChangeTracker.HasChanges())
                    throw new InvalidOperationException("Bank credit limit decision has unflushed owner work.");
            }
        }
        bool CleanEntry() => auditOwner is not null && ownedDecision is null && System.Transactions.Transaction.Current is null
            && db.Database.IsRelational() && db.Database.CurrentTransaction is null
            && !db.ChangeTracker.HasChanges() && !auditOwner.HasPendingDrafts;
        CreditLimitResultV1 Unavailable() => Failure(context, CommandOutcome.Unavailable);
    }

    private async Task SaveOwnedDecisionAsync(FinanceContext context, CancellationToken token)
    {
        var state = ownedDecision ?? throw new InvalidOperationException("Bank credit limit decision ownership is missing.");
        if (state.ReplayOnly) throw new InvalidOperationException("Bank credit limit decision winner read cannot write.");
        await FreshSaveFence();
        if (state.Writer.HasPendingDrafts)
        {
            await state.Writer.WritePendingAsync(db.Database.CurrentTransaction!.GetDbTransaction(), token);
            await FreshSaveFence();
        }
        if (state.Writer.HasPendingDrafts) throw new InvalidOperationException("Bank credit limit decision Audit remains pending.");
        state.SaveAttempted = true; await db.SaveChangesAsync(token); state.Saved = true;
        await FreshSaveFence();
        async Task FreshSaveFence()
        {
            if (await Fence(context, FinanceAction.ApproveCredit, token) is { } refusal)
                throw new LimitDecisionRefusal(refusal);
        }
    }
    private sealed record OwnedLimitDecision(Guid LimitId, IAuditTransactionWriterV1 Writer, bool ReplayOnly)
    { internal bool SaveAttempted; internal bool Saved; }
    private sealed class LimitDecisionRefusal(CreditLimitResultV1 result) : Exception
    { internal CreditLimitResultV1 Result { get; } = result; }
    private sealed class LimitDecisionRace : Exception;
}

internal sealed class GuardedFinanceCreditLimitServiceV1(FinanceCreditLimitServiceV1 core) : IFinanceCreditLimitServiceV1, IFinanceBankCreditLimitDecisionReadV1
{
    public Task<ReadResult<FinanceBankCreditLimitDecisionDetailV1>> ReadDecisionAsync(FinanceContext c, Guid id, Func<CancellationToken, Task> fence, CancellationToken t) => core.ReadDecisionAsync(c, id, fence, t);
    public Task<CreditLimitResultV1> DecideAsync(FinanceContext c, CommandMetadata m, CreditLimitDecisionV1 r, CancellationToken t) => core.RunOwnedDecisionAsync(c, m, r, t);
    public Task<CreditLimitResultV1> ProposeAsync(FinanceContext c, CommandMetadata m, CreditLimitProposeV1 r, CancellationToken t) => core.ProposeAsync(c, m, r, t);
    public Task<ReadResult<CreditLimitWorkspaceV1>> ReadAsync(FinanceContext c, Guid id, CancellationToken t) => core.ReadAsync(c, id, t);
}
