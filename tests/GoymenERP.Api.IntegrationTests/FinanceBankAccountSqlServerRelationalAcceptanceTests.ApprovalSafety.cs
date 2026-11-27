using System.Data.Common;
using System.Transactions;
using GoymenERP.BuildingBlocks.Multitenancy;
using GoymenERP.BuildingBlocks.Security;
using GoymenERP.Modules.Audit.Application.Contracts;
using GoymenERP.Modules.Audit.Infrastructure.Persistence;
using GoymenERP.Modules.Audit.Infrastructure.Services;
using GoymenERP.Modules.Finance.Contracts;
using GoymenERP.Modules.Finance.Infrastructure;
using GoymenERP.Modules.Finance.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;

namespace GoymenERP.Api.IntegrationTests;

public sealed partial class FinanceBankAccountSqlServerRelationalAcceptanceTests
{
    [SqlServerFact]
    public async Task Bank_approval_owned_entry_refusals_preserve_caller_work_including_original_key_replay()
    {
        await using var run=await Run.StartAsync();var id=await BankSafetyProposal(run);
        await using(var initial=new BankSafetyScope(run))Assert.Equal(CommandOutcome.Succeeded,(await initial.Approve(id,"entry-original")).Outcome);
        foreach(var boundary in new[]{"missing-writer","borrowed","dirty","audit","ambient"})
        foreach(var replay in new[]{false,true})
        {
            await using var scope=new BankSafetyScope(run,missingWriter:boundary=="missing-writer");
            await using var borrowed=boundary=="borrowed"?await scope.Database.Database.BeginTransactionAsync():null;
            var row=boundary=="dirty"?await scope.Database.BankAccounts.SingleAsync(r=>r.Id==id):null;
            if(row is not null)row.DisplayName="SENTETIK caller change";
            if(boundary=="audit")scope.Collector.Enqueue(BankSafetyDraft(run,id));
            using var ambient=boundary=="ambient"?new TransactionScope(TransactionScopeAsyncFlowOption.Enabled):null;
            var result=await scope.Approve(id,replay?"entry-original":"entry-new");
            Assert.Equal(CommandOutcome.Unavailable,result.Outcome);Assert.Same(borrowed,scope.Database.Database.CurrentTransaction);
            Assert.Equal(boundary=="dirty",scope.Database.ChangeTracker.HasChanges());Assert.Equal(boundary=="audit",scope.Writer.HasPendingDrafts);
            if(row is not null)Assert.Same(row,Assert.Single(scope.Database.ChangeTracker.Entries()).Entity);
            scope.Writer.DiscardPending();
        }
    }

    [SqlServerFact]
    public async Task Bank_approval_pre_save_post_audit_post_save_permission_actor_or_company_loss_rolls_back_same_key_then_replays_once()
    {
        await using var run=await Run.StartAsync();
        foreach(var fault in new[]{"before-audit","after-audit","after-save","company","actor"})
        {
            var id=await BankSafetyProposal(run);var probe=new BankSafetySaveProbe(id);await using var scope=new BankSafetyScope(run,probe:probe);
            var deny=true;var observed=false;
            scope.Permissions.BeforeRead=()=>
            {
                var staged=scope.Database.ChangeTracker.Entries<BankAccountRow>().Any(r=>r.Entity.Id==id && r.Entity.ApprovedBy is not null);
                var phase=fault=="before-audit"?staged&&scope.Writer.HasPendingDrafts:fault=="after-audit"?staged&&!scope.Writer.HasPendingDrafts:probe.Saved;
                if(!deny||!phase)return PermissionDecision.Allowed;observed=true;
                if(fault=="company")scope.Company.Set(CompanyB);else if(fault=="actor")scope.User.Actor="SENTETIK-other";
                else return PermissionDecision.Denied;
                return PermissionDecision.Allowed;
            };
            var refused=await scope.Approve(id,"late-"+fault);Assert.True(observed);Assert.Equal(CommandOutcome.Denied,refused.Outcome);
            Assert.Empty(scope.Database.ChangeTracker.Entries());Assert.False(scope.Writer.HasPendingDrafts);Assert.Null(scope.Database.Database.CurrentTransaction);
            Assert.Equal(1L,await run.ScalarAsync<long>("SELECT Version FROM finance.BankAccounts WHERE Id=@id",("@id",id)));
            Assert.Equal(0,await run.ScalarAsync<int>("SELECT COUNT(*) FROM finance.Commands WHERE CommandName=@command AND BankAccountId=@id",("@command",FinanceBankAccountService.ApproveCommand),("@id",id)));
            Assert.Equal(0,await run.ScalarAsync<int>("SELECT COUNT(*) FROM audit.AuditEvents WHERE EventType=N'finance.bank-account.approved.v1' AND EntityId=@id",("@id",id.ToString("D"))));
            deny=false;scope.Company.Set(CompanyA);scope.User.Actor=Approver;
            var saved=await scope.Approve(id,"late-"+fault);Assert.Equal(CommandOutcome.Succeeded,saved.Outcome);
            var replay=await scope.Approve(id,"late-"+fault);Assert.True(replay.IsReplay);Assert.Equal(saved.Resource,replay.Resource);
            Assert.Equal(saved.OriginalCorrelationId,replay.OriginalCorrelationId);Assert.False(scope.Writer.HasPendingDrafts);
            Assert.Equal(1,await run.ScalarAsync<int>("SELECT COUNT(*) FROM audit.AuditEvents WHERE EventType=N'finance.bank-account.approved.v1' AND EntityId=@id",("@id",id.ToString("D"))));
        }
    }

    [SqlServerFact]
    public async Task Bank_approval_failed_save_reads_only_one_fresh_winner_after_abort_and_never_creates_a_conflict_receipt()
    {
        await using var run=await Run.StartAsync();
        foreach(var publish in new[]{false,true})
        {
            var id=await BankSafetyProposal(run);var probe=new BankSafetySaveProbe(id){FailSave=true};await using var scope=new BankSafetyScope(run,probe:probe);
            var boundaries=new HashSet<Guid>();BankAccountCommandResult? winner=null;
            scope.Permissions.AfterRead=async()=>
            {
                if(scope.Database.Database.CurrentTransaction is not{}tx)return;boundaries.Add(tx.TransactionId);
                if(boundaries.Count!=2||winner is not null||!publish)return;
                Assert.Empty(scope.Database.ChangeTracker.Entries());Assert.False(scope.Writer.HasPendingDrafts);Assert.Equal(1,probe.Attempts);
                await using var actual=new BankSafetyScope(run);winner=await actual.Approve(id,"race-key");Assert.False(winner.IsReplay);
            };
            var result=await scope.Approve(id,"race-key");Assert.Equal(2,boundaries.Count);Assert.Equal(1,probe.Attempts);
            Assert.Empty(scope.Database.ChangeTracker.Entries());Assert.False(scope.Writer.HasPendingDrafts);Assert.Null(scope.Database.Database.CurrentTransaction);
            if(publish){Assert.NotNull(winner);Assert.True(result.IsReplay);Assert.Equal(winner.Resource,result.Resource);Assert.Equal(winner.OriginalCorrelationId,result.OriginalCorrelationId);}
            else{Assert.Equal(CommandOutcome.VersionConflict,result.Outcome);Assert.Equal("concurrent_change",Assert.Single(result.Issues).Code);}
            Assert.Equal(publish?1:0,await run.ScalarAsync<int>("SELECT COUNT(*) FROM finance.Commands WHERE CommandName=@command AND BankAccountId=@id",("@command",FinanceBankAccountService.ApproveCommand),("@id",id)));
            Assert.Equal(publish ? 2L : 1L, await run.ScalarAsync<long>("SELECT Version FROM finance.BankAccounts WHERE Id=@id", ("@id", id)));
            Assert.Equal(publish ? 1 : 0, await run.ScalarAsync<int>("SELECT COUNT(*) FROM audit.AuditEvents WHERE EventType=N'finance.bank-account.approved.v1' AND EntityId=@id", ("@id", id.ToString("D"))));
        }
    }

    [SqlServerFact]
    public async Task Bank_approval_rollback_failure_still_discards_owned_tracker_and_actual_collector_without_starting_replay()
    {
        await using var run=await Run.StartAsync();var id=await BankSafetyProposal(run);var rollback=new BankSafetyRollbackFailure();
        await using var scope=new BankSafetyScope(run,extra:rollback);var observed=false;
        scope.Permissions.BeforeRead=()=>{if(scope.Writer.HasPendingDrafts){observed=true;return PermissionDecision.Denied;}return PermissionDecision.Allowed;};
        await Assert.ThrowsAsync<InvalidOperationException>(()=>scope.Approve(id,"rollback-fails"));
        Assert.True(observed);Assert.Equal(1,rollback.Attempts);Assert.Empty(scope.Database.ChangeTracker.Entries());Assert.False(scope.Writer.HasPendingDrafts);
        Assert.Null(scope.Database.Database.CurrentTransaction);
        Assert.Equal(1L,await run.ScalarAsync<long>("SELECT Version FROM finance.BankAccounts WHERE Id=@id",("@id",id)));
        Assert.Equal(0,await run.ScalarAsync<int>("SELECT COUNT(*) FROM finance.Commands WHERE CommandName=@command AND BankAccountId=@id",("@command",FinanceBankAccountService.ApproveCommand),("@id",id)));
    }

    [SqlServerFact]
    public async Task Bank_approval_stale_version_keeps_its_original_terminal_receipt_without_changing_account_or_audit()
    {
        await using var run = await Run.StartAsync(); var id = await BankSafetyProposal(run);
        await using var scope = new BankSafetyScope(run);
        var request = new BankAccountApproveRequest(id, "99");
        var first = await scope.Service.ApproveAsync(new(CompanyA, Approver, "SENTETIK-terminal-original", null), Key("terminal-key"), request, default);
        Assert.Equal(CommandOutcome.VersionConflict, first.Outcome); Assert.False(first.IsReplay);
        var replay = await scope.Service.ApproveAsync(new(CompanyA, Approver, "SENTETIK-terminal-retry", null), Key("terminal-key"), request, default);
        Assert.True(replay.IsReplay); Assert.Equal(CommandOutcome.VersionConflict, replay.Outcome);
        Assert.Equal("SENTETIK-terminal-original", replay.OriginalCorrelationId);
        Assert.Equal(1L, await run.ScalarAsync<long>("SELECT Version FROM finance.BankAccounts WHERE Id=@id", ("@id", id)));
        Assert.Equal(1, await run.ScalarAsync<int>("SELECT COUNT(*) FROM finance.Commands WHERE CommandName=@command AND BankAccountId=@id", ("@command", FinanceBankAccountService.ApproveCommand), ("@id", id)));
        Assert.Equal(0, await run.ScalarAsync<int>("SELECT COUNT(*) FROM audit.AuditEvents WHERE EventType=N'finance.bank-account.approved.v1' AND EntityId=@id", ("@id", id.ToString("D"))));
        Assert.False(scope.Writer.HasPendingDrafts);
    }

    private static async Task<Guid> BankSafetyProposal(Run run)
    {
        await using var proposer=run.Open(CompanyA,Proposer);var answer=await proposer.Accounts.ProposeAsync(proposer.Context,Key("safety-propose-"+Guid.NewGuid().ToString("N")),
            new("SENTETIK safety",Currency,"SENTETIK bank","SENTETIK term",false),default);Assert.Equal(CommandOutcome.Succeeded,answer.Outcome);return answer.Resource!.Id;
    }
    private static AuditEventDraft BankSafetyDraft(Run run,Guid id)=>new(CompanyA,"finance.bank-account.approved.v1","Finance.BankAccount",id.ToString("D"),"Approve",1,2,
        "User",Approver,"finance.bank-account.approve",[],"{}","{}","SENTETIK caller draft",null,AuditAiInvolvement.None,null,"Web",null,null,run.Correlation,null,"SENTETIK caller");

    // Real Finance SQL/store/writer/audit; only permission/currency ports and the concurrency/rollback faults are controlled.
    private sealed class BankSafetyScope:IAsyncDisposable
    {
        private readonly IServiceScope auditScope;public ICompanyContext Company{get;}public FinanceDbContext Database{get;}public AuditEventCollector Collector{get;}public IAuditTransactionWriterV1 Writer{get;}
        public readonly BankSafetyUser User=new();public readonly BankSafetyPermissions Permissions=new();public FinanceBankAccountService Service{get;}private readonly string correlation="bank-safety-"+Guid.NewGuid().ToString("N");
        public BankSafetyScope(Run run,bool missingWriter=false,BankSafetySaveProbe? probe=null,IInterceptor? extra=null)
        {
            auditScope=run.Factory.Services.CreateScope();Company=auditScope.ServiceProvider.GetRequiredService<ICompanyContext>();Company.Set(CompanyA);
            Collector=auditScope.ServiceProvider.GetRequiredService<AuditEventCollector>();Writer=auditScope.ServiceProvider.GetRequiredService<IAuditTransactionWriterV1>();
            var options=new DbContextOptionsBuilder<FinanceDbContext>().UseSqlServer(run.Factory.DatabaseConnectionString,sql=>sql.MigrationsHistoryTable("__EFMigrationsHistory",FinanceDbContext.Schema));
            options.AddInterceptors(new AuditSaveChangesInterceptor(Collector,Company));if(probe is not null)options.AddInterceptors(probe);if(extra is not null)options.AddInterceptors(extra);
            Database=new(options.Options,Company);
            Service=new(Database,Company,User,Permissions,new SyntheticCandidates(),Collector,new(new(FinanceBankAccountService.ProposeCommand,[],"Web"),new(FinanceBankAccountService.ApproveCommand,[],"Web")),TimeProvider.System,missingWriter?null:Writer);
        }
        public Task<BankAccountCommandResult> Approve(Guid id,string key)=>Service.ApproveAsync(new(CompanyA,Approver,correlation,null),new(key),new(id,"1"),default);
        public async ValueTask DisposeAsync(){try{await Database.DisposeAsync();}finally{auditScope.Dispose();}}
    }
    private sealed class BankSafetyUser:ICurrentUser
    {public string Actor=Approver;public bool IsAuthenticated=>true;public string UserId=>Actor;public IReadOnlySet<Guid> AllowedCompanyIds{get;}=new HashSet<Guid>{CompanyA,CompanyB};public bool CanAccessCompany(Guid id)=>AllowedCompanyIds.Contains(id);}
    private sealed class BankSafetyPermissions:IFinancePermissionPort
    {public Func<PermissionDecision>? BeforeRead;public Func<Task>? AfterRead;public async Task<PermissionDecision> CheckAsync(FinanceContext c,FinanceAction a,Guid? id,CancellationToken token){var result=BeforeRead?.Invoke()??PermissionDecision.Allowed;if(AfterRead is not null)await AfterRead();return result;}}
    private sealed class BankSafetySaveProbe(Guid id):SaveChangesInterceptor
    {
        public bool Saved,FailSave;public int Attempts;
        public override ValueTask<InterceptionResult<int>> SavingChangesAsync(DbContextEventData data,InterceptionResult<int> result,CancellationToken token=default)
        {if(data.Context is FinanceDbContext db&&db.ChangeTracker.Entries<BankAccountRow>().Any(r=>r.Entity.Id==id&&r.State==EntityState.Modified)){Attempts++;if(FailSave)throw new DbUpdateConcurrencyException("SENTETIK bank save fault");}return ValueTask.FromResult(result);}
        public override ValueTask<int> SavedChangesAsync(SaveChangesCompletedEventData data,int result,CancellationToken token=default){if(Attempts>0)Saved=true;return ValueTask.FromResult(result);}
    }
    private sealed class BankSafetyRollbackFailure:DbTransactionInterceptor
    {public int Attempts;public override ValueTask<InterceptionResult> TransactionRollingBackAsync(DbTransaction transaction,TransactionEventData data,InterceptionResult result,CancellationToken token=default){Attempts++;throw new InvalidOperationException("SENTETIK rollback fault");}}
}
