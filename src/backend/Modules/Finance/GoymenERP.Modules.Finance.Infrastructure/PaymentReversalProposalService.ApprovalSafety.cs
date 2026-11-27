using System.Data;
using GoymenERP.Modules.Audit.Application.Contracts;
using GoymenERP.Modules.Finance.Contracts;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace GoymenERP.Modules.Finance.Infrastructure;

public sealed partial class PaymentReversalProposalService
{
    private OwnedPaymentDecision? ownedDecision;

    // Runtime composition only. Existing synthetic core policy tests retain the native calculation paths.
    public IPaymentReversalProposalService WithApprovalSafety() => new GuardedPaymentReversalProposalService(this);

    internal async Task<PaymentReversalProposalCommandResult> RunOwnedDecisionAsync(FinanceContext context,
        CommandMetadata metadata, Guid proposalId, string expectedVersion, string? reason, bool approve, CancellationToken token)
    {
        ArgumentNullException.ThrowIfNull(context); ArgumentNullException.ThrowIfNull(metadata);
        token.ThrowIfCancellationRequested();
        try { return await Run(false); }
        catch (PaymentDecisionRace) { return await Run(true); }
        async Task<PaymentReversalProposalCommandResult> Run(bool replayOnly)
        {
            // Capture entry before the first await, then recheck: another call's work is never owned or discarded.
            var cleanBefore = CleanEntry();
            if (await StartAsync(context, metadata, FinanceAction.ApprovePaymentReversal, proposalId, token) is { } denied) return denied;
            if (!cleanBefore || !CleanEntry()) return Unavailable();
            var state = new OwnedPaymentDecision(approve ? ApproveCommand : RejectCommand, proposalId, auditOwner!, replayOnly);
            IDbContextTransaction? transaction = null; var committed = false; ownedDecision = state;
            try
            {
                transaction = await dbContext.Database.BeginTransactionAsync(IsolationLevel.Serializable, token);
                await RequireFence();
                // Reuse the exact native business methods. ApproveCore avoids nesting the legacy Treasury transaction helper.
                var result = approve ? await ApproveCoreAsync(context, metadata, new(proposalId, expectedVersion), token)
                    : await RejectAsync(context, metadata, new(proposalId, expectedVersion, reason!), token);
                if (result.Outcome is CommandOutcome.Denied or CommandOutcome.Unavailable) throw new PaymentDecisionRefusal(result);
                await RequireFence(); RequireFlushed();
                if (result.Outcome == CommandOutcome.Succeeded && !result.IsReplay && !state.Saved)
                    throw new InvalidOperationException("Payment reversal decision has no saved native receipt.");
                await RequireFence(); RequireFlushed();
                await transaction.CommitAsync(token); committed = true;
                var completed = transaction; transaction = null; await completed.DisposeAsync();
                // A refusal here suppresses release; it cannot claim that an already committed decision was rolled back.
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
                        if (!deadlock || transaction.GetDbTransaction() is not SqlTransaction { Connection: null })
                            await transaction.RollbackAsync(CancellationToken.None);
                    }
                    catch (Exception error) { faults.Add(error); }
                    try { await transaction.DisposeAsync(); } catch (Exception error) { faults.Add(error); }
                }
                try { dbContext.ChangeTracker.Clear(); } catch (Exception error) { faults.Add(error); }
                try { state.Writer.DiscardPending(); } catch (Exception error) { faults.Add(error); }
                if (faults.Count > 0) throw new AggregateException("Payment reversal cleanup failed; replay is unsafe.", [failure, .. faults]);
                if (failure is PaymentDecisionRefusal refusal) return refusal.Result;
                if (!replayOnly && !committed && (deadlock || state.SaveAttempted && !state.Saved
                    && failure is DbUpdateException update && (update is DbUpdateConcurrencyException || update.InnerException is SqlException { Number: 2601 or 2627 })))
                    throw new PaymentDecisionRace();
                if (FinanceTreasuryTransaction.IsTreasuryDatabaseFailure(failure)) return Unavailable();
                throw;
            }
            finally { ownedDecision = null; }
            async Task RequireFence()
            {
                if (await FenceAsync(context, FinanceAction.ApprovePaymentReversal, proposalId, token) is { } refusal)
                    throw new PaymentDecisionRefusal(refusal);
            }
            void RequireFlushed()
            {
                if (state.Writer.HasPendingDrafts || dbContext.ChangeTracker.HasChanges())
                    throw new InvalidOperationException("Payment reversal decision has unflushed owner work.");
            }
        }
        bool CleanEntry() => auditOwner is not null && ownedDecision is null && System.Transactions.Transaction.Current is null
            && dbContext.Database.IsRelational() && dbContext.Database.CurrentTransaction is null
            && !dbContext.ChangeTracker.HasChanges() && !auditOwner.HasPendingDrafts;
        PaymentReversalProposalCommandResult Unavailable() => Refused(context, CommandOutcome.Unavailable);
    }

    private async Task SaveOwnedDecisionAsync(FinanceContext context, string command, CancellationToken token)
    {
        var state = ownedDecision ?? throw new InvalidOperationException("Payment reversal ownership is missing.");
        if (state.ReplayOnly || state.Command != command) throw new InvalidOperationException("Payment reversal replay cannot write or mutate another command.");
        await Fence();
        if (state.Writer.HasPendingDrafts)
        {
            await state.Writer.WritePendingAsync(dbContext.Database.CurrentTransaction!.GetDbTransaction(), token);
            await Fence();
        }
        if (state.Writer.HasPendingDrafts) throw new InvalidOperationException("Payment reversal Audit remains pending.");
        state.SaveAttempted = true; await dbContext.SaveChangesAsync(token); state.Saved = true;
        await Fence();
        async Task Fence()
        {
            if (await FenceAsync(context, FinanceAction.ApprovePaymentReversal, state.ProposalId, token) is { } refusal)
                throw new PaymentDecisionRefusal(refusal);
        }
    }

    private sealed record OwnedPaymentDecision(string Command, Guid ProposalId, IAuditTransactionWriterV1 Writer, bool ReplayOnly)
    { internal bool SaveAttempted; internal bool Saved; }
    private sealed class PaymentDecisionRefusal(PaymentReversalProposalCommandResult result) : Exception
    { internal PaymentReversalProposalCommandResult Result { get; } = result; }
    private sealed class PaymentDecisionRace : Exception;
}

internal sealed class GuardedPaymentReversalProposalService(PaymentReversalProposalService core) : IPaymentReversalProposalService, IPaymentReversalDecisionReadV1
{
    public Task<PaymentReversalProposalCommandResult> ApproveAsync(FinanceContext c, CommandMetadata m, ApprovePaymentReversalRequest r, CancellationToken t)
    { ArgumentNullException.ThrowIfNull(r); return core.RunOwnedDecisionAsync(c, m, r.ProposalId, r.ExpectedVersion, null, true, t); }
    public Task<PaymentReversalProposalCommandResult> RejectAsync(FinanceContext c, CommandMetadata m, RejectPaymentReversalRequest r, CancellationToken t)
    { ArgumentNullException.ThrowIfNull(r); return core.RunOwnedDecisionAsync(c, m, r.ProposalId, r.ExpectedVersion, r.Reason, false, t); }
    public Task<PaymentReversalProposalCommandResult> ProposeAsync(FinanceContext c, CommandMetadata m, ProposePaymentReversalRequest r, CancellationToken t) => core.ProposeAsync(c, m, r, t);
    public Task<ReadResult<PaymentReversalProposalView>> DetailAuthorizedAsync(FinanceContext c, Guid id, CancellationToken t) => core.DetailAuthorizedAsync(c, id, t);
    public Task<ReadResult<PaymentReversalProposalPage>> PendingAuthorizedAsync(FinanceContext c, CancellationToken t) => core.PendingAuthorizedAsync(c, t);
    public Task<ReadResult<PaymentReversalProposalView>> ReadDecisionAsync(FinanceContext c, Guid id, Func<CancellationToken, Task> fence, CancellationToken t) => core.ReadDecisionAsync(c, id, fence, t);
}
