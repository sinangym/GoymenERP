using System.Reflection;
using GoymenERP.Api.WorkRuntime;
using GoymenERP.BuildingBlocks.Multitenancy;
using GoymenERP.BuildingBlocks.Observability;
using GoymenERP.BuildingBlocks.Security;
using GoymenERP.BuildingBlocks.Work;
using GoymenERP.Modules.Sales.Application.Contracts;
using GoymenERP.Modules.Work.Infrastructure.Delegations;

namespace GoymenERP.Api.IntegrationTests;

/// <summary>Host composition with synthetic native owners, not SQL/HTTP acceptance.</summary>
public sealed class WorkSalesDecisionHostTests
{
    private static readonly Guid Company=Guid.Parse("d8220000-0000-4000-8000-000000000001"),Item=Guid.Parse("d8220000-0000-4000-8000-000000000002");
    private const string Actor="SENTETIK-reviewer";
    [Theory]
    [InlineData(WorkDecisionHostV1.SalesOrder,true)]
    [InlineData(WorkDecisionHostV1.SalesOrder,false)]
    [InlineData(WorkDecisionHostV1.SalesInvoice,true)]
    [InlineData(WorkDecisionHostV1.SalesInvoice,false)]
    [InlineData(WorkDecisionHostV1.SalesSubcontractInvoice,true)]
    [InlineData(WorkDecisionHostV1.SalesSubcontractInvoice,false)]
    public async Task The_native_owner_receives_the_original_version_optional_reason_key_and_correlation_after_completion(string kind,bool approve)
    {
        var run=new Run(kind);run.Source.Pending=false;
        var result=await run.Host.DecideAsync(kind,Item,new("7",approve,null),"SENTETIK-original-key",default);
        Assert.Equal(approve?"Approved":"Rejected",result.State);Assert.True(result.Replayed);Assert.Equal("SENTETIK-original",result.OriginalCorrelationId);
        var call=Assert.Single(run.Calls);Assert.Equal(WorkDecisionHostV1.SalesOrder==kind?(approve?"ApproveDraftAsync":"RejectDraftAsync"):kind==WorkDecisionHostV1.SalesSubcontractInvoice?(approve?"ApproveSubcontractInvoiceAsync":"RejectSubcontractInvoiceAsync"):(approve?"ApproveInvoiceAsync":"RejectInvoiceAsync"),call.Method);
        Assert.Equal(Item,call.Id);Assert.Equal(new SalesDraftStateCommandRequest("7",null),call.Body);
        Assert.Equal(("SENTETIK-original-key","SENTETIK-correlation"),(call.Metadata.IdempotencyKey,call.Metadata.CorrelationId));
        Assert.DoesNotContain(SalesPermissionManifestV1.DraftsRead,run.Raw.Asked);Assert.DoesNotContain(SalesPermissionManifestV1.InvoicesRead,run.Raw.Asked);
    }
    [Theory]
    [InlineData(WorkDecisionHostV1.SalesOrder)]
    [InlineData(WorkDecisionHostV1.SalesInvoice)]
    [InlineData(WorkDecisionHostV1.SalesSubcontractInvoice)]
    public async Task Maker_and_submitter_are_excluded_without_excluding_an_unrelated_reviewer(string kind)
    {
        var run=new Run(kind);run.Source.Maker=Actor;
        Assert.Equal(403,(await Assert.ThrowsAsync<WorkDecisionException>(()=>run.Host.PreviewAsync(kind,Item,default))).Status);
        run.Source.Maker="SENTETIK-maker";run.Source.Submitter=Actor;
        Assert.Equal(403,(await Assert.ThrowsAsync<WorkDecisionException>(()=>run.Host.DecideAsync(kind,Item,new("7",true,null),"key",default))).Status);
        Assert.Empty(run.Calls);run.Source.Submitter="SENTETIK-submitter";
        Assert.Equal("Approved",(await run.Host.DecideAsync(kind,Item,new("7",true,null),"key",default)).State);
    }
    [Theory]
    [InlineData(WorkDecisionHostV1.SalesInvoice)]
    [InlineData(WorkDecisionHostV1.SalesSubcontractInvoice)]
    public async Task Invoice_price_co_permission_is_freshly_required_before_any_owner_command(string kind)
    {
        var run=new Run(kind);run.Raw.Denied=SalesPermissionManifestV1.OrdersPriceRead;
        Assert.Equal(403,(await Assert.ThrowsAsync<WorkDecisionException>(()=>run.Host.DecideAsync(run.Source.Kind,Item,new("7",true,null),"key",default))).Status);
        Assert.Empty(run.Calls);
    }
    [Theory]
    [InlineData(WorkDecisionHostV1.SalesInvoice)]
    [InlineData(WorkDecisionHostV1.SalesSubcontractInvoice)]
    public async Task A_delegation_revoked_inside_the_native_command_is_seen_at_its_next_authority_fence(string kind)
    {
        var run=new Run(kind);run.BeforeFence=()=>run.Delegation.Allowed=false;
        Assert.Equal(403,(await Assert.ThrowsAsync<WorkDecisionException>(()=>run.Host.DecideAsync(run.Source.Kind,Item,new("7",true,null,Guid.NewGuid()),"key",default))).Status);
        Assert.False(run.NativeEffect);Assert.True(run.Delegation.Reads>=2);
    }
    private sealed class Run
    {
        public readonly RawAuthority Raw=new();public readonly Source Source;public readonly Delegation Delegation=new();
        public readonly List<(string Method,Guid Id,SalesDraftStateCommandRequest Body,SalesCommandMetadata Metadata)> Calls=[];
        public Action? BeforeFence;public bool NativeEffect;public readonly WorkDecisionHostV1 Host;
        public Run(string kind)
        {
            Source=new(kind);var company=new CompanyContext();company.Set(Company);var correlation=new CorrelationContext();correlation.Set("SENTETIK-correlation",null,"api",null,null);
            var fence=new WorkDecisionExecutionFenceV1();var authority=new WorkDecisionAuthorityV1(Raw,fence);
            var orders=DispatchProxy.Create<ISalesDraftService,NativeOwnerProxy>();var invoices=DispatchProxy.Create<ISalesInvoiceService,NativeOwnerProxy>();
            var subcontract=DispatchProxy.Create<ISalesSubcontractInvoiceLifecycleServiceV1,NativeOwnerProxy>();
            async Task<T> Answer<T>(MethodInfo method,object?[] args,Func<bool,T> result)
            {
                Calls.Add((method.Name,(Guid)args[0]!, (SalesDraftStateCommandRequest)args[1]!, (SalesCommandMetadata)args[2]!));
                BeforeFence?.Invoke();
                if(!(await authority.RevalidatePermissionScopeAsync(Source.ApproverPermission,Company)).IsGranted)throw new SalesDraftServiceException(403,"forbidden");
                if((kind is WorkDecisionHostV1.SalesInvoice or WorkDecisionHostV1.SalesSubcontractInvoice)&&!(await authority.RevalidatePermissionScopeAsync(SalesPermissionManifestV1.OrdersPriceRead,Company)).IsGranted)throw new SalesDraftServiceException(403,"forbidden");
                NativeEffect=true;return result(method.Name.StartsWith("Approve",StringComparison.Ordinal));
            }
            ((NativeOwnerProxy)(object)orders).Handler=(method,args)=>Answer(method,args,OrderResult);
            ((NativeOwnerProxy)(object)invoices).Handler=(method,args)=>Answer(method,args,InvoiceResult);
            ((NativeOwnerProxy)(object)subcontract).Handler=(method,args)=>Answer(method,args,SubcontractResult);
            Host=new(company,new User(),correlation,authority,[Source],Delegation,fence,null!,null!,orders,invoices,subcontractInvoices:subcontract);
        }
    }
    public class NativeOwnerProxy:DispatchProxy
    {
        public Func<MethodInfo,object?[],object?> Handler {get;set;}=(_,_)=>throw new NotSupportedException();
        protected override object? Invoke(MethodInfo? targetMethod,object?[]? args)=>Handler(targetMethod!,args!);
    }
    private static SalesOrderDraftResponse OrderResult(bool approve)=>new(new(new(Item,Company,"SENTETIK order","Order","Draft","9","Rev.0",Company,Company,Company,
        new DateOnly(2026,11,27),DateTimeOffset.UtcNow,1,approve?"Approved":"Rejected"),null,null,[],null,null,null,null,null,null,
        new("SENTETIK-submitter",DateTimeOffset.UtcNow,Actor,DateTimeOffset.UtcNow,null),null!,false,false),true,"SENTETIK-original");
    private static SalesInvoiceCommandResult InvoiceResult(bool approve)=>new(new(Item,"SENTETIK invoice",approve?"Approved":"Rejected","9",new DateOnly(2026,11,27),
        Company,Company,Company,2,null,null!,null,[],[],[],"SENTETIK-maker",DateTimeOffset.UtcNow),true,"SENTETIK-original");
    private static SalesSubcontractServiceInvoiceCommandResult SubcontractResult(bool approve)=>new(new(Item,"SENTETIK subcontract",approve?"Approved":"Rejected","9",new DateOnly(2026,11,27),
        Company,Company,Company,2,Company,Company,Company,1,"10",null!,[],"SENTETIK-maker",DateTimeOffset.UtcNow,true),true,"SENTETIK-original");
    private sealed class RawAuthority:IErpAuthorityRevalidationService
    {
        public string? Denied;public List<string> Asked=[];
        public Task<PermissionScope> RevalidatePermissionScopeAsync(string permission,Guid companyId,CancellationToken cancellationToken=default)
        {Asked.Add(permission);return Task.FromResult(permission==Denied?PermissionScope.Denied:new PermissionScope(true,true,new HashSet<string>(),new HashSet<Guid>(),new HashSet<Guid>(),new HashSet<Guid>()));}
    }
    private sealed class Source(string kind):IWorkApprovalDecisionSourceV1
    {
        public bool Pending=true;public string Maker="SENTETIK-maker",Submitter="SENTETIK-submitter";
        public string Module=>"sales";public string Kind=>kind;
        public string ApproverPermission=>kind==WorkDecisionHostV1.SalesOrder?SalesPermissionManifestV1.OrdersApprove:SalesPermissionManifestV1.InvoicesApprove;
        public Task<WorkApprovalDecisionSnapshotV1?> ReadDecisionAsync(WorkInboxSourceContextV1 context,Guid itemId,CancellationToken cancellationToken)=>
            Task.FromResult<WorkApprovalDecisionSnapshotV1?>(new(Company,new(Module,kind,itemId,"SENTETIK",DateTimeOffset.UtcNow,"/onaylarim",Maker,[],[],[Submitter]),"8",Pending));
        public Task<WorkInboxSourceResultV1> ListPendingAsync(WorkInboxSourceContextV1 context,int maxCount,CancellationToken cancellationToken)=>throw new NotSupportedException();
    }
    private sealed class User:ICurrentUser
    {
        public bool IsAuthenticated=>true;public string? UserId=>Actor;public IReadOnlySet<Guid> AllowedCompanyIds {get;}=new HashSet<Guid>([Company]);
        public bool CanAccessCompany(Guid companyId)=>companyId==Company;
    }
    private sealed class Delegation:IWorkDelegationReaderV1
    {
        public bool Allowed=true;public int Reads;
        public Task<WorkDelegationViewV1?> FindForActorAsync(Guid id,WorkInboxItemV1 item,CancellationToken cancellationToken=default)
        {Reads++;return Task.FromResult<WorkDelegationViewV1?>(Allowed?new(id,Company,item.Kind,"sales.invoices.approve",Guid.NewGuid(),Guid.NewGuid(),DateTimeOffset.UtcNow.AddDays(-1),DateTimeOffset.UtcNow.AddDays(1),"1",Guid.NewGuid(),DateTimeOffset.UtcNow,"SENTETIK",null,null,null):null);}
    }
}
