using System.Text.Json;
using GoymenERP.Modules.Accounting.Application;
using GoymenERP.Modules.Audit.Application.Contracts;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;

namespace GoymenERP.Modules.Accounting.Infrastructure.Persistence;

public sealed partial class AccountingStore
{
    private const string SourceVoucherApproveCommand="accounting.source-voucher.approve.v1";
    private AccountingSourceVoucherStoreV1? ownedSourceVoucherCaller;
    private HashSet<object>? sourceVoucherTrackedAtEntry;
    internal async Task<AccountingApiResult<AccountingSourceVoucherViewV1>> RunSourceVoucherApprovalAsync(
        AccountingSourceVoucherStoreV1 caller,AccountingDbContext candidateDb,IAuditEventCollector candidateCollector,
        IAuditTransactionWriterV1? candidateWriter,Func<bool,Task<AccountingApiResult<AccountingSourceVoucherViewV1>>> attempt,
        Func<CancellationToken,Task> fence,CancellationToken token)
    {
        ArgumentNullException.ThrowIfNull(attempt);ArgumentNullException.ThrowIfNull(fence);
        if(!SameSourceVoucherScope(candidateDb,candidateCollector,candidateWriter))return new(AccountingApiStatus.Unavailable,null,false,null);
        try
        {
            return await RunOwnedAccountingApprovalAsync(SourceVoucherApproveCommand,async replayOnly=>
            {
                if(ownedSourceVoucherCaller is not null||ownedNormalApproval?.CommandName!=SourceVoucherApproveCommand)
                    throw new InvalidOperationException("Source voucher approval requires its exact core-owned command.");
                ownedSourceVoucherCaller=caller;
                sourceVoucherTrackedAtEntry=dbContext.ChangeTracker.Entries().Select(x=>x.Entity).ToHashSet(ReferenceEqualityComparer.Instance);
                try
                {
                    var result=await attempt(replayOnly);
                    RequireOwnedSourceVoucherApproval(caller,candidateDb,candidateCollector,candidateWriter);
                    // Keep native refusal code/field after abort, including a save interceptor failing after SQL accepted a journal.
                    if(result.Status==AccountingApiStatus.Unavailable
                        ||(!replayOnly&&(result.Status is AccountingApiStatus.Unprocessable or AccountingApiStatus.Conflict or AccountingApiStatus.NotFound)
                            &&HasSourceVoucherWork()&&ownedNormalApproval!.Command?.ResultJson!=OriginalSourceVoucherAnswer(result)))
                        throw new SourceVoucherRollbackAnswer(result);
                    return result;
                }
                finally{ownedSourceVoucherCaller=null;sourceVoucherTrackedAtEntry=null;}
            },fence,token);
        }
        catch(SourceVoucherRollbackAnswer refused)
        {
            // Only direct rethrow proves rollback/dispose/clear/discard succeeded. Cleanup aggregates must escape.
            try{await fence(token);token.ThrowIfCancellationRequested();}
            catch(NormalJournalAuthorityDeniedException){return new(AccountingApiStatus.Denied,null,false,AccountingApiCodes.AuthorityDenied);}
            return refused.Answer;
        }
    }
    private sealed class SourceVoucherRollbackAnswer(AccountingApiResult<AccountingSourceVoucherViewV1> answer):Exception
    {internal AccountingApiResult<AccountingSourceVoucherViewV1> Answer{get;}=answer;}
    private static string OriginalSourceVoucherAnswer(AccountingApiResult<AccountingSourceVoucherViewV1> result)=>
        JsonSerializer.Serialize(result with{IsReplay=false,OriginalCorrelationId=null},Json);
    private bool HasSourceVoucherWork()=>ownedNormalApproval!.Saved||ownedNormalApproval.Command is not null
        ||dbContext.ChangeTracker.HasChanges()||ownedNormalApproval.Writer.HasPendingDrafts
        ||dbContext.ChangeTracker.Entries<JournalRow>().Any(x=>!sourceVoucherTrackedAtEntry!.Contains(x.Entity))
        ||dbContext.ChangeTracker.Entries<DeliveryRow>().Any(x=>!sourceVoucherTrackedAtEntry!.Contains(x.Entity));
    private bool SameSourceVoucherScope(AccountingDbContext candidateDb,IAuditEventCollector candidateCollector,IAuditTransactionWriterV1? candidateWriter)=>
        ReferenceEquals(dbContext,candidateDb)&&ReferenceEquals(auditEvents,candidateCollector)
        &&normalApprovalAuditWriter is not null&&ReferenceEquals(normalApprovalAuditWriter,candidateWriter);
    internal bool IsOwnedSourceVoucherApproval(AccountingSourceVoucherStoreV1 caller,AccountingDbContext candidateDb,
        IAuditEventCollector candidateCollector,IAuditTransactionWriterV1? candidateWriter)=>
        ReferenceEquals(ownedSourceVoucherCaller,caller)&&SameSourceVoucherScope(candidateDb,candidateCollector,candidateWriter)
        &&ownedNormalApproval?.CommandName==SourceVoucherApproveCommand;
    private void RequireOwnedSourceVoucherApproval(AccountingSourceVoucherStoreV1 caller,AccountingDbContext candidateDb,
        IAuditEventCollector candidateCollector,IAuditTransactionWriterV1? candidateWriter)
    {if(!IsOwnedSourceVoucherApproval(caller,candidateDb,candidateCollector,candidateWriter))throw new InvalidOperationException("Source voucher cannot borrow another caller/context/collector/writer/command.");}
    internal Task ReadOwnedSourceVoucherApprovalAsync(AccountingSourceVoucherStoreV1 caller,AccountingDbContext candidateDb,
        IAuditEventCollector candidateCollector,IAuditTransactionWriterV1 candidateWriter,CancellationToken token)
    {RequireOwnedSourceVoucherApproval(caller,candidateDb,candidateCollector,candidateWriter);return NormalApprovalFence(token);}
    internal void CompleteOwnedSourceVoucherApproval(AccountingSourceVoucherStoreV1 caller,AccountingDbContext candidateDb,
        IAuditEventCollector candidateCollector,IAuditTransactionWriterV1 candidateWriter,AccountingCommandRecord command)
    {RequireOwnedSourceVoucherApproval(caller,candidateDb,candidateCollector,candidateWriter);AddCommand(RequireResolvedCompany(),command);}
    internal Task SaveOwnedSourceVoucherApprovalAsync(AccountingSourceVoucherStoreV1 caller,AccountingDbContext candidateDb,
        IAuditEventCollector candidateCollector,IAuditTransactionWriterV1 candidateWriter,CancellationToken token)
    {RequireOwnedSourceVoucherApproval(caller,candidateDb,candidateCollector,candidateWriter);return SaveSourceVoucherCommandAsync(token);}
    internal Task RecordOwnedSourceVoucherRefusalAsync(AccountingSourceVoucherStoreV1 caller,AccountingDbContext candidateDb,
        IAuditEventCollector candidateCollector,IAuditTransactionWriterV1 candidateWriter,AccountingCommandRecord command,CancellationToken token)
    {
        RequireOwnedSourceVoucherApproval(caller,candidateDb,candidateCollector,candidateWriter);
        if(HasSourceVoucherWork())throw new SourceVoucherRollbackAnswer(JsonSerializer.Deserialize<AccountingApiResult<AccountingSourceVoucherViewV1>>(command.ResultJson,Json)
            ??throw new InvalidOperationException("Source voucher refusal result is unavailable."));
        AddCommand(RequireResolvedCompany(),command);return SaveSourceVoucherCommandAsync(token);
    }
    private async Task SaveSourceVoucherCommandAsync(CancellationToken token)
    {
        try{await SaveOwnedNormalApprovalAsync(token);}
        catch(DbUpdateConcurrencyException error){throw new NormalJournalApprovalRaceException(AccountingStoreCodes.ConcurrentChange,error);}
        catch(DbUpdateException error)when(error.GetBaseException() is SqlException{Number:2601 or 2627})
        {throw new NormalJournalApprovalRaceException(AccountingStoreCodes.ConcurrentChange,error);}
        // Actual 1205 (including an already aborted provider transaction) remains the shared owner's one readonly-winner path.
    }
    // Root adds one call in its existing OwnedPostingReadFences relay; unrelated commands remain no-op.
    private Task SourceVoucherApprovalReadFenceAsync(CancellationToken token)=>ownedSourceVoucherCaller is{} caller
        &&IsOwnedSourceVoucherApproval(caller,dbContext,auditEvents,normalApprovalAuditWriter)?NormalApprovalFence(token):Task.CompletedTask;
}
