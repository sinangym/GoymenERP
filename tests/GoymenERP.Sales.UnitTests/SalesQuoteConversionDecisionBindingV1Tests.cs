using System.Globalization;
using GoymenERP.BuildingBlocks.Work;
using GoymenERP.Modules.Sales.Application.Contracts;
using GoymenERP.Modules.Sales.Infrastructure.Persistence;
using GoymenERP.Modules.Sales.Infrastructure.WorkInbox;
using static GoymenERP.Sales.UnitTests.SalesWorkInboxTestDoubles;
using static GoymenERP.Sales.UnitTests.SalesQuoteConversionWorkDecisionSourceV1Tests;

namespace GoymenERP.Sales.UnitTests;

/// <summary>Controlled private-receipt protocol. Genuine Audit/Attempt provenance is tested by the actual SQL source fixture.</summary>
public sealed class SalesQuoteConversionDecisionBindingV1Tests
{
    [Fact]
    public async Task Approval_quote_N_and_rejection_request_M_are_not_substituted_and_historical_original_target_is_retained()
    {
        var detail=Detail("Pending");var snapshot=await Snapshot(detail);var target=SalesQuoteConversionDecisionBindingV1.Target(snapshot.SalesQuoteConversionProof!);
        var body=new SalesQuoteExpiryApprovalRequest("9");var approved=Approval(snapshot,target,body);
        Assert.True(SalesQuoteConversionDecisionBindingV1.MatchesOriginalDecisionReceipt(snapshot,target,approved,body,"SENTETIK-key",Approver));
        Assert.False(SalesQuoteConversionDecisionBindingV1.MatchesOriginalDecisionReceipt(snapshot,target,approved,new("1"),"SENTETIK-key",Approver));
        var rejectedBody=new SalesDraftStateCommandRequest("1","  SENTETIK ret\n  ");var rejected=Rejection(snapshot,target,rejectedBody);
        Assert.True(SalesQuoteConversionDecisionBindingV1.MatchesOriginalDecisionReceipt(snapshot,target,rejected,rejectedBody,"SENTETIK-key",Approver));
        Assert.False(SalesQuoteConversionDecisionBindingV1.MatchesOriginalDecisionReceipt(snapshot,target,rejected,new("9",rejectedBody.Reason),"SENTETIK-key",Approver));
        var later=Reseal(detail with {Quote=approved.Detail,Request=rejected.Request,ApprovalSupported=false,RejectionSupported=false});
        var laterSnapshot=await Snapshot(later);Assert.False(laterSnapshot.Pending);Assert.Equal(("10","2"),(laterSnapshot.SourceVersion,laterSnapshot.SalesQuoteConversionProof!.RequestVersion));
        var replay=approved with {IsReplay=true};
        Assert.True(SalesQuoteConversionDecisionBindingV1.MatchesOriginalDecisionReceipt(laterSnapshot,target,replay,body,"SENTETIK-key",Approver));
        Assert.False(SalesQuoteConversionDecisionBindingV1.MatchesOriginalDecisionReceipt(laterSnapshot,target with {RequestVersion="2"},replay,body,"SENTETIK-key",Approver));
        Assert.True(SalesQuoteConversionDecisionBindingV1.MatchesOriginalDecisionReceipt(laterSnapshot,target,rejected with {IsReplay=true},rejectedBody,"SENTETIK-key",Approver));
    }

    [Theory]
    [InlineData("missing")] [InlineData("key")] [InlineData("actor")] [InlineData("company")] [InlineData("selected-id")]
    [InlineData("request-no")] [InlineData("request-version")] [InlineData("quote-id")] [InlineData("hash")]
    [InlineData("correlation")] [InlineData("result-version")] [InlineData("original-digest")] [InlineData("current-response")]
    [InlineData("caption")] [InlineData("source-version")] [InlineData("snapshot-pending")]
    public async Task Native_receipt_proof_and_frozen_selection_cannot_be_relabelled_or_replaced(string fault)
    {
        var detail=Detail("Pending");var snapshot=await Snapshot(detail);var target=SalesQuoteConversionDecisionBindingV1.Target(snapshot.SalesQuoteConversionProof!);
        var body=new SalesQuoteExpiryApprovalRequest("9");var reply=Approval(snapshot,target,body);var receipt=reply.OriginalDecisionReceipt!;
        if(fault=="missing")reply=reply with {OriginalDecisionReceipt=null};
        if(fault=="key")receipt=receipt with {IdempotencyKeyHash=new('a',64)};
        if(fault=="actor")receipt=receipt with {ActorId=Maker};
        if(fault=="company")receipt=receipt with {CompanyId=OtherCompany};
        if(fault=="selected-id")target=target with {RequestId=Guid.NewGuid()};
        if(fault=="request-no")target=target with {RequestNo=4};
        if(fault=="request-version")target=target with {RequestVersion="2"};
        if(fault=="quote-id")target=target with {QuoteId=Guid.NewGuid()};
        if(fault=="hash")receipt=receipt with {PayloadHash=new('a',64)};
        if(fault=="correlation")receipt=receipt with {OriginalCorrelationId="different"};
        if(fault=="result-version")receipt=receipt with {ResultVersion="11"};
        if(fault=="current-response")reply=reply with {Detail=reply.Detail with {Note="changed observed response"}};
        if(fault=="caption")snapshot=snapshot with {Item=snapshot.Item with {DocumentLabel="changed"}};
        if(fault=="source-version")snapshot=snapshot with {SourceVersion="1"};
        if(fault=="snapshot-pending")snapshot=snapshot with {Pending=false};
        receipt=receipt with {OriginalDecisionDigest=SalesQuoteDecisionDigestsV1.OriginalDecisionDigest(receipt)};
        if(fault=="original-digest")receipt=receipt with {OriginalDecisionDigest=new('f',64)};
        if(fault!="missing")reply=reply with {OriginalDecisionReceipt=receipt};
        Assert.False(SalesQuoteConversionDecisionBindingV1.MatchesOriginalDecisionReceipt(snapshot,target,reply,body,"SENTETIK-key",Approver));
    }

    private static async Task<WorkApprovalDecisionSnapshotV1> Snapshot(SalesQuoteConversionDecisionDetailV1 detail)=>
        Assert.IsType<WorkApprovalDecisionSnapshotV1>(await Source(new Reader(detail)).ReadDecisionAsync(Context(),Id,default));
    private static SalesQuoteResponse Approval(WorkApprovalDecisionSnapshotV1 snapshot,SalesQuoteConversionTargetV1 target,SalesQuoteExpiryApprovalRequest body)
    {
        var detail=Detail("Approved").Quote;
        detail=detail with {Quote=detail.Quote with {Version=(long.Parse(body.ExpectedVersion,CultureInfo.InvariantCulture)+1).ToString(CultureInfo.InvariantCulture)}};
        var response=new SalesQuoteResponse(detail,false,"SENTETIK-original");
        var receipt=Receipt(snapshot,target,body.ExpectedVersion,"Approved",SalesQuoteCommands.ApproveExpiredConversionV1,SalesQuoteHash.Compute(target.QuoteId,body),null,
            SalesQuoteDecisionDigestsV1.CurrentResponseDigest(response));
        return response with {OriginalDecisionReceipt=receipt};
    }
    private static SalesApprovalRequestCommandResult Rejection(WorkApprovalDecisionSnapshotV1 snapshot,SalesQuoteConversionTargetV1 target,SalesDraftStateCommandRequest body)
    {
        var request=Detail("Rejected").Request with {DecisionReason=body.Reason,Version=(long.Parse(body.ExpectedVersion,CultureInfo.InvariantCulture)+1).ToString(CultureInfo.InvariantCulture)};
        var response=new SalesApprovalRequestCommandResult(request,false,"SENTETIK-original");
        var receipt=Receipt(snapshot,target,body.ExpectedVersion,"Rejected",SalesApprovalRequestCommands.RejectQuoteConversionV1,
            SalesApprovalRequestHash.Compute(SalesApprovalRequestCommands.RejectQuoteConversionV1,target.QuoteId,target.RequestNo,body),body.Reason,
            SalesQuoteDecisionDigestsV1.CurrentResponseDigest(response));
        return response with {OriginalDecisionReceipt=receipt};
    }
    private static SalesQuoteOriginalDecisionReceiptV1 Receipt(WorkApprovalDecisionSnapshotV1 snapshot,SalesQuoteConversionTargetV1 target,string expected,string state,
        string command,string hash,string? note,string responseDigest)
    {
        var r=new SalesQuoteOriginalDecisionReceiptV1(snapshot.CompanyId,target,command,Approver,SalesStore.IdempotencyKeyHash("SENTETIK-key"),hash,expected,
            (long.Parse(expected,CultureInfo.InvariantCulture)+1).ToString(CultureInfo.InvariantCulture),state,T0,note,"SENTETIK-original",Guid.NewGuid(),new('a',64),"",responseDigest);
        return r with {OriginalDecisionDigest=SalesQuoteDecisionDigestsV1.OriginalDecisionDigest(r)};
    }
}
