using GoymenERP.Modules.Accounting.Application;

namespace GoymenERP.Modules.Accounting.Infrastructure.Persistence;

public sealed partial class AccountingInventoryCostStoreV1
{
    public Task<AccountingApiResult<AccountingInventoryCostProposalV1>> RunApprovalAsync(
        Func<bool, Task<AccountingApiResult<AccountingInventoryCostProposalV1>>> attempt,
        Func<CancellationToken, Task> freshFence, CancellationToken token = default) =>
        posting.RunInventoryCostApprovalAsync(this, db, audit, auditWriter, attempt, freshFence, token);
    private bool IsOwnedApproval => posting.IsOwnedInventoryCostApproval(this, db, audit, auditWriter);
    private Task ApprovalReadFence(CancellationToken token) => IsOwnedApproval
        ? posting.ReadOwnedInventoryCostApprovalAsync(this, db, audit, auditWriter, token) : Task.CompletedTask;
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
            posting.CompleteOwnedInventoryCostApproval(this, db, audit, auditWriter, command);
        await posting.SaveOwnedInventoryCostApprovalAsync(this, db, audit, auditWriter, token);
        await ApprovalReadFence(token);
        return result;
    }
}
