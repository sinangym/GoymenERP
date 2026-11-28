using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using GoymenERP.Api.WorkRuntime;
using GoymenERP.Modules.Accounting.Application;
using static GoymenERP.Api.IntegrationTests.InventoryCostWorkDecisionHostTests;
namespace GoymenERP.Api.IntegrationTests;

// Typed whitelist/transport evidence only; no SQL, allocation or native business approval acceptance.
public sealed class WorkInventoryCostCancellationDecisionPreviewV1Tests
{
    [Theory] [InlineData(1L)] [InlineData(9007199254740993L)] [InlineData(long.MaxValue)]
    public void Independent_outer_cancel_invoice_revision_and_profile_longs_are_lossless_text(long version)
    {
        var d=InventoryCostCancellationWorkDecisionHostTests.Facts(version,version==long.MaxValue?version-1:version);
        d=d with{OriginalProposal=d.OriginalProposal with{InvoiceVersion=version},
            PreparedCancellation=d.PreparedCancellation with{InvoiceLineRevision=version,MoneyProfiles=d.PreparedCancellation.MoneyProfiles.Select(x=>x with{Version=version}).ToArray()},
            Cancellation=d.Cancellation with{InvoiceLineRevision=version,MoneyProfiles=d.Cancellation.MoneyProfiles.Select(x=>x with{Version=version}).ToArray()},
            SourceCancellation=d.SourceCancellation with{FinalizedVersion=version}};
        var native=JsonSerializer.Serialize(d,Json);var human=WorkInventoryCostCancellationPreviewV1.FromNative(d);var wire=JsonSerializer.SerializeToNode(human,Json)!;
        var expected=JsonSerializer.SerializeToNode(d,Json)!;Normalize(expected);var actual=wire.DeepClone();RemoveNulls(actual);
        Assert.True(JsonNode.DeepEquals(expected,actual));Assert.Equal(5,wire.AsObject().Count);
        var text=version.ToString(CultureInfo.InvariantCulture);
        Assert.Equal(text,human.OriginalProposal.Version);Assert.Equal(text,human.OriginalProposal.InvoiceVersion);Assert.Equal(text,human.SourceCancellation.FinalizedVersion);
        Assert.Equal(d.PreparedCancellation.Version.ToString(CultureInfo.InvariantCulture),human.PreparedCancellation.Version);
        Assert.Equal(d.Cancellation.Version.ToString(CultureInfo.InvariantCulture),human.Cancellation.Version);
        Assert.Equal(text,human.PreparedCancellation.InvoiceLineRevision);Assert.Equal(text,human.Cancellation.MoneyProfiles[0].Version);
        Assert.Equal(d.PreparedRecordedAtUtc,human.PreparedRecordedAtUtc);Assert.Equal(native,JsonSerializer.Serialize(d,Json));
        Assert.Equal(version,JsonSerializer.SerializeToElement(d.OriginalProposal,Json).GetProperty("version").GetInt64());
    }
    [Fact]
    public void All_cancellation_components_are_visible_but_opaque_economic_keys_source_hashes_and_receipts_are_not()
    {
        var d=InventoryCostCancellationWorkDecisionHostTests.Facts();var c=d.PreparedCancellation;
        var rows=Enumerable.Range(0,137).Select(i=>c.Components[0] with{EconomicKey=AccountingInventoryCostSourceValidationV1.Sha256("SENTETIK-economic-"+i),SignedAmount="-600"}).ToArray();
        var approved=rows.Select(x=>x with{ReversingJournalId=Guid.NewGuid()}).ToArray();
        var p=d.OriginalProposal with{Component=d.OriginalProposal.Component with{SourceProofJson="{\"SENTETIK RAW PRIVATE\":true}"}};
        p=p with{CancellationApprovalReceipt=new(p.CompanyId,p.ProposalId,c.Id,c.CancellationId,AccountingInventoryCostServiceV1.InventoryCostCancellationApproveCommand,
            new('A',64),new('B',64),Actor,7,1,7,2,Maker,At,d.PreparedRecordedAtUtc,At.AddMinutes(1),"SENTETIK PRIVATE RECEIPT",d.SourceFactsDigest,new('D',64))};
        d=d with{OriginalProposal=p,PreparedCancellation=c with{Components=rows},Cancellation=d.Cancellation with{Components=approved}};
        var human=WorkInventoryCostCancellationPreviewV1.FromNative(d);var raw=JsonSerializer.Serialize(human,Json);
        Assert.Equal(137,human.PreparedCancellation.Components.Count);Assert.Equal(137,human.Cancellation.Components.Count);
        Assert.Equal("-600",human.PreparedCancellation.Components[0].SignedAmount);Assert.Equal("600",human.Cancellation.Components[0].JournalTotal);
        Assert.Equal(approved[136].ReversingJournalId,human.Cancellation.Components[136].ReversingJournalId);
        foreach(var field in PrivateFields)Assert.DoesNotContain("\""+field+"\"",raw,StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("SENTETIK RAW PRIVATE",raw,StringComparison.Ordinal);Assert.DoesNotContain("SENTETIK PRIVATE RECEIPT",raw,StringComparison.Ordinal);
        Assert.DoesNotContain(d.SourceCancellation.CorrelationId,raw,StringComparison.Ordinal);Assert.DoesNotContain(d.SourceFactsDigest,raw,StringComparison.Ordinal);Assert.DoesNotContain(d.PreviewDigest,raw,StringComparison.Ordinal);
        Assert.DoesNotContain(rows[0].EconomicKey,raw,StringComparison.Ordinal);Assert.Contains(rows[0].EconomicKey,JsonSerializer.Serialize(d.PreparedCancellation,Json),StringComparison.Ordinal);
        Assert.DoesNotContain("cancellationApprovalReceipt",JsonSerializer.Serialize(p,Json),StringComparison.OrdinalIgnoreCase);Assert.NotNull(p.CancellationApprovalReceipt);
        Assert.Equal(d.SourceCancellation.Reason,human.SourceCancellation.Reason);Assert.Equal(c.Reason,human.PreparedCancellation.Reason);
    }
    [Fact]
    public void Nullable_fields_dates_and_distinct_prepared_recorded_timestamps_receive_no_new_defaults_or_chronology()
    {
        var d=InventoryCostCancellationWorkDecisionHostTests.Facts();var before=d.PreparedCancellation with{AccountingDate=new(1,1,2),TransactionDate=new(9999,12,31),PreparedAtUtc=DateTimeOffset.UnixEpoch.AddTicks(1),Components=d.PreparedCancellation.Components.Select(x=>x with{SourceJournalId=null,SignedAmount="0"}).ToArray()};
        d=d with{PreparedCancellation=before,Cancellation=before,PreparedRecordedAtUtc=DateTimeOffset.UnixEpoch,
            OriginalProposal=d.OriginalProposal with{OfficialInvoiceNumber=null},SourceCancellation=d.SourceCancellation with{CancelledAtUtc=DateTimeOffset.UnixEpoch.AddTicks(17)}};
        var human=WorkInventoryCostCancellationPreviewV1.FromNative(d);
        Assert.Null(human.OriginalProposal.OfficialInvoiceNumber);Assert.Null(human.PreparedCancellation.ApprovedBy);Assert.Null(human.PreparedCancellation.ApprovedAtUtc);
        Assert.Null(human.PreparedCancellation.Components[0].SourceJournalId);Assert.Null(human.Cancellation.Components[0].ReversingJournalId);
        Assert.Equal(before.AccountingDate,human.PreparedCancellation.AccountingDate);Assert.Equal(before.TransactionDate,human.PreparedCancellation.TransactionDate);
        Assert.Equal(before.PreparedAtUtc,human.PreparedCancellation.PreparedAtUtc);Assert.Equal(d.PreparedRecordedAtUtc,human.PreparedRecordedAtUtc);
        Assert.Equal(d.SourceCancellation.CancelledAtUtc,human.SourceCancellation.CancelledAtUtc);Assert.Equal("0",human.Cancellation.Components[0].SignedAmount);
    }
    [Fact]
    public void Work_selection_is_exactly_outer_N_text_and_other_previews_do_not_serialize_the_new_human_or_selection()
    {
        var request=new WorkDecisionRequestV1("9007199254740993",true,null){InventoryCostCancellation=new(long.MaxValue.ToString(CultureInfo.InvariantCulture))};
        var wire=JsonSerializer.SerializeToElement(request,Json);Assert.Equal(5,wire.EnumerateObject().Count());
        Assert.Equal("9007199254740993",wire.GetProperty("expectedVersion").GetString());var selection=wire.GetProperty("inventoryCostCancellation");
        Assert.Single(selection.EnumerateObject());Assert.Equal(long.MaxValue.ToString(CultureInfo.InvariantCulture),selection.GetProperty("expectedProposalVersion").GetString());
        Assert.DoesNotContain("inventoryCostCancellation",JsonSerializer.Serialize(new WorkDecisionRequestV1("1",true,null),Json),StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("inventoryCostCancellation",JsonSerializer.Serialize(new WorkDecisionPreviewV1(Company,WorkDecisionHostV1.SalesOrder,Id,"SENTETIK","1",true,false,[],[]),Json),StringComparison.OrdinalIgnoreCase);
    }
    private static readonly HashSet<string> LongFields=["version","invoiceVersion","moneyProfileVersion","mappingVersion","invoiceLineRevision","accountPlanPackageVersion","accountPlanCatalogVersion","proposalVersion","provenOriginalSequence","previousFinalizedVersion","previousProposalVersion","previousGeneration","preparedVersion","previousClosedVersion","finalizedVersion"];
    private static readonly HashSet<string> PrivateFields=["economicKey","sourceFingerprint","sourceProofJson","sourceProofSha256","accountPlanSha256","physicalProofJson","sourcePayloadSha256","lineageSha256","historySha256","fingerprint","physicalSourceSha256","previousSourceSha256","previousClaimSha256","capturedCostFingerprints","approvalReceipt","cancellationApprovalReceipt","sourceFactsDigest","previewDigest","requestHash","correlationId"];
    // Test-only shape comparison. Production uses explicit typed whitelists, not a generic JSON walker.
    private static void Normalize(JsonNode node)
    {
        if(node is JsonArray a){foreach(var entry in a)if(entry is not null)Normalize(entry);return;}
        if(node is not JsonObject o)return;foreach(var pair in o.ToArray()){
            if(pair.Value is null||PrivateFields.Contains(pair.Key)){o.Remove(pair.Key);continue;}
            if(LongFields.Contains(pair.Key))o[pair.Key]=pair.Value.GetValue<long>().ToString(CultureInfo.InvariantCulture);else Normalize(pair.Value);}
    }
    private static void RemoveNulls(JsonNode node)
    {if(node is JsonArray a){foreach(var entry in a)if(entry is not null)RemoveNulls(entry);return;}if(node is not JsonObject o)return;foreach(var pair in o.ToArray()){if(pair.Value is null)o.Remove(pair.Key);else RemoveNulls(pair.Value);}}
}
