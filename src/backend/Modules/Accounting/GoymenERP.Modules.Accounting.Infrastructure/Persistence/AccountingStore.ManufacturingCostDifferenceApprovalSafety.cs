using GoymenERP.Modules.Accounting.Application;
using GoymenERP.Modules.Audit.Application.Contracts;

namespace GoymenERP.Modules.Accounting.Infrastructure.Persistence;

public sealed partial class AccountingStore
{
    private ManufacturingCostDifferenceStoreV1? ownedManufacturingCostDifferenceCaller;
    internal Task<AccountingApiResult<ManufacturingCostDifferenceViewV1>> RunManufacturingCostDifferenceApprovalAsync(
        ManufacturingCostDifferenceStoreV1 caller, AccountingDbContext candidateDb, IAuditEventCollector candidateCollector,
        Func<bool, Task<AccountingApiResult<ManufacturingCostDifferenceViewV1>>> attempt,
        Func<CancellationToken, Task> fence, CancellationToken token)
    {
        ArgumentNullException.ThrowIfNull(attempt); ArgumentNullException.ThrowIfNull(fence);
        if (!SameManufacturingCostDifferenceScope(candidateDb, candidateCollector))
            return Task.FromResult(new AccountingApiResult<ManufacturingCostDifferenceViewV1>(AccountingApiStatus.Unavailable, null, false, null));
        return RunOwnedAccountingApprovalAsync(ManufacturingCostDifferenceCommandNamesV1.Approve, async replayOnly =>
        {
            if (ownedManufacturingCostDifferenceCaller is not null || ownedNormalApproval?.CommandName != ManufacturingCostDifferenceCommandNamesV1.Approve)
                throw new InvalidOperationException("Cost difference approval requires its exact core-owned command boundary.");
            ownedManufacturingCostDifferenceCaller = caller;
            try { return await attempt(replayOnly); }
            finally { ownedManufacturingCostDifferenceCaller = null; }
        }, fence, token);
    }
    private bool SameManufacturingCostDifferenceScope(AccountingDbContext candidateDb, IAuditEventCollector candidateCollector) =>
        ReferenceEquals(dbContext, candidateDb) && ReferenceEquals(auditEvents, candidateCollector) && normalApprovalAuditWriter is not null;
    internal bool IsOwnedManufacturingCostDifferenceApproval(ManufacturingCostDifferenceStoreV1 caller, AccountingDbContext candidateDb, IAuditEventCollector candidateCollector) =>
        ReferenceEquals(ownedManufacturingCostDifferenceCaller, caller) && SameManufacturingCostDifferenceScope(candidateDb, candidateCollector)
        && ownedNormalApproval?.CommandName == ManufacturingCostDifferenceCommandNamesV1.Approve;
    private void RequireOwnedManufacturingCostDifferenceApproval(ManufacturingCostDifferenceStoreV1 caller, AccountingDbContext candidateDb, IAuditEventCollector candidateCollector)
    {
        if (!IsOwnedManufacturingCostDifferenceApproval(caller, candidateDb, candidateCollector))
            throw new InvalidOperationException("Cost difference approval cannot borrow another context, collector or command marker.");
    }
    internal void CompleteOwnedManufacturingCostDifferenceApproval(ManufacturingCostDifferenceStoreV1 caller, AccountingDbContext candidateDb,
        IAuditEventCollector candidateCollector, AccountingCommandRecord command)
    { RequireOwnedManufacturingCostDifferenceApproval(caller, candidateDb, candidateCollector); AddCommand(RequireResolvedCompany(), command); }
    internal Task SaveOwnedManufacturingCostDifferenceApprovalAsync(ManufacturingCostDifferenceStoreV1 caller, AccountingDbContext candidateDb,
        IAuditEventCollector candidateCollector, CancellationToken token)
    { RequireOwnedManufacturingCostDifferenceApproval(caller, candidateDb, candidateCollector); return SaveOwnedNormalApprovalAsync(token); }
    // No-op for every other existing Accounting caller. The active exact caller has already passed scope identity checks.
    private Task ManufacturingCostDifferenceApprovalReadFenceAsync(CancellationToken token) =>
        ownedManufacturingCostDifferenceCaller is { } caller && IsOwnedManufacturingCostDifferenceApproval(caller, dbContext, auditEvents)
            ? NormalApprovalFence(token) : Task.CompletedTask;
}
