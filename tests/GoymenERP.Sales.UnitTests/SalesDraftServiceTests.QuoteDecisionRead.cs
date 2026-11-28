using GoymenERP.BuildingBlocks.Multitenancy;
using GoymenERP.Modules.Sales.Application;
using GoymenERP.Modules.Sales.Application.Contracts;
using GoymenERP.Modules.Sales.Domain.ApprovalRequests;
using GoymenERP.Modules.Sales.Domain.Drafts;
using GoymenERP.Modules.Sales.Domain.Quotes;

namespace GoymenERP.Sales.UnitTests;

public sealed partial class SalesDraftServiceTests
{
    [Theory]
    [InlineData("Pending",true,true)] [InlineData("Approved",false,true)] [InlineData("Rejected",false,false)]
    public async Task Selected_quote_read_keeps_request_and_quote_versions_separate_and_uses_only_existing_price_disclosure(string state,bool approve,bool reject)
    {
        var (world,source)=await QuoteHumanSource(state);
        world.Authority.Denied.Add(SalesPermissionManifestV1.QuotesRead);world.Authority.Denied.Add(PriceReadPermission);
        world.Authority.Measured.Clear();var reader=new PurposeQuoteStore(world.Quotes!,source);var calls=0;
        var result=await QuotePurposeService(world,reader).ReadQuoteConversionDecisionAsync(source.Request.Id,_=>{calls++;return Task.CompletedTask;});
        Assert.Equal(SalesQuoteDecisionReadStateV1.Found,result.State);var human=Assert.IsType<SalesQuoteConversionDecisionDetailV1>(result.Detail);
        Assert.Equal((state,approve,reject),(human.Request.Status,human.ApprovalSupported,human.RejectionSupported));
        Assert.Equal(source.Request.Version.ToString(System.Globalization.CultureInfo.InvariantCulture),human.Request.Version);
        Assert.Equal(source.Quote.Version.ToString(System.Globalization.CultureInfo.InvariantCulture),human.Quote.Quote.Version);
        Assert.Equal(source.Request.Id,human.RequestId);Assert.Equal(source.Quote.Id,human.Request.SubjectId);
        var line=Assert.Single(human.Quote.Lines);Assert.Equal(("1.25",(string?)null,(Guid?)null,(string?)null,SalesPriceDisclosure.Withheld),
            (line.Quantity,line.UnitPrice,line.PriceUnitId,line.DiscountPercent,line.PriceDisclosure));
        Assert.Null(human.Quote.DueTerms);Assert.Equal(6,calls);Assert.Equal(1,reader.Reads);
        Assert.DoesNotContain(SalesPermissionManifestV1.QuotesRead,world.Authority.Measured);
        Assert.Equal(SalesQuoteDecisionDigestsV1.SourceFactsDigest(human.Request,human.Quote),human.SourceFactsDigest);
        Assert.Equal(SalesQuoteDecisionDigestsV1.PreviewDigest(human),human.PreviewDigest);
        Assert.Equal(Actor,source.Request.RequestedBy); // SAT-D44: same preparer may read and decide; no new SoD.
    }

    [Theory]
    [InlineData(1)] [InlineData(2)] [InlineData(3)] [InlineData(4)] [InlineData(5)] [InlineData(6)]
    public async Task Selected_quote_each_consumer_completion_rechecks_authority_and_cancellation(int boundary)
    {
        foreach(var cancel in new[]{false,true})
        {
            var (world,source)=await QuoteHumanSource("Pending");using var cancellation=new CancellationTokenSource();var calls=0;
            var service=QuotePurposeService(world,new PurposeQuoteStore(world.Quotes!,source));
            Task Fence(CancellationToken _){if(++calls==boundary){if(cancel)cancellation.Cancel();else world.Authority.Denied.Add(SalesPermissionManifestV1.QuotesExpiredConversionApprove);}return Task.CompletedTask;}
            if(cancel)await Assert.ThrowsAnyAsync<OperationCanceledException>(()=>service.ReadQuoteConversionDecisionAsync(source.Request.Id,Fence,cancellation.Token));
            else{var result=await service.ReadQuoteConversionDecisionAsync(source.Request.Id,Fence);Assert.Equal(SalesQuoteDecisionReadStateV1.Denied,result.State);Assert.Null(result.Detail);}
            Assert.Equal(boundary,calls);
        }
    }

    [Theory]
    [InlineData("wrong-id")] [InlineData("missing")] [InlineData("unconfigured")] [InlineData("permission")] [InlineData("company")] [InlineData("actor")]
    public async Task Selected_quote_read_never_substitutes_another_request_or_late_actor(string fault)
    {
        var (world,source)=await QuoteHumanSource("Pending");var reader=new PurposeQuoteStore(world.Quotes!,fault=="missing"?null:source);var calls=0;
        if(fault=="permission")world.Authority.Denied.Add(SalesPermissionManifestV1.QuotesExpiredConversionApprove);
        var service=QuotePurposeService(world,fault=="unconfigured"?world.Quotes!:reader);
        var result=await service.ReadQuoteConversionDecisionAsync(fault=="wrong-id"?Guid.NewGuid():source.Request.Id,_=>
        {if(++calls==6){if(fault=="company")world.User.Company=Guid.NewGuid();if(fault=="actor")world.User.Id="SENTETIK-other";}return Task.CompletedTask;});
        var expected=fault=="missing"?SalesQuoteDecisionReadStateV1.NotFound:fault is "permission" or "company" or "actor"?SalesQuoteDecisionReadStateV1.Denied:SalesQuoteDecisionReadStateV1.Unavailable;
        Assert.Equal(expected,result.State);Assert.Null(result.Detail);
    }

    [Fact]
    public async Task Selected_quote_final_consumer_price_revocation_withholds_prices_without_demanding_QuotesRead()
    {
        var (world,source)=await QuoteHumanSource("Pending");world.Authority.Denied.Add(SalesPermissionManifestV1.QuotesRead);var calls=0;
        var result=await QuotePurposeService(world,new PurposeQuoteStore(world.Quotes!,source)).ReadQuoteConversionDecisionAsync(source.Request.Id,_=>
        {if(++calls==6)world.Authority.Denied.Add(PriceReadPermission);return Task.CompletedTask;});
        Assert.Equal(SalesQuoteDecisionReadStateV1.Found,result.State);var human=Assert.IsType<SalesQuoteConversionDecisionDetailV1>(result.Detail);
        var line=Assert.Single(human.Quote.Lines);Assert.Equal(SalesPriceDisclosure.Withheld,line.PriceDisclosure);
        Assert.Null(line.UnitPrice);Assert.Null(line.PriceUnitId);Assert.Null(line.DiscountPercent);Assert.Null(human.Quote.DueTerms);
        Assert.Equal(SalesQuoteDecisionDigestsV1.SourceFactsDigest(human.Request,human.Quote),human.SourceFactsDigest);
        Assert.Equal(SalesQuoteDecisionDigestsV1.PreviewDigest(human),human.PreviewDigest);Assert.Equal(6,calls);
    }

    [Fact]
    public async Task Selected_quote_initial_cancel_never_reads_any_port()
    {
        var world=new World();var reader=new PurposeQuoteStore(new FakeQuoteStore(world.Store,world.Requests,()=>world.Clock),null);
        using var cancellation=new CancellationTokenSource();cancellation.Cancel();var calls=0;
        await Assert.ThrowsAnyAsync<OperationCanceledException>(()=>QuotePurposeService(world,reader).ReadQuoteConversionDecisionAsync(Guid.NewGuid(),_=>{calls++;return Task.CompletedTask;},cancellation.Token));
        Assert.Equal(0,calls);Assert.Equal(0,reader.Reads);Assert.Equal(0,world.Authority.Calls);
    }

    private static async Task<(World World,SalesQuoteDecisionSourceV1 Source)> QuoteHumanSource(string state)
    {
        var world=new World{Clock=QuoteClock()};var quoteId=(await world.Service().CreateQuoteAsync(QuoteOf(),Metadata("human-quote"))).Detail.Quote.Id;
        world.Clock=new MutableClock(new DateTimeOffset(2026,10,25,9,0,0,TimeSpan.Zero));
        var selected=SalesApprovalRequest.RequestQuoteConversion(Guid.NewGuid(),Company,quoteId,1,0,"SENTETIK",Actor,world.Clock.GetUtcNow());
        if(state=="Approved")await world.Service().ApproveExpiredQuoteConversionAsync(quoteId,new("1"),Metadata("human-approve"));
        if(state=="Rejected")selected.Reject(1,Actor,"SENTETIK ret",world.Clock.GetUtcNow());
        return(world,new(selected,(await world.Quotes!.FindQuoteAsync(quoteId))!));
    }
    private static SalesDraftService QuotePurposeService(World world,ISalesQuoteStore reader)
    {
        var company=new CompanyContext();company.Set(Company);
        return new(company,world.User,world.Authority,world.ActiveRoleCodes,new FixedCorrelation(),world.Accounts,world.Settings,world.Products,world.Store,world.Bindings,world.Clock,quotes:reader);
    }
    // Synthetic read protocol only. Real SQL command ownership is exercised in the relational source fixture.
    private sealed class PurposeQuoteStore(FakeQuoteStore inner,SalesQuoteDecisionSourceV1? source):ISalesQuoteStore,ISalesQuoteDecisionReadStoreV1
    {
        internal int Reads;
        public async Task<SalesQuoteDecisionSourceV1?> ReadQuoteConversionDecisionAsync(Guid id,Func<CancellationToken,Task> fence,CancellationToken ct=default)
        {await fence(ct);Reads++;await fence(ct);return source;}
        public Task<SalesQuoteStoreResult> StoreQuoteAsync(SalesDraftWriteEnvelope e,string h,Func<string,SalesQuote> b,CancellationToken ct=default)=>inner.StoreQuoteAsync(e,h,b,ct);
        public Task<SalesQuoteReplay?> FindQuoteReplayAsync(SalesDraftWriteEnvelope e,string h,string c,CancellationToken ct=default)=>inner.FindQuoteReplayAsync(e,h,c,ct);
        public Task RecordQuoteRefusalAsync(SalesDraftWriteEnvelope e,string h,string c,Guid? id,SalesDraftTerminalRefusal r,CancellationToken ct=default)=>inner.RecordQuoteRefusalAsync(e,h,c,id,r,ct);
        public Task<SalesQuoteStoreResult?> UpdateQuoteAsync(SalesDraftWriteEnvelope e,string h,string c,Guid id,Action<SalesQuote,DateTimeOffset> m,SalesDraftStateAudit a,CancellationToken ct=default)=>inner.UpdateQuoteAsync(e,h,c,id,m,a,ct);
        public Task<SalesQuote?> FindQuoteAsync(Guid id,CancellationToken ct=default)=>inner.FindQuoteAsync(id,ct);
        public Task<SalesQuotePage> ListQuotesAsync(int size,string? cursor,SalesQuoteState? state,CancellationToken ct=default)=>inner.ListQuotesAsync(size,cursor,state,ct);
        public Task<bool> IsRequestQuotedAsync(Guid id,CancellationToken ct=default)=>inner.IsRequestQuotedAsync(id,ct);
        public Task<SalesDraftStoreResult> StoreConvertedDraftAsync(SalesDraftWriteEnvelope e,string h,Guid id,long v,Func<string,SalesOrderDraft> b,CancellationToken ct=default)=>inner.StoreConvertedDraftAsync(e,h,id,v,b,ct);
    }
}
