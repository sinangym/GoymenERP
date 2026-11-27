using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using GoymenERP.Api.WorkRuntime;
using GoymenERP.Modules.Accounting.Application;

namespace GoymenERP.Api.IntegrationTests;

// Explicit transport shapes only: no SQL, financial calculation or native approval acceptance.
public sealed class WorkInventoryCostDecisionPreviewV1Tests
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    private static readonly string EconomicKey = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(
        System.Text.Encoding.UTF8.GetBytes("SENTETIK-economic")));
    [Theory] [InlineData(1L)] [InlineData(9007199254740993L)] [InlineData(long.MaxValue)]
    public void All_nested_native_longs_are_lossless_Work_text_and_all_other_human_facts_are_unchanged(long version)
    {
        var detail=FullDetail(version);var before=JsonSerializer.Serialize(detail.Proposal,Json);
        var human=WorkInventoryCostPreviewV1.FromNative(detail);var wire=JsonSerializer.SerializeToNode(human,Json)!;
        var expected=JsonSerializer.SerializeToNode(detail.Proposal,Json)!;NormalizeExpected(expected);
        var actual=wire["proposal"]!.DeepClone();RemoveNulls(actual);
        Assert.True(JsonNode.DeepEquals(expected,actual));
        Assert.Equal(2,wire.AsObject().Count);Assert.Equal(detail.PreparedRecordedAtUtc, human.PreparedRecordedAtUtc);
        Assert.Equal(before,JsonSerializer.Serialize(detail.Proposal,Json));
        Assert.Equal(version,JsonSerializer.SerializeToElement(detail.Proposal,Json).GetProperty("version").GetInt64());
        var text=version.ToString(CultureInfo.InvariantCulture);
        Assert.Equal(text,human.Proposal.Version);Assert.Equal(text,human.Proposal.InvoiceVersion);
        Assert.Equal(text,human.Proposal.Preparation!.AccountPlanPackageVersion);Assert.Equal(text,human.Proposal.Preparation.AccountPlanCatalogVersion);
        Assert.Equal(text,human.Proposal.Preparation.Classification.MappingVersion);Assert.Equal(text,human.Proposal.Preparation.Classification.InvoiceLineRevision);
        Assert.Equal(text,human.Proposal.Component.AlreadyPostedComponents![0].MoneyProfileVersion);
        Assert.Equal(text,human.Proposal.Component.AlreadyPostedComponents[0].Shipment.ProvenOriginalSequence);
        Assert.Equal(text,human.Proposal.Component.AlreadyPostedPurchaseComponents![0].Approval.RecordedRounding.MoneyProfileVersion);
        Assert.Equal(text,human.Proposal.Component.ClaimSuccession!.Claims[0].PreviousGeneration);
        Assert.Equal(text,human.Proposal.ComponentCancellation!.MoneyProfiles[0].Version);
        Assert.Equal(text,human.Proposal.StaleClosure!.PreparedVersion);Assert.Equal(text,human.Proposal.RenewedFrom!.PreviousClosedVersion);
        Assert.Equal("-2.0100",human.Proposal.Component.AlreadyPostedComponents[0].SignedAmount);
        Assert.Equal(detail.Proposal.Component.AlreadyPostedComponents[0].ExactSignedAmount,human.Proposal.Component.AlreadyPostedComponents[0].ExactSignedAmount);
    }
    [Fact]
    public void Native_Int32_line_identities_remain_numbers_at_their_exact_upper_bound()
    {
        var detail=FullDetail(9007199254740993L);var c=detail.Proposal.Component;
        c=c with{InvoiceLineNumber=int.MaxValue,SalesPhysicalClaims=c.SalesPhysicalClaims!.Select(x=>x with{MovementLineNumber=int.MaxValue}).ToArray(),
            AlreadyPostedComponents=c.AlreadyPostedComponents!.Select(x=>x with{Shipment=x.Shipment with{MovementLineNumber=int.MaxValue}}).ToArray(),
            AlreadyPostedPurchaseComponents=c.AlreadyPostedPurchaseComponents!.Select(x=>x with{MovementLineNumber=int.MaxValue,
                Candidates=x.Candidates.Select(y=>y with{Shipment=y.Shipment with{MovementLineNumber=int.MaxValue}}).ToArray()}).ToArray()};
        var human=WorkInventoryCostPreviewV1.FromNative(detail with{Proposal=detail.Proposal with{Component=c}});
        var wire=JsonSerializer.SerializeToElement(human.Proposal.Component,Json);
        Assert.Equal(int.MaxValue,wire.GetProperty("invoiceLineNumber").GetInt32());
        Assert.Equal(int.MaxValue,wire.GetProperty("salesPhysicalClaims")[0].GetProperty("movementLineNumber").GetInt32());
        Assert.Equal(int.MaxValue,wire.GetProperty("alreadyPostedComponents")[0].GetProperty("shipment").GetProperty("movementLineNumber").GetInt32());
        Assert.Equal(int.MaxValue,wire.GetProperty("alreadyPostedPurchaseComponents")[0].GetProperty("candidates")[0].GetProperty("shipment").GetProperty("movementLineNumber").GetInt32());
        Assert.Equal("9007199254740993",human.Proposal.Component.AlreadyPostedComponents![0].Shipment.ProvenOriginalSequence);
    }
    [Fact]
    public void Whitelist_keeps_all_rows_but_never_raw_proofs_opaque_digests_or_native_receipt()
    {
        var detail=FullDetail(long.MaxValue);var p=detail.Proposal;
        var receipt=new AccountingInventoryCostApprovalReceiptV1(p.CompanyId,p.ProposalId,p.Kind,p.EventId,p.InvoiceId,p.Preparation!.PreparedBy,
            p.Preparation.PreparedAtUtc,detail.PreparedRecordedAtUtc,AccountingInventoryCostServiceV1.InventoryCostApproveCommand,new('A',64),new('B',64),
            Guid.NewGuid(),7,8,"Posted","SENTETIK PRIVATE RECEIPT",p.SourceOccurredAtUtc,new('C',64),new('D',64));
        p=p with{ApprovalReceipt=receipt,Component=p.Component with{AlreadyPostedComponents=Enumerable.Repeat(p.Component.AlreadyPostedComponents![0],137).ToArray()}};
        var human=WorkInventoryCostPreviewV1.FromNative(detail with{Proposal=p});var raw=JsonSerializer.Serialize(human,Json);
        Assert.Equal(137,human.Proposal.Component.AlreadyPostedComponents!.Count);Assert.Equal(2,JsonSerializer.SerializeToElement(human,Json).EnumerateObject().Count());
        foreach(var name in PrivateFields)Assert.DoesNotContain("\""+name+"\"",raw,StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("sourceFactsDigest",raw,StringComparison.OrdinalIgnoreCase);Assert.DoesNotContain("previewDigest",raw,StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("SENTETIK RAW PRIVATE",raw);Assert.DoesNotContain(receipt.OriginalCorrelationId,raw);Assert.Same(receipt,p.ApprovalReceipt);
        var wire=JsonSerializer.SerializeToElement(human,Json);
        Assert.False(wire.GetProperty("proposal").GetProperty("component").GetProperty("claimSuccession").GetProperty("reversedComponents")[0].TryGetProperty("economicKey",out _));
        Assert.False(wire.GetProperty("proposal").GetProperty("componentCancellation").GetProperty("components")[0].TryGetProperty("economicKey",out _));
        Assert.Equal(64,EconomicKey.Length);Assert.DoesNotContain(EconomicKey,raw);
        Assert.Equal(EconomicKey,p.Component.ClaimSuccession!.ReversedComponents[0].EconomicKey);
        Assert.Contains(EconomicKey,JsonSerializer.Serialize(p,Json)); // Native fact/wire is retained; only Work omits the opaque key.
        Assert.DoesNotContain("approvalReceipt",JsonSerializer.Serialize(p,Json),StringComparison.OrdinalIgnoreCase);
        Assert.Equal("100.0100",human.Proposal.Component.AlreadyPostedComponents[0].JournalTotal);
        Assert.Equal("0.0000",human.Proposal.Component.AlreadyPostedPurchaseComponents![0].Approval.SignedAmount);
    }
    [Fact]
    public void Nullable_legacy_fields_and_existing_business_dates_and_UTC_ticks_receive_no_new_defaults()
    {
        var f=InventoryCostWorkDecisionHostTests.NewFacts(AccountingInventoryCostKindV1.PurchaseInvoiceDifference,"Posted");
        var p=f.Prepared with{SourceOccurredAtUtc=DateTimeOffset.UnixEpoch.AddTicks(1),OfficialInvoiceNumber=null,
            Preparation=f.Prepared.Preparation! with{AccountingDate=new(1,1,2),TransactionDate=new(9999,12,31),AccountPlanCatalogVersion=null}};
        var human=WorkInventoryCostPreviewV1.FromNative(new(p,f.PreparedRecordedAtUtc,new('A',64),new('B',64)));
        Assert.Null(human.Proposal.OfficialInvoiceNumber);Assert.Null(human.Proposal.Preparation!.AccountPlanCatalogVersion);
        Assert.Equal(p.SourceOccurredAtUtc,human.Proposal.SourceOccurredAtUtc);Assert.Equal(p.Preparation.AccountingDate,human.Proposal.Preparation.AccountingDate);
        Assert.Equal(p.Preparation.TransactionDate,human.Proposal.Preparation.TransactionDate);Assert.Null(human.Proposal.Component.ClaimSuccession);
        Assert.Equal(p.ExactTlAmount,human.Proposal.ExactTlAmount);Assert.Null(human.Proposal.UnpostedTlAmountText);
    }
    private static AccountingInventoryCostDecisionDetailV1 FullDetail(long v)
    {
        var f=InventoryCostWorkDecisionHostTests.NewFacts(AccountingInventoryCostKindV1.SalesInvoicedShipment,"Posted");var p=f.Proposal;var id=p.ProposalId;var at=p.SourceOccurredAtUtc;
        var ratio=new AccountingInventoryCostRatioV1("1","3");var raw="{\"SENTETIK RAW PRIVATE\":true}";
        var shipment=new ManufacturingAttributionShipmentV1(id,id,1,id,id,id,100.001m,ratio,ratio,at,v,true,raw,[new(id,id,1,id,.001m,at)]);
        var manufacturing=new PostedManufacturingCostShareV1(p.CompanyId,id,id,id,"SENTETIK-generation",id,id,id,id,id,"100.0100","-2.0100",ratio,"0.0000",id,v,2,p.InvoiceDate,shipment,new('A',64),new('B',64),new('C',64),new('D',64));
        var purchasedShipment=new PurchaseFirstCostShipmentV1(id,id,1,id,id,id,100.001m,ratio,ratio,at,v,[new(id,id,1,id,.001m,at)],raw);
        var rounding=new AccountingInventoryCostRecordedRoundingV1(id,id,v,2,new(ratio,"0.3300",ratio,"SENTETIK-rule"));
        var approval=new PurchaseFirstCostApprovalV1(p.CompanyId,id,id,id,v,null,"RoundedNoCost","0.0000",id,id,v,2,id,id,p.InvoiceDate,p.Preparation!.PreparedBy,id,at,rounding,new('A',64),new('B',64));
        var candidate=new PurchaseFirstCostCandidateV1(p.CompanyId,id,id,id,id,id,id,ratio,ratio,ratio,purchasedShipment,new('A',64),new('B',64),new('C',64));
        var share=new PostedPurchaseCostShareV1(approval,id,id,1,id,100.001m,"-2.0100",ratio,ratio,[candidate],new('A',64));
        var physical=new AccountingSalesPhysicalCostClaimV1(id,id,1,id,100.001m);
        var reversed=new InventoryCostCancellationComponentV1(EconomicKey,"Known",id,id,"SENTETIK-purpose",id,"100.0100","-2.0100",id,id,new('A',64),id);
        var succession=new InventoryCostClaimSuccessionV1(id,id,id,id,v,id,v,new('A',64),id,v,[new(physical,v,new('A',64))],[reversed],[new('B',64)],ratio,ratio,ratio);
        p=p with{Version=v,InvoiceVersion=v,Preparation=p.Preparation with{MoneyProfileVersion=v,AccountPlanPackageVersion=v,AccountPlanCatalogVersion=v,
                Classification=p.Preparation.Classification with{MappingVersion=v,InvoiceLineRevision=v}},
            Component=p.Component with{SourceProofJson=raw,SalesPhysicalClaims=[physical],AlreadyPostedComponents=[manufacturing],AlreadyPostedPurchaseComponents=[share],RecordedRounding=rounding,UnpostedTlAmount=ratio,ClaimSuccession=succession},
            ComponentCancellation=new(id,p.CompanyId,id,id,v,"Approved",v,id,p.InvoiceDate,p.InvoiceDate,"SENTETIK-cancel",id,at,[reversed],[new(id,id,v,2)],id,at),
            StaleClosure=new(id,v,id,at,"SENTETIK-code","SENTETIK-text","SENTETIK-correlation",null),RenewedFrom=new(id,v,id)};
        return new(p,f.PreparedRecordedAtUtc,new('A',64),new('B',64));
    }
    private static readonly HashSet<string> LongFields=["version","invoiceVersion","moneyProfileVersion","mappingVersion","invoiceLineRevision","accountPlanPackageVersion","accountPlanCatalogVersion","proposalVersion","provenOriginalSequence","previousFinalizedVersion","previousProposalVersion","previousGeneration","preparedVersion","previousClosedVersion"];
    private static readonly HashSet<string> PrivateFields=["economicKey","sourceProofJson","sourceProofSha256","accountPlanSha256","physicalProofJson","sourcePayloadSha256","lineageSha256","historySha256","fingerprint","physicalSourceSha256","previousSourceSha256","previousClaimSha256","sourceFingerprint","capturedCostFingerprints","approvalReceipt"];
    // Test-only comparison: the production projection is a closed typed whitelist, with no generic JSON walker.
    private static void NormalizeExpected(JsonNode node)
    {
        if(node is JsonArray a){foreach(var item in a)if(item is not null)NormalizeExpected(item);return;}
        if(node is not JsonObject o)return;
        foreach(var pair in o.ToArray()){
            if(pair.Value is null||PrivateFields.Contains(pair.Key)){o.Remove(pair.Key);continue;}
            if(LongFields.Contains(pair.Key))o[pair.Key]=pair.Value.GetValue<long>().ToString(CultureInfo.InvariantCulture);
            else NormalizeExpected(pair.Value);
        }
    }
    private static void RemoveNulls(JsonNode node)
    {
        if(node is JsonArray a){foreach(var item in a)if(item is not null)RemoveNulls(item);return;}
        if(node is not JsonObject o)return;foreach(var pair in o.ToArray()){if(pair.Value is null)o.Remove(pair.Key);else RemoveNulls(pair.Value);}
    }
}
