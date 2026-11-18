using GoymenERP.BuildingBlocks.Multitenancy;
using GoymenERP.Modules.Sales.Application;
using GoymenERP.Modules.Sales.Application.Contracts;
using GoymenERP.Modules.Sales.Domain;
using GoymenERP.Modules.Sales.Domain.Invoices;

namespace GoymenERP.Sales.UnitTests;

public sealed partial class SalesDraftServiceTests
{
    [Theory]
    [InlineData("unavailable",503)] [InlineData("not-found",409)] [InlineData("foreign",503)] [InlineData("malformed",503)]
    public async Task Official_cancellation_requires_actual_exact_external_evidence_and_does_not_consume_key_when_pending(string fault,int status)
    {
        var f = new CancellationWorld();
        f.Evidence.Answer = fault switch {
            "unavailable" => new(SalesOfficialCancellationEvidenceOutcomeV1.Unavailable,null),
            "not-found" => new(SalesOfficialCancellationEvidenceOutcomeV1.NotFound,null),
            "foreign" => new(SalesOfficialCancellationEvidenceOutcomeV1.Available,f.Proof with {CompanyId=Guid.NewGuid()}),
            _ => new(SalesOfficialCancellationEvidenceOutcomeV1.Available,null) };
        var error = await Assert.ThrowsAsync<SalesDraftServiceException>(()=>f.Service().RecordAsync(SalesOfficialInvoiceKindV1.Normal,f.Id,new("4","SENTETIK"),Metadata("official-pending")));
        Assert.Equal(status,error.Status); Assert.Equal(0,f.Store.Writes); Assert.Equal(0,f.Store.Refusals);
        f.Evidence.Answer = new(SalesOfficialCancellationEvidenceOutcomeV1.Available,f.Proof);
        var result = await f.Service().RecordAsync(SalesOfficialInvoiceKindV1.Normal,f.Id,new("4","SENTETIK"),Metadata("official-pending"));
        Assert.False(result.IsReplay); Assert.Equal(1,f.Store.Writes);
    }
    [Fact]
    public async Task Official_cancellation_uses_finalize_only_and_replays_original_answer_without_rechecking_external_status()
    {
        var f = new CancellationWorld(); f.World.Authority.Denied.UnionWith([SalesPermissionManifestV1.InvoicesRead,SalesPermissionManifestV1.OrdersPriceRead,SalesPermissionManifestV1.InvoicesWrite]);
        var first = await f.Service().RecordAsync(SalesOfficialInvoiceKindV1.Normal,f.Id,new("4","SENTETIK"),Metadata("official-record"));
        f.Store.Replay=first with {IsReplay=true}; f.Evidence.Answer=new(SalesOfficialCancellationEvidenceOutcomeV1.Unavailable,null);
        var again=await f.Service().RecordAsync(SalesOfficialInvoiceKindV1.Normal,f.Id,new("4","SENTETIK"),Metadata("official-record"));
        Assert.True(again.IsReplay); Assert.Equal(first.Cancellation,again.Cancellation); Assert.Equal(1,f.Evidence.Calls);
        Assert.All(f.World.Authority.Measured,x=>Assert.Equal(SalesPermissionManifestV1.InvoicesFinalize,x));
    }
    [Theory]
    [InlineData("actor")] [InlineData("company")] [InlineData("permission")]
    public async Task Official_evidence_await_fences_actor_company_and_current_finalize_authority(string drift)
    {
        var f=new CancellationWorld(); f.Evidence.OnRead=()=> { if(drift=="actor")f.World.User.Id=Guid.NewGuid().ToString("D"); else if(drift=="company")f.Company.Set(Guid.NewGuid()); else f.World.Authority.Granted=false; };
        var error=await Assert.ThrowsAsync<SalesDraftServiceException>(()=>f.Service().RecordAsync(SalesOfficialInvoiceKindV1.Normal,f.Id,new("4","SENTETIK"),Metadata("official-drift")));
        Assert.Equal(403,error.Status); Assert.Equal(0,f.Store.Writes); Assert.Equal(0,f.Store.Refusals);
    }
    [Fact]
    public async Task Normal_official_cancellation_preserves_match_approval_amounts_and_original_admission_and_is_terminal()
    {
        var (world,id)=await MatchedInvoiceAsync(null);
        await world.Service().FinalizeInvoiceAsync(id,Command("3"),Metadata("official-domain-final"));
        var invoice=(await world.Invoices.FindInvoiceAsync(id))!; var final=invoice.Finalization; var match=invoice.Match;
        var decision=invoice.Decision; var totals=invoice.Totals; var time=DateTimeOffset.UtcNow.AddMinutes(1);
        var proof=new SalesOfficialInvoiceCancellationEvidenceV1(invoice.CompanyId,id,match!.OfficialNo,match.Ettn,"SENTETIK-status","SENTETIK-record","1",new string('a',64),time.AddSeconds(-1));
        invoice.RecordOfficialCancellation(4,Guid.NewGuid().ToString("D"),"SENTETIK",proof,time);
        Assert.Equal(SalesInvoiceState.OfficiallyCancelled,invoice.State); Assert.False(invoice.HoldsShares);
        Assert.Equal(final,invoice.Finalization); Assert.Equal(match,invoice.Match); Assert.Equal(totals,invoice.Totals);
        Assert.Equal(decision.DecidedBy,invoice.Decision.DecidedBy); Assert.Equal(decision.DecidedAtUtc,invoice.Decision.DecidedAtUtc);
        Assert.Throws<SalesRuleException>(()=>invoice.RecordOfficialCancellation(5,Guid.NewGuid().ToString("D"),"SENTETIK",proof,time));
    }
    // Adversarial company drift tests the service fence; the production context stays immutable.
    private sealed class CancellationCompany : ICompanyContext
    {
        public bool IsResolved => CompanyId != Guid.Empty;
        public Guid CompanyId { get; private set; }
        public void Set(Guid id) => CompanyId = id;
    }
    private sealed class CancellationWorld
    {
        public World World {get;}=new(); public CancellationCompany Company {get;}=new();
        public Guid Id {get;}=Guid.NewGuid(); public CancellationStore Store {get;}=new(); public CancellationEvidence Evidence {get;}=new();
        public SalesOfficialInvoiceCancellationEvidenceV1 Proof {get;}
        public CancellationWorld(){Company.Set(SalesDraftServiceTests.Company);World.User.Id=Guid.NewGuid().ToString("D");
            Proof=new(SalesDraftServiceTests.Company,Id,"SENTETIK-OFFICIAL",Guid.NewGuid().ToString("D"),"SENTETIK-status","SENTETIK-record","1",new string('a',64),DateTimeOffset.UtcNow.AddMinutes(-1));
            Evidence.Answer=new(SalesOfficialCancellationEvidenceOutcomeV1.Available,Proof);
            Store.Target=new(SalesDraftServiceTests.Company,Id,SalesOfficialInvoiceKindV1.Normal,4,Proof.OfficialInvoiceNo,Proof.Ettn,
                new(Guid.NewGuid(),Id,"4",Guid.NewGuid(),Guid.NewGuid(),Guid.NewGuid(),null,[],false,Proof.OfficialInvoiceNo,Proof.Ettn));}
        public SalesOfficialInvoiceCancellationServiceV1 Service()=>new(Company,World.User,World.Authority,new FixedCorrelation(),Store,Evidence);
    }
    private sealed class CancellationEvidence : ISalesOfficialCancellationEvidenceReadPortV1
    {
        public SalesOfficialCancellationEvidenceAnswerV1 Answer {get;set;}=null!; public int Calls{get;private set;} public Action? OnRead{get;set;}
        public Task<SalesOfficialCancellationEvidenceAnswerV1> ReadAsync(Guid companyId,SalesOfficialInvoiceKindV1 kind,Guid invoiceId,string officialInvoiceNo,string ettn,CancellationToken cancellationToken=default){Calls++;OnRead?.Invoke();return Task.FromResult(Answer);}
    }
    private sealed class CancellationStore : ISalesOfficialInvoiceCancellationStoreV1
    {
        public SalesOfficialCancellationTargetV1 Target {get;set;}=null!; public SalesOfficialInvoiceCancellationResultV1? Replay {get;set;}
        public int Writes{get;private set;} public int Refusals{get;private set;}
        public Task<SalesOfficialInvoiceCancellationResultV1?> FindReplayAsync(SalesDraftWriteEnvelope e,string h,string c,Func<CancellationToken,Task> f,CancellationToken t=default)=>Task.FromResult(Replay);
        public Task<SalesOfficialCancellationTargetV1?> ReadTargetAsync(SalesOfficialInvoiceKindV1 k,Guid id,Func<CancellationToken,Task> f,CancellationToken t=default)=>Task.FromResult<SalesOfficialCancellationTargetV1?>(Target);
        public async Task<SalesOfficialInvoiceCancellationResultV1> RecordAsync(SalesDraftWriteEnvelope e,string h,SalesOfficialInvoiceKindV1 k,Guid id,long v,string r,SalesOfficialInvoiceCancellationEvidenceV1 proof,Func<CancellationToken,Task> f,CancellationToken t=default){await f(t);Writes++;return new(new(proof.CompanyId,Guid.NewGuid(),id,k,"5","4",Target.Admission.AdmissionId,proof.OfficialInvoiceNo,proof.Ettn,e.ActorId,DateTimeOffset.UtcNow,r,proof),false,e.CorrelationId);}
        public Task<SalesOfficialCancellationDeliveryV1?> ReadDeliveryAsync(Guid id,Func<CancellationToken,Task> f,CancellationToken t=default)=>throw new NotSupportedException();
        public Task<SalesOfficialCancellationDeliveryV1?> ReadForInvoiceAsync(SalesOfficialInvoiceKindV1 k,Guid id,Func<CancellationToken,Task> f,CancellationToken t=default)=>throw new NotSupportedException();
        public Task<SalesOfficialCancellationCandidatesV1> ListCandidatesAsync(SalesOfficialInvoiceKindV1 k,Guid? id,Func<CancellationToken,Task> f,CancellationToken t=default)=>throw new NotSupportedException();
        public Task<SalesOfficialInvoiceCancellationResultV1> RecordDeliveryAsync(SalesDraftWriteEnvelope e,string h,Guid id,long v,bool release,SalesFinanceCancellationAnswerV1 a,Func<CancellationToken,Task> f,CancellationToken t=default)=>throw new NotSupportedException();
        public Task<SalesOfficialInvoiceCancellationResultV1> RecordRefusalAsync(SalesDraftWriteEnvelope e,string h,string c,SalesDraftTerminalRefusal refusal,Func<CancellationToken,Task> f,CancellationToken t=default){Refusals++;throw new SalesDraftServiceException(refusal.Status,refusal.Code,refusal.Errors);}
    }
}
