using System.Data.Common;
using GoymenERP.BuildingBlocks.Multitenancy;
using GoymenERP.BuildingBlocks.Observability;
using GoymenERP.BuildingBlocks.Security;
using GoymenERP.Modules.Accounting.Application;
using GoymenERP.Modules.Accounting.Domain;
using GoymenERP.Modules.Accounting.Infrastructure.Persistence;
using GoymenERP.Modules.Audit.Application.Contracts;
using GoymenERP.Modules.Audit.Infrastructure.Persistence;
using GoymenERP.Modules.Audit.Infrastructure.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.DependencyInjection;

namespace GoymenERP.Api.IntegrationTests;

public sealed partial class AccountingSourceDocumentAdmissionSqlServerRelationalAcceptanceTests
{
    [SqlServerFact]
    public async Task Source_voucher_owned_native_approval_keeps_original_posted_receipt_after_current_source_cancellation()
    {
        await using var run=await Run.StartAsync(true);await OpenVoucherPeriod(run);
        var admitted=await run.AdmitAsync(CompanyA,Request(CompanyA));var id=admitted.VoucherDraftId!.Value;var before=await VoucherSafetyCounts(run);
        await using var owner=new VoucherApprovalScope(run);
        var first=await owner.Approve(id,"SENTETIK-original");var replay=await owner.Approve(id,"SENTETIK-original");
        Assert.Equal(AccountingApiStatus.Ok,first.Status);Assert.Equal("Posted",first.Resource?.Status);Assert.Equal(2L,first.Resource?.Version);
        Assert.Equal(Request(CompanyA).SourceActorId,first.Resource?.SourceActorId);Assert.Equal(VoucherChecker,first.Resource?.ApprovedBy);
        Assert.Equal(new[]{"1440","1000","200","240"},first.Resource!.Lines.Select(x=>x.FunctionalAmount));
        Assert.True(replay.IsReplay);Assert.Equal(owner.CorrelationId,replay.OriginalCorrelationId);
        Assert.Equal(before with{Journals=before.Journals+1,Lines=before.Lines+4,Deliveries=before.Deliveries+1,Commands=before.Commands+1,Audits=before.Audits+2},await VoucherSafetyCounts(run));
        await CancelVoucherSource(run,Cancellation());var afterCancellation=await VoucherSafetyCounts(run);
        var old=await owner.Approve(id,"SENTETIK-original");Assert.True(old.IsReplay);Assert.Equal(2L,old.Resource?.Version);Assert.Equal("Posted",old.Resource?.Status);
        Assert.Equal(first.Resource.JournalId,old.Resource?.JournalId);Assert.Equal(owner.CorrelationId,old.OriginalCorrelationId);
        Assert.Equal(afterCancellation,await VoucherSafetyCounts(run));Assert.Equal(2L,await run.ScalarAsync<long>("SELECT Version FROM accounting.SourceDocumentVoucherDrafts WHERE Id=@id",("@id",id)));
        await using var current=run.Factory.Services.CreateAsyncScope();current.ServiceProvider.GetRequiredService<ICompanyContext>().Set(CompanyA);
        var currentView=await current.ServiceProvider.GetRequiredService<IAccountingSourceVoucherStoreV1>().FindAsync(id);
        Assert.Equal("CancellationReversalPending",currentView?.Status);Assert.NotNull(currentView?.CancellationId);Assert.Equal(2L,currentView?.Version);
        owner.AssertClean();
    }
    [SqlServerFact]
    public async Task Source_voucher_owned_entry_preserves_borrowed_ambient_dirty_and_pending_caller_state()
    {
        await using var run=await Run.StartAsync(true);await OpenVoucherPeriod(run);var admitted=await run.AdmitAsync(CompanyA,Request(CompanyA));var before=await VoucherSafetyCounts(run);
        foreach(var fault in new[]{"borrowed","ambient","dirty","pending","await-dirty","await-pending","missing-writer","other-db","other-collector","other-writer"})
        {
            await using var owner=new VoucherApprovalScope(run,entryFault:fault);var caller=VoucherSafetyCaller();
            if(fault=="dirty")owner.Db.Commands.Add(caller);if(fault=="pending")owner.Collector.Enqueue(VoucherSafetyAudit());
            await using var borrowed=fault=="borrowed"?await owner.Db.Database.BeginTransactionAsync():null;
            using(var ambient=fault=="ambient"?new System.Transactions.TransactionScope(System.Transactions.TransactionScopeAsyncFlowOption.Enabled):null)
            {
                owner.Authority.BeforeRead=_=>{if(fault=="await-dirty")owner.Db.Commands.Add(caller);if(fault=="await-pending")owner.Collector.Enqueue(VoucherSafetyAudit());return Task.CompletedTask;};
                Assert.Equal(AccountingApiStatus.Unavailable,(await owner.Approve(admitted.VoucherDraftId!.Value,"SENTETIK-entry")).Status);
                Assert.Equal(0,owner.Writer.Writes);Assert.Equal(0,owner.Writer.Discards);Assert.Equal(fault is "dirty" or "await-dirty",owner.Db.ChangeTracker.HasChanges());
                Assert.Equal(fault is "pending" or "await-pending",owner.Writer.HasPendingDrafts);
                if(owner.Db.ChangeTracker.HasChanges())Assert.Same(caller,Assert.Single(owner.Db.ChangeTracker.Entries()).Entity);
                if(borrowed is not null)Assert.Same(borrowed,owner.Db.Database.CurrentTransaction);
            }
            if(borrowed is not null)await borrowed.RollbackAsync();Assert.Equal(before,await VoucherSafetyCounts(run));
        }
    }
    [SqlServerFact]
    public async Task Source_voucher_owned_postsave_precommit_and_postcommit_fences_rollback_or_suppress_original_once()
    {
        foreach(var phase in new[]{1,5,6})
        {
            await using var run=await Run.StartAsync(true);await OpenVoucherPeriod(run);var admission=await run.AdmitAsync(CompanyA,Request(CompanyA));var before=await VoucherSafetyCounts(run);
            var save=new VoucherApprovalSave();await using var owner=new VoucherApprovalScope(run,save:save);var savedFences=0;var fired=false;
            // Final voucher Save: 1 SaveOwned postSave, 2 store release, 3 service release, 4 runner result fence, 5 precommit, 6 postcommit release.
            owner.Authority.BeforeRead=_=>{if(save.VoucherSaved&&++savedFences==phase){fired=true;owner.Authority.Granted=false;}return Task.CompletedTask;};
            var denied=await owner.Approve(admission.VoucherDraftId!.Value,"SENTETIK-late");Assert.True(fired);Assert.Equal(AccountingApiStatus.Denied,denied.Status);Assert.Null(denied.Resource);
            var committed=phase==6;Assert.Equal(committed?before with{Journals=before.Journals+1,Lines=before.Lines+4,Deliveries=before.Deliveries+1,Commands=before.Commands+1,Audits=before.Audits+2}:before,await VoucherSafetyCounts(run));
            Assert.Equal(1,owner.Writer.Discards);owner.AssertClean();owner.Authority.BeforeRead=null;owner.Authority.Granted=true;
            var accepted=await owner.Approve(admission.VoucherDraftId!.Value,"SENTETIK-late");Assert.Equal(AccountingApiStatus.Ok,accepted.Status);Assert.Equal(committed,accepted.IsReplay);
            var replay=await owner.Approve(admission.VoucherDraftId.Value,"SENTETIK-late");Assert.True(replay.IsReplay);Assert.Equal(owner.CorrelationId,replay.OriginalCorrelationId);
            Assert.Equal(before with{Journals=before.Journals+1,Lines=before.Lines+4,Deliveries=before.Deliveries+1,Commands=before.Commands+1,Audits=before.Audits+2},await VoucherSafetyCounts(run));owner.AssertClean();
        }
    }
    [SqlServerFact]
    public async Task Source_voucher_owned_inner_read_and_actual_audit_writer_recheck_completed_authority_loss_before_next_effect()
    {
        foreach(var audit in new[]{false,true})
        foreach(var cancel in new[]{false,true})
        {
            using var cancellation=new CancellationTokenSource();
            await using var run=await Run.StartAsync(true);await OpenVoucherPeriod(run);var admission=await run.AdmitAsync(CompanyA,Request(CompanyA));var before=await VoucherSafetyCounts(run);
            VoucherApprovalScope? owner=null;var voucherQueries=0;var hit=false;
            var probe=new VoucherApprovalQuery(sql=>{if(sql.Contains("[SourceDocumentVoucherDrafts]",StringComparison.Ordinal))voucherQueries++;
                if(!audit&&sql.Contains("[SourceInvoiceCancellations]",StringComparison.Ordinal)){hit=true;if(cancel)cancellation.Cancel();else owner!.Authority.Granted=false;}});
            await using var active=owner=new VoucherApprovalScope(run,extra:probe);
            if(audit)owner.Writer.AfterWrite=()=>{hit=true;if(cancel)cancellation.Cancel();else owner.Authority.Granted=false;};
            if(cancel)await Assert.ThrowsAnyAsync<OperationCanceledException>(()=>owner.Approve(admission.VoucherDraftId!.Value,"SENTETIK-read-audit",cancellation.Token));
            else Assert.Equal(AccountingApiStatus.Denied,(await owner.Approve(admission.VoucherDraftId!.Value,"SENTETIK-read-audit")).Status);
            Assert.True(hit);if(!audit)Assert.Equal(1,voucherQueries); // No tracked voucher/period/line read follows the cancellation-query release.
            Assert.Equal(before,await VoucherSafetyCounts(run));Assert.Equal(1,owner.Writer.Discards);owner.AssertClean();
        }
    }
    [SqlServerFact]
    public async Task Source_voucher_native_early_and_postjournal_business_refusals_keep_codes_and_abort_only_their_own_effects()
    {
        foreach(var fault in new[]{"early","after-journal-domain","after-voucher-domain"})
        {
            var late=fault!="early";
            await using var run=await Run.StartAsync(true);if(late)await OpenVoucherPeriod(run);var admission=await run.AdmitAsync(CompanyA,Request(CompanyA));var before=await VoucherSafetyCounts(run);
            var save=new VoucherApprovalSave{Fault=late?fault:null};await using var owner=new VoucherApprovalScope(run,save:save);
            var answer=await owner.Approve(admission.VoucherDraftId!.Value,"SENTETIK-business");Assert.Equal(AccountingApiStatus.Unprocessable,answer.Status);
            Assert.Equal(late?"source_voucher_posting_refused":"source_voucher_period_waiting",answer.Code);Assert.Null(answer.Resource);
            Assert.Equal(late,save.Injected);Assert.Equal(late?1:0,owner.Writer.Discards);Assert.Equal(before,await VoucherSafetyCounts(run));owner.AssertClean();
            if(!late)await OpenVoucherPeriod(run);save.Fault=null;
            Assert.Equal(AccountingApiStatus.Ok,(await owner.Approve(admission.VoucherDraftId!.Value,"SENTETIK-business")).Status);
            Assert.True((await owner.Approve(admission.VoucherDraftId.Value,"SENTETIK-business")).IsReplay);
            Assert.Equal(before with{Journals=before.Journals+1,Lines=before.Lines+4,Deliveries=before.Deliveries+1,Commands=before.Commands+1,Audits=before.Audits+2},await VoucherSafetyCounts(run));owner.AssertClean();
        }
    }
    [SqlServerFact]
    public async Task Source_voucher_native_maker_refusal_and_durable_version_receipt_keep_original_codes_after_separate_approval()
    {
        await using var run=await Run.StartAsync(true);await OpenVoucherPeriod(run);var request=Request(CompanyA);var admission=await run.AdmitAsync(CompanyA,request);var id=admission.VoucherDraftId!.Value;
        var before=await VoucherSafetyCounts(run);await using var owner=new VoucherApprovalScope(run);owner.User.Actor=request.SourceActorId;
        var denied=await owner.Approve(id,"SENTETIK-maker");Assert.Equal(AccountingApiStatus.Denied,denied.Status);Assert.Equal("source_voucher_approval_refused",denied.Code);
        Assert.Equal(before,await VoucherSafetyCounts(run));owner.AssertClean();owner.User.Actor=VoucherChecker;
        var refused=await owner.Service.ApproveAsync(id,2,"SENTETIK-stale");Assert.Equal(AccountingApiStatus.Conflict,refused.Status);Assert.Equal(AccountingApiCodes.VersionConflict,refused.Code);
        Assert.Equal(before with{Commands=before.Commands+1},await VoucherSafetyCounts(run));
        Assert.Equal(AccountingApiStatus.Ok,(await owner.Approve(id,"SENTETIK-valid")).Status);var after=await VoucherSafetyCounts(run);
        var old=await owner.Service.ApproveAsync(id,2,"SENTETIK-stale");Assert.True(old.IsReplay);Assert.Equal(refused.Code,old.Code);Assert.Equal(refused.ExpectedVersion,old.ExpectedVersion);Assert.Equal(refused.CurrentVersion,old.CurrentVersion);
        Assert.Equal(owner.CorrelationId,old.OriginalCorrelationId);Assert.Equal(after,await VoucherSafetyCounts(run));owner.AssertClean();
    }
    [SqlServerFact]
    public async Task Source_voucher_controlled_provider_races_make_only_one_readonly_original_key_pass_after_owned_abort()
    {
        // Injected provider failures over real SQL rollback/receipts; this is not a claim of a natural two-session deadlock.
        foreach(var fault in new[]{"direct","wrapped","audit","completed-provider","concurrency","final-2601","final-2627"})
        foreach(var winner in new[]{false,true})
        {
            if(winner&&fault=="completed-provider")continue; // ADO already-completed TX skips EF rollback callback; no invented winner timing.
            await using var run=await Run.StartAsync(true);await OpenVoucherPeriod(run);var admission=await run.AdmitAsync(CompanyA,Request(CompanyA));var id=admission.VoucherDraftId!.Value;
            var before=await VoucherSafetyCounts(run);var save=new VoucherApprovalSave{Fault=fault=="audit"?null:fault};
            var commandReads=0;var probe=new VoucherApprovalQuery(sql=>{if(sql.Contains("[Commands]",StringComparison.Ordinal)&&sql.TrimStart().StartsWith("SELECT",StringComparison.OrdinalIgnoreCase))commandReads++;});
            var afterAbort=new VoucherApprovalAfterRollback();AccountingApiResult<AccountingSourceVoucherViewV1>? winnerAnswer=null;
            if(winner)afterAbort.After=async()=>{await using var other=new VoucherApprovalScope(run);winnerAnswer=await other.Approve(id,"SENTETIK-race");Assert.Equal(AccountingApiStatus.Ok,winnerAnswer.Status);};
            await using var owner=new VoucherApprovalScope(run,save:save,extra:probe,transactions:afterAbort);owner.Writer.InjectDeadlock=fault=="audit";
            var answer=await owner.Approve(id,"SENTETIK-race");Assert.True(fault=="audit"?owner.Writer.Injected:save.Injected);
            Assert.Equal(2,commandReads);Assert.Equal(1,owner.Writer.Discards);owner.AssertClean();
            Assert.Equal(fault=="audit"?0:fault.StartsWith("final-",StringComparison.Ordinal)?2:1,save.Attempts);
            if(winner)
            {
                Assert.True(afterAbort.Fired);Assert.True(answer.IsReplay);Assert.Equal(AccountingApiStatus.Ok,answer.Status);Assert.Equal(winnerAnswer!.Resource!.JournalId,answer.Resource?.JournalId);
                Assert.Equal(before with{Journals=before.Journals+1,Lines=before.Lines+4,Deliveries=before.Deliveries+1,Commands=before.Commands+1,Audits=before.Audits+2},await VoucherSafetyCounts(run));
            }
            else
            {
                Assert.Equal(AccountingApiStatus.Conflict,answer.Status);Assert.Equal(AccountingStoreCodes.ConcurrentChange,answer.Code);Assert.False(answer.IsReplay);Assert.Equal(before,await VoucherSafetyCounts(run));
                save.Fault=null;owner.Writer.InjectDeadlock=false;Assert.Equal(AccountingApiStatus.Ok,(await owner.Approve(id,"SENTETIK-race")).Status);
                Assert.True((await owner.Approve(id,"SENTETIK-race")).IsReplay);
                Assert.Equal(before with{Journals=before.Journals+1,Lines=before.Lines+4,Deliveries=before.Deliveries+1,Commands=before.Commands+1,Audits=before.Audits+2},await VoucherSafetyCounts(run));
            }
        }
    }
    [SqlServerFact]
    public async Task Source_voucher_abort_cleanup_faults_remain_failures_and_cannot_be_returned_as_clean_native_refusals()
    {
        foreach(var dispose in new[]{false,true})
        {
            await using var run=await Run.StartAsync(true);await OpenVoucherPeriod(run);var admission=await run.AdmitAsync(CompanyA,Request(CompanyA));
            var save=new VoucherApprovalSave{Fault="after-journal-domain"};var cleanup=new VoucherApprovalCleanup(dispose);
            await using var owner=new VoucherApprovalScope(run,save:save,extra:cleanup);
            var error=await Assert.ThrowsAsync<AggregateException>(()=>owner.Approve(admission.VoucherDraftId!.Value,"SENTETIK-cleanup"));
            Assert.True(save.Injected);Assert.True(cleanup.Failed);Assert.Contains(error.InnerExceptions,x=>x is InvalidOperationException);
            Assert.Equal(1,owner.Writer.Discards);Assert.False(owner.Db.ChangeTracker.HasChanges());Assert.False(owner.Writer.HasPendingDrafts);
            // No native 422 / clean-rollback acceptance claim when provider rollback/dispose failed.
        }
    }
    private sealed record VoucherSafetyCount(int Journals,int Lines,int Deliveries,int Commands,int Audits);
    private static async Task<VoucherSafetyCount> VoucherSafetyCounts(Run run)=>new(
        await run.ScalarAsync<int>("SELECT COUNT(*) FROM accounting.Journals WHERE CompanyId=@c",("@c",CompanyA)),
        await run.ScalarAsync<int>("SELECT COUNT(*) FROM accounting.JournalLines WHERE CompanyId=@c",("@c",CompanyA)),
        await run.ScalarAsync<int>("SELECT COUNT(*) FROM accounting.Deliveries WHERE CompanyId=@c",("@c",CompanyA)),
        await run.ScalarAsync<int>("SELECT COUNT(*) FROM accounting.Commands WHERE CompanyId=@c",("@c",CompanyA)),
        await run.ScalarAsync<int>("SELECT COUNT(*) FROM audit.AuditEvents WHERE CompanyId=@c",("@c",CompanyA)));
    private sealed class VoucherApprovalScope:IAsyncDisposable
    {
        private readonly IServiceScope auditScope;private readonly AccountingDbContext other;
        public VoucherApprovalCompany Company{get;}=new();public VoucherApprovalUser User{get;}=new();public VoucherApprovalAuthority Authority{get;}=new();
        public AccountingDbContext Db{get;}public AuditEventCollector Collector{get;}public VoucherApprovalWriter Writer{get;}
        public AccountingSourceVoucherServiceV1 Service{get;}public string CorrelationId{get;}="SENTETIK-source-voucher-"+Guid.NewGuid().ToString("N");
        public VoucherApprovalScope(Run run,VoucherApprovalSave? save=null,IInterceptor? extra=null,IInterceptor? transactions=null,string? entryFault=null)
        {
            auditScope=run.Factory.Services.CreateScope();var services=auditScope.ServiceProvider;services.GetRequiredService<ICompanyContext>().Set(CompanyA);
            Collector=services.GetRequiredService<AuditEventCollector>();Writer=new(services.GetRequiredService<IAuditTransactionWriterV1>());
            var options=new DbContextOptionsBuilder<AccountingDbContext>().UseSqlServer(run.Factory.DatabaseConnectionString)
                .AddInterceptors(new AuditSaveChangesInterceptor(Collector,Company));
            if(save is not null)options.AddInterceptors(save);if(extra is not null)options.AddInterceptors(extra);if(transactions is not null)options.AddInterceptors(transactions);
            if(extra is VoucherApprovalCleanup cleanup)cleanup.Configure(options);
            Db=new(options.Options,Company);other=new(options.Options,Company);
            var core=new AccountingStore(entryFault=="other-db"?other:Db,Company,entryFault=="other-collector"?new AuditEventCollector():Collector,TimeProvider.System,
                new AccountingAmountScaleVerifier(new AccountingSqlServerRelationalAcceptanceTests.SyntheticMoneyProfiles(CompanyA)),
                normalApprovalAuditWriter:entryFault=="missing-writer"?null:entryFault=="other-writer"?new VoucherApprovalWriter(Writer):Writer);
            var store=new AccountingSourceVoucherStoreV1(Db,core,Company,services.GetRequiredService<IActiveAccountPlanReaderV1>(),new VoucherCurrencies(),Collector,TimeProvider.System,Writer);
            var correlation=new CorrelationContext();correlation.Set(CorrelationId,null,SourceChannels.Web,null,null);
            Service=new(store,core,Authority,Company,User,correlation);
        }
        public Task<AccountingApiResult<AccountingSourceVoucherViewV1>> Approve(Guid id,string key,CancellationToken token=default)=>Service.ApproveAsync(id,1,key,token);
        public void AssertClean(){Assert.False(Db.ChangeTracker.HasChanges());Assert.False(Writer.HasPendingDrafts);Assert.Null(Db.Database.CurrentTransaction);}
        public async ValueTask DisposeAsync(){try{await Db.DisposeAsync();await other.DisposeAsync();}finally{auditScope.Dispose();}}
    }
    private sealed class VoucherApprovalCompany:ICompanyContext{public Guid CompanyId{get;private set;}=CompanyA;public bool IsResolved=>CompanyId!=Guid.Empty;public void Set(Guid id)=>CompanyId=id;}
    private sealed class VoucherApprovalUser:ICurrentUser{public Guid Actor=VoucherChecker;public bool Member=true;public bool IsAuthenticated=>true;public string UserId=>Actor.ToString("D");public IReadOnlySet<Guid> AllowedCompanyIds{get;}=new HashSet<Guid>{CompanyA};public bool CanAccessCompany(Guid id)=>Member&&id==CompanyA;}
    private sealed class VoucherApprovalAuthority:IErpAuthorityRevalidationService
    {
        public bool Granted=true;public Func<CancellationToken,Task>? BeforeRead;
        public async Task<PermissionScope> RevalidatePermissionScopeAsync(string permission,Guid company,CancellationToken token=default)
        {Assert.Equal(NormalJournalPermissionNamesV1.DraftApprove,permission);if(BeforeRead is not null)await BeforeRead(token);return new(Granted,true,new HashSet<string>(),new HashSet<Guid>(),new HashSet<Guid>(),new HashSet<Guid>());}
    }
    private sealed class VoucherApprovalWriter(IAuditTransactionWriterV1 actual):IAuditTransactionWriterV1
    {
        public int Writes,Discards;public bool InjectDeadlock,Injected;public Action? AfterWrite;
        public bool HasPendingDrafts=>actual.HasPendingDrafts;
        public async Task WritePendingAsync(System.Data.IDbTransaction tx,CancellationToken token=default)
        {Writes++;if(InjectDeadlock&&!Injected){Injected=true;throw SqlConcurrencyWrapperPolicyTests.SqlFailure(1205);}await actual.WritePendingAsync(tx,token);AfterWrite?.Invoke();}
        public void DiscardPending(){Discards++;actual.DiscardPending();}
    }
    private sealed class VoucherApprovalSave:SaveChangesInterceptor
    {
        public bool JournalSaved,VoucherSaved,Injected;public string? Fault;public int Attempts;private bool journalSave,voucherSave;
        public override ValueTask<InterceptionResult<int>> SavingChangesAsync(DbContextEventData data,InterceptionResult<int> result,CancellationToken token=default)
        {
            if(data.Context is AccountingDbContext db)
            {
                journalSave=db.ChangeTracker.Entries<JournalRow>().Any(x=>x.State==EntityState.Added);
                voucherSave=db.ChangeTracker.Entries<AccountingSourceDocumentVoucherDraftRow>().Any(x=>x.State==EntityState.Modified&&x.Entity.Status==2);
                if(journalSave||voucherSave)
                {
                    Attempts++;
                    if(Fault is not null&&Fault is not ("after-journal-domain" or "after-voucher-domain")&&!Injected&&(!Fault.StartsWith("final-",StringComparison.Ordinal)||voucherSave))
                    {
                        Injected=true;if(Fault=="concurrency")throw new DbUpdateConcurrencyException("SENTETIK-source-voucher-race");
                        if(Fault is "final-2601" or "final-2627")throw new DbUpdateException("SENTETIK-source-voucher-duplicate",SqlConcurrencyWrapperPolicyTests.SqlFailure(Fault=="final-2601"?2601:2627));
                        if(Fault=="completed-provider")db.Database.CurrentTransaction!.GetDbTransaction().Rollback();
                        var failure=SqlConcurrencyWrapperPolicyTests.SqlFailure(1205);if(Fault=="wrapped")throw new DbUpdateException("SENTETIK-source-voucher-wrapped",failure);throw failure;
                    }
                }
            }
            return ValueTask.FromResult(result);
        }
        public override ValueTask<int> SavedChangesAsync(SaveChangesCompletedEventData data,int result,CancellationToken token=default)
        {
            if(journalSave)JournalSaved=true;if(voucherSave)VoucherSaved=true;
            if(!Injected&&(journalSave&&Fault=="after-journal-domain"||voucherSave&&Fault=="after-voucher-domain"))
            {Injected=true;throw new AccountingDomainException("SENTETIK-native-domain","SENTETIK post-save native refusal");}
            return ValueTask.FromResult(result);
        }
    }
    private sealed class VoucherApprovalQuery(Action<string> after):DbCommandInterceptor
    {public override ValueTask<DbDataReader> ReaderExecutedAsync(DbCommand command,CommandExecutedEventData data,DbDataReader result,CancellationToken token=default){after(command.CommandText);return ValueTask.FromResult(result);}}
    private sealed class VoucherApprovalAfterRollback:DbTransactionInterceptor
    {
        public bool Fired;public Func<Task>? After;
        public override async Task TransactionRolledBackAsync(DbTransaction tx,TransactionEndEventData data,CancellationToken token=default)
        {if(!Fired&&After is not null){Fired=true;await After();}}
    }
    private sealed class VoucherApprovalCleanup(bool dispose):DbTransactionInterceptor
    {
        public bool Failed;
        public void Configure(DbContextOptionsBuilder<AccountingDbContext> options)=>options.LogTo((id,_)=>id==RelationalEventId.TransactionDisposed,
            _=>{if(dispose&&!Failed){Failed=true;throw new InvalidOperationException("SENTETIK source-voucher dispose fault");}});
        public override ValueTask<InterceptionResult> TransactionRollingBackAsync(DbTransaction tx,TransactionEventData data,InterceptionResult result,CancellationToken token=default)
        {if(!dispose&&!Failed){Failed=true;throw new InvalidOperationException("SENTETIK source-voucher rollback fault");}return ValueTask.FromResult(result);}
    }
    private static AccountingCommandRow VoucherSafetyCaller()=>new(){CompanyId=CompanyA,CommandName="SENTETIK-caller",IdempotencyKeyHash="caller",ActorId="caller",PayloadHash="hash",ResultJson="{}",CorrelationId="corr"};
    private static AuditEventDraft VoucherSafetyAudit()=>new(CompanyA,"SENTETIK-caller","Accounting.SourceVoucher","caller","Approve",1,2,"User",VoucherChecker.ToString("D"),NormalJournalPermissionNamesV1.DraftApprove,[],"{}","{}","SENTETIK",null,AuditAiInvolvement.None,null,SourceChannels.Web,null,null,"SENTETIK-caller",null,"key");
}
