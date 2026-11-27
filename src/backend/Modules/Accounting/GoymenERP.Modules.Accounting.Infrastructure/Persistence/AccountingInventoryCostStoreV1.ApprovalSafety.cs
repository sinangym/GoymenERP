using GoymenERP.Modules.Accounting.Application;

namespace GoymenERP.Modules.Accounting.Infrastructure.Persistence;

public sealed partial class AccountingInventoryCostStoreV1
{
    public Task<AccountingApiResult<AccountingInventoryCostProposalV1>> RunApprovalAsync(
        Func<bool, Task<AccountingApiResult<AccountingInventoryCostProposalV1>>> attempt,
        Func<CancellationToken, Task> freshFence, CancellationToken token = default) =>
        posting.RunInventoryCostApprovalAsync(this, db, audit, auditWriter, attempt, freshFence, token);
    private bool IsOwnedCancellationApproval => posting.IsOwnedInventoryCostCancellationApproval(this, db, audit, auditWriter);
    private bool IsOwnedApproval => IsOwnedCancellationApproval || posting.IsOwnedInventoryCostApproval(this, db, audit, auditWriter);
    public Task<AccountingApiResult<AccountingInventoryCostProposalV1>> RunCancellationApprovalAsync(
        Func<bool, Task<AccountingApiResult<AccountingInventoryCostProposalV1>>> attempt,
        Func<CancellationToken, Task> freshFence, CancellationToken token = default) =>
        posting.RunInventoryCostCancellationApprovalAsync(this, db, audit, auditWriter, attempt, freshFence, token);
    private Task RecordOwnedApprovalRefusalAsync(AccountingCommandRecord command, CancellationToken token) => IsOwnedCancellationApproval
        ? posting.RecordOwnedInventoryCostCancellationRefusalAsync(this, db, audit, auditWriter, command, token)
        : posting.RecordOwnedInventoryCostRefusalAsync(this, db, audit, auditWriter, command, token);
    private async Task ApprovalReadFence(CancellationToken token)
    {
        if (decisionReadFence is not null)
        {
            // A reentrant command cannot borrow a read callback captured outside its exact native ownership.
            if (decisionReadOwnedAtEntry != IsOwnedApproval) throw Refuse("inventory_cost_source_unavailable");
            await decisionReadFence(token);
        }
        else if (IsOwnedCancellationApproval) await posting.ReadOwnedInventoryCostCancellationApprovalAsync(this, db, audit, auditWriter, token);
        else if (IsOwnedApproval) await posting.ReadOwnedInventoryCostApprovalAsync(this, db, audit, auditWriter, token);
    }
    private async Task<T> ApprovalRead<T>(Func<Task<T>> read, CancellationToken token)
    {
        await ApprovalReadFence(token);
        var result = await read();
        await ApprovalReadFence(token);
        return result;
    }
    private async Task<T> AtomicApproval<T>(AccountingCommandMetadata metadata, Func<CancellationToken, Task>? fence,
        Func<Task<T>> operation, CancellationToken token)
    {
        await ApprovalReadFence(token);
        if (fence is not null) { await fence(token); token.ThrowIfCancellationRequested(); }
        var result = await operation();
        await ApprovalReadFence(token);
        if (metadata.Completion?.Invoke(result!) is { } command)
        {
            if (IsOwnedCancellationApproval) posting.CompleteOwnedInventoryCostCancellationApproval(this, db, audit, auditWriter, command);
            else posting.CompleteOwnedInventoryCostApproval(this, db, audit, auditWriter, command);
        }
        if (IsOwnedCancellationApproval) await posting.SaveOwnedInventoryCostCancellationApprovalAsync(this, db, audit, auditWriter, token);
        else await posting.SaveOwnedInventoryCostApprovalAsync(this, db, audit, auditWriter, token);
        await ApprovalReadFence(token);
        return result;
    }
}
