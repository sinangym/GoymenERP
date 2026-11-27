using System.Text.Json;
using GoymenERP.Modules.Accounting.Application;
using GoymenERP.Modules.Audit.Application.Contracts;

namespace GoymenERP.Modules.Accounting.Infrastructure.Persistence;

public sealed partial class AccountingStore
{
    private const string InvoiceLineCogsApproveCommand = "accounting.invoice-line-cogs.approve.v1";
    private InvoiceLineCogsCorrectionStoreV1? ownedInvoiceLineCogsCaller;
    internal async Task<AccountingApiResult<InvoiceLineCogsCorrectionViewV1>> RunInvoiceLineCogsApprovalAsync(
        InvoiceLineCogsCorrectionStoreV1 caller, AccountingDbContext candidateDb, IAuditEventCollector candidateCollector,
        IAuditTransactionWriterV1 candidateWriter, Func<bool, Task<AccountingApiResult<InvoiceLineCogsCorrectionViewV1>>> attempt,
        Func<CancellationToken, Task> fence, CancellationToken token)
    {
        ArgumentNullException.ThrowIfNull(attempt); ArgumentNullException.ThrowIfNull(fence);
        if (!SameInvoiceLineCogsScope(candidateDb, candidateCollector, candidateWriter))
            return new(AccountingApiStatus.Unavailable, null, false, null);
        try
        {
            return await RunOwnedAccountingApprovalAsync(InvoiceLineCogsApproveCommand, async replayOnly =>
            {
                if (ownedInvoiceLineCogsCaller is not null || ownedNormalApproval?.CommandName != InvoiceLineCogsApproveCommand)
                    throw new InvalidOperationException("Invoice-line COGS approval requires its exact owned command boundary.");
                ownedInvoiceLineCogsCaller = caller;
                try
                {
                    var result = await attempt(replayOnly);
                    RequireOwnedInvoiceLineCogsApproval(caller, candidateDb, candidateCollector, candidateWriter);
                    // A native late refusal can follow staged pairs or a completed Save whose interceptor threw before Saved was set.
                    // Abort only our owned work; a clean early refusal keeps the native read-only path.
                    if (!replayOnly && ownedNormalApproval is { } state
                        && result.Status is AccountingApiStatus.Unprocessable or AccountingApiStatus.Conflict or AccountingApiStatus.NotFound
                        && (dbContext.ChangeTracker.HasChanges() || state.Writer.HasPendingDrafts
                            || state.Command is { } command && command.ResultJson != JsonSerializer.Serialize(result with { IsReplay = false, OriginalCorrelationId = null }, Json)))
                        throw new InvoiceLineCogsRollbackAnswer(result);
                    return result;
                }
                finally { ownedInvoiceLineCogsCaller = null; }
            }, fence, token);
        }
        catch (InvoiceLineCogsRollbackAnswer refused)
        {
            // A direct rethrow proves abort/dispose/clear/discard succeeded. Cleanup AggregateException must escape.
            try { await fence(token); token.ThrowIfCancellationRequested(); }
            catch (NormalJournalAuthorityDeniedException) { return new(AccountingApiStatus.Denied, null, false, AccountingApiCodes.AuthorityDenied); }
            return refused.Answer;
        }
    }
    private sealed class InvoiceLineCogsRollbackAnswer(AccountingApiResult<InvoiceLineCogsCorrectionViewV1> answer) : Exception
    { internal AccountingApiResult<InvoiceLineCogsCorrectionViewV1> Answer { get; } = answer; }
    private bool SameInvoiceLineCogsScope(AccountingDbContext candidateDb, IAuditEventCollector candidateCollector, IAuditTransactionWriterV1 candidateWriter) =>
        ReferenceEquals(dbContext, candidateDb) && ReferenceEquals(auditEvents, candidateCollector)
        && normalApprovalAuditWriter is not null && ReferenceEquals(normalApprovalAuditWriter, candidateWriter);
    internal bool IsOwnedInvoiceLineCogsApproval(InvoiceLineCogsCorrectionStoreV1 caller, AccountingDbContext candidateDb,
        IAuditEventCollector candidateCollector, IAuditTransactionWriterV1 candidateWriter) =>
        ReferenceEquals(ownedInvoiceLineCogsCaller, caller) && SameInvoiceLineCogsScope(candidateDb, candidateCollector, candidateWriter)
        && ownedNormalApproval?.CommandName == InvoiceLineCogsApproveCommand;
    private void RequireOwnedInvoiceLineCogsApproval(InvoiceLineCogsCorrectionStoreV1 caller, AccountingDbContext candidateDb,
        IAuditEventCollector candidateCollector, IAuditTransactionWriterV1 candidateWriter)
    {
        if (!IsOwnedInvoiceLineCogsApproval(caller, candidateDb, candidateCollector, candidateWriter))
            throw new InvalidOperationException("Invoice-line COGS approval cannot adopt a different store, context, collector or writer.");
    }
    internal Task InvoiceLineCogsApprovalFenceAsync(InvoiceLineCogsCorrectionStoreV1 caller, AccountingDbContext candidateDb,
        IAuditEventCollector candidateCollector, IAuditTransactionWriterV1 candidateWriter, CancellationToken token)
    { RequireOwnedInvoiceLineCogsApproval(caller, candidateDb, candidateCollector, candidateWriter); return NormalApprovalFence(token); }
    internal void CompleteOwnedInvoiceLineCogsApproval(InvoiceLineCogsCorrectionStoreV1 caller, AccountingDbContext candidateDb,
        IAuditEventCollector candidateCollector, IAuditTransactionWriterV1 candidateWriter, AccountingCommandRecord command)
    { RequireOwnedInvoiceLineCogsApproval(caller, candidateDb, candidateCollector, candidateWriter); AddCommand(RequireResolvedCompany(), command); }
    internal Task SaveOwnedInvoiceLineCogsApprovalAsync(InvoiceLineCogsCorrectionStoreV1 caller, AccountingDbContext candidateDb,
        IAuditEventCollector candidateCollector, IAuditTransactionWriterV1 candidateWriter, CancellationToken token)
    { RequireOwnedInvoiceLineCogsApproval(caller, candidateDb, candidateCollector, candidateWriter); return SaveOwnedNormalApprovalAsync(token); }
    // Root's closed shared-read relay may call this; all other Accounting commands remain no-op.
    private Task InvoiceLineCogsApprovalReadFenceAsync(CancellationToken token) =>
        ownedInvoiceLineCogsCaller is { } caller && IsOwnedInvoiceLineCogsApproval(caller, dbContext, auditEvents, normalApprovalAuditWriter!)
            ? NormalApprovalFence(token) : Task.CompletedTask;
}
