using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using GoymenERP.Api.WorkRuntime;
using GoymenERP.Modules.Accounting.Application;
using static GoymenERP.Api.IntegrationTests.InventoryCostWorkDecisionHostTests;
namespace GoymenERP.Api.IntegrationTests;

// Transport whitelist only. These deliberately broad values do not claim native approval or SQL acceptance.
public sealed class WorkSourceVoucherDecisionPreviewV1Tests
{
    [Theory] [InlineData(1L)] [InlineData(9007199254740993L)] [InlineData(long.MaxValue)]
    public void Every_Int64_is_lossless_text_and_all_other_twenty_five_native_fields_remain_identical(long version)
    {
        var source=SourceVoucherWorkDecisionHostTests.Facts(version,version);var v=source.StoredVoucher with{Version=version};
        var detail=Detail(source with{StoredVoucher=v,CurrentVoucher=v});var before=JsonSerializer.Serialize(v,Json);
        var human=WorkSourceVoucherPreviewV1.FromNative(detail);var wire=JsonSerializer.SerializeToNode(human,Json)!;
        var expected=JsonSerializer.SerializeToNode(v,Json)!.AsObject();
        foreach(var field in new[]{"finalizedVersion","version","mappingVersion"})expected[field]=version.ToString(CultureInfo.InvariantCulture);
        Assert.True(JsonNode.DeepEquals(expected,wire["voucher"]));Assert.Equal(4,wire.AsObject().Count);
        Assert.Equal(before,JsonSerializer.Serialize(v,Json));Assert.Equal(version,JsonSerializer.SerializeToElement(v,Json).GetProperty("version").GetInt64());
        Assert.Equal(version.ToString(CultureInfo.InvariantCulture),human.Voucher.FinalizedVersion);Assert.Equal(human.Voucher.FinalizedVersion,human.Voucher.MappingVersion);Assert.Equal(human.Voucher.MappingVersion,human.Voucher.Version);
        Assert.Equal(detail.CreatedAtUtc,human.CreatedAtUtc);Assert.False(human.SourceCancelled);Assert.True(human.PeriodOpen);
    }
    [Fact]
    public void Optional_cancellation_and_reversal_keep_all_sixteen_public_fields_and_native_Int32_version()
    {
        var source=SourceVoucherWorkDecisionHostTests.Facts();var reversal=new AccountingJournalReversalProposalViewV1(Guid.NewGuid(),Company,Id,Guid.NewGuid(),Guid.NewGuid(),
            new(1,1,2),new(2026,11,28),new(9999,12,31),"SENTETIK full reason",Maker,At,"Approved",Actor,At.AddMinutes(1),null,int.MaxValue);
        var v=source.StoredVoucher with{CancellationId=Guid.NewGuid(),CancellationReason="SENTETIK source cancelled",ReversalProposal=reversal};
        var human=WorkSourceVoucherPreviewV1.FromNative(Detail(source with{CurrentVoucher=v,SourceCancelled=true}));
        var wire=JsonSerializer.SerializeToElement(human,Json);var nested=wire.GetProperty("voucher").GetProperty("reversalProposal");
        Assert.Equal(25,wire.GetProperty("voucher").EnumerateObject().Count());Assert.Equal(16,nested.EnumerateObject().Count());
        Assert.Equal(int.MaxValue,nested.GetProperty("version").GetInt32());
        Assert.True(JsonNode.DeepEquals(JsonSerializer.SerializeToNode(reversal,Json),JsonSerializer.SerializeToNode(human.Voucher.ReversalProposal,Json)));
        Assert.Equal(v.CancellationId,human.Voucher.CancellationId);Assert.Equal(v.CancellationReason,human.Voucher.CancellationReason);
    }
    [Fact]
    public void Full_lines_and_raw_money_are_not_truncated_reformatted_or_replaced_by_private_proofs()
    {
        var source=SourceVoucherWorkDecisionHostTests.Facts();var v=source.StoredVoucher with{Lines=Enumerable.Range(1,137).Select(n=>new AccountingSourceVoucherLineViewV1(n,"SENTETIK mapping "+n,n%2+1,Guid.NewGuid(),"-1.2300",int.MaxValue,"20","120","SENTETIK full account")).ToArray(),
            FunctionalPayable="120.0000",ExchangeRate="1.0000",DocumentDate=new(1,1,2),DueDate=new(9999,12,31)};
        v=v with{ApprovalReceipt=new(Company,Id,v.SourceDocumentId,v.InvoiceId,Maker,At,"SENTETIK PRIVATE COMMAND",new('A',64),new('B',64),Actor,1,2,Id,"SENTETIK PRIVATE CORRELATION",At,new('C',64),new('D',64))};
        var human=WorkSourceVoucherPreviewV1.FromNative(Detail(source with{CurrentVoucher=v}));var wire=JsonSerializer.SerializeToElement(human,Json);
        Assert.Equal(137,human.Voucher.Lines.Count);Assert.All(wire.GetProperty("voucher").GetProperty("lines").EnumerateArray(),line=>{
            Assert.Equal(9,line.EnumerateObject().Count());Assert.Equal("-1.2300",line.GetProperty("functionalAmount").GetString());Assert.Equal(int.MaxValue,line.GetProperty("sourceLineNumber").GetInt32());});
        Assert.Equal("120.0000",human.Voucher.FunctionalPayable);Assert.Equal("1.0000",human.Voucher.ExchangeRate);
        var text=wire.GetRawText();foreach(var forbidden in new[]{"approvalReceipt","sourceFactsDigest","previewDigest","PRIVATE","sourceVoucherProof"})Assert.DoesNotContain(forbidden,text,StringComparison.OrdinalIgnoreCase);
        Assert.Equal(23,wire.GetProperty("voucher").EnumerateObject().Count());Assert.False(wire.GetProperty("voucher").TryGetProperty("cancellationId",out _));
        Assert.False(wire.GetProperty("voucher").TryGetProperty("reversalProposal",out _));
    }
    [Fact]
    public void Public_request_and_response_keep_existing_four_and_six_fields_without_human_or_private_metadata()
    {
        var request=new WorkDecisionRequestV1("1",true,null);var body=JsonSerializer.SerializeToElement(request,Json);
        Assert.Equal(new[]{"expectedVersion","approve","reason","delegationId"},body.EnumerateObject().Select(p=>p.Name));
        Assert.Equal(JsonValueKind.Null,body.GetProperty("reason").ValueKind);Assert.Equal(JsonValueKind.Null,body.GetProperty("delegationId").ValueKind);
        var result=new WorkDecisionResultV1(Company,"accounting.source-voucher.v1",Id,"Posted",true,"SENTETIK original");
        Assert.Equal(6,JsonSerializer.SerializeToElement(result,Json).EnumerateObject().Count());
        var generic=new WorkDecisionPreviewV1(Company,"sales.order.v1",Id,"SENTETIK","1",true,false,[],[]);
        Assert.DoesNotContain("sourceVoucher",JsonSerializer.Serialize(generic,Json),StringComparison.OrdinalIgnoreCase);
    }
    private static AccountingSourceVoucherDecisionDetailV1 Detail(AccountingSourceVoucherDecisionSourceV1 source)=>new(source.CurrentVoucher,source.CreatedAtUtc,source.SourceCancelled,source.PeriodOpen,new('E',64),new('F',64));
}
