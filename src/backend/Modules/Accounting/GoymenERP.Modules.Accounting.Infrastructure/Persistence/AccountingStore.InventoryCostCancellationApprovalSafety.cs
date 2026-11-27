using System.Text.Json;
using GoymenERP.Modules.Accounting.Application;
using GoymenERP.Modules.Audit.Application.Contracts;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;

namespace GoymenERP.Modules.Accounting.Infrastructure.Persistence;

public sealed partial class AccountingStore
{
    private const string InventoryCostCancellationApproveCommand = "accounting.inventory-cost.approve-component-cancellation.v1";
    private AccountingInventoryCostStoreV1? ownedInventoryCostCancellationCaller;
    private HashSet<object>? inventoryCostCancellationTrackedAtEntry;
    internal async Task<AccountingApiResult<AccountingInventoryCostProposalV1>> RunInventoryCostCancellationApprovalAsync(
        AccountingInventoryCostStoreV1 caller, AccountingDbContext candidateDb, IAuditEventCollector candidateCollector,
        IAuditTransactionWriterV1 candidateWriter, Func<bool, Task<AccountingApiResult<AccountingInventoryCostProposalV1>>> attempt,
        Func<CancellationToken, Task> fence, CancellationToken token)
    {
        ArgumentNullException.ThrowIfNull(attempt); ArgumentNullException.ThrowIfNull(fence);
        if (!SameInventoryCostCancellationScope(candidateDb, candidateCollector, candidateWriter))
            return new(AccountingApiStatus.Unavailable, null, false, null);
        try
        {
            return await RunOwnedAccountingApprovalAsync(InventoryCostCancellationApproveCommand, async replayOnly =>
            {
                if (ownedInventoryCostCancellationCaller is not null || ownedNormalApproval?.CommandName != InventoryCostCancellationApproveCommand)
                    throw new InvalidOperationException("Inventory cost cancellation approval requires its exact core-owned boundary.");
                ownedInventoryCostCancellationCaller = caller;
                inventoryCostCancellationTrackedAtEntry = dbContext.ChangeTracker.Entries().Select(entry => entry.Entity).ToHashSet(ReferenceEqualityComparer.Instance);
                try
                {
                    var result = await attempt(replayOnly);
                    RequireOwnedInventoryCostCancellationApproval(caller, candidateDb, candidateCollector, candidateWriter);
                    if (!replayOnly && ownedNormalApproval is { } state
                        && result.Status is AccountingApiStatus.Unprocessable or AccountingApiStatus.Conflict or AccountingApiStatus.NotFound
                        && (state.Command is not null && state.Command.ResultJson != JsonSerializer.Serialize(result with { IsReplay = false, OriginalCorrelationId = null }, Json)
                            || state.Command is null && state.Saved || dbContext.ChangeTracker.HasChanges() || candidateWriter.HasPendingDrafts
                            || state.Command is null && HasNewCancellationPostingEntries()))
                        throw new InventoryCostCancellationRollbackAnswer(result);
                    return result;
                }
                finally { ownedInventoryCostCancellationCaller = null; inventoryCostCancellationTrackedAtEntry = null; }
            }, fence, token);
        }
        catch (InventoryCostCancellationRollbackAnswer refused)
        {
            // Only a clean owner abort rethrows this exact answer. Aggregate cleanup faults must remain failures.
            try { await fence(token); token.ThrowIfCancellationRequested(); }
            catch (NormalJournalAuthorityDeniedException) { return new(AccountingApiStatus.Denied, null, false, AccountingApiCodes.AuthorityDenied); }
            return refused.Answer;
        }
    }
    private bool HasNewCancellationPostingEntries() => inventoryCostCancellationTrackedAtEntry is not null
        && (dbContext.ChangeTracker.Entries<JournalRow>().Any(entry => !inventoryCostCancellationTrackedAtEntry.Contains(entry.Entity))
            || dbContext.ChangeTracker.Entries<DeliveryRow>().Any(entry => !inventoryCostCancellationTrackedAtEntry.Contains(entry.Entity)));
    private sealed class InventoryCostCancellationRollbackAnswer(AccountingApiResult<AccountingInventoryCostProposalV1> answer) : Exception
    { internal AccountingApiResult<AccountingInventoryCostProposalV1> Answer { get; } = answer; }
    private bool SameInventoryCostCancellationScope(AccountingDbContext candidateDb, IAuditEventCollector candidateCollector, IAuditTransactionWriterV1 candidateWriter) =>
        ReferenceEquals(dbContext, candidateDb) && ReferenceEquals(auditEvents, candidateCollector)
        && normalApprovalAuditWriter is not null && ReferenceEquals(normalApprovalAuditWriter, candidateWriter);
    internal bool IsOwnedInventoryCostCancellationApproval(AccountingInventoryCostStoreV1 caller, AccountingDbContext candidateDb,
        IAuditEventCollector candidateCollector, IAuditTransactionWriterV1 candidateWriter) =>
        ReferenceEquals(ownedInventoryCostCancellationCaller, caller) && SameInventoryCostCancellationScope(candidateDb, candidateCollector, candidateWriter)
        && ownedNormalApproval?.CommandName == InventoryCostCancellationApproveCommand;
    private void RequireOwnedInventoryCostCancellationApproval(AccountingInventoryCostStoreV1 caller, AccountingDbContext candidateDb,
        IAuditEventCollector candidateCollector, IAuditTransactionWriterV1 candidateWriter)
    {
        if (!IsOwnedInventoryCostCancellationApproval(caller, candidateDb, candidateCollector, candidateWriter))
            throw new InvalidOperationException("Inventory cost cancellation approval cannot borrow another context, collector, writer or command marker.");
    }
    internal void CompleteOwnedInventoryCostCancellationApproval(AccountingInventoryCostStoreV1 caller, AccountingDbContext candidateDb,
        IAuditEventCollector candidateCollector, IAuditTransactionWriterV1 candidateWriter, AccountingCommandRecord command)
    {
        RequireOwnedInventoryCostCancellationApproval(caller, candidateDb, candidateCollector, candidateWriter);
        AddCommand(RequireResolvedCompany(), command);
    }
    internal Task RecordOwnedInventoryCostCancellationRefusalAsync(AccountingInventoryCostStoreV1 caller, AccountingDbContext candidateDb,
        IAuditEventCollector candidateCollector, IAuditTransactionWriterV1 candidateWriter, AccountingCommandRecord command, CancellationToken token)
    {
        RequireOwnedInventoryCostCancellationApproval(caller, candidateDb, candidateCollector, candidateWriter);
        // A business refusal after journal save or a staged zero-cost proposal must not commit partial native work.
        if (ownedNormalApproval!.Saved || ownedNormalApproval.Command is not null || dbContext.ChangeTracker.HasChanges()
            || dbContext.ChangeTracker.Entries<JournalRow>().Any(entry => !inventoryCostCancellationTrackedAtEntry!.Contains(entry.Entity))
            || dbContext.ChangeTracker.Entries<DeliveryRow>().Any(entry => !inventoryCostCancellationTrackedAtEntry!.Contains(entry.Entity))
            || candidateWriter.HasPendingDrafts)
            throw new InventoryCostCancellationRollbackAnswer(JsonSerializer.Deserialize<AccountingApiResult<AccountingInventoryCostProposalV1>>(command.ResultJson, Json)
                ?? throw new InvalidOperationException("Inventory cost cancellation refusal result is unavailable."));
        AddCommand(RequireResolvedCompany(), command);
        return SaveOwnedInventoryCostCancellationCommandAsync(token);
    }
    internal Task SaveOwnedInventoryCostCancellationApprovalAsync(AccountingInventoryCostStoreV1 caller, AccountingDbContext candidateDb,
        IAuditEventCollector candidateCollector, IAuditTransactionWriterV1 candidateWriter, CancellationToken token)
    { RequireOwnedInventoryCostCancellationApproval(caller, candidateDb, candidateCollector, candidateWriter); return SaveOwnedInventoryCostCancellationCommandAsync(token); }
    private async Task SaveOwnedInventoryCostCancellationCommandAsync(CancellationToken token)
    {
        try { await SaveOwnedNormalApprovalAsync(token); }
        catch (DbUpdateConcurrencyException failure)
        { throw new NormalJournalApprovalRaceException(AccountingStoreCodes.ConcurrentChange, failure); }
        catch (DbUpdateException failure) when (failure.GetBaseException() is SqlException { Number: 2601 or 2627 })
        { throw new NormalJournalApprovalRaceException(AccountingStoreCodes.ConcurrentChange, failure); }
        // SQL1205 remains the shared owner's deadlock path, including auto-aborted transaction cleanup.
    }
    internal Task ReadOwnedInventoryCostCancellationApprovalAsync(AccountingInventoryCostStoreV1 caller, AccountingDbContext candidateDb,
        IAuditEventCollector candidateCollector, IAuditTransactionWriterV1 candidateWriter, CancellationToken token)
    { RequireOwnedInventoryCostCancellationApproval(caller, candidateDb, candidateCollector, candidateWriter); return NormalApprovalFence(token); }
    // Root composes this exact marker into existing posting read sites; unrelated callers remain no-op.
    private Task InventoryCostCancellationApprovalReadFenceAsync(CancellationToken token) =>
        ownedInventoryCostCancellationCaller is { } caller && IsOwnedInventoryCostCancellationApproval(caller, dbContext, auditEvents, normalApprovalAuditWriter!)
            ? NormalApprovalFence(token) : Task.CompletedTask;
}
