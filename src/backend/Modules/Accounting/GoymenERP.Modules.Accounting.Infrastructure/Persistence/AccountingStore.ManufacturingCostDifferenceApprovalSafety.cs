using System.Text.Json;
using GoymenERP.Modules.Accounting.Application;
using GoymenERP.Modules.Audit.Application.Contracts;
using Microsoft.EntityFrameworkCore;

namespace GoymenERP.Modules.Accounting.Infrastructure.Persistence;

public sealed partial class AccountingStore
{
    private ManufacturingCostDifferenceStoreV1? ownedManufacturingCostDifferenceCaller;
    private HashSet<object>? manufacturingCostDifferenceTrackedAtEntry;
    internal async Task<AccountingApiResult<ManufacturingCostDifferenceViewV1>> RunManufacturingCostDifferenceApprovalAsync(
        ManufacturingCostDifferenceStoreV1 caller, AccountingDbContext candidateDb, IAuditEventCollector candidateCollector,
        Func<bool, Task<AccountingApiResult<ManufacturingCostDifferenceViewV1>>> attempt,
        Func<CancellationToken, Task> fence, CancellationToken token)
    {
        ArgumentNullException.ThrowIfNull(attempt); ArgumentNullException.ThrowIfNull(fence);
        if (!SameManufacturingCostDifferenceScope(candidateDb, candidateCollector))
            return new(AccountingApiStatus.Unavailable, null, false, null);
        try
        {
            return await RunOwnedAccountingApprovalAsync(ManufacturingCostDifferenceCommandNamesV1.Approve, async replayOnly =>
            {
                if (ownedManufacturingCostDifferenceCaller is not null || ownedNormalApproval?.CommandName != ManufacturingCostDifferenceCommandNamesV1.Approve)
                    throw new InvalidOperationException("Cost difference approval requires its exact core-owned command boundary.");
                ownedManufacturingCostDifferenceCaller = caller;
                manufacturingCostDifferenceTrackedAtEntry = dbContext.ChangeTracker.Entries().Select(entry => entry.Entity).ToHashSet(ReferenceEqualityComparer.Instance);
                try
                {
                    var result = await attempt(replayOnly);
                    RequireOwnedManufacturingCostDifferenceApproval(caller, candidateDb, candidateCollector);
                    // EF may accept the SQL writes before a SavedChanges callback throws and before Saved=true.
                    // Retain this attempt's writes even when its tracker is clean; prior successful tracked rows are not ours.
                    if (!replayOnly && ownedNormalApproval is { } state
                        && result.Status is AccountingApiStatus.Unprocessable or AccountingApiStatus.Conflict or AccountingApiStatus.NotFound
                        && (state.Saved || state.Command is not null || dbContext.ChangeTracker.HasChanges() || state.Writer.HasPendingDrafts
                            || dbContext.ChangeTracker.Entries<JournalRow>().Any(entry => !manufacturingCostDifferenceTrackedAtEntry!.Contains(entry.Entity))
                            || dbContext.ChangeTracker.Entries<DeliveryRow>().Any(entry => !manufacturingCostDifferenceTrackedAtEntry!.Contains(entry.Entity)))
                        && state.Command?.ResultJson != JsonSerializer.Serialize(result with { IsReplay = false, OriginalCorrelationId = null }, Json))
                        throw new CostDifferenceRollbackAnswer(result);
                    return result;
                }
                finally { ownedManufacturingCostDifferenceCaller = null; manufacturingCostDifferenceTrackedAtEntry = null; }
            }, fence, token);
        }
        catch (CostDifferenceRollbackAnswer refused)
        {
            // Only a direct rethrow proves that the existing owner finished abort/dispose/clear/discard.
            // Cleanup faults are AggregateException and deliberately do not enter this business-answer path.
            try { await fence(token); token.ThrowIfCancellationRequested(); }
            catch (NormalJournalAuthorityDeniedException) { return new(AccountingApiStatus.Denied, null, false, AccountingApiCodes.AuthorityDenied); }
            return refused.Answer;
        }
    }
    private sealed class CostDifferenceRollbackAnswer(AccountingApiResult<ManufacturingCostDifferenceViewV1> answer) : Exception
    { internal AccountingApiResult<ManufacturingCostDifferenceViewV1> Answer { get; } = answer; }
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
