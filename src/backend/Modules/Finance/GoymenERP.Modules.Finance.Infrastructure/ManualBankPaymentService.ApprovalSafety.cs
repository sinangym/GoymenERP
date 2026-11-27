using System.Data;
using GoymenERP.Modules.Audit.Application.Contracts;
using GoymenERP.Modules.Finance.Contracts;
using GoymenERP.Modules.Finance.Infrastructure.Persistence;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace GoymenERP.Modules.Finance.Infrastructure;

public sealed partial class ManualBankPaymentService
{
    private bool ownedApprovalActive;
    private bool ownedApprovalReplayOnly;
    private Guid ownedApprovalPaymentId;

    // Only the production owner-composition wrapper enters this lease. Raw synthetic core policy tests are not SQL proof.
    internal async Task<ManualBankPaymentCommandResult> RunOwnedApprovalCoreAsync(FinanceContext context, CommandMetadata metadata,
        ApproveManualBankPaymentRequest request, IAuditTransactionWriterV1 writer, bool replayOnly, CancellationToken token)
    {
        if (ownedApprovalActive || auditOwner is null || !ReferenceEquals(auditOwner, writer)
            || dbContext.Database.CurrentTransaction is null)
            throw new InvalidOperationException("A native approval requires its own transaction and the same Audit owner.");
        ownedApprovalActive = true;
        ownedApprovalReplayOnly = replayOnly;
        ownedApprovalPaymentId = request.PaymentId;
        try { return await ApproveAsync(context, metadata, request, token); }
        finally { ownedApprovalPaymentId = Guid.Empty; ownedApprovalReplayOnly = false; ownedApprovalActive = false; }
    }

    internal async Task RequireOwnedApprovalFenceAsync(FinanceContext context, Guid paymentId, CancellationToken token)
    {
        if (await FenceAsync(context, FinanceAction.ApproveManualBankPayment, paymentId, token) is { } refused)
            throw new OwnedApprovalRefusal(refused);
    }

    private async Task SaveOwnedApprovalAsync(FinanceContext context, CancellationToken token)
    {
        await RequireOwnedApprovalFenceAsync(context, ownedApprovalPaymentId, token);
        if (auditOwner!.HasPendingDrafts)
        {
            await auditOwner.WritePendingAsync(dbContext.Database.CurrentTransaction!.GetDbTransaction(), token);
            await RequireOwnedApprovalFenceAsync(context, ownedApprovalPaymentId, token);
        }
        if (auditOwner.HasPendingDrafts) throw new InvalidOperationException("Approval Audit remains pending.");
        await dbContext.SaveChangesAsync(token);
        await RequireOwnedApprovalFenceAsync(context, ownedApprovalPaymentId, token);
    }

    internal sealed class OwnedApprovalRefusal(ManualBankPaymentCommandResult result) : Exception
    { internal ManualBankPaymentCommandResult Result { get; } = result; }
}

/// <summary>Native runtime composition: only Approve gains an owned boundary. Draft, Record and authorized reads retain the
/// existing core paths. Same clean-owned/audit/abort pattern as FinanceBankAccountService; no second payment calculation.</summary>
internal sealed class GuardedManualBankPaymentService(ManualBankPaymentService core, FinanceDbContext database,
    IAuditTransactionWriterV1? audit) : IManualBankPaymentService
{
    private bool active;

    public async Task<ManualBankPaymentCommandResult> ApproveAsync(FinanceContext context, CommandMetadata metadata,
        ApproveManualBankPaymentRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(metadata);
        ArgumentNullException.ThrowIfNull(request);
        cancellationToken.ThrowIfCancellationRequested();
        try { return await RunAsync(false); }
        catch (ApprovalSaveRace) { return await RunAsync(true); }

        async Task<ManualBankPaymentCommandResult> RunAsync(bool replayOnly)
        {
            // Entry refusal is OUTSIDE owned cleanup: never clear a caller's tracker, borrowed transaction or Audit batch.
            if (audit is null || System.Transactions.Transaction.Current is not null || !database.Database.IsRelational()
                || database.Database.CurrentTransaction is not null || database.ChangeTracker.HasChanges()
                || audit.HasPendingDrafts || active) return Unavailable(context);
            active = true;
            IDbContextTransaction? transaction = null;
            var disposed = false;
            var committed = false;
            try
            {
                transaction = await database.Database.BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken);
                await core.RequireOwnedApprovalFenceAsync(context, request.PaymentId, cancellationToken);
                var result = await core.RunOwnedApprovalCoreAsync(context, metadata, request, audit, replayOnly, cancellationToken);
                if (result.Outcome is CommandOutcome.Denied or CommandOutcome.Unavailable)
                    throw new ManualBankPaymentService.OwnedApprovalRefusal(result);
                await core.RequireOwnedApprovalFenceAsync(context, request.PaymentId, cancellationToken);
                if (audit.HasPendingDrafts) throw new InvalidOperationException("Approval Audit remains pending.");
                await core.RequireOwnedApprovalFenceAsync(context, request.PaymentId, cancellationToken);
                await transaction.CommitAsync(cancellationToken);
                committed = true;
                return result;
            }
            catch (Exception error)
            {
                var deadlock = IsApprovalDeadlock(error);
                if (transaction is not null)
                {
                    try
                    {
                        // A real 1205 victim can already be rolled back by SQL Server. Skip only this completed
                        // SqlClient transaction; other rollback faults remain failures, and cleanup is mandatory.
                        if (!committed && !(deadlock && transaction.GetDbTransaction() is SqlTransaction { Connection: null }))
                            await transaction.RollbackAsync(CancellationToken.None);
                    }
                    finally
                    {
                        try { disposed = true; await transaction.DisposeAsync(); }
                        finally
                        {
                            try { database.ChangeTracker.Clear(); }
                            finally { audit.DiscardPending(); }
                        }
                    }
                }
                if (error is ManualBankPaymentService.OwnedApprovalRefusal refused) return refused.Result;
                // A Serializable deadlock can occur in owner reads/Audit before Save, or after Save before Commit.
                // Cleanup must finish first; the only recovery is one immutable original-key read.
                if (!replayOnly && !committed && deadlock) throw new ApprovalSaveRace();
                if (error is DbUpdateException update && (update is DbUpdateConcurrencyException
                    || update.InnerException is SqlException { Number: 2601 or 2627 }))
                {
                    if (!replayOnly) throw new ApprovalSaveRace();
                    return new(CommandOutcome.VersionConflict, null, false, context.CorrelationId, [new("request", "concurrent_change")]);
                }
                if (FinanceTreasuryTransaction.IsTreasuryDatabaseFailure(error)) return Unavailable(context);
                throw;
            }
            finally
            {
                try { if (transaction is not null && !disposed) await transaction.DisposeAsync(); }
                finally { active = false; }
            }
        }
    }

    private static ManualBankPaymentCommandResult Unavailable(FinanceContext context) => new(CommandOutcome.Unavailable,
        null, false, context.CorrelationId, [new("request", "manual_bank_payment_approval_unavailable")]);
    private static bool IsApprovalDeadlock(Exception error) => error is SqlException { Number: 1205 }
        or DbUpdateException { InnerException: SqlException { Number: 1205 } };
    private sealed class ApprovalSaveRace : Exception;

    public Task<ManualBankPaymentCommandResult> DraftSaveAsync(FinanceContext c, CommandMetadata m, ManualBankPaymentDraftSaveRequest r, CancellationToken t) => core.DraftSaveAsync(c, m, r, t);
    public Task<ManualBankPaymentCommandResult> RecordAsync(FinanceContext c, CommandMetadata m, RecordManualBankPaymentRequest r, CancellationToken t) => core.RecordAsync(c, m, r, t);
    public Task<ReadResult<ManualBankPaymentPage>> ListAuthorizedAsync(FinanceContext c, string? cursor, CancellationToken t) => core.ListAuthorizedAsync(c, cursor, t);
    public Task<ReadResult<ManualBankPaymentView>> DetailAuthorizedAsync(FinanceContext c, Guid id, CancellationToken t) => core.DetailAuthorizedAsync(c, id, t);
}
