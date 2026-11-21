using GoymenERP.Api.Shell;
using GoymenERP.BuildingBlocks.Multitenancy;
using GoymenERP.BuildingBlocks.Security;
using GoymenERP.Modules.Finance.Contracts;
using GoymenERP.Modules.Sales.Application.Contracts;
using GoymenERP.Modules.Sales.Domain.Invoices;
namespace GoymenERP.Api.IntegrationTests;

public sealed class SalesFinanceCancellationAdapterTests
{
    [Theory]
    [InlineData(false)] [InlineData(true)]
    public async Task Explicit_retry_delivers_original_immutable_admission_before_cancellation_and_never_reprices_or_infers_release(bool release)
    {
        var f=new Fixture();var answer=await f.Adapter.CancelAsync(f.Request with {ReleaseActiveAllocations=release});
        Assert.Equal("Cancelled",answer.Status);Assert.Equal(new[]{"admit","cancel"},f.Calls);
        Assert.Equal(f.Source.Admission.AdmissionId,f.Admission.Request!.AdmissionId);Assert.Equal("4",f.Admission.Request.InvoiceVersion);
        Assert.Equal("40.25",f.Admission.Request.ExchangeRate);Assert.Same(f.Source.Admission.Instalments,f.Admission.Request.Instalments);
        Assert.Equal(f.Request.CancellationId,f.Cancel.Request!.CancellationId);Assert.Equal("4",f.Cancel.Request.InvoiceVersion);
        Assert.Equal(release,f.Cancel.Request.ReleaseActiveAllocations);Assert.Equal(FinanceInvoiceProducerV1.Sales,f.Cancel.Request.Producer);
        Assert.All(f.Authority.Permissions,x=>Assert.Equal(SalesPermissionManifestV1.InvoicesFinalize,x));
        Assert.NotEqual(f.Admission.Request.IdempotencyKey,f.Cancel.Metadata!.IdempotencyKey);
    }
    [Fact]
    public async Task Unavailable_original_admission_remains_pending_and_does_not_call_cancellation()
    {
        var f=new Fixture();f.Admission.Answer=SalesFinanceAdmissionAnswerV1.Unavailable;
        Assert.Equal("Pending",(await f.Adapter.CancelAsync(f.Request)).Status);Assert.Equal(new[]{"admit"},f.Calls);Assert.Null(f.Cancel.Request);
    }
    [Fact]
    public async Task A_changed_original_source_or_lost_authority_never_delivers_a_new_finance_effect()
    {
        var f=new Fixture();Assert.Equal("Pending",(await f.Adapter.CancelAsync(f.Request with {Admission=f.Source.Admission with {CurrencyId=Guid.NewGuid()}})).Status);Assert.Empty(f.Calls);
        f.Authority.Allowed=false;await Assert.ThrowsAsync<UnauthorizedAccessException>(()=>f.Adapter.CancelAsync(f.Request));Assert.Empty(f.Calls);
    }
    private sealed class Fixture
    {
        public Guid Company{get;}=Guid.NewGuid();public string Actor{get;}=Guid.NewGuid().ToString("D");public List<string> Calls{get;}=[];
        public Authority Authority{get;}=new();public Admission Admission{get;}public Cancel Cancel{get;}public SourceStore Store{get;}
        public SalesOfficialCancellationDeliveryV1 Source{get;}public SalesFinanceCancellationRequestV1 Request{get;}public SalesFinanceCancellationAdapterV1 Adapter{get;}
        public Fixture(){var invoice=Guid.NewGuid();var ettn=Guid.NewGuid().ToString("D");var id=Guid.NewGuid();var now=DateTimeOffset.UtcNow;
            var admission=new SalesOfficialCancellationAdmissionV1(Guid.NewGuid(),invoice,"4",Guid.NewGuid(),Guid.NewGuid(),Guid.NewGuid(),40.25m,[new(Guid.NewGuid(),Guid.NewGuid(),1,new(2026,12,1),"10.01","402.9")],false,"SENTETIK",ettn);
            var view=new SalesOfficialInvoiceCancellationViewV1(Company,id,invoice,SalesOfficialInvoiceKindV1.Normal,"5","4",admission.AdmissionId,"SENTETIK",ettn,Actor,now,"SENTETIK",new(Company,invoice,"SENTETIK",ettn,"SENTETIK-status","record","1",new string('a',64),now.AddMinutes(-1)));
            Source=new(view,admission);Request=new(Company,Actor,"SENTETIK-correlation",null,"SENTETIK-key",id,"SENTETIK",admission,false);Store=new(Source);Admission=new(Calls);Cancel=new(Calls);
            var company=new CompanyContext();company.Set(Company);Adapter=new(Admission,Cancel,Store,new(Authority,company,new User(Actor,Company),Store));}
    }
    private sealed class Authority:IErpAuthorityRevalidationService
    {public bool Allowed{get;set;}=true;public List<string> Permissions{get;}=[];public Task<PermissionScope> RevalidatePermissionScopeAsync(string permission,Guid company,CancellationToken ct=default){Permissions.Add(permission);return Task.FromResult(Allowed?new PermissionScope(true,true,new HashSet<string>(),new HashSet<Guid>(),new HashSet<Guid>(),new HashSet<Guid>()):PermissionScope.Denied);}}
    private sealed class User(string actor,Guid company):ICurrentUser
    {public bool IsAuthenticated=>true;public string UserId=>actor;public IReadOnlySet<Guid> AllowedCompanyIds=>new HashSet<Guid>{company};public bool CanAccessCompany(Guid id)=>id==company;}
    private sealed class Admission(List<string> calls):ISalesFinanceAdmissionPortV1
    {public SalesFinanceAdmissionRequestV1? Request{get;private set;}public SalesFinanceAdmissionAnswerV1 Answer{get;set;}=new(SalesFinanceAdmissionOutcomeV1.Admitted,"admitted");public Task<SalesFinanceAdmissionAnswerV1> AdmitAsync(SalesFinanceAdmissionRequestV1 request,CancellationToken ct){calls.Add("admit");Request=request;return Task.FromResult(Answer);}}
    private sealed class Cancel(List<string> calls):IFinanceInvoiceOpenItemCancellationPortV1
    {public FinanceInvoiceOpenItemCancellationV1? Request{get;private set;}public CommandMetadata? Metadata{get;private set;}public Task<FinanceInvoiceOpenItemCancellationResultV1> CancelAsync(FinanceContext c,CommandMetadata m,FinanceInvoiceOpenItemCancellationV1 request,CancellationToken ct){calls.Add("cancel");Request=request;Metadata=m;return Task.FromResult(new FinanceInvoiceOpenItemCancellationResultV1(CommandOutcome.Succeeded,[],false,c.CorrelationId,[]));}}
    private sealed class SourceStore(SalesOfficialCancellationDeliveryV1 source):ISalesOfficialInvoiceCancellationStoreV1
    {
        public async Task<SalesOfficialCancellationDeliveryV1?> ReadDeliveryAsync(Guid id,Func<CancellationToken,Task> fence,CancellationToken ct=default){await fence(ct);return id==source.Cancellation.CancellationId?source:null;}
        public Task<SalesOfficialCancellationDeliveryV1?> ReadForInvoiceAsync(SalesOfficialInvoiceKindV1 kind,Guid id,Func<CancellationToken,Task> fence,CancellationToken ct=default)=>Task.FromResult<SalesOfficialCancellationDeliveryV1?>(id==source.Cancellation.InvoiceId&&kind==source.Cancellation.Kind?source:null);
        public Task<SalesOfficialCancellationCandidatesV1> ListCandidatesAsync(SalesOfficialInvoiceKindV1 kind,Guid? after,Func<CancellationToken,Task> fence,CancellationToken ct=default)=>throw new NotSupportedException();
        public Task<SalesOfficialInvoiceCancellationResultV1?> FindReplayAsync(SalesDraftWriteEnvelope e,string h,string c,Func<CancellationToken,Task> f,CancellationToken ct=default)=>throw new NotSupportedException();
        public Task<SalesOfficialCancellationTargetV1?> ReadTargetAsync(SalesOfficialInvoiceKindV1 k,Guid id,Func<CancellationToken,Task> f,CancellationToken ct=default)=>throw new NotSupportedException();
        public Task<SalesOfficialInvoiceCancellationResultV1> RecordAsync(SalesDraftWriteEnvelope e,string h,SalesOfficialInvoiceKindV1 k,Guid id,long v,string r,SalesOfficialInvoiceCancellationEvidenceV1 proof,Func<CancellationToken,Task> f,CancellationToken ct=default)=>throw new NotSupportedException();
        public Task<SalesOfficialInvoiceCancellationResultV1> RecordDeliveryAsync(SalesDraftWriteEnvelope e,string h,Guid id,long v,bool release,SalesFinanceCancellationAnswerV1 a,Func<CancellationToken,Task> f,CancellationToken ct=default)=>throw new NotSupportedException();
        public Task<SalesOfficialInvoiceCancellationResultV1> RecordRefusalAsync(SalesDraftWriteEnvelope e,string h,string c,SalesDraftTerminalRefusal r,Func<CancellationToken,Task> f,CancellationToken ct=default)=>throw new NotSupportedException();
    }
}
