using System.Text.Json;
using GoymenERP.Modules.Accounting.Application;
using GoymenERP.Modules.Audit.Application.Contracts;
using Microsoft.EntityFrameworkCore;

namespace GoymenERP.Modules.Accounting.Infrastructure.Persistence;

public sealed partial class AccountingStore
{
    private const string InventoryCostApproveCommand = "accounting.inventory-cost.approve.v1";
    private AccountingInventoryCostStoreV1? ownedInventoryCostCaller;
    private HashSet<object>? inventoryCostTrackedAtEntry;
    internal async Task<AccountingApiResult<AccountingInventoryCostProposalV1>> RunInventoryCostApprovalAsync(
        AccountingInventoryCostStoreV1 caller, AccountingDbContext candidateDb, IAuditEventCollector candidateCollector,
        IAuditTransactionWriterV1 candidateWriter, Func<bool, Task<AccountingApiResult<AccountingInventoryCostProposalV1>>> attempt,
        Func<CancellationToken, Task> fence, CancellationToken token)
    {
        ArgumentNullException.ThrowIfNull(attempt); ArgumentNullException.ThrowIfNull(fence);
        if (!SameInventoryCostScope(candidateDb, candidateCollector, candidateWriter))
            return new(AccountingApiStatus.Unavailable, null, false, null);
        try
        {
            return await RunOwnedAccountingApprovalAsync(InventoryCostApproveCommand, async replayOnly =>
            {
                if (ownedInventoryCostCaller is not null || ownedNormalApproval?.CommandName != InventoryCostApproveCommand)
                    throw new InvalidOperationException("Inventory cost approval requires its exact core-owned boundary.");
                ownedInventoryCostCaller = caller;
                inventoryCostTrackedAtEntry = dbContext.ChangeTracker.Entries().Select(entry => entry.Entity).ToHashSet(ReferenceEqualityComparer.Instance);
                try
                {
                    var result = await attempt(replayOnly);
                    RequireOwnedInventoryCostApproval(caller, candidateDb, candidateCollector, candidateWriter);
                    if (!replayOnly && ownedNormalApproval is { Saved: true } state
                        && result.Status is AccountingApiStatus.Unprocessable or AccountingApiStatus.Conflict or AccountingApiStatus.NotFound
                        && state.Command?.ResultJson != JsonSerializer.Serialize(result with { IsReplay = false, OriginalCorrelationId = null }, Json))
                        throw new InventoryCostRollbackAnswer(result);
                    return result;
                }
                finally { ownedInventoryCostCaller = null; inventoryCostTrackedAtEntry = null; }
            }, fence, token);
        }
        catch (InventoryCostRollbackAnswer refused)
        {
            // Only a clean owner abort rethrows this exact answer. Aggregate cleanup faults must remain failures.
            try { await fence(token); token.ThrowIfCancellationRequested(); }
            catch (NormalJournalAuthorityDeniedException) { return new(AccountingApiStatus.Denied, null, false, AccountingApiCodes.AuthorityDenied); }
            return refused.Answer;
        }
    }
    private sealed class InventoryCostRollbackAnswer(AccountingApiResult<AccountingInventoryCostProposalV1> answer) : Exception
    { internal AccountingApiResult<AccountingInventoryCostProposalV1> Answer { get; } = answer; }
    private bool SameInventoryCostScope(AccountingDbContext candidateDb, IAuditEventCollector candidateCollector, IAuditTransactionWriterV1 candidateWriter) =>
        ReferenceEquals(dbContext, candidateDb) && ReferenceEquals(auditEvents, candidateCollector)
        && normalApprovalAuditWriter is not null && ReferenceEquals(normalApprovalAuditWriter, candidateWriter);
    internal bool IsOwnedInventoryCostApproval(AccountingInventoryCostStoreV1 caller, AccountingDbContext candidateDb,
        IAuditEventCollector candidateCollector, IAuditTransactionWriterV1 candidateWriter) =>
        ReferenceEquals(ownedInventoryCostCaller, caller) && SameInventoryCostScope(candidateDb, candidateCollector, candidateWriter)
        && ownedNormalApproval?.CommandName == InventoryCostApproveCommand;
    private void RequireOwnedInventoryCostApproval(AccountingInventoryCostStoreV1 caller, AccountingDbContext candidateDb,
        IAuditEventCollector candidateCollector, IAuditTransactionWriterV1 candidateWriter)
    {
        if (!IsOwnedInventoryCostApproval(caller, candidateDb, candidateCollector, candidateWriter))
            throw new InvalidOperationException("Inventory cost approval cannot borrow another context, collector, writer or command marker.");
    }
    internal void CompleteOwnedInventoryCostApproval(AccountingInventoryCostStoreV1 caller, AccountingDbContext candidateDb,
        IAuditEventCollector candidateCollector, IAuditTransactionWriterV1 candidateWriter, AccountingCommandRecord command)
    {
        RequireOwnedInventoryCostApproval(caller, candidateDb, candidateCollector, candidateWriter);
        AddCommand(RequireResolvedCompany(), command);
    }
    internal Task RecordOwnedInventoryCostRefusalAsync(AccountingInventoryCostStoreV1 caller, AccountingDbContext candidateDb,
        IAuditEventCollector candidateCollector, IAuditTransactionWriterV1 candidateWriter, AccountingCommandRecord command, CancellationToken token)
    {
        RequireOwnedInventoryCostApproval(caller, candidateDb, candidateCollector, candidateWriter);
        // A business refusal after journal save or a staged zero-cost proposal must not commit partial native work.
        if (ownedNormalApproval!.Saved || ownedNormalApproval.Command is not null || dbContext.ChangeTracker.HasChanges()
            || dbContext.ChangeTracker.Entries<JournalRow>().Any(entry => !inventoryCostTrackedAtEntry!.Contains(entry.Entity))
            || dbContext.ChangeTracker.Entries<DeliveryRow>().Any(entry => !inventoryCostTrackedAtEntry!.Contains(entry.Entity))
            || candidateWriter.HasPendingDrafts)
            throw new InventoryCostRollbackAnswer(JsonSerializer.Deserialize<AccountingApiResult<AccountingInventoryCostProposalV1>>(command.ResultJson, Json)
                ?? throw new InvalidOperationException("Inventory cost refusal result is unavailable."));
        AddCommand(RequireResolvedCompany(), command);
        return SaveOwnedNormalApprovalAsync(token);
    }
    internal Task SaveOwnedInventoryCostApprovalAsync(AccountingInventoryCostStoreV1 caller, AccountingDbContext candidateDb,
        IAuditEventCollector candidateCollector, IAuditTransactionWriterV1 candidateWriter, CancellationToken token)
    { RequireOwnedInventoryCostApproval(caller, candidateDb, candidateCollector, candidateWriter); return SaveOwnedNormalApprovalAsync(token); }
    internal Task ReadOwnedInventoryCostApprovalAsync(AccountingInventoryCostStoreV1 caller, AccountingDbContext candidateDb,
        IAuditEventCollector candidateCollector, IAuditTransactionWriterV1 candidateWriter, CancellationToken token)
    { RequireOwnedInventoryCostApproval(caller, candidateDb, candidateCollector, candidateWriter); return NormalApprovalFence(token); }
    // Root composes this exact marker into existing posting read sites; unrelated callers remain no-op.
    private Task InventoryCostApprovalReadFenceAsync(CancellationToken token) =>
        ownedInventoryCostCaller is { } caller && IsOwnedInventoryCostApproval(caller, dbContext, auditEvents, normalApprovalAuditWriter!)
            ? NormalApprovalFence(token) : Task.CompletedTask;
}
