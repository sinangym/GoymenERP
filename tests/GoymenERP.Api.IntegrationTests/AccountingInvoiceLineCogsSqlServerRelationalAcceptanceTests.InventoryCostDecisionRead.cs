using System.Text.Json;
using System.Text.Json.Nodes;
using GoymenERP.BuildingBlocks.Work;
using GoymenERP.Modules.Accounting.Application;
using GoymenERP.Modules.Accounting.Infrastructure.Persistence;
using GoymenERP.Modules.Accounting.Infrastructure.WorkInbox;

namespace GoymenERP.Api.IntegrationTests;

public sealed partial class AccountingInvoiceLineCogsSqlServerRelationalAcceptanceTests
{
    [SqlServerFact]
    public async Task Inventory_cost_two_purpose_readers_preserve_real_recorded_views_and_genuine_Posted_zero_or_Stale_original_receipts()
    {
        foreach (var purchase in new[]{false,true})
        foreach (var status in new[]{"Posted","RoundedNoCost","Stale"})
        {
            await using var run = await Run.Start(); var setup = await InventoryApprovalPrepare(run,purchase,status=="RoundedNoCost");
            await using var owner = new InventoryApprovalSqlScope(run,setup.Reader,setup.Profiles); var before = await InventoryApprovalCounts(run);
            var human = await owner.Service.ReadDecisionAsync(setup.Id,setup.Kind,_=>Task.CompletedTask);
            Assert.Equal(AccountingApiStatus.Ok,human.Status); var detail = Assert.IsType<AccountingInventoryCostDecisionDetailV1>(human.Resource);
            Assert.Equal("Prepared",detail.Proposal.Status); Assert.Equal(2L,detail.Proposal.Version); Assert.False(detail.SourceCancelled);
            Assert.Equal(before,await InventoryApprovalCounts(run));
            var fence = new AccountingWorkInboxFence(owner.Company,owner.User,owner.Authority);
            IWorkApprovalDecisionSourceV1 source = purchase ? new AccountingPurchaseCostDifferenceWorkInboxSourceV1(owner.Db,fence,owner.Service)
                : new AccountingInvoicedShipmentCostWorkInboxSourceV1(owner.Db,fence,owner.Service);
            var context = new WorkInboxSourceContextV1(Company,Checker.ToString("D"),owner.CorrelationId,null);
            var list = await source.ListPendingAsync(context,10,default); var item = Assert.Single(list.Items!);
            Assert.True(AccountingInventoryCostDecisionBindingV1.MatchesDecisionItem(item,detail));
            var snapshot = Assert.IsType<WorkApprovalDecisionSnapshotV1>(await source.ReadDecisionAsync(context,setup.Id,default));
            Assert.True(snapshot.Pending); Assert.Equal(JsonSerializer.Serialize(item,InventoryDecisionJson),JsonSerializer.Serialize(snapshot.Item,InventoryDecisionJson)); Assert.Equal((int)setup.Kind,snapshot.InventoryCostProof?.CostKind);
            Assert.DoesNotContain("inventoryCostProof",JsonSerializer.Serialize(snapshot,InventoryDecisionJson),StringComparison.OrdinalIgnoreCase);
            if(status=="Stale")setup.Profiles.Version++;
            var answer=await owner.Approve(setup.Id,setup.Kind,"decision-original");
            Assert.Equal(status=="Stale"?AccountingApiStatus.Conflict:AccountingApiStatus.Ok,answer.Status);
            Assert.Equal(status,answer.Resource?.Status);var receipt=Assert.IsType<AccountingInventoryCostApprovalReceiptV1>(answer.Resource?.ApprovalReceipt);
            Assert.Equal(2L,receipt.ExpectedVersion);Assert.Equal(3L,receipt.ResultVersion);Assert.Equal(owner.CorrelationId,receipt.OriginalCorrelationId);
            Assert.True(AccountingInventoryCostDecisionBindingV1.MatchesDecisionItem(item,answer.Resource,receipt));
            Assert.DoesNotContain("approvalReceipt",JsonSerializer.Serialize(answer,InventoryDecisionJson),StringComparison.OrdinalIgnoreCase);
            var counts=await InventoryApprovalCounts(run);var plans=owner.Plan.Reads;var currencies=owner.Currencies.Reads;
            setup.Profiles.Version++; // Original receipt verification never queries today's plan/profile or latest result.
            var replay=await owner.Approve(setup.Id,setup.Kind,"decision-original");
            Assert.True(replay.IsReplay);Assert.Equal(receipt,replay.Resource?.ApprovalReceipt);Assert.Equal(answer.Status,replay.Status);
            Assert.Equal(counts,await InventoryApprovalCounts(run));Assert.Equal(plans,owner.Plan.Reads);Assert.Equal(currencies,owner.Currencies.Reads);
            owner.User.Actor=Maker;
            Assert.Equal(AccountingApiStatus.Denied,(await owner.Service.ReadDecisionAsync(setup.Id,setup.Kind,_=>Task.CompletedTask)).Status);
            owner.AssertClean();
        }
    }
    [SqlServerFact]
    public async Task Inventory_cost_original_receipt_binds_actual_retained_command_and_kind_while_rejecting_private_or_unknown_json()
    {
        await using var run=await Run.Start();var setup=await InventoryApprovalPrepare(run);await using var owner=new InventoryApprovalSqlScope(run,setup.Reader,setup.Profiles);
        var answer=await owner.Approve(setup.Id,setup.Kind,"binding-original");Assert.NotNull(answer.Resource?.ApprovalReceipt);
        var key=AccountingInventoryCostServiceV1.InventoryCostApprovalKeyHash("binding-original");
        var row=Assert.Single(owner.Db.ChangeTracker.Entries<AccountingCommandRow>().Where(x=>x.Entity.CommandName==AccountingInventoryCostServiceV1.InventoryCostApproveCommand).Select(x=>x.Entity));
        var command=new AccountingCommandRecord(row.CommandName,row.IdempotencyKeyHash,row.ActorId,row.PayloadHash,row.ResultJson,row.CorrelationId);
        var store=InventoryDecisionStore(owner,setup);
        var facts=await store.ReadInventoryCostDecisionAsync(setup.Id,setup.Kind,3,_=>Task.CompletedTask);
        Assert.NotNull(facts);Assert.Equal(key,command.IdempotencyKeyHash);
        var original=AccountingInventoryCostServiceV1.BindInventoryCostApprovalReceipt(command,facts,answer,setup.Id,setup.Kind,2,"binding-original",Checker);
        Assert.Equal(answer.Resource?.ApprovalReceipt,original);
        foreach(var privateField in new[]{"unexpected","approvalReceipt"})
        {
            var altered=JsonNode.Parse(command.ResultJson)!.AsObject();altered["resource"]![privateField]=true;
            Assert.Null(AccountingInventoryCostServiceV1.BindInventoryCostApprovalReceipt(command with{ResultJson=altered.ToJsonString(InventoryDecisionJson)},facts,answer,setup.Id,setup.Kind,2,"binding-original",Checker));
        }
        Assert.Null(AccountingInventoryCostServiceV1.BindInventoryCostApprovalReceipt(command,facts,answer,setup.Id,AccountingInventoryCostKindV1.PurchaseInvoiceDifference,2,"binding-original",Checker));
        Assert.Null(AccountingInventoryCostServiceV1.BindInventoryCostApprovalReceipt(command,facts,answer,setup.Id,setup.Kind,3,"binding-original",Checker));
        Assert.Null(AccountingInventoryCostServiceV1.BindInventoryCostApprovalReceipt(command,facts,answer,setup.Id,setup.Kind,2,"another-key",Checker));
        var before=await InventoryApprovalCounts(run);var calls=0;
        Assert.Equal(AccountingApiStatus.Ok,(await owner.Service.ReadDecisionAsync(setup.Id,setup.Kind,_=>{calls++;return Task.CompletedTask;})).Status);
        foreach(var cancel in new[]{false,true})
        {
            var read=0;using var cancellation=new CancellationTokenSource();owner.Authority.Granted=true;
            Task Consumer(CancellationToken _){if(++read==calls){if(cancel)cancellation.Cancel();else owner.Authority.Granted=false;}return Task.CompletedTask;}
            if(cancel)await Assert.ThrowsAnyAsync<OperationCanceledException>(()=>owner.Service.ReadDecisionAsync(setup.Id,setup.Kind,Consumer,cancellation.Token));
            else Assert.Equal(AccountingApiStatus.Denied,(await owner.Service.ReadDecisionAsync(setup.Id,setup.Kind,Consumer)).Status);
            Assert.Equal(before,await InventoryApprovalCounts(run));owner.AssertClean();
        }
    }
    [SqlServerFact]
    public async Task Inventory_cost_new_original_receipt_read_fences_roll_back_native_effects_and_leave_same_key_retry_once()
    {
        // Exact Posted path phases documented in InventoryCostApprovalSafety: command4/5, retained reads6..22, receipt release23.
        foreach(var phase in new[]{4,5,7,10,13,16,19,21,22,23})
        {
            await using var run=await Run.Start();var setup=await InventoryApprovalPrepare(run);var before=await InventoryApprovalCounts(run);
            var save=new InventoryApprovalSave();await using var owner=new InventoryApprovalSqlScope(run,setup.Reader,setup.Profiles,save);
            var savedFences=0;var fired=false;
            owner.Authority.Read=()=>{if(save.SavedProposal&&++savedFences==phase){fired=true;return false;}return true;};
            var denied=await owner.Approve(setup.Id,setup.Kind,"receipt-fence");
            Assert.True(fired);Assert.Equal(AccountingApiStatus.Denied,denied.Status);Assert.Null(denied.Resource);
            Assert.Equal(before,await InventoryApprovalCounts(run));Assert.Equal(1,owner.Writer.Discards);owner.AssertClean();
            owner.Authority.Read=null;var accepted=await owner.Approve(setup.Id,setup.Kind,"receipt-fence");var replay=await owner.Approve(setup.Id,setup.Kind,"receipt-fence");
            Assert.Equal(AccountingApiStatus.Ok,accepted.Status);Assert.NotNull(accepted.Resource?.ApprovalReceipt);Assert.True(replay.IsReplay);
            Assert.Equal(accepted.Resource?.ApprovalReceipt,replay.Resource?.ApprovalReceipt);
            Assert.Equal(before with{Proposals=before.Proposals+1,Commands=before.Commands+1,Journals=before.Journals+1,Deliveries=before.Deliveries+1,Audits=before.Audits+2},await InventoryApprovalCounts(run));
            owner.AssertClean();
        }
    }
    private static readonly JsonSerializerOptions InventoryDecisionJson=new(JsonSerializerDefaults.Web);
    private static AccountingInventoryCostStoreV1 InventoryDecisionStore(InventoryApprovalSqlScope owner,InventoryApprovalSetup setup)
    {
        var core=new AccountingStore(owner.Db,owner.Company,owner.Collector,TimeProvider.System,new(setup.Profiles),normalApprovalAuditWriter:owner.Writer);
        return new(owner.Db,core,setup.Reader,new CogsMapping(),owner.Plan,owner.Currencies,new(setup.Profiles),owner.Company,owner.Collector,owner.Writer,TimeProvider.System);
    }
}
