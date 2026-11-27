using System.Data;
using GoymenERP.Modules.Audit.Application.Contracts;
using GoymenERP.Modules.Finance.Contracts;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace GoymenERP.Modules.Finance.Infrastructure;

public sealed partial class ExpensePaymentService
{
    private OwnedExpenseApproval? ownedApproval;

    // Same native owner-composition seam as ManualBankPaymentService: only runtime Approve gains an owned boundary.
    // Core synthetic policy goldens retain their existing paths; they are not evidence of relational rollback.
    public IExpensePaymentService WithApprovalSafety() => new GuardedExpensePaymentService(this);

    internal async Task<ExpensePaymentCommandResult> RunOwnedApprovalAsync(FinanceContext context, CommandMetadata metadata,
        ApproveExpensePaymentRequest request, CancellationToken token)
    {
        ArgumentNullException.ThrowIfNull(context); ArgumentNullException.ThrowIfNull(metadata); ArgumentNullException.ThrowIfNull(request);
        token.ThrowIfCancellationRequested();
        try { return await Run(false); }
        catch (ExpenseApprovalRace) { return await Run(true); }
        async Task<ExpensePaymentCommandResult> Run(bool replayOnly)
        {
            // Capture before awaiting fresh authority; check again before ownership. Caller work is never cleanup-owned.
            var cleanBefore = CleanEntry();
            if (await FenceAsync(context, FinanceAction.ApproveExpensePayment, request.PaymentId, token) is { } denied) return denied;
            if (!cleanBefore || !CleanEntry()) return Unavailable();
            var state = new OwnedExpenseApproval(request.PaymentId, auditOwner!, replayOnly);
            IDbContextTransaction? transaction = null; var committed = false; ownedApproval = state;
            try
            {
                transaction = await dbContext.Database.BeginTransactionAsync(IsolationLevel.Serializable, token);
                await RequireFence();
                // Exact native authority, maker/reviser, version, cash-passivation, attempt and Audit behavior.
                var result = await ApproveAsync(context, metadata, request, token);
                if (result.Outcome is CommandOutcome.Denied or CommandOutcome.Unavailable) throw new ExpenseApprovalRefusal(result);
                if (result.Outcome == CommandOutcome.Succeeded && (state.Saved || result.IsReplay))
                    result = result with { DecisionReceipt = await ReadDecisionReceiptAsync(context, metadata, request, result, token) };
                await RequireFence(); RequireFlushed();
                if (result.Outcome == CommandOutcome.Succeeded && !result.IsReplay && !state.Saved)
                    throw new InvalidOperationException("Expense approval has no saved native command receipt.");
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
                try { dbContext.ChangeTracker.Clear(); } catch (Exception error) { faults.Add(error); }
                try { state.Writer.DiscardPending(); } catch (Exception error) { faults.Add(error); }
                if (faults.Count > 0) throw new AggregateException("Expense approval cleanup failed; replay is unsafe.", [failure, .. faults]);
                if (failure is ExpenseApprovalRefusal refusal) return refusal.Result;
                if (!replayOnly && !committed && (deadlock || state.SaveAttempted && !state.Saved
                    && failure is DbUpdateException update && (update is DbUpdateConcurrencyException || update.InnerException is SqlException { Number: 2601 or 2627 })))
                    throw new ExpenseApprovalRace();
                if (FinanceTreasuryTransaction.IsTreasuryDatabaseFailure(failure)) return Unavailable();
                throw;
            }
            finally { ownedApproval = null; }
            async Task RequireFence()
            {
                if (await FenceAsync(context, FinanceAction.ApproveExpensePayment, request.PaymentId, token) is { } refusal)
                    throw new ExpenseApprovalRefusal(refusal);
            }
            void RequireFlushed()
            {
                if (state.Writer.HasPendingDrafts || dbContext.ChangeTracker.HasChanges())
                    throw new InvalidOperationException("Expense approval has unflushed owner work.");
            }
        }
        bool CleanEntry() => auditOwner is not null && ownedApproval is null && System.Transactions.Transaction.Current is null
            && dbContext.Database.IsRelational() && dbContext.Database.CurrentTransaction is null
            && !dbContext.ChangeTracker.HasChanges() && !auditOwner.HasPendingDrafts;
        ExpensePaymentCommandResult Unavailable() => Refused(context, CommandOutcome.Unavailable);
    }

    private async Task SaveOwnedApprovalAsync(FinanceContext context, CancellationToken token)
    {
        var state = ownedApproval ?? throw new InvalidOperationException("Expense approval ownership is missing.");
        if (state.ReplayOnly) throw new InvalidOperationException("Expense approval winner read cannot write.");
        await Fence();
        if (state.Writer.HasPendingDrafts)
        {
            await state.Writer.WritePendingAsync(dbContext.Database.CurrentTransaction!.GetDbTransaction(), token);
            await Fence();
        }
        if (state.Writer.HasPendingDrafts) throw new InvalidOperationException("Expense approval Audit remains pending.");
        state.SaveAttempted = true; await dbContext.SaveChangesAsync(token); state.Saved = true;
        await Fence();
        async Task Fence()
        {
            if (await FenceAsync(context, FinanceAction.ApproveExpensePayment, state.PaymentId, token) is { } refusal)
                throw new ExpenseApprovalRefusal(refusal);
        }
    }
    private sealed record OwnedExpenseApproval(Guid PaymentId, IAuditTransactionWriterV1 Writer, bool ReplayOnly)
    { internal bool SaveAttempted; internal bool Saved; }
    private sealed class ExpenseApprovalRefusal(ExpensePaymentCommandResult result) : Exception
    { internal ExpensePaymentCommandResult Result { get; } = result; }
    private sealed class ExpenseApprovalRace : Exception;
}

internal sealed class GuardedExpensePaymentService(ExpensePaymentService core) : IExpensePaymentService, IExpensePaymentDecisionReadV1
{
    public Task<ReadResult<ExpensePaymentDecisionDetailV1>> ReadDecisionAsync(FinanceContext c, Guid id, Func<CancellationToken, Task> fence, CancellationToken t) => core.ReadDecisionAsync(c, id, fence, t);
    public Task<ExpensePaymentCommandResult> ApproveAsync(FinanceContext c, CommandMetadata m, ApproveExpensePaymentRequest r, CancellationToken t) => core.RunOwnedApprovalAsync(c, m, r, t);
    public Task<ExpensePaymentCommandResult> DraftSaveAsync(FinanceContext c, CommandMetadata m, ExpensePaymentDraftSaveRequest r, CancellationToken t) => core.DraftSaveAsync(c, m, r, t);
    public Task<ExpensePaymentCommandResult> RecordAsync(FinanceContext c, CommandMetadata m, RecordExpensePaymentRequest r, CancellationToken t) => core.RecordAsync(c, m, r, t);
    public Task<ReadResult<ExpensePaymentPage>> ListAuthorizedAsync(FinanceContext c, string? cursor, CancellationToken t) => core.ListAuthorizedAsync(c, cursor, t);
    public Task<ReadResult<ExpensePaymentView>> DetailAuthorizedAsync(FinanceContext c, Guid id, CancellationToken t) => core.DetailAuthorizedAsync(c, id, t);
    public Task<ReadResult<ExpenseItemCatalogView>> ExpenseItemsAuthorizedAsync(FinanceContext c, CancellationToken t) => core.ExpenseItemsAuthorizedAsync(c, t);
    public Task<ReadResult<ExpensePaymentDraftContextView>> DraftContextAuthorizedAsync(FinanceContext c, ExpensePaymentSourceKind kind, Guid treasuryAccountId, DateOnly occurredOn, CancellationToken t) => core.DraftContextAuthorizedAsync(c, kind, treasuryAccountId, occurredOn, t);
}
