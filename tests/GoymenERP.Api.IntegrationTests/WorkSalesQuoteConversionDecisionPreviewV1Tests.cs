using System.Text.Json;
using System.Text.Json.Serialization;
using GoymenERP.Api.WorkRuntime;
using GoymenERP.Modules.Sales.Application.Contracts;

namespace GoymenERP.Api.IntegrationTests;

public sealed class WorkSalesQuoteConversionDecisionPreviewV1Tests
{
    internal static readonly Guid Company=Guid.Parse("5a180000-0000-4000-8000-000000000001"),Id=Guid.Parse("5a180000-0000-4000-8000-000000000002"),
        Quote=Guid.Parse("5a180000-0000-4000-8000-000000000003"),Actor=Guid.Parse("5a180000-0000-4000-8000-000000000004"),Maker=Guid.Parse("5a180000-0000-4000-8000-000000000005");
    internal static readonly DateTimeOffset At=new DateTimeOffset(2026,11,28,9,0,0,TimeSpan.Zero).AddTicks(1);
    internal static readonly JsonSerializerOptions Json=new(JsonSerializerDefaults.Web);

    [Theory] [InlineData("Pending","9")] [InlineData("Approved","9007199254740993")] [InlineData("Rejected","9223372036854775807")]
    public void Human_wire_has_five_fields_full_native_shapes_and_preserves_every_line_version_text_and_100ns_timestamp(string state,string version)
    {
        var d=Facts(state,version);var view=WorkSalesQuoteConversionDecisionPreviewV1.FromNative(d);
        Assert.Equal(JsonSerializer.Serialize(d.Quote,Json),JsonSerializer.Serialize(view.Quote,Json));Assert.Equal(d.Request,view.Request);
        using var json=JsonDocument.Parse(JsonSerializer.Serialize(view,Json));var root=json.RootElement;
        Assert.Equal(5,root.EnumerateObject().Count());Assert.True(root.TryGetProperty("request",out var request));Assert.Equal(18,request.EnumerateObject().Count());
        Assert.True(root.TryGetProperty("quote",out var quote));Assert.Equal(13,quote.EnumerateObject().Count());
        Assert.True(quote.TryGetProperty("quote",out var header));Assert.Equal(14,header.EnumerateObject().Count());
        Assert.True(header.TryGetProperty("version",out var v));Assert.Equal(version,v.GetString());
        Assert.True(request.TryGetProperty("requestedAtUtc",out var time));Assert.Equal(At,time.GetDateTimeOffset());
        Assert.True(quote.TryGetProperty("lines",out var lines));Assert.Equal(2,lines.GetArrayLength());
        Assert.All(lines.EnumerateArray(),line=>{Assert.Equal(15,line.EnumerateObject().Count());Assert.True(line.TryGetProperty("productVersion",out var p));Assert.Equal("9007199254740993",p.GetString());});
        Assert.Equal("0001.2500",view.Quote.Lines[0].Quantity);Assert.DoesNotContain("Digest",root.GetRawText(),StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Receipt",root.GetRawText(),StringComparison.OrdinalIgnoreCase);Assert.DoesNotContain("audit",root.GetRawText(),StringComparison.OrdinalIgnoreCase);
    }

    [Theory] [InlineData("source-digest")] [InlineData("preview-digest")] [InlineData("null-lines")] [InlineData("line-count")] [InlineData("wrong-request")]
    public void Malformed_or_mixed_owner_human_facts_are_unavailable(string fault)
    {
        var d=Facts();d=fault switch {"source-digest"=>d with{SourceFactsDigest=new('a',64)},"preview-digest"=>d with{PreviewDigest=new('b',64)},
            "null-lines"=>d with{Quote=d.Quote with{Lines=null!}},"line-count"=>Seal(d with{Quote=d.Quote with{Quote=d.Quote.Quote with{LineCount=1}}}),
            _=>Seal(d with{Request=d.Request with{SubjectId=Guid.NewGuid()}})};
        Assert.Equal(503,Assert.Throws<WorkDecisionException>(()=>WorkSalesQuoteConversionDecisionPreviewV1.FromNative(d)).Status);
    }

    [Theory] [InlineData(true)] [InlineData(false)]
    public void Null_owner_detail_or_quote_is_unavailable_before_native_digest(bool nullDetail)
    {
        SalesQuoteConversionDecisionDetailV1 malformed = nullDetail ? null! : Facts() with { Quote = null! };
        Assert.Equal(503,Assert.Throws<WorkDecisionException>(()=>WorkSalesQuoteConversionDecisionPreviewV1.FromNative(malformed)).Status);
    }

    [Fact]
    public void Human_projection_detaches_all_provider_owned_collection_views()
    {
        var d=Facts("Approved");var lines=d.Quote.Lines.ToList();var requests=new List<Guid>{Guid.NewGuid()};var revisions=d.Quote.Revisions.ToList();var approvals=d.Quote.ExpiryApprovals.ToList();
        d=Seal(d with{Quote=d.Quote with{Lines=lines,RequestIds=requests,Revisions=revisions,ExpiryApprovals=approvals}});
        var view=WorkSalesQuoteConversionDecisionPreviewV1.FromNative(d);var before=JsonSerializer.Serialize(view,Json);
        lines.Clear();requests.Clear();revisions.Clear();approvals.Clear();Assert.Equal(before,JsonSerializer.Serialize(view,Json));Assert.Equal(2,view.Quote.Lines.Count);
    }

    [Fact]
    public void Strict_endpoint_selection_preserves_Rev0_and_requires_all_four_frozen_fields_without_changing_legacy_body()
    {
        var strict=new JsonSerializerOptions(Json){UnmappedMemberHandling=JsonUnmappedMemberHandling.Disallow,RespectRequiredConstructorParameters=true};
        var request=new WorkDecisionRequestV1("9",true,null){SalesQuoteConversion=new(Quote,3,0,"9007199254740993")};
        var wire=JsonSerializer.Serialize(request,strict);var restored=JsonSerializer.Deserialize<WorkDecisionRequestV1>(wire,strict)!;
        Assert.Equal(request,restored);Assert.Equal(0,restored.SalesQuoteConversion!.SubjectRevision);
        foreach(var name in new[]{"quoteId","requestNo","subjectRevision","requestVersion"})
        {
            var node=System.Text.Json.Nodes.JsonNode.Parse(wire)!;node["salesQuoteConversion"]!.AsObject().Remove(name);
            Assert.Throws<JsonException>(()=>JsonSerializer.Deserialize<WorkDecisionRequestV1>(node.ToJsonString(),strict));
        }
        var extra=System.Text.Json.Nodes.JsonNode.Parse(wire)!;extra["salesQuoteConversion"]!["companyId"]=Company.ToString("D");
        Assert.Throws<JsonException>(()=>JsonSerializer.Deserialize<WorkDecisionRequestV1>(extra.ToJsonString(),strict));
        using var legacy=JsonDocument.Parse(JsonSerializer.Serialize(new WorkDecisionRequestV1("1",true,null),strict));
        Assert.Equal(4,legacy.RootElement.EnumerateObject().Count());Assert.False(legacy.RootElement.TryGetProperty("salesQuoteConversion",out _));
    }

    [Fact]
    public void Visible_due_terms_are_detached_and_malformed_null_instalments_are_unavailable()
    {
        var instalments=new List<SalesDueInstalmentV1>{new(1,SalesDueBasisNamesV1.OrderDate,30,"100.00")};
        var d=VisibleFacts();d=Seal(d with{Quote=d.Quote with{DueTerms=new(instalments)}});
        var view=WorkSalesQuoteConversionDecisionPreviewV1.FromNative(d);var json=JsonSerializer.Serialize(view,Json);instalments.Clear();
        Assert.Equal(json,JsonSerializer.Serialize(view,Json));Assert.Single(view.Quote.DueTerms!.Instalments);
        // A malformed owner response cannot be sealed by the strict native JSON writer.
        var malformed=d with{Quote=d.Quote with{DueTerms=new(null!)}};
        Assert.Equal(503,Assert.Throws<WorkDecisionException>(()=>WorkSalesQuoteConversionDecisionPreviewV1.FromNative(malformed)).Status);
    }

    internal static SalesQuoteConversionDecisionDetailV1 VisibleFacts()
    {
        var d=Facts();return Seal(d with{Quote=d.Quote with{DueTermsDisclosure="Visible",
            Lines=d.Quote.Lines.Select(l=>l with{PriceDisclosure=SalesPriceDisclosure.Visible,UnitPrice="00012.3400",PriceUnitId=l.OrderUnitId}).ToArray()}});
    }

    internal static SalesQuoteConversionDecisionDetailV1 Facts(string state="Pending",string quoteVersion="9")
    {
        var request=new SalesApprovalRequestView("QuoteExpiredConversion",Quote,3,"Rev.0",state,state=="Rejected"?"2":"1",null,null,null,null,null,null,"  SENTETIK talep  ",Maker.ToString("D"),At,
            state=="Rejected"?Actor.ToString("D"):null,state=="Rejected"?At:null,state=="Rejected"?"  SENTETIK ret  ":null);
        var header=new SalesQuoteSummaryView(Quote,Company,"SENTETIK-SQ-8","Open",quoteVersion,"Rev.0",Guid.NewGuid(),Guid.NewGuid(),Guid.NewGuid(),new(2026,11,1),new(2026,11,10),At,2,null);
        var lines=Enumerable.Range(1,2).Select(n=>new SalesQuoteLineView(Guid.NewGuid(),n,Guid.NewGuid(),"9007199254740993","SNT-"+n,"Sentetik ürün",Guid.NewGuid(),"KG",
            n==1?"0001.2500":"2.500",null,null,null,SalesPriceDisclosure.Withheld,new(2026,12,1),"SENTETIK satır")).ToArray();
        var quote=new SalesQuoteDetailView(header,[],new(2026,12,1),"SENTETIK teklif",lines,null,null,Maker.ToString("D"),
            [new("Rev.0",header.ValidUntil,Maker.ToString("D"),At)],state=="Approved"?[new("Rev.0",header.ValidUntil!.Value,new(2026,11,28),Actor.ToString("D"),At,null)]:[],new(new(2026,11,28),true,state=="Approved"));
        return Seal(new(Id,request,quote,state=="Pending",state!="Rejected","",""));
    }
    internal static SalesQuoteConversionDecisionDetailV1 Seal(SalesQuoteConversionDecisionDetailV1 d)
    {d=d with{SourceFactsDigest=SalesQuoteDecisionDigestsV1.SourceFactsDigest(d.Request,d.Quote)};return d with{PreviewDigest=SalesQuoteDecisionDigestsV1.PreviewDigest(d)};}
}
