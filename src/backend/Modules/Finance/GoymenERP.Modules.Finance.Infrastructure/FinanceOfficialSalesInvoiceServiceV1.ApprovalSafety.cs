using System.Data;
using GoymenERP.Modules.Audit.Application.Contracts;
using GoymenERP.Modules.Finance.Contracts;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace GoymenERP.Modules.Finance.Infrastructure;

public sealed partial class FinanceOfficialSalesInvoiceServiceV1
{
    private OwnedInvoiceDecision? ownedInvoiceDecision;

    // Runtime DI exposes this composition, including its read-only candidates alias, never the raw policy-test core.
    public IFinanceOfficialSalesInvoiceServiceV1 WithApprovalSafety(IAuditTransactionWriterV1? writer) =>
        new GuardedFinanceOfficialSalesInvoiceServiceV1(this, writer);

    internal async Task<FinanceOfficialSalesInvoiceResultV1> RunOwnedDecisionAsync(FinanceContext context,
        CommandMetadata metadata, Guid invoiceId, string expectedVersion, string? reason, string command,
        FinanceOfficialSalesInvoiceAuthorityActionV1 action, IAuditTransactionWriterV1? writer,
        Func<CancellationToken, Task<FinanceOfficialSalesInvoiceResultV1>> execute, CancellationToken token)
    {
        ArgumentNullException.ThrowIfNull(context); ArgumentNullException.ThrowIfNull(metadata);
        token.ThrowIfCancellationRequested();
        try { return await RunAsync(false); }
        catch (InvoiceDecisionSaveRace) { return await RunAsync(true); }

        async Task<FinanceOfficialSalesInvoiceResultV1> RunAsync(bool replayOnly)
        {
            // Refusals before ownership never clear a caller's tracker, transaction or queued Audit.
            var cleanBefore = CleanEntry();
            if (await FenceAsync(context, action, invoiceId, token) is { } denied) return denied;
            if (EntryRefusal() is { } invalid) return invalid;
            if (!cleanBefore || !CleanEntry()) return Unavailable();
            if (command != ApproveCommand && command != RejectCommand
                || action != (command == ApproveCommand ? FinanceOfficialSalesInvoiceAuthorityActionV1.Approve : FinanceOfficialSalesInvoiceAuthorityActionV1.Reject))
                throw new InvalidOperationException("Unknown official sales invoice decision.");
            var state = new OwnedInvoiceDecision(command, action, writer!, replayOnly);
            IDbContextTransaction? transaction = null;
            var committed = false;
            ownedInvoiceDecision = state;
            try
            {
                transaction = await dbContext.Database.BeginTransactionAsync(IsolationLevel.Serializable, token);
                await RequireFence();
                var result = await execute(token);
                if (result.Outcome is CommandOutcome.Denied or CommandOutcome.Unavailable) throw new InvoiceDecisionRefusal(result);
                if (result.Outcome == CommandOutcome.Succeeded && (state.Saved || result.IsReplay))
                    result = result with { DecisionReceipt = await ReadDecisionReceiptAsync(context, metadata, invoiceId, command, result, token) };
                await RequireFence(); RequireFlushed();
                if (result.Outcome == CommandOutcome.Succeeded && !result.IsReplay && !state.Saved)
                    throw new InvalidOperationException("Official sales invoice decision has no saved native receipt.");
                await RequireFence(); RequireFlushed();
                await transaction.CommitAsync(token); committed = true;
                var completed = transaction; transaction = null;
                await completed.DisposeAsync();
                // Release can be refused after commit; this never claims that the durable decision was rolled back.
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
                        // SQL Server already aborts a deadlock victim. Only its invalid provider transaction skips a second rollback.
                        if (!deadlock || transaction.GetDbTransaction() is not SqlTransaction { Connection: null })
                            await transaction.RollbackAsync(CancellationToken.None);
                    }
                    catch (Exception error) { faults.Add(error); }
                    try { await transaction.DisposeAsync(); } catch (Exception error) { faults.Add(error); }
                }
                try { dbContext.ChangeTracker.Clear(); } catch (Exception error) { faults.Add(error); }
                try { state.Writer.DiscardPending(); } catch (Exception error) { faults.Add(error); }
                if (faults.Count > 0) throw new AggregateException("Official sales invoice cleanup failed; replay is unsafe.", [failure, .. faults]);
                if (failure is InvoiceDecisionRefusal refusal) return refusal.Result;
                // Serializable reads and the actual Audit write can lose a deadlock before owner SaveChanges.
                if (!replayOnly && !committed && (deadlock
                    || state.SaveAttempted && !state.Saved && failure is DbUpdateException update && IsLostRace(update)))
                    throw new InvoiceDecisionSaveRace();
                throw;
            }
            finally { ownedInvoiceDecision = null; }

            async Task RequireFence()
            {
                if (await FenceAsync(context, action, invoiceId, token) is { } refusal) throw new InvoiceDecisionRefusal(refusal);
            }
            void RequireFlushed()
            {
                if (state.Writer.HasPendingDrafts || dbContext.ChangeTracker.HasChanges())
                    throw new InvalidOperationException("Official sales invoice decision has unflushed owner work.");
            }
        }
        bool CleanEntry() => writer is not null && ownedInvoiceDecision is null && dbContext.Database.IsRelational()
            && System.Transactions.Transaction.Current is null && dbContext.Database.CurrentTransaction is null
            && !dbContext.ChangeTracker.HasChanges() && !writer.HasPendingDrafts;
        FinanceOfficialSalesInvoiceResultV1 Unavailable() => Refused(context, CommandOutcome.Unavailable,
            Issue("request", "finance_official_documents_unavailable"));
        // Preserve the native Approve/Reject entry order and guards before an unavailable persistence boundary.
        FinanceOfficialSalesInvoiceResultV1? EntryRefusal()
        {
            if (command == RejectCommand)
            {
                if (string.IsNullOrWhiteSpace(reason) || !string.Equals(reason, reason.Trim(), StringComparison.Ordinal))
                    return Refused(context, CommandOutcome.ValidationFailed, Issue("reason", "reason_required"));
                if (reason.Length > RejectionReasonMaximumLength)
                    return Refused(context, CommandOutcome.ValidationFailed, Issue("reason", "finance_value_does_not_fit"));
            }
            if (!ValidIdentity(metadata.IdempotencyKey) || !ValidIdentity(expectedVersion) || !ValidIdentity(context.ActorId) || !ValidIdentity(context.CorrelationId))
                return Refused(context, CommandOutcome.ValidationFailed, Issue("request", "finance_value_does_not_fit"));
            return invoiceId == Guid.Empty ? Refused(context, CommandOutcome.ValidationFailed, Issue("invoiceId", "identity_required")) : null;
        }
    }

    private async Task SaveOwnedDecisionAsync(FinanceContext context, string command,
        FinanceOfficialSalesInvoiceAuthorityActionV1 action, Guid invoiceId, CancellationToken token)
    {
        var state = ownedInvoiceDecision ?? throw new InvalidOperationException("Official sales invoice decision ownership is absent.");
        if (state.ReplayOnly || state.Command != command || state.Action != action)
            throw new InvalidOperationException("Official sales invoice decision cannot write another command or retry mutation.");
        await RequireFence();
        if (state.Writer.HasPendingDrafts)
        {
            await state.Writer.WritePendingAsync(dbContext.Database.CurrentTransaction!.GetDbTransaction(), token);
            await RequireFence();
        }
        if (state.Writer.HasPendingDrafts) throw new InvalidOperationException("Official sales invoice Audit remains pending.");
        state.SaveAttempted = true; await dbContext.SaveChangesAsync(token); state.Saved = true;
        await RequireFence();
        async Task RequireFence()
        {
            if (await FenceAsync(context, action, invoiceId, token) is { } refusal) throw new InvoiceDecisionRefusal(refusal);
        }
    }

    private sealed record OwnedInvoiceDecision(string Command, FinanceOfficialSalesInvoiceAuthorityActionV1 Action,
        IAuditTransactionWriterV1 Writer, bool ReplayOnly) { internal bool SaveAttempted; internal bool Saved; }
    private sealed class InvoiceDecisionRefusal(FinanceOfficialSalesInvoiceResultV1 result) : Exception
    { internal FinanceOfficialSalesInvoiceResultV1 Result { get; } = result; }
    private sealed class InvoiceDecisionSaveRace : Exception;
}

internal sealed class GuardedFinanceOfficialSalesInvoiceServiceV1(FinanceOfficialSalesInvoiceServiceV1 core,
    IAuditTransactionWriterV1? writer) : IFinanceOfficialSalesInvoiceServiceV1, IFinanceOfficialSalesInvoiceCandidatesV1, IFinanceOfficialSalesInvoiceDecisionReadV1
{
    public Task<FinanceOfficialSalesInvoiceResultV1> ApproveAsync(FinanceContext c, CommandMetadata m,
        FinanceOfficialSalesInvoiceApproveRequestV1 r, CancellationToken t)
    {
        ArgumentNullException.ThrowIfNull(r);
        return core.RunOwnedDecisionAsync(c, m, r.InvoiceId, r.ExpectedVersion, null, FinanceOfficialSalesInvoiceServiceV1.ApproveCommand,
            FinanceOfficialSalesInvoiceAuthorityActionV1.Approve, writer, token => core.ApproveAsync(c, m, r, token), t);
    }
    public Task<FinanceOfficialSalesInvoiceResultV1> RejectAsync(FinanceContext c, CommandMetadata m,
        FinanceOfficialSalesInvoiceRejectRequestV1 r, CancellationToken t)
    {
        ArgumentNullException.ThrowIfNull(r);
        return core.RunOwnedDecisionAsync(c, m, r.InvoiceId, r.ExpectedVersion, r.Reason, FinanceOfficialSalesInvoiceServiceV1.RejectCommand,
            FinanceOfficialSalesInvoiceAuthorityActionV1.Reject, writer, token => core.RejectAsync(c, m, r, token), t);
    }
    public Task<ReadResult<FinanceOfficialSalesInvoiceViewV1>> ReadDecisionAsync(FinanceContext c, Guid id,
        Func<CancellationToken, Task> fence, CancellationToken t) => core.ReadDecisionAsync(c, id, fence, t);
    public Task<FinanceOfficialSalesInvoiceReservationResultV1> ReserveAsync(FinanceContext c, CommandMetadata m, FinanceOfficialSalesInvoiceReservationRequestV1 r, CancellationToken t) => core.ReserveAsync(c, m, r, t);
    public Task<FinanceOfficialSalesInvoiceResultV1> RecordAsync(FinanceContext c, CommandMetadata m, FinanceOfficialSalesInvoiceRecordRequestV1 r, CancellationToken t) => core.RecordAsync(c, m, r, t);
    public Task<FinanceOfficialSalesInvoiceResultV1> WithdrawAsync(FinanceContext c, CommandMetadata m, FinanceOfficialSalesInvoiceWithdrawRequestV1 r, CancellationToken t) => core.WithdrawAsync(c, m, r, t);
    public Task<ReadResult<FinanceOfficialSalesInvoiceViewV1>> GetAsync(FinanceContext c, Guid id, CancellationToken t) => core.GetAsync(c, id, t);
    public Task<ReadResult<FinanceOfficialSalesInvoicePageV1>> ListAsync(FinanceContext c, bool pending, string? cursor, CancellationToken t) => core.ListAsync(c, pending, cursor, t);
    public Task<ReadResult<FinanceOfficialSalesInvoiceCurrenciesV1>> ReadCurrenciesAsync(FinanceContext c, CancellationToken t) => core.ReadCurrenciesAsync(c, t);
}
