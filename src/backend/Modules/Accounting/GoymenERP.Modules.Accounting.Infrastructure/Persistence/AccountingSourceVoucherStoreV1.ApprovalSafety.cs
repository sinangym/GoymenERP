using GoymenERP.Modules.Accounting.Application;

namespace GoymenERP.Modules.Accounting.Infrastructure.Persistence;

public sealed partial class AccountingSourceVoucherStoreV1
{
    public Task<AccountingApiResult<AccountingSourceVoucherViewV1>> RunApprovalAsync(
        Func<bool,Task<AccountingApiResult<AccountingSourceVoucherViewV1>>> attempt,Func<CancellationToken,Task> fence,CancellationToken token=default) =>
        posting.RunSourceVoucherApprovalAsync(this,db,audit,approvalAuditWriter,attempt,fence,token);
    public async Task RecordApprovalAttemptAsync(AccountingCommandRecord command,Func<CancellationToken,Task> fence,CancellationToken token=default)
    {
        await ApprovalReadFence(token);await fence(token);token.ThrowIfCancellationRequested();
        await posting.RecordOwnedSourceVoucherRefusalAsync(this,db,audit,approvalAuditWriter!,command,token);
        await ApprovalReadFence(token);
    }
    private bool IsOwnedApproval => posting.IsOwnedSourceVoucherApproval(this,db,audit,approvalAuditWriter);
    private Task ApprovalReadFence(CancellationToken token) =>
        posting.ReadOwnedSourceVoucherApprovalAsync(this,db,audit,approvalAuditWriter!,token);
    private async Task<T> ApprovalRead<T>(Func<Task<T>> read,CancellationToken token)
    {await ApprovalReadFence(token);var answer=await read();await ApprovalReadFence(token);return answer;}
}
