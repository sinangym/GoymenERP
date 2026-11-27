using System.Reflection;
using GoymenERP.Api.WorkRuntime;
using GoymenERP.BuildingBlocks.Multitenancy;
using GoymenERP.BuildingBlocks.Observability;
using GoymenERP.BuildingBlocks.Security;
using GoymenERP.BuildingBlocks.Work;
using GoymenERP.Modules.Finance.Contracts;
using GoymenERP.Modules.Finance.Infrastructure;
using GoymenERP.Modules.Work.Infrastructure.Delegations;

namespace GoymenERP.Api.IntegrationTests;

/// <summary>Synthetic transport/authority composition; native transaction rollback is the separate real-SQL source.</summary>
public sealed class WorkCreditDecisionHostTests
{
    private static readonly Guid Company=Guid.Parse("c4710000-0000-4000-8000-000000000001"),Item=Guid.Parse("c4710000-0000-4000-8000-000000000002");
    private const string Actor="SENTETIK-credit-checker";
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Native_credit_owner_receives_exact_version_key_reason_and_correlation_even_after_completion(bool approve)
    {
        var run=new Run();run.Source.Pending=false;
        var reason=approve?null:new string('r',500);
        var answer=await run.Host.DecideAsync(WorkDecisionHostV1.CreditAccount,Item,new("1",approve,reason),"SENTETIK-key",default);
        Assert.Equal(approve?"Approved":"Rejected",answer.State);Assert.True(answer.Replayed);Assert.Equal("SENTETIK-original",answer.OriginalCorrelationId);
        Assert.Equal(approve?"ApproveAsync":"RejectAsync",run.Method);
        Assert.Equal(new FinanceContext(Company,Actor,"SENTETIK-correlation",null),run.Context);
        Assert.Equal("SENTETIK-key",run.Key);
        if(approve)Assert.Equal(new CreditAccountApproveRequest(Item,"1"),run.Request);
        else Assert.Equal(new CreditAccountRejectRequest(Item,"1",reason!),run.Request);
        Assert.All(run.Raw.Asked,name=>Assert.Equal(ErpPermissions.FinanceCreditsApprove,name));
    }
    [Fact]
    public async Task Credit_maker_or_denied_approver_never_reaches_the_native_command()
    {
        var run=new Run();run.Source.Maker=Actor;
        Assert.Equal(403,(await Assert.ThrowsAsync<WorkDecisionException>(()=>run.Host.PreviewAsync(WorkDecisionHostV1.CreditAccount,Item,default))).Status);
        run.Source.Maker="SENTETIK-maker";run.Raw.Allowed=false;
        Assert.Equal(403,(await Assert.ThrowsAsync<WorkDecisionException>(()=>run.Host.DecideAsync(WorkDecisionHostV1.CreditAccount,Item,new("1",true,null),"key",default))).Status);
        Assert.Null(run.Method);
    }
    [Fact]
    public async Task Work_delegation_loss_is_seen_through_the_real_Finance_permission_port_at_the_next_native_fence()
    {
        var run=new Run();run.BeforeNativeFence=()=>run.Delegation.Allowed=false;
        Assert.Equal(403,(await Assert.ThrowsAsync<WorkDecisionException>(()=>run.Host.DecideAsync(WorkDecisionHostV1.CreditAccount,Item,new("1",true,null,Guid.NewGuid()),"key",default))).Status);
        Assert.False(run.NativeEffect);
    }
    [Fact]
    public async Task Credit_rejection_keeps_required_500_reason_and_a_foreign_native_success_is_not_accepted()
    {
        var run=new Run();
        foreach(var reason in new[]{"", " ",new string('r',501)})
            Assert.Equal(400,(await Assert.ThrowsAsync<WorkDecisionException>(()=>run.Host.DecideAsync(WorkDecisionHostV1.CreditAccount,Item,new("1",false,reason),"key",default))).Status);
        Assert.Null(run.Method);run.ResultCompany=Guid.NewGuid();
        Assert.Equal(503,(await Assert.ThrowsAsync<WorkDecisionException>(()=>run.Host.DecideAsync(WorkDecisionHostV1.CreditAccount,Item,new("1",true,null),"key",default))).Status);
    }
    private sealed class Run
    {
        public readonly RawAuthority Raw=new();public readonly CreditSource Source=new();public readonly DelegationReader Delegation=new();
        public string? Method,Key;public FinanceContext? Context;public object? Request;public Guid ResultCompany=Company;
        public bool NativeEffect;public Action? BeforeNativeFence;public readonly WorkDecisionHostV1 Host;
        public Run()
        {
            var company=new CompanyContext();company.Set(Company);var user=new User();
            var correlation=new CorrelationContext();correlation.Set("SENTETIK-correlation",null,"api",null,null);
            var fence=new WorkDecisionExecutionFenceV1();var authority=new WorkDecisionAuthorityV1(Raw,fence);
            var permission=new FinancePermissionPort(authority,company,user);
            var owner=DispatchProxy.Create<IFinanceCreditAccountService,CreditOwnerProxy>();
            async Task<CreditAccountCommandResult> Answer(MethodInfo method,object?[] args)
            {
                var context=(FinanceContext)args[0]!;Method=method.Name;Context=context;Key=((CommandMetadata)args[1]!).IdempotencyKey;Request=args[2];
                BeforeNativeFence?.Invoke();
                if(await permission.CheckAsync(context,FinanceAction.ApproveCredit,null,default)!=PermissionDecision.Allowed)
                    return new(CommandOutcome.Denied,null,false,"SENTETIK",[]);
                NativeEffect=true;
                var status=method.Name=="ApproveAsync"?CreditAccountStatus.Approved:CreditAccountStatus.Rejected;
                return new(CommandOutcome.Succeeded,new(Item,ResultCompany,CreditAccountGroup.Cash,"SENTETIK bank","SENTETIK contract",Company,"1000",new(2026,11,27),status,false,
                    DateTimeOffset.UtcNow,DateTimeOffset.UtcNow,null,"2",null,[]),true,"SENTETIK-original",[]);
            }
            ((CreditOwnerProxy)(object)owner).Handler=Answer;
            Host=new(company,user,correlation,authority,[Source],Delegation,fence,null!,null!,credits:owner);
        }
    }
    public class CreditOwnerProxy:DispatchProxy
    {
        public Func<MethodInfo,object?[],Task<CreditAccountCommandResult>> Handler {get;set;}=(_,_)=>throw new NotSupportedException();
        protected override object? Invoke(MethodInfo? targetMethod,object?[]? args)=>Handler(targetMethod!,args!);
    }
    private sealed class RawAuthority:IErpAuthorityRevalidationService
    {
        public bool Allowed=true;public List<string> Asked=[];
        public Task<PermissionScope> RevalidatePermissionScopeAsync(string permission,Guid companyId,CancellationToken cancellationToken=default)
        {Asked.Add(permission);return Task.FromResult(Allowed?new PermissionScope(true,true,new HashSet<string>(),new HashSet<Guid>(),new HashSet<Guid>(),new HashSet<Guid>()):PermissionScope.Denied);}
    }
    private sealed class CreditSource:IWorkApprovalDecisionSourceV1
    {
        public bool Pending=true;public string Maker="SENTETIK-maker";
        public string Module=>"finance";public string Kind=>WorkDecisionHostV1.CreditAccount;public string ApproverPermission=>ErpPermissions.FinanceCreditsApprove;
        public Task<WorkApprovalDecisionSnapshotV1?> ReadDecisionAsync(WorkInboxSourceContextV1 context,Guid id,CancellationToken cancellationToken)=>
            Task.FromResult<WorkApprovalDecisionSnapshotV1?>(new(Company,new(Module,Kind,Item,"SENTETIK",DateTimeOffset.UtcNow,$"/onaylarim/{Kind}/{Item:D}",Maker,
                [new("finance.credit-account-approval-required",null,"1000",null,null)],[]),"2",Pending));
        public Task<WorkInboxSourceResultV1> ListPendingAsync(WorkInboxSourceContextV1 context,int maxCount,CancellationToken cancellationToken)=>throw new NotSupportedException();
    }
    private sealed class User:ICurrentUser
    {
        public bool IsAuthenticated=>true;public string? UserId=>Actor;public IReadOnlySet<Guid> AllowedCompanyIds {get;}=new HashSet<Guid>([Company]);
        public bool CanAccessCompany(Guid companyId)=>companyId==Company;
    }
    private sealed class DelegationReader:IWorkDelegationReaderV1
    {
        public bool Allowed=true;
        public Task<WorkDelegationViewV1?> FindForActorAsync(Guid id,WorkInboxItemV1 item,CancellationToken cancellationToken=default)=>
            Task.FromResult<WorkDelegationViewV1?>(Allowed?new(id,Company,item.Kind,ErpPermissions.FinanceCreditsApprove,Guid.NewGuid(),Guid.NewGuid(),DateTimeOffset.UtcNow.AddDays(-1),DateTimeOffset.UtcNow.AddDays(1),"1",Guid.NewGuid(),DateTimeOffset.UtcNow,"SENTETIK",null,null,null):null);
    }
}
