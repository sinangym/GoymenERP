using System.Globalization;
using System.Text.Json;
using GoymenERP.BuildingBlocks.Work;
using GoymenERP.Modules.Sales.Application.Contracts;
using GoymenERP.Modules.Sales.Infrastructure.WorkInbox;
using static GoymenERP.Sales.UnitTests.SalesWorkInboxTestDoubles;

namespace GoymenERP.Sales.UnitTests;

public sealed class SalesQuoteConversionWorkDecisionSourceV1Tests
{
    internal static readonly Guid Id=Guid.Parse("5a1e0000-0000-4000-8000-000000000008");
    internal static readonly Guid Quote=Guid.Parse("5a1e0000-0000-4000-8000-000000000108");

    [Theory]
    [InlineData("Pending",true,true)] [InlineData("Approved",false,true)] [InlineData("Rejected",false,false)] [InlineData("Stale",false,true)]
    public async Task Exact_selected_source_separates_queue_membership_actions_and_quote_vs_request_versions(string state,bool approve,bool reject)
    {
        var detail=Detail(state);var owner=new Reader(detail);var authority=ApproveAuthority();var store=new SalesWorkInboxApprovalFakeStore();
        var source=new SalesQuoteConversionRequestWorkInboxSourceV1(store,new CompanyContext(Company),new User(Maker,Company),authority,owner);
        var snapshot=Assert.IsType<WorkApprovalDecisionSnapshotV1>(await source.ReadDecisionAsync(Context(Maker),Id,default));
        Assert.Equal(("9",state=="Pending"),(snapshot.SourceVersion,snapshot.Pending));var p=Assert.IsType<WorkSalesQuoteConversionProofV1>(snapshot.SalesQuoteConversionProof);
        Assert.Equal((detail.Request.Version,"9",approve,reject),(p.RequestVersion,p.QuoteVersion,p.ApprovalSupported,p.RejectionSupported));
        Assert.Equal((Id,Quote,3,0),(p.RequestId,p.QuoteId,p.RequestNo,p.SubjectRevision));Assert.True(SalesQuoteConversionDecisionBindingV1.MatchesDecisionSnapshot(snapshot,detail));
        Assert.Null(snapshot.Item.PreparedBy);Assert.Null(snapshot.Item.ExcludedDeciders);Assert.False(snapshot.Item.Excludes(Maker));
        Assert.Equal($"/satis/teklifler/{Quote:D}",snapshot.Item.Link);Assert.Equal(Quote,Assert.Single(snapshot.Item.References).RecordId);
        Assert.Empty(store.Calls);Assert.Equal(1,owner.Reads);Assert.Equal(0,owner.Commands);
        Assert.All(authority.Asked,p=>Assert.Equal(SalesPermissionManifestV1.QuotesExpiredConversionApprove,p));
        using var json=JsonDocument.Parse(JsonSerializer.Serialize(snapshot,SalesDraftJson.Options));
        Assert.False(json.RootElement.TryGetProperty("salesQuoteConversionProof",out _));Assert.DoesNotContain(detail.PreviewDigest,json.RootElement.GetRawText());
    }

    [Fact]
    public async Task Pending_list_keeps_legacy_constructor_formatter_and_no_selected_owner_fallback()
    {
        var detail=Detail("Pending");var store=new SalesWorkInboxApprovalFakeStore{QuoteRequests=[new(Id,Company,Quote,3,"SENTETIK-SQ-8",Maker,T0)]};
        var source=new SalesQuoteConversionRequestWorkInboxSourceV1(store,new CompanyContext(Company),new User(Approver,Company),ApproveAuthority());
        var list=await source.ListPendingAsync(Context(),10,default);var item=Assert.Single(list.Items!);
        Assert.True(SalesQuoteConversionDecisionBindingV1.MatchesDecisionItem(item,detail));Assert.Single(store.Calls);
        await Assert.ThrowsAsync<InvalidOperationException>(()=>source.ReadDecisionAsync(Context(),Id,default));Assert.Single(store.Calls);
    }

    [Theory]
    [InlineData("Denied")] [InlineData("NotFound")] [InlineData("Unavailable")] [InlineData("null")]
    [InlineData("company")] [InlineData("id")] [InlineData("quote-id")] [InlineData("flags")]
    [InlineData("digest")] [InlineData("partial-lines")] [InlineData("request-version")] [InlineData("revision")]
    [InlineData("price-withheld")] [InlineData("foreign-kind")]
    public async Task Selected_source_rejects_inconsistent_or_unavailable_native_owner_facts(string fault)
    {
        var detail=Detail("Pending");
        if(fault=="company")detail=Reseal(detail with {Quote=detail.Quote with {Quote=detail.Quote.Quote with {CompanyId=OtherCompany}}});
        if(fault=="id")detail=Reseal(detail with {RequestId=Guid.NewGuid()});
        if(fault=="quote-id")detail=Reseal(detail with {Request=detail.Request with {SubjectId=Guid.NewGuid()}});
        if(fault=="flags")detail=Reseal(detail with {RejectionSupported=false});
        if(fault=="digest")detail=detail with {SourceFactsDigest=new('a',64)};
        if(fault=="partial-lines")detail=Reseal(detail with {Quote=detail.Quote with {Lines=[]}});
        if(fault=="request-version")detail=Reseal(detail with {Request=detail.Request with {Version="01"}});
        if(fault=="revision")detail=Reseal(detail with {Request=detail.Request with {SubjectRevision="Rev.01"}});
        if(fault=="price-withheld")detail=Reseal(detail with {Quote=detail.Quote with {Lines=[detail.Quote.Lines[0] with {UnitPrice="12.50"}]}});
        if(fault=="foreign-kind")detail=Reseal(detail with {Request=detail.Request with {Kind=SalesApprovalRequestApiV1.KindOverDelivery}});
        var owner=new Reader(detail);if(Enum.TryParse<SalesQuoteDecisionReadStateV1>(fault,out var state))owner.Answer=new(state,null);if(fault=="null")owner.Answer=null!;
        var source=Source(owner);
        if(fault=="NotFound")Assert.Null(await source.ReadDecisionAsync(Context(),Id,default));
        else if(fault=="Denied")await Assert.ThrowsAsync<UnauthorizedAccessException>(()=>source.ReadDecisionAsync(Context(),Id,default));
        else await Assert.ThrowsAsync<InvalidOperationException>(()=>source.ReadDecisionAsync(Context(),Id,default));
        Assert.Equal(1,owner.Reads);Assert.Equal(0,owner.Commands);
    }

    [Theory]
    [InlineData("permission")] [InlineData("company")] [InlineData("actor")] [InlineData("scoped")]
    public async Task Selected_source_requires_only_actual_companywide_approve_before_owner_read(string fault)
    {
        var owner=new Reader(Detail("Pending"));var context=Context();
        if(fault=="company")context=context with {CompanyId=OtherCompany};if(fault=="actor")context=context with {UserId=Maker};
        if(fault=="scoped")context=context with {GrantScope=new(new HashSet<Guid>(),new HashSet<Guid>(),new HashSet<Guid>())};
        var source=new SalesQuoteConversionRequestWorkInboxSourceV1(new SalesWorkInboxApprovalFakeStore(),new CompanyContext(Company),new User(Approver,Company),
            new Authority(p=>fault!="permission" && p==SalesPermissionManifestV1.QuotesExpiredConversionApprove),owner);
        await Assert.ThrowsAsync<UnauthorizedAccessException>(()=>source.ReadDecisionAsync(context,Id,default));Assert.Equal(0,owner.Reads);Assert.Equal(0,owner.Commands);
    }

    [Theory]
    [InlineData("permission")] [InlineData("company")] [InlineData("cancel")]
    public async Task Normal_completed_owner_read_cannot_release_after_revocation_context_switch_or_cancellation(string fault)
    {
        var company=new CompanyContext(Company);var granted=true;using var cancellation=new CancellationTokenSource();
        var owner=new Reader(Detail("Pending")){AfterRead=()=>{if(fault=="permission")granted=false;if(fault=="company")company.Set(OtherCompany);if(fault=="cancel")cancellation.Cancel();}};
        var source=new SalesQuoteConversionRequestWorkInboxSourceV1(new SalesWorkInboxApprovalFakeStore(),company,new User(Approver,Company),
            new Authority(p=>granted && p==SalesPermissionManifestV1.QuotesExpiredConversionApprove),owner);
        if(fault=="cancel")await Assert.ThrowsAnyAsync<OperationCanceledException>(()=>source.ReadDecisionAsync(Context(),Id,cancellation.Token));
        else await Assert.ThrowsAsync<UnauthorizedAccessException>(()=>source.ReadDecisionAsync(Context(),Id,default));Assert.Equal(1,owner.Reads);
    }

    [Theory]
    [InlineData("caption")] [InlineData("ref-id")] [InlineData("ref-link")] [InlineData("rule")] [InlineData("kind")] [InlineData("maker")]
    public async Task Pure_binding_uses_exact_original_formatter_not_snapshot_text(string fault)
    {
        var detail=Detail("Approved");var snapshot=Assert.IsType<WorkApprovalDecisionSnapshotV1>(await Source(new Reader(detail)).ReadDecisionAsync(Context(),Id,default));var item=snapshot.Item;
        item=fault switch{"caption"=>item with {DocumentLabel="other"},"ref-id"=>item with {References=[item.References[0] with {RecordId=Guid.NewGuid()}]},
            "ref-link"=>item with {References=[item.References[0] with {Link="/satis/talepler"}]},"rule"=>item with {Rules=[item.Rules[0] with {Actual="1"}]},
            "kind"=>item with {Kind="sales.request.v1"},_=>item with {PreparedBy=Maker}};
        Assert.False(SalesQuoteConversionDecisionBindingV1.MatchesDecisionItem(item,detail));Assert.False(SalesQuoteConversionDecisionBindingV1.MatchesDecisionProof(item,snapshot.SalesQuoteConversionProof));
        Assert.False(SalesQuoteConversionDecisionBindingV1.MatchesDecisionSnapshot(snapshot with {Item=item},detail));
    }

    [Fact]
    public async Task Full_same_header_changed_line_body_cannot_replace_frozen_human_digest_and_versions_remain_lossless()
    {
        var detail=Detail("Rejected");detail=Reseal(detail with {Quote=detail.Quote with {Quote=detail.Quote.Quote with {Version=long.MaxValue.ToString(CultureInfo.InvariantCulture)}}});
        var snapshot=Assert.IsType<WorkApprovalDecisionSnapshotV1>(await Source(new Reader(detail)).ReadDecisionAsync(Context(),Id,default));Assert.Equal("9223372036854775807",snapshot.SourceVersion);
        var changed=Reseal(detail with {Quote=detail.Quote with {Lines=[detail.Quote.Lines[0] with {Quantity="0001.25000"}]}});
        Assert.True(SalesQuoteConversionDecisionBindingV1.MatchesDecisionItem(snapshot.Item,changed));Assert.False(SalesQuoteConversionDecisionBindingV1.MatchesDecisionSnapshot(snapshot,changed));
        Assert.False(snapshot.Pending);Assert.False(snapshot.SalesQuoteConversionProof!.ApprovalSupported);Assert.False(snapshot.SalesQuoteConversionProof.RejectionSupported);
        Assert.Equal("9007199254740993",Assert.Single(detail.Quote.Lines).ProductVersion);
    }

    internal static SalesQuoteConversionDecisionDetailV1 Detail(string state)
    {
        var request=new SalesApprovalRequestView("QuoteExpiredConversion",Quote,3,"Rev.0",state,state=="Rejected"?"2":"1",null,null,null,null,null,null,"SENTETIK istek",Maker,T0,
            state=="Rejected"?Approver:null,state=="Rejected"?T0:null,state=="Rejected"?"  SENTETIK ret  ":null);
        var h=new SalesQuoteSummaryView(Quote,Company,"SENTETIK-SQ-8","Open","9",state=="Stale"?"Rev.1":"Rev.0",Guid.NewGuid(),Guid.NewGuid(),Guid.NewGuid(),new(2026,11,1),new(2026,11,10),T0,1,null);
        var line=new SalesQuoteLineView(Guid.NewGuid(),1,Guid.NewGuid(),"9007199254740993","SNT","Sentetik ürün",Guid.NewGuid(),"KG","0001.2500",null,null,null,SalesPriceDisclosure.Withheld,null,"SENTETIK satır");
        SalesQuoteRevisionRecordV1[] revisions=state=="Stale"?[new("Rev.0",h.ValidUntil,Maker,T0),new("Rev.1",h.ValidUntil,Maker,T0)]:[new("Rev.0",h.ValidUntil,Maker,T0)];
        var quote=new SalesQuoteDetailView(h,[],null,null,[line],null,null,Maker,revisions,state=="Approved"?[new("Rev.0",h.ValidUntil!.Value,new(2026,11,20),Approver,T0,null)]:[],
            new(new(2026,11,20),true,state=="Approved"));
        return Reseal(new(Id,request,quote,state=="Pending",state!="Rejected","",""));
    }
    internal static SalesQuoteConversionDecisionDetailV1 Reseal(SalesQuoteConversionDecisionDetailV1 detail)
    {var d=detail with {SourceFactsDigest=SalesQuoteDecisionDigestsV1.SourceFactsDigest(detail.Request,detail.Quote)};return d with {PreviewDigest=SalesQuoteDecisionDigestsV1.PreviewDigest(d)};}
    internal static SalesQuoteConversionRequestWorkInboxSourceV1 Source(Reader owner)=>new(new SalesWorkInboxApprovalFakeStore(),new CompanyContext(Company),new User(Approver,Company),ApproveAuthority(),owner);
    private static Authority ApproveAuthority()=>new(p=>p==SalesPermissionManifestV1.QuotesExpiredConversionApprove);
    // Explicit controlled owner protocol. These success replies are not proof of SQL/Audit acceptance.
    internal sealed class Reader(SalesQuoteConversionDecisionDetailV1 detail):ISalesQuoteConversionDecisionReadV1
    {
        internal int Reads;internal int Commands;internal Action? AfterRead;internal SalesQuoteDecisionReadResultV1 Answer=new(SalesQuoteDecisionReadStateV1.Found,detail);
        public async Task<SalesQuoteDecisionReadResultV1> ReadQuoteConversionDecisionAsync(Guid id,Func<CancellationToken,Task> fence,CancellationToken ct=default)
        {await fence(ct);Reads++;AfterRead?.Invoke();await fence(ct);return Answer;}
        public Task<SalesQuoteResponse> ApproveSelectedQuoteConversionAsync(SalesQuoteConversionTargetV1 t,SalesQuoteExpiryApprovalRequest r,SalesCommandMetadata m,CancellationToken ct=default)
        {Commands++;throw new InvalidOperationException("A source read cannot mutate.");}
        public Task<SalesApprovalRequestCommandResult> RejectSelectedQuoteConversionAsync(SalesQuoteConversionTargetV1 t,SalesDraftStateCommandRequest r,SalesCommandMetadata m,CancellationToken ct=default)
        {Commands++;throw new InvalidOperationException("A source read cannot mutate.");}
    }
}
