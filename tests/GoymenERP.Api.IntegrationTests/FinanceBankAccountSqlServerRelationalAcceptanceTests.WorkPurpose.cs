using GoymenERP.Api.WorkRuntime;
using GoymenERP.BuildingBlocks.Multitenancy;
using GoymenERP.BuildingBlocks.Observability;
using GoymenERP.BuildingBlocks.Security;
using GoymenERP.BuildingBlocks.Work;
using GoymenERP.Modules.Audit.Application.Contracts;
using GoymenERP.Modules.Audit.Infrastructure.Services;
using GoymenERP.Modules.Audit.Infrastructure.Persistence;
using GoymenERP.Modules.Finance.Contracts;
using GoymenERP.Modules.Finance.Infrastructure;
using GoymenERP.Modules.Finance.Infrastructure.Persistence;
using GoymenERP.Modules.Finance.Infrastructure.WorkInbox;
using GoymenERP.Modules.Work.Infrastructure.Delegations;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;

namespace GoymenERP.Api.IntegrationTests;

public sealed partial class FinanceBankAccountSqlServerRelationalAcceptanceTests
{
    [SqlServerFact]
    public async Task Work_bank_approval_proves_saved_and_original_command_receipt_after_later_native_IBAN_revision_without_card_disclosure()
    {
        await using var run=await Run.StartAsync();var id=await BankSafetyProposal(run);
        var execution=new WorkDecisionExecutionFenceV1();var authority=new WorkDecisionAuthorityV1(new BankWorkAllowedAuthority(),execution);var delegation=new BankWorkDelegations();
        WorkDecisionResultV1 first;
        await using(var scope=new BankWorkScope(run,Approver,authority,execution,delegation))
        {
            var preview=await scope.Host.PreviewAsync(WorkDecisionHostV1.BankAccount,id,default);
            Assert.True(preview.Pending);Assert.Equal(Proposer,preview.BankAccount!.ProposedBy);Assert.False(preview.RejectionSupported);
            first=await scope.Host.DecideAsync(WorkDecisionHostV1.BankAccount,id,new("1",true,null),"bank-work-key",default);
            Assert.Equal(("Approved",false),(first.State,first.Replayed));
        }
        await using(var maker=new BankWorkScope(run,Proposer,authority,execution,delegation))
        {
            Assert.Equal(CommandOutcome.Succeeded,(await maker.Service.ProposeIbanChangeAsync(maker.Context,new("later-iban-propose"),new(id,"2","TR330006100519786457841326","SENTETIK IBAN revision"),default)).Outcome);
        }
        await using(var scope=new BankWorkScope(run,Approver,authority,execution,delegation))
        {
            Assert.Equal(CommandOutcome.Succeeded,(await scope.Service.ApproveIbanChangeAsync(scope.Context,new("later-iban-approve"),new(id,"3"),default)).Outcome);
            var preview=await scope.Host.PreviewAsync(WorkDecisionHostV1.BankAccount,id,default);Assert.False(preview.Pending);Assert.Equal("4",preview.SourceVersion);
            var wire=System.Text.Json.JsonSerializer.Serialize(preview,new System.Text.Json.JsonSerializerOptions(System.Text.Json.JsonSerializerDefaults.Web));
            foreach(var field in new[]{"iban","accountNumber","bankName","bankAccountProof","approvalReceipt","TR330006100519786457841326"})Assert.DoesNotContain(field,wire);
            var replay=await scope.Host.DecideAsync(WorkDecisionHostV1.BankAccount,id,new("1",true,null),"bank-work-key",default);
            Assert.True(replay.Replayed);Assert.Equal(first.OriginalCorrelationId,replay.OriginalCorrelationId);
            var native=await scope.Service.ApproveAsync(scope.Context,new("bank-work-key"),new(id,"1"),default);
            Assert.True(native.IsReplay);Assert.Equal("2",native.Resource!.Version);
            var receipt=Assert.IsType<BankAccountApprovalReceiptV1>(native.ApprovalReceipt);
            Assert.Equal((CompanyA,id,Approver,FinanceBankAccountService.ApproveCommand),(receipt.CompanyId,receipt.AccountId,receipt.ActorId,receipt.CommandName));
            Assert.Equal(await run.ScalarAsync<DateTimeOffset>("SELECT RecordedAtUtc FROM finance.Commands WHERE CommandName=@c AND BankAccountId=@id",("@c",FinanceBankAccountService.ApproveCommand),("@id",id)),receipt.RecordedAtUtc);
        }
        Assert.Equal(1,await run.ScalarAsync<int>("SELECT COUNT(*) FROM audit.AuditEvents WHERE EventType=N'finance.bank-account.approved.v1' AND EntityId=@id",("@id",id.ToString("D"))));
        Assert.Equal(0,await run.ScalarAsync<int>("SELECT COUNT(*) FROM finance.MoneyEvents"));
        Assert.DoesNotContain("approvalReceipt",await run.ScalarAsync<string>("SELECT ResultJson FROM finance.Commands WHERE CommandName=@c AND BankAccountId=@id",("@c",FinanceBankAccountService.ApproveCommand),("@id",id)));
    }

    [SqlServerFact]
    public async Task Work_bank_post_save_delegation_revocation_rolls_back_actual_approval_command_and_audit_then_same_key_succeeds()
    {
        await using var run=await Run.StartAsync();var id=await BankSafetyProposal(run);var delegation=new BankWorkDelegations();var probe=new BankWorkRevokeAfterSave(delegation);
        var execution=new WorkDecisionExecutionFenceV1();var authority=new WorkDecisionAuthorityV1(new BankWorkAllowedAuthority(),execution);
        await using var scope=new BankWorkScope(run,Approver,authority,execution,delegation,probe);
        var request=new WorkDecisionRequestV1("1",true,null,Guid.NewGuid());
        Assert.Equal(403,(await Assert.ThrowsAsync<WorkDecisionException>(()=>scope.Host.DecideAsync(WorkDecisionHostV1.BankAccount,id,request,"bank-work-revoked",default))).Status);
        Assert.True(probe.Saved);Assert.Empty(scope.Database.ChangeTracker.Entries());Assert.False(scope.Writer.HasPendingDrafts);
        Assert.Equal(1L,await run.ScalarAsync<long>("SELECT Version FROM finance.BankAccounts WHERE Id=@id",("@id",id)));
        Assert.Equal(0,await run.ScalarAsync<int>("SELECT COUNT(*) FROM finance.Commands WHERE CommandName=@c AND BankAccountId=@id",("@c",FinanceBankAccountService.ApproveCommand),("@id",id)));
        Assert.Equal(0,await run.ScalarAsync<int>("SELECT COUNT(*) FROM audit.AuditEvents WHERE EventType=N'finance.bank-account.approved.v1' AND EntityId=@id",("@id",id.ToString("D"))));
        delegation.Live=true;probe.Enabled=false;
        Assert.Equal("Approved",(await scope.Host.DecideAsync(WorkDecisionHostV1.BankAccount,id,request,"bank-work-revoked",default)).State);
        Assert.True((await scope.Host.DecideAsync(WorkDecisionHostV1.BankAccount,id,request,"bank-work-revoked",default)).Replayed);
    }

    // Existing real SQL fixture, owner, collector and transaction writer. Permission/currency/delegation availability
    // are controlled ports, like the Cash Work fixture; this is not hosted Identity/Work or bank-provider acceptance.
    private sealed class BankWorkScope:IAsyncDisposable
    {
        private readonly IServiceScope auditScope;public FinanceDbContext Database{get;}public IAuditTransactionWriterV1 Writer{get;}public FinanceBankAccountService Service{get;}public WorkDecisionHostV1 Host{get;}public FinanceContext Context{get;}
        public BankWorkScope(Run run,string actor,IErpAuthorityRevalidationService authority,WorkDecisionExecutionFenceV1 execution,IWorkDelegationReaderV1 delegations,params IInterceptor[] extra)
        {
            auditScope=run.Factory.Services.CreateScope();var company=auditScope.ServiceProvider.GetRequiredService<ICompanyContext>();company.Set(CompanyA);
            var user=new FixedCurrentUser(actor,CompanyA);var collector=auditScope.ServiceProvider.GetRequiredService<AuditEventCollector>();Writer=auditScope.ServiceProvider.GetRequiredService<IAuditTransactionWriterV1>();
            var options=new DbContextOptionsBuilder<FinanceDbContext>().UseSqlServer(run.Factory.DatabaseConnectionString,sql=>sql.MigrationsHistoryTable("__EFMigrationsHistory",FinanceDbContext.Schema))
                .AddInterceptors(new AuditSaveChangesInterceptor(collector,company)).AddInterceptors(extra);
            Database=new(options.Options,company);Context=new(CompanyA,actor,run.Correlation,null);
            Service=new(Database,company,user,new BankWorkPermissionBridge(authority),new SyntheticCandidates(),collector,
                new(new(FinanceBankAccountService.ProposeCommand,["SENTETIK"],"Web"),new(FinanceBankAccountService.ApproveCommand,["SENTETIK"],"Web")),TimeProvider.System,Writer);
            var source=new FinanceBankAccountWorkInboxSourceV1(Database,new FinanceWorkInboxFence(company,user,authority));
            var correlation=new CorrelationContext();correlation.Set(run.Correlation,null,"Web",null,null);
            Host=new(company,user,correlation,authority,[source],delegations,execution,null!,null!,bankAccounts:Service);
        }
        public async ValueTask DisposeAsync(){try{await Database.DisposeAsync();}finally{auditScope.Dispose();}}
    }
    private sealed class BankWorkPermissionBridge(IErpAuthorityRevalidationService authority):IFinancePermissionPort
    {
        public async Task<PermissionDecision> CheckAsync(FinanceContext context,FinanceAction action,Guid? id,CancellationToken token)
        {var permission=FinancePermissionPort.NameOf(action);if(permission is null)return PermissionDecision.Unavailable;var grant=await authority.RevalidatePermissionScopeAsync(permission,context.CompanyId,token);return grant.IsGranted&&grant.IsCompanyWide?PermissionDecision.Allowed:PermissionDecision.Denied;}
    }
    private sealed class BankWorkAllowedAuthority:IErpAuthorityRevalidationService
    {public Task<PermissionScope> RevalidatePermissionScopeAsync(string p,Guid c,CancellationToken token=default)=>Task.FromResult(new PermissionScope(true,true,new HashSet<string>(),new HashSet<Guid>(),new HashSet<Guid>(),new HashSet<Guid>()));}
    private sealed class BankWorkDelegations:IWorkDelegationReaderV1
    {public bool Live=true;public Task<WorkDelegationViewV1?> FindForActorAsync(Guid id,WorkInboxItemV1 item,CancellationToken token=default)=>Task.FromResult<WorkDelegationViewV1?>(Live?new(id,CompanyA,item.Kind,ErpPermissions.FinanceBankAccountApprove,Guid.NewGuid(),Guid.NewGuid(),DateTimeOffset.UtcNow.AddDays(-1),DateTimeOffset.UtcNow.AddDays(1),"1",Guid.NewGuid(),DateTimeOffset.UtcNow,"SENTETIK",null,null,null):null);}
    private sealed class BankWorkRevokeAfterSave(BankWorkDelegations delegation):SaveChangesInterceptor
    {public bool Saved;public bool Enabled=true;public override ValueTask<int> SavedChangesAsync(SaveChangesCompletedEventData data,int result,CancellationToken token=default){Saved=true;if(Enabled)delegation.Live=false;return ValueTask.FromResult(result);}}
}
