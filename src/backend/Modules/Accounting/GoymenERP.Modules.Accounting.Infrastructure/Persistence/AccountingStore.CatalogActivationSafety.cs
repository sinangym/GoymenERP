using GoymenERP.Modules.Accounting.Application;
using GoymenERP.Modules.Audit.Application.Contracts;

namespace GoymenERP.Modules.Accounting.Infrastructure.Persistence;

public sealed partial class AccountingStore
{
    private ApprovedAccountingSourceCatalogStoreV1? ownedCatalogActivationCaller;

    internal Task<AccountingApiResult<ApprovedAccountingSourceCatalogViewV1>> RunCatalogActivationAsync(
        ApprovedAccountingSourceCatalogStoreV1 caller, AccountingDbContext candidateDb, IAuditEventCollector candidateCollector,
        Func<bool, Task<AccountingApiResult<ApprovedAccountingSourceCatalogViewV1>>> attempt,
        Func<CancellationToken, Task> fence, CancellationToken token)
    {
        ArgumentNullException.ThrowIfNull(attempt); ArgumentNullException.ThrowIfNull(fence);
        if (!SameCatalogScope(candidateDb, candidateCollector))
            return Task.FromResult(new AccountingApiResult<ApprovedAccountingSourceCatalogViewV1>(AccountingApiStatus.Unavailable, null, false, null));
        return RunOwnedAccountingApprovalAsync(ApprovedAccountingSourceCatalogCommandNamesV1.Activate, async replayOnly =>
        {
            if (ownedCatalogActivationCaller is not null || ownedNormalApproval?.CommandName != ApprovedAccountingSourceCatalogCommandNamesV1.Activate)
                throw new InvalidOperationException("Catalog activation requires its exact core-owned command boundary.");
            ownedCatalogActivationCaller = caller;
            try { return await attempt(replayOnly); }
            finally { ownedCatalogActivationCaller = null; }
        }, fence, token);
    }
    private bool SameCatalogScope(AccountingDbContext candidateDb, IAuditEventCollector candidateCollector) =>
        ReferenceEquals(dbContext, candidateDb) && ReferenceEquals(auditEvents, candidateCollector) && normalApprovalAuditWriter is not null;
    internal bool IsOwnedCatalogActivation(ApprovedAccountingSourceCatalogStoreV1 caller, AccountingDbContext candidateDb, IAuditEventCollector candidateCollector) =>
        ReferenceEquals(ownedCatalogActivationCaller, caller) && SameCatalogScope(candidateDb, candidateCollector)
        && ownedNormalApproval?.CommandName == ApprovedAccountingSourceCatalogCommandNamesV1.Activate;
    private void RequireOwnedCatalogActivation(ApprovedAccountingSourceCatalogStoreV1 caller, AccountingDbContext candidateDb, IAuditEventCollector candidateCollector)
    {
        if (!IsOwnedCatalogActivation(caller, candidateDb, candidateCollector))
            throw new InvalidOperationException("Catalog activation cannot borrow another store, context, collector or command marker.");
    }
    internal Task CatalogActivationFenceAsync(ApprovedAccountingSourceCatalogStoreV1 caller, AccountingDbContext candidateDb, IAuditEventCollector candidateCollector, CancellationToken token)
    { RequireOwnedCatalogActivation(caller, candidateDb, candidateCollector); return NormalApprovalFence(token); }
    internal void CompleteOwnedCatalogActivation(ApprovedAccountingSourceCatalogStoreV1 caller, AccountingDbContext candidateDb, IAuditEventCollector candidateCollector, AccountingCommandRecord command)
    { RequireOwnedCatalogActivation(caller, candidateDb, candidateCollector); AddCommand(RequireResolvedCompany(), command); }
    internal Task SaveOwnedCatalogActivationAsync(ApprovedAccountingSourceCatalogStoreV1 caller, AccountingDbContext candidateDb, IAuditEventCollector candidateCollector, CancellationToken token)
    { RequireOwnedCatalogActivation(caller, candidateDb, candidateCollector); return SaveOwnedNormalApprovalAsync(token); }
}
