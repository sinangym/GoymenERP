using GoymenERP.BuildingBlocks.Observability;
using System.Text.Json;
using System.Text.Json.Nodes;
using GoymenERP.BuildingBlocks.Work;
using GoymenERP.Modules.Accounting.Application;
using GoymenERP.Modules.Accounting.Infrastructure.Persistence;
using GoymenERP.Modules.Accounting.Infrastructure.WorkInbox;
using Microsoft.EntityFrameworkCore;
namespace GoymenERP.Api.IntegrationTests;
public sealed partial class AccountingInvoiceLineCogsSqlServerRelationalAcceptanceTests
{
    [SqlServerFact]
    public async Task Cancellation_decision_retains_original_N_prepared_M_recorded_time_and_genuine_restart_receipt_without_current_plan()
    {
        foreach(var auditInterceptor in new[]{false,true})
        {
            await using var run=await Run.Start();var setup=await CancellationSafetyPrepare(run);var before=await CancellationSafetyCounts(run);
            AccountingInventoryCostCancellationApprovalReceiptV1 firstReceipt;WorkInboxItemV1 firstItem;
            await using(var owner=new CancellationSafetyOwner(run,setup,auditInterceptor))
            {
                var human=await owner.Service.ReadCancellationDecisionAsync(setup.Id,null,_=>Task.CompletedTask);
                Assert.Equal(AccountingApiStatus.Ok,human.Status);var detail=Assert.IsType<AccountingInventoryCostCancellationDecisionDetailV1>(human.Resource);
                Assert.True(detail.Pending);Assert.Equal(setup.Request.ExpectedVersion,detail.OriginalProposal.Version);
                Assert.Equal(setup.Request.ExpectedCancellationVersion,detail.PreparedCancellation.Version);
                var prepared=await owner.Db.InventoryCostCancellationVersions.AsNoTracking().SingleAsync(x=>x.InventoryCostProposalId==setup.Id&&x.Version==setup.Request.ExpectedCancellationVersion);
                Assert.Equal(prepared.InitialProposalVersion,detail.OriginalProposal.Version);Assert.Equal(prepared.RecordedAtUtc,detail.PreparedRecordedAtUtc);
                var source=new AccountingInventoryCostCancellationWorkInboxSourceV1(owner.Db,new(owner.Company,owner.User,owner.Authority),owner.Service);
                var context=new WorkInboxSourceContextV1(Company,Checker.ToString("D"),owner.CorrelationId,null);
                firstItem=Assert.Single((await source.ListPendingAsync(context,10,default)).Items!);
                Assert.True(AccountingInventoryCostCancellationDecisionBindingV1.MatchesDecisionItem(firstItem,detail));
                var snapshot=Assert.IsType<WorkApprovalDecisionSnapshotV1>(await source.ReadDecisionAsync(context,setup.Id,default));
                Assert.True(snapshot.Pending);Assert.Equal(setup.Request.ExpectedCancellationVersion.ToString(System.Globalization.CultureInfo.InvariantCulture),snapshot.SourceVersion);
                Assert.Equal(setup.Request.ExpectedVersion,snapshot.InventoryCostCancellationProof?.ProposalVersion);
                var scalar=JsonSerializer.Serialize(snapshot,Json);Assert.DoesNotContain(detail.PreparedCancellation.Reason,scalar,StringComparison.Ordinal);Assert.DoesNotContain(detail.SourceFactsDigest,scalar,StringComparison.Ordinal);
                Assert.Equal(before,await CancellationSafetyCounts(run));
                var answer=await owner.Approve("cancel-decision-original");Assert.Equal(AccountingApiStatus.Ok,answer.Status);
                firstReceipt=Assert.IsType<AccountingInventoryCostCancellationApprovalReceiptV1>(answer.Resource?.CancellationApprovalReceipt);
                Assert.Equal(detail.SourceFactsDigest,firstReceipt.SourceFactsDigest);Assert.Equal(detail.PreparedRecordedAtUtc,firstReceipt.PreparedRecordedAtUtc);
                Assert.Equal(setup.Request.ExpectedVersion,firstReceipt.ResultProposalVersion);Assert.Equal(setup.Request.ExpectedCancellationVersion+1,firstReceipt.ResultCancellationVersion);
                Assert.True(AccountingInventoryCostCancellationDecisionBindingV1.MatchesDecisionItem(firstItem,answer.Resource,firstReceipt));
                Assert.DoesNotContain("cancellationApprovalReceipt",JsonSerializer.Serialize(answer,Json),StringComparison.OrdinalIgnoreCase);
                var retained=Assert.IsType<WorkApprovalDecisionSnapshotV1>(await source.ReadDecisionVersionAsync(context,setup.Id,snapshot.SourceVersion,default));
                Assert.False(retained.Pending);Assert.Equal(snapshot.SourceVersion,retained.SourceVersion);Assert.Equal("Approved",retained.InventoryCostCancellationProof?.Status);
                owner.AssertClean();
            }
            var completed=await CancellationSafetyCounts(run);await using var restarted=new CancellationSafetyOwner(run,setup,auditInterceptor);
            var replay=await restarted.Approve("cancel-decision-original");Assert.True(replay.IsReplay);Assert.Equal(firstReceipt,replay.Resource?.CancellationApprovalReceipt);
            Assert.True(AccountingInventoryCostCancellationDecisionBindingV1.MatchesDecisionItem(firstItem,replay.Resource,replay.Resource?.CancellationApprovalReceipt));
            var plans=new InventoryApprovalPlan();var currencies=new InventoryApprovalCurrencies();
            var profiles=new AccountingSqlServerRelationalAcceptanceTests.SyntheticMoneyProfiles(Company);
            var core=new AccountingStore(restarted.Db,restarted.Company,restarted.Collector,TimeProvider.System,new(profiles),normalApprovalAuditWriter:restarted.Writer);
            var readOnlyStore=new AccountingInventoryCostStoreV1(restarted.Db,core,setup.Reader,new CogsMapping(),plans,currencies,new(profiles),restarted.Company,restarted.Collector,restarted.Writer,TimeProvider.System);
            var correlation=new CorrelationContext();correlation.Set("SENTETIK-restart-without-current-plan",null,"Web",null,null);
            var replayService=new AccountingInventoryCostServiceV1(readOnlyStore,core,restarted.Authority,restarted.Company,restarted.User,correlation);
            var noPlanReplay=await replayService.ApproveCancellationAsync(setup.Id,setup.Request,"cancel-decision-original");
            Assert.True(noPlanReplay.IsReplay);Assert.Equal(firstReceipt,noPlanReplay.Resource?.CancellationApprovalReceipt);Assert.Equal(0,plans.Reads);Assert.Equal(0,currencies.Reads);
            Assert.Equal(completed,await CancellationSafetyCounts(run));restarted.AssertClean();
        }
    }
    [SqlServerFact]
    public async Task Cancellation_version_source_keeps_old_preparation_M_and_actual_maker_time_after_a_new_native_preparation()
    {
        await using var run=await Run.Start();var setup=await CancellationSafetyPrepare(run);
        await using var owner=new CancellationSafetyOwner(run,setup);var old=Assert.IsType<AccountingInventoryCostCancellationDecisionDetailV1>((await owner.Service.ReadCancellationDecisionAsync(setup.Id,setup.Request.ExpectedCancellationVersion,_=>Task.CompletedTask)).Resource);
        var request=new ProposeAccountingInventoryCostReversalV1(setup.Request.ExpectedVersion,Guid.NewGuid(),old.PreparedCancellation.PeriodId,
            old.PreparedCancellation.AccountingDate,old.PreparedCancellation.TransactionDate,"SENTETIK replacement cancellation",setup.Request.ExpectedCancellationVersion);
        owner.User.Actor=Guid.NewGuid();var replacement=await owner.Service.ProposeReversalAsync(setup.Id,request,"reprepare");Assert.Equal(AccountingApiStatus.Ok,replacement.Status);owner.User.Actor=Checker;
        var current=Assert.IsType<AccountingInventoryCostCancellationDecisionDetailV1>((await owner.Service.ReadCancellationDecisionAsync(setup.Id,null,_=>Task.CompletedTask)).Resource);
        Assert.True(current.Pending);Assert.Equal(old.PreparedCancellation.Version+1,current.PreparedCancellation.Version);Assert.NotEqual(old.PreparedCancellation.Id,current.PreparedCancellation.Id);Assert.NotEqual(old.PreparedCancellation.PreparedBy,current.PreparedCancellation.PreparedBy);
        var retained=Assert.IsType<AccountingInventoryCostCancellationDecisionDetailV1>((await owner.Service.ReadCancellationDecisionAsync(setup.Id,old.PreparedCancellation.Version,_=>Task.CompletedTask)).Resource);
        Assert.False(retained.Pending);Assert.Equal(JsonSerializer.Serialize(old.PreparedCancellation,Json),JsonSerializer.Serialize(retained.PreparedCancellation,Json));Assert.Equal(old.PreparedRecordedAtUtc,retained.PreparedRecordedAtUtc);
        Assert.Equal(old.SourceFactsDigest,retained.SourceFactsDigest);Assert.Equal("Prepared",retained.Cancellation.Status);Assert.Equal(JsonSerializer.Serialize(old.OriginalProposal,Json),JsonSerializer.Serialize(retained.OriginalProposal,Json));
        var source=new AccountingInventoryCostCancellationWorkInboxSourceV1(owner.Db,new(owner.Company,owner.User,owner.Authority),owner.Service);var context=new WorkInboxSourceContextV1(Company,Checker.ToString("D"),owner.CorrelationId,null);
        var original=Assert.IsType<WorkApprovalDecisionSnapshotV1>(await source.ReadDecisionVersionAsync(context,setup.Id,old.PreparedCancellation.Version.ToString(System.Globalization.CultureInfo.InvariantCulture),default));
        Assert.False(original.Pending);Assert.Equal(old.PreparedRecordedAtUtc,original.Item.WaitingSinceUtc);
        Assert.Equal(current.PreparedRecordedAtUtc,Assert.Single((await source.ListPendingAsync(context,10,default)).Items!).WaitingSinceUtc);
        var result=await owner.Service.ApproveCancellationAsync(setup.Id,new(setup.Request.ExpectedVersion,current.PreparedCancellation.Version),"replacement-approve");
        Assert.Equal(AccountingApiStatus.Ok,result.Status);Assert.NotNull(result.Resource?.CancellationApprovalReceipt);owner.AssertClean();
    }
    [SqlServerFact]
    public async Task Cancellation_original_receipt_rejects_unknown_private_JSON_and_changed_native_two_version_payload()
    {
        await using var run=await Run.Start();var setup=await CancellationSafetyPrepare(run);await using var owner=new CancellationSafetyOwner(run,setup);
        var answer=await owner.Approve("binding-original");Assert.NotNull(answer.Resource?.CancellationApprovalReceipt);
        var detail=Assert.IsType<AccountingInventoryCostCancellationDecisionDetailV1>((await owner.Service.ReadCancellationDecisionAsync(setup.Id,setup.Request.ExpectedCancellationVersion,_=>Task.CompletedTask)).Resource);
        var row=await owner.Db.Commands.AsNoTracking().SingleAsync(x=>x.CommandName==AccountingInventoryCostServiceV1.InventoryCostCancellationApproveCommand);
        var command=new AccountingCommandRecord(row.CommandName,row.IdempotencyKeyHash,row.ActorId,row.PayloadHash,row.ResultJson,row.CorrelationId);
        Assert.NotNull(AccountingInventoryCostServiceV1.BindCancellationApprovalReceipt(command,detail,answer,setup.Id,setup.Request,"binding-original",Checker));
        foreach(var field in new[]{"unexpected","cancellationApprovalReceipt"})
        {var changed=JsonNode.Parse(command.ResultJson)!.AsObject();changed["resource"]![field]=true;
            Assert.Null(AccountingInventoryCostServiceV1.BindCancellationApprovalReceipt(command with{ResultJson=changed.ToJsonString(Json)},detail,answer,setup.Id,setup.Request,"binding-original",Checker));}
        Assert.Null(AccountingInventoryCostServiceV1.BindCancellationApprovalReceipt(command,detail,answer,setup.Id,setup.Request with{ExpectedVersion=setup.Request.ExpectedVersion+1},"binding-original",Checker));
        Assert.Null(AccountingInventoryCostServiceV1.BindCancellationApprovalReceipt(command,detail,answer,setup.Id,setup.Request with{ExpectedCancellationVersion=setup.Request.ExpectedCancellationVersion+1},"binding-original",Checker));owner.AssertClean();
    }
    [SqlServerFact]
    public async Task Cancellation_decision_actual_completed_reads_recheck_consumer_authority_and_cancel_before_any_further_query()
    {
        foreach(var table in new[]{"InventoryCostCancellationVersions","InventoryCostSources","SourceInvoiceCancellations"})
        foreach(var cancel in new[]{false,true})
        {
            await using var run=await Run.Start();var setup=await CancellationSafetyPrepare(run);var before=await CancellationSafetyCounts(run);using var stop=new CancellationTokenSource();
            CancellationSafetyOwner? owner=null;var fired=false;var later=0;
            var probe=new InventoryApprovalQuery(sql=>{if(fired){later++;return;}if(!sql.Contains("["+table+"]",StringComparison.Ordinal))return;fired=true;if(cancel)stop.Cancel();else owner!.Authority.Granted=false;});
            await using(owner=new CancellationSafetyOwner(run,setup,true,probe))
            {
                if(cancel)await Assert.ThrowsAnyAsync<OperationCanceledException>(()=>owner.Service.ReadCancellationDecisionAsync(setup.Id,null,_=>Task.CompletedTask,stop.Token));
                else{var denied=await owner.Service.ReadCancellationDecisionAsync(setup.Id,null,_=>Task.CompletedTask);Assert.Equal(AccountingApiStatus.Denied,denied.Status);Assert.Null(denied.Resource);}
                Assert.True(fired);Assert.Equal(0,later);Assert.Equal(before,await CancellationSafetyCounts(run));Assert.Equal(0,owner.Writer.Writes);Assert.Equal(0,owner.Writer.Discards);owner.AssertClean();
            }
        }
    }
}
