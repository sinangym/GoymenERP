using GoymenERP.Modules.Accounting.Application;
using GoymenERP.Modules.Audit.Application.Contracts;

namespace GoymenERP.Modules.Accounting.Infrastructure.Persistence;

public sealed partial class AccountingStore
{
    private const string InvoiceLineCogsApproveCommand = "accounting.invoice-line-cogs.approve.v1";
    private InvoiceLineCogsCorrectionStoreV1? ownedInvoiceLineCogsCaller;
    internal Task<AccountingApiResult<InvoiceLineCogsCorrectionViewV1>> RunInvoiceLineCogsApprovalAsync(
        InvoiceLineCogsCorrectionStoreV1 caller, AccountingDbContext candidateDb, IAuditEventCollector candidateCollector,
        IAuditTransactionWriterV1 candidateWriter, Func<bool, Task<AccountingApiResult<InvoiceLineCogsCorrectionViewV1>>> attempt,
        Func<CancellationToken, Task> fence, CancellationToken token)
    {
        ArgumentNullException.ThrowIfNull(attempt); ArgumentNullException.ThrowIfNull(fence);
        if (!SameInvoiceLineCogsScope(candidateDb, candidateCollector, candidateWriter))
            return Task.FromResult(new AccountingApiResult<InvoiceLineCogsCorrectionViewV1>(AccountingApiStatus.Unavailable, null, false, null));
        return RunOwnedAccountingApprovalAsync(InvoiceLineCogsApproveCommand, async replayOnly =>
        {
            if (ownedInvoiceLineCogsCaller is not null || ownedNormalApproval?.CommandName != InvoiceLineCogsApproveCommand)
                throw new InvalidOperationException("Invoice-line COGS approval requires its exact owned command boundary.");
            ownedInvoiceLineCogsCaller = caller;
            try { return await attempt(replayOnly); }
            finally { ownedInvoiceLineCogsCaller = null; }
        }, fence, token);
    }
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
}
