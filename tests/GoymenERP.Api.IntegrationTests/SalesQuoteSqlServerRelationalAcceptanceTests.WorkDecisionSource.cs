using System.Text.Json;
using GoymenERP.BuildingBlocks.Multitenancy;
using GoymenERP.BuildingBlocks.Work;
using GoymenERP.Modules.Sales.Application.Contracts;
using GoymenERP.Modules.Sales.Infrastructure.WorkInbox;

namespace GoymenERP.Api.IntegrationTests;

public sealed partial class SalesQuoteSqlServerRelationalAcceptanceTests
{
    [SqlServerFact]
    public async Task Quote_selected_source_uses_actual_guarded_owner_and_separates_approval_and_rejection_version_axes()
    {
        await using var run=await Run.StartAsync();var q=await SeedSafeQuote(run,true);await using var safe=QuoteSafetyScope(run);
        var target=await SelectedQuoteTarget(safe,q.Id,q.RequestNo);var source=QuoteDecisionSource(safe);var context=new WorkInboxSourceContextV1(CompanyA,run.Actor,run.Correlation,null);
        var pending=Assert.IsType<WorkApprovalDecisionSnapshotV1>(await source.ReadDecisionAsync(context,target.RequestId,default));
        Assert.True(pending.Pending);Assert.Null(pending.Item.PreparedBy);Assert.False(pending.Item.Excludes(run.Actor));
        Assert.Equal(target.RequestId,Assert.Single((await source.ListPendingAsync(context,10,default)).Items!).ItemId);
        Assert.Equal($"/onaylarim/sales.quote-conversion-request.v1/{target.RequestId:D}",pending.Item.Link);Assert.Equal($"/satis/teklifler/{q.Id:D}",Assert.Single(pending.Item.References).Link);Assert.Equal(target,SalesQuoteConversionDecisionBindingV1.Target(pending.SalesQuoteConversionProof!));
        var body=new SalesQuoteExpiryApprovalRequest("1");var approved=await safe.Service.ApproveSelectedQuoteConversionAsync(target,body,run.Metadata("quote-source-approve","quote-source-original"));
        Assert.True(SalesQuoteConversionDecisionBindingV1.MatchesOriginalDecisionReceipt(pending,target,approved,body,"quote-source-approve",run.Actor));
        var after=Assert.IsType<WorkApprovalDecisionSnapshotV1>(await source.ReadDecisionAsync(context,target.RequestId,default));
        Assert.Equal((false,"2","1",false,true),(after.Pending,after.SourceVersion,after.SalesQuoteConversionProof!.RequestVersion,after.SalesQuoteConversionProof.ApprovalSupported,after.SalesQuoteConversionProof.RejectionSupported));
        Assert.Empty((await source.ListPendingAsync(context,10,default)).Items!);
        var rejectionBody=new SalesDraftStateCommandRequest("1","  SENTETIK ret  ");
        var rejected=await safe.Service.RejectSelectedQuoteConversionAsync(target,rejectionBody,run.Metadata("quote-source-reject"));
        Assert.True(SalesQuoteConversionDecisionBindingV1.MatchesOriginalDecisionReceipt(after,target,rejected,rejectionBody,"quote-source-reject",run.Actor));
        var terminal=Assert.IsType<WorkApprovalDecisionSnapshotV1>(await source.ReadDecisionAsync(context,target.RequestId,default));
        Assert.Equal((false,"2","2",false,false),(terminal.Pending,terminal.SourceVersion,terminal.SalesQuoteConversionProof!.RequestVersion,terminal.SalesQuoteConversionProof.ApprovalSupported,terminal.SalesQuoteConversionProof.RejectionSupported));
        var original=await safe.Service.ApproveSelectedQuoteConversionAsync(target,body,run.Metadata("quote-source-approve"));
        Assert.True(original.IsReplay);Assert.Equal("quote-source-original",original.OriginalCorrelationId);
        Assert.True(SalesQuoteConversionDecisionBindingV1.MatchesOriginalDecisionReceipt(terminal,target,original,body,"quote-source-approve",run.Actor));
        Assert.False(SalesQuoteConversionDecisionBindingV1.MatchesOriginalDecisionReceipt(terminal,target with{RequestVersion="2"},original,body,"quote-source-approve",run.Actor));
        using var json=JsonDocument.Parse(JsonSerializer.Serialize(terminal,SalesDraftJson.Options));Assert.False(json.RootElement.TryGetProperty("salesQuoteConversionProof",out _));
        Assert.Equal((1,1),(await QuoteDecisionCount(run,q.Id,true),await QuoteDecisionCount(run,q.Id,false)));AssertQuoteClean(safe);
    }

    [SqlServerFact]
    public async Task Quote_selected_binding_rejects_wrong_request_key_body_and_observed_response_without_another_owner_read_or_mutation()
    {
        await using var run=await Run.StartAsync();var q=await SeedSafeQuote(run,true);await using var safe=QuoteSafetyScope(run);
        var target=await SelectedQuoteTarget(safe,q.Id,q.RequestNo);var context=new WorkInboxSourceContextV1(CompanyA,run.Actor,run.Correlation,null);
        var snapshot=Assert.IsType<WorkApprovalDecisionSnapshotV1>(await QuoteDecisionSource(safe).ReadDecisionAsync(context,target.RequestId,default));
        var body=new SalesQuoteExpiryApprovalRequest("1");var response=await safe.Service.ApproveSelectedQuoteConversionAsync(target,body,run.Metadata("quote-binding"));
        Assert.True(Match(snapshot,target,response,body,"quote-binding",run.Actor));
        Assert.False(Match(snapshot,target,response with{OriginalDecisionReceipt=null},body,"quote-binding",run.Actor));
        Assert.False(Match(snapshot,target with{RequestId=Guid.NewGuid()},response,body,"quote-binding",run.Actor));
        Assert.False(Match(snapshot,target,response,body,"another-key",run.Actor));Assert.False(Match(snapshot,target,response,body,"quote-binding","another-actor"));
        Assert.False(Match(snapshot,target,response,new("2"),"quote-binding",run.Actor));Assert.False(Match(snapshot with{CompanyId=CompanyB},target,response,body,"quote-binding",run.Actor));
        Assert.False(Match(snapshot with{Item=snapshot.Item with{References=[snapshot.Item.References[0] with{RecordId=Guid.NewGuid()}]}},target,response,body,"quote-binding",run.Actor));
        Assert.False(Match(snapshot,target,response with{Detail=response.Detail with{Note="changed current reply"}},body,"quote-binding",run.Actor));
        Assert.Equal(1,await QuoteDecisionCount(run,q.Id,true));Assert.Equal(1,await QuoteAuditCount(run,q.Id,true));AssertQuoteClean(safe);
        static bool Match(WorkApprovalDecisionSnapshotV1 s,SalesQuoteConversionTargetV1 t,SalesQuoteResponse r,SalesQuoteExpiryApprovalRequest b,string key,string actor)=>
            SalesQuoteConversionDecisionBindingV1.MatchesOriginalDecisionReceipt(s,t,r,b,key,actor);
        // Binding calls are pure protocol checks. The real Audit/Attempt witness comes from the guarded native owner above.
    }

    private static SalesQuoteConversionRequestWorkInboxSourceV1 QuoteDecisionSource(QuoteSafetyScopeState safe)
    {
        var company=new CompanyContext();company.Set(CompanyA);
        return new(new SalesWorkInboxApprovalStoreV1(safe.Db),company,safe.User,safe.Authority,safe.Service);
    }
}
