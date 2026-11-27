using System.Data;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using GoymenERP.BuildingBlocks.Multitenancy;
using GoymenERP.BuildingBlocks.Security;
using GoymenERP.Modules.Accounting.Application;
using GoymenERP.Modules.Accounting.Infrastructure.Persistence;
using GoymenERP.Modules.Audit.Application.Contracts;
using Microsoft.EntityFrameworkCore;

namespace GoymenERP.Accounting.UnitTests;

public sealed partial class AccountingSourceVoucherTests
{
    [Theory]
    [InlineData("missing-writer")] [InlineData("other-db")] [InlineData("other-collector")] [InlineData("other-writer")]
    [InlineData("nonrelational")] [InlineData("dirty")] [InlineData("pending")] [InlineData("ambient")]
    [InlineData("await-dirty")] [InlineData("await-pending")]
    public async Task Production_source_voucher_entry_never_adopts_or_discards_caller_work(string fault)
    {
        var company=new MutableCompanyContextForFenceTests(Company);var collector=new VoucherSafetyCollector();var writer=new VoucherSafetyWriter(collector);
        await using var db=VoucherSafetyDb(company);await using var other=VoucherSafetyDb(company);
        var core=new AccountingStore(fault=="other-db"?other:db,company,fault=="other-collector"?new VoucherSafetyCollector():collector,
            TimeProvider.System,null!,normalApprovalAuditWriter:fault=="missing-writer"?null:fault=="other-writer"?new VoucherSafetyWriter(collector):writer);
        var store=new AccountingSourceVoucherStoreV1(db,core,company,null!,null!,collector,TimeProvider.System,writer);
        var row=VoucherSafetyCaller();if(fault=="dirty")db.Commands.Add(row);if(fault=="pending")collector.Enqueue(VoucherSafetyAudit());
        using var ambient=fault=="ambient"?new System.Transactions.TransactionScope(System.Transactions.TransactionScopeAsyncFlowOption.Enabled):null;
        var calls=0;var result=await store.RunApprovalAsync(_=>{calls++;throw new InvalidOperationException("No native IO permitted");},_=>{
            if(fault=="await-dirty")db.Commands.Add(row);if(fault=="await-pending")collector.Enqueue(VoucherSafetyAudit());return Task.CompletedTask;});
        Assert.Equal(AccountingApiStatus.Unavailable,result.Status);Assert.Equal(0,calls);Assert.Equal(0,writer.Writes);Assert.Equal(0,writer.Discards);
        Assert.Equal(fault is "dirty" or "await-dirty",db.ChangeTracker.HasChanges());Assert.Equal(fault is "pending" or "await-pending",writer.HasPendingDrafts);
        if(db.ChangeTracker.HasChanges())Assert.Same(row,Assert.Single(db.ChangeTracker.Entries()).Entity);Assert.Null(db.Database.CurrentTransaction);
    }
    [Theory] [InlineData("actor")] [InlineData("company")] [InlineData("member")] [InlineData("permission")]
    [InlineData("scope")] [InlineData("null-scope")] [InlineData("cancel")]
    public async Task First_fresh_scope_denial_or_completed_cancellation_preserves_pending_caller_audit(string fault)
    {
        var company=new MutableCompanyContextForFenceTests(Company);var user=new VoucherSafetyUser();var authority=new VoucherSafetyAuthority();
        var collector=new VoucherSafetyCollector();collector.Enqueue(VoucherSafetyAudit());var writer=new VoucherSafetyWriter(collector);
        await using var db=VoucherSafetyDb(company);var core=new AccountingStore(db,company,collector,TimeProvider.System,null!,normalApprovalAuditWriter:writer);
        var store=new AccountingSourceVoucherStoreV1(db,core,company,null!,null!,collector,TimeProvider.System,writer);
        var commands=new Commands();var service=new AccountingSourceVoucherServiceV1(store,commands,authority,company,user,new Correlation());
        using var cancellation=new CancellationTokenSource();authority.AfterRead=()=>{
            if(fault=="actor")user.Id=Maker;if(fault=="company")company.Set(Guid.NewGuid());if(fault=="member")user.Member=false;
            if(fault=="permission")authority.Granted=false;if(fault=="scope")authority.Wide=false;if(fault=="null-scope")authority.Null=true;
            if(fault=="cancel")cancellation.Cancel();};
        if(fault=="cancel")await Assert.ThrowsAnyAsync<OperationCanceledException>(()=>service.ApproveAsync(Invoice,1,"SENTETIK-scope",cancellation.Token));
        else Assert.Equal(AccountingApiStatus.Denied,(await service.ApproveAsync(Invoice,1,"SENTETIK-scope")).Status);
        Assert.Equal(0,commands.Reads);Assert.Equal(1,authority.Reads);Assert.True(writer.HasPendingDrafts);Assert.Equal(0,writer.Discards);Assert.Equal(0,writer.Writes);
        Assert.False(db.ChangeTracker.HasChanges());Assert.Null(db.Database.CurrentTransaction);
    }
    [Fact]
    public async Task Legacy_store_default_boundary_fails_closed_and_raw_production_store_cannot_bypass_it()
    {
        IAccountingSourceVoucherStoreV1 legacy=new LegacyVoucherStore();var calls=0;
        var unavailable=await legacy.RunApprovalAsync(_=>{calls++;throw new InvalidOperationException();},_=>Task.CompletedTask);
        Assert.Equal(AccountingApiStatus.Unavailable,unavailable.Status);Assert.Equal(0,calls);
        var company=new MutableCompanyContextForFenceTests(Company);var collector=new VoucherSafetyCollector();var writer=new VoucherSafetyWriter(collector);
        await using var db=VoucherSafetyDb(company);var core=new AccountingStore(db,company,collector,TimeProvider.System,null!,normalApprovalAuditWriter:writer);
        var store=new AccountingSourceVoucherStoreV1(db,core,company,null!,null!,collector,TimeProvider.System,writer);
        var error=await Assert.ThrowsAsync<AccountingStoreException>(()=>store.ApproveAsync(Invoice,1,Actor,_=>Task.CompletedTask,null!));
        Assert.Equal("source_voucher_source_unavailable",error.Code);Assert.False(db.ChangeTracker.HasChanges());Assert.Equal(0,writer.Discards);
    }
    [Fact]
    public async Task Synthetic_native_command_hash_and_eight_field_result_keep_original_actor_and_version()
    {
        var f=new Fixture();var first=await f.Service.ApproveAsync(Invoice,1,"SENTETIK-key");var command=Assert.IsType<AccountingCommandRecord>(f.Commands.Record);
        var payload=JsonSerializer.Serialize(new{id=Invoice,version=1L},new JsonSerializerOptions(JsonSerializerDefaults.Web));
        Assert.Equal("accounting.source-voucher.approve.v1",command.CommandName);
        Assert.Equal(Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(command.CommandName+"|"+payload))),command.RequestHash);
        Assert.Equal(2L,first.Resource?.Version);Assert.Equal(Maker,first.Resource?.SourceActorId);Assert.Equal(Actor,first.Resource?.ApprovedBy);
        Assert.Equal(8,JsonSerializer.SerializeToElement(first,new JsonSerializerOptions(JsonSerializerDefaults.Web)).EnumerateObject().Count());
        var replay=await f.Service.ApproveAsync(Invoice,1,"SENTETIK-key");Assert.True(replay.IsReplay);Assert.Equal(1,f.Store.Writes);
        f.User.UserId=Maker.ToString("D");Assert.Equal(AccountingApiCodes.IdempotencyConflict,(await f.Service.ApproveAsync(Invoice,1,"SENTETIK-key")).Code);Assert.Equal(1,f.Store.Writes);
    }
    [Theory] [InlineData("actor")] [InlineData("company")] [InlineData("permission")]
    public async Task Synthetic_original_key_reply_is_fresh_after_command_read(string fault)
    {
        var f=new Fixture();Assert.Equal(AccountingApiStatus.Ok,(await f.Service.ApproveAsync(Invoice,1,"SENTETIK-key")).Status);
        f.Commands.AfterFind=()=>{if(fault=="actor")f.User.UserId=Maker.ToString("D");if(fault=="company")f.Company.Set(Guid.NewGuid());if(fault=="permission")f.Authority.Granted=false;};
        var answer=await f.Service.ApproveAsync(Invoice,1,"SENTETIK-key");Assert.Equal(AccountingApiStatus.Denied,answer.Status);Assert.Null(answer.Resource);Assert.Equal(1,f.Store.Writes);
    }
    [Fact]
    public async Task Synthetic_readonly_winner_pass_cannot_mutate_or_consume_a_missing_key()
    {
        var f=new Fixture();f.Store.ReplayOnly=true;var answer=await f.Service.ApproveAsync(Invoice,1,"SENTETIK-no-winner");
        Assert.Equal(AccountingApiStatus.Conflict,answer.Status);Assert.Equal(AccountingStoreCodes.ConcurrentChange,answer.Code);
        Assert.Equal(0,f.Store.Writes);Assert.Null(f.Commands.Record);Assert.Equal(1,f.Commands.Reads);
    }
    private static AccountingDbContext VoucherSafetyDb(ICompanyContext company)=>new(new DbContextOptionsBuilder<AccountingDbContext>().UseInMemoryDatabase("source-voucher-safety-"+Guid.NewGuid()).Options,company);
    private static AccountingCommandRow VoucherSafetyCaller()=>new(){CompanyId=Company,CommandName="SENTETIK-caller",IdempotencyKeyHash="caller",ActorId="caller",PayloadHash="hash",ResultJson="{}",CorrelationId="corr"};
    private static AuditEventDraft VoucherSafetyAudit()=>new(Company,"SENTETIK-caller","Accounting.SourceVoucher","caller","Approve",1,2,"User",Actor.ToString("D"),NormalJournalPermissionNamesV1.DraftApprove,[],"{}","{}","SENTETIK",null,AuditAiInvolvement.None,null,"UnitTest",null,null,"SENTETIK",null,"key");
    private sealed class VoucherSafetyCollector:IAuditEventCollector{internal List<AuditEventDraft> Entries{get;}=[];public void Enqueue(AuditEventDraft draft)=>Entries.Add(draft);}
    private sealed class VoucherSafetyWriter(VoucherSafetyCollector collector):IAuditTransactionWriterV1{internal int Writes,Discards;public bool HasPendingDrafts=>collector.Entries.Count!=0;public Task WritePendingAsync(IDbTransaction tx,CancellationToken token=default){Writes++;throw new InvalidOperationException("Entry-only writer");}public void DiscardPending(){Discards++;collector.Entries.Clear();}}
    private sealed class VoucherSafetyUser:ICurrentUser{public Guid Id=Actor;public bool Member=true;public bool IsAuthenticated=>true;public string UserId=>Id.ToString("D");public IReadOnlySet<Guid> AllowedCompanyIds{get;}=new HashSet<Guid>{Company};public bool CanAccessCompany(Guid id)=>Member&&id==Company;}
    private sealed class VoucherSafetyAuthority:IErpAuthorityRevalidationService{public bool Granted=true,Wide=true,Null;public int Reads;public Action? AfterRead;public Task<PermissionScope> RevalidatePermissionScopeAsync(string permission,Guid company,CancellationToken token=default){Reads++;AfterRead?.Invoke();return Task.FromResult(Null?null!:new PermissionScope(Granted,Wide,new HashSet<string>(),new HashSet<Guid>(),new HashSet<Guid>(),new HashSet<Guid>()));}}
    private sealed class LegacyVoucherStore:IAccountingSourceVoucherStoreV1
    {
        public Task<IReadOnlyList<AccountingSourceVoucherViewV1>> ListAsync(CancellationToken token=default)=>throw new NotSupportedException();
        public Task<AccountingSourceVoucherViewV1?> FindAsync(Guid id,CancellationToken token=default)=>throw new NotSupportedException();
        public Task<AccountingSourceVoucherViewV1> ApproveAsync(Guid id,long version,Guid actor,Func<CancellationToken,Task> fence,AccountingCommandMetadata metadata,CancellationToken token=default)=>throw new NotSupportedException();
        public Task<AccountingSourceVoucherViewV1> ProposeCancellationReversalAsync(Guid id,ProposeAccountingSourceVoucherReversalV1 request,Guid actor,Func<CancellationToken,Task> fence,AccountingCommandMetadata metadata,CancellationToken token=default)=>throw new NotSupportedException();
    }
}
