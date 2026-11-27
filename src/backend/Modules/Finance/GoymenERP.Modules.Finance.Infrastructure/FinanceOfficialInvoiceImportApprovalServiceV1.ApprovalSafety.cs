using System.Data;
using GoymenERP.Modules.Audit.Application.Contracts;
using GoymenERP.Modules.Finance.Contracts;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace GoymenERP.Modules.Finance.Infrastructure;

public sealed partial class FinanceOfficialInvoiceImportApprovalServiceV1
{
    private OwnedApproval? ownedApproval;

    public async Task<FinanceOfficialInvoiceImportApprovalResultV1> ApproveAsync(FinanceContext context,
        CommandMetadata metadata, FinanceOfficialInvoiceImportApproveRequestV1 request, CancellationToken token)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(metadata);
        ArgumentNullException.ThrowIfNull(request);
        token.ThrowIfCancellationRequested();
        try { return await RunAsync(false); }
        catch (ApprovalSaveRace) { return await RunAsync(true); }

        async Task<FinanceOfficialInvoiceImportApprovalResultV1> RunAsync(bool replayOnly)
        {
            // Both checks precede owned cleanup: authority callbacks cannot donate caller work to this command.
            var cleanBefore = CleanEntry();
            if (await FenceAsync(context, FinanceOfficialInvoiceImportAuthorityActionV1.Approve, request.BatchId, token) is { } denied)
                return denied;
            if (!ValidIdentity(metadata.IdempotencyKey) || !ValidIdentity(request.ExpectedVersion)
                || !ValidIdentity(context.ActorId) || !ValidIdentity(context.CorrelationId))
                return Refused(context, CommandOutcome.ValidationFailed, Issue("request", "finance_value_does_not_fit"));
            if (!cleanBefore || !CleanEntry())
                return Refused(context, CommandOutcome.Unavailable);

            var state = new OwnedApproval(auditWriter!, replayOnly);
            IDbContextTransaction? transaction = null;
            var committed = false;
            ownedApproval = state;
            try
            {
                transaction = await dbContext.Database.BeginTransactionAsync(IsolationLevel.Serializable, token);
                await RequireFence();
                var result = await ApproveCoreAsync(context, metadata, request, token);
                if (result.Outcome is CommandOutcome.Denied or CommandOutcome.Unavailable)
                    throw new ApprovalRefusal(result);
                await RequireFence();
                RequireFlushed();
                if (result.Outcome == CommandOutcome.Succeeded && !result.IsReplay && !state.Saved)
                    throw new InvalidOperationException("Import approval success has no saved native receipt.");
                await RequireFence();
                RequireFlushed();
                await transaction.CommitAsync(token);
                committed = true;
                var completed = transaction; transaction = null;
                await completed.DisposeAsync();
                // Release loss after commit suppresses the response, never promises a rollback.
                await RequireFence();
                RequireFlushed();
                return result;
            }
            catch (Exception failure)
            {
                var faults = new List<Exception>();
                if (transaction is not null)
                {
                    if (!committed) try { await transaction.RollbackAsync(CancellationToken.None); } catch (Exception error) { faults.Add(error); }
                    try { await transaction.DisposeAsync(); } catch (Exception error) { faults.Add(error); }
                }
                try { dbContext.ChangeTracker.Clear(); } catch (Exception error) { faults.Add(error); }
                try { state.Writer.DiscardPending(); } catch (Exception error) { faults.Add(error); }
                if (faults.Count > 0)
                    throw new AggregateException("Import approval cleanup failed; winner replay is unsafe.", [failure, .. faults]);
                if (failure is ApprovalRefusal refusal) return refusal.Result;
                // Serializable read/insert conversions can lose before Save (including the Audit owner write).
                // Only a fully cleaned, uncommitted attempt may inspect the original key once; never rerun mutation.
                var deadlock = failure is SqlException { Number: 1205 }
                    or DbUpdateException { InnerException: SqlException { Number: 1205 } };
                var saveRace = state.SaveAttempted && !state.Saved && failure is DbUpdateException update
                    && (update is DbUpdateConcurrencyException || update.InnerException is SqlException { Number: 2601 or 2627 });
                if (!replayOnly && !committed && (deadlock || saveRace))
                    throw new ApprovalSaveRace();
                throw;
            }
            finally { ownedApproval = null; }

            async Task RequireFence()
            {
                token.ThrowIfCancellationRequested();
                if (await FenceAsync(context, FinanceOfficialInvoiceImportAuthorityActionV1.Approve, request.BatchId, token) is { } refusal)
                    throw new ApprovalRefusal(refusal);
            }
            void RequireFlushed()
            {
                if (state.Writer.HasPendingDrafts || dbContext.ChangeTracker.HasChanges())
                    throw new InvalidOperationException("Import approval has unflushed owner work.");
            }
        }

        bool CleanEntry() => auditWriter is not null && ownedApproval is null
            && System.Transactions.Transaction.Current is null && dbContext.Database.CurrentTransaction is null
            && dbContext.Database.IsRelational() && !dbContext.ChangeTracker.HasChanges() && !auditWriter.HasPendingDrafts;
    }

    private async Task SaveOwnedApprovalAsync(FinanceContext context, Guid batchId, CancellationToken token)
    {
        var state = ownedApproval ?? throw new InvalidOperationException("Import approval boundary is absent.");
        if (state.ReplayOnly) throw new InvalidOperationException("An import approval winner read cannot mutate.");
        await RequireFence();
        if (state.Writer.HasPendingDrafts)
        {
            await state.Writer.WritePendingAsync(dbContext.Database.CurrentTransaction!.GetDbTransaction(), token);
            await RequireFence();
        }
        if (state.Writer.HasPendingDrafts) throw new InvalidOperationException("Import approval Audit remains pending.");
        state.SaveAttempted = true;
        await dbContext.SaveChangesAsync(token);
        state.Saved = true;
        await RequireFence();

        async Task RequireFence()
        {
            token.ThrowIfCancellationRequested();
            if (await FenceAsync(context, FinanceOfficialInvoiceImportAuthorityActionV1.Approve, batchId, token) is { } refusal)
                throw new ApprovalRefusal(refusal);
        }
    }

    private sealed record OwnedApproval(IAuditTransactionWriterV1 Writer, bool ReplayOnly)
    { internal bool SaveAttempted; internal bool Saved; }
    private sealed class ApprovalRefusal(FinanceOfficialInvoiceImportApprovalResultV1 result) : Exception
    { internal FinanceOfficialInvoiceImportApprovalResultV1 Result { get; } = result; }
    private sealed class ApprovalSaveRace : Exception;
}
