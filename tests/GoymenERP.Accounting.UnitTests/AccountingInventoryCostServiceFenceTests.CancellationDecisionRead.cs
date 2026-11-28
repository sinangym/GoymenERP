using GoymenERP.BuildingBlocks.Messaging;
using GoymenERP.Modules.Accounting.Application.Contracts;
using System.Text.Json;
using System.Text.Json.Nodes;
using GoymenERP.BuildingBlocks.Work;
using GoymenERP.Modules.Accounting.Application;
using GoymenERP.Modules.Accounting.Infrastructure.WorkInbox;
namespace GoymenERP.Accounting.UnitTests;
public sealed partial class AccountingInventoryCostServiceFenceTests
{
    [Theory]
    [InlineData(1L,1L)] [InlineData(9007199254740993L,9L)] [InlineData(long.MaxValue,long.MaxValue-1)]
    public async Task Cancellation_owner_retains_two_independent_versions_and_original_command_body(long outer,long cancellation)
    {
        var detail=CancellationDecisionDetail(outer,cancellation);var port=new CancellationDecisionProtocol(detail);var service=CancellationDecisionService(port);
        var human=await service.ReadCancellationDecisionAsync(SafetyId,null,_=>Task.CompletedTask);
        Assert.Equal(AccountingApiStatus.Ok,human.Status);Assert.Equal(outer,human.Resource?.OriginalProposal.Version);
        var first=await service.ApproveCancellationAsync(SafetyId,new(outer,cancellation),"receipt");
        var receipt=Assert.IsType<AccountingInventoryCostCancellationApprovalReceiptV1>(first.Resource?.CancellationApprovalReceipt);
        Assert.Equal(outer,receipt.ExpectedProposalVersion);Assert.Equal(outer,receipt.ResultProposalVersion);
        Assert.Equal(cancellation,receipt.ExpectedCancellationVersion);Assert.Equal(cancellation+1,receipt.ResultCancellationVersion);
        Assert.Equal(cancellation,port.Selected);Assert.Equal(detail.PreparedRecordedAtUtc,receipt.PreparedRecordedAtUtc);
        Assert.Equal(8,JsonSerializer.SerializeToElement(first,DecisionJson).EnumerateObject().Count());
        Assert.DoesNotContain("cancellationApprovalReceipt",JsonSerializer.Serialize(first,DecisionJson),StringComparison.OrdinalIgnoreCase);
        port.Current=detail with{OriginalProposal=detail.OriginalProposal with{Version=2},Pending=false};
        var replay=await service.ApproveCancellationAsync(SafetyId,new(outer,cancellation),"receipt");
        Assert.True(replay.IsReplay);Assert.Equal(receipt,replay.Resource?.CancellationApprovalReceipt);Assert.Equal(1,port.Effects);
        Assert.Equal(cancellation,port.Selected);Assert.Equal(outer,replay.Resource?.Version);
        Assert.Equal(AccountingApiCodes.IdempotencyConflict,(await service.ApproveCancellationAsync(SafetyId,new(outer,cancellation+1),"receipt")).Code);
    }
    [Theory]
    [InlineData("company")] [InlineData("actor")] [InlineData("member")] [InlineData("permission")] [InlineData("scope")] [InlineData("null-scope")] [InlineData("cancel")]
    public async Task Cancellation_owner_final_completed_consumer_cannot_release_changed_authority(string fault)
    {
        var port=new CancellationDecisionProtocol(CancellationDecisionDetail());var company=new MutableCompany();var user=new MutableUser();var authority=new SafetyAuthority();using var stop=new CancellationTokenSource();
        var service=CancellationDecisionService(port,company,user,authority);var calls=0;
        Task Consumer(CancellationToken _){if(++calls==4)Change(fault,company,user,authority,stop);return Task.CompletedTask;}
        if(fault=="cancel")await Assert.ThrowsAnyAsync<OperationCanceledException>(()=>service.ReadCancellationDecisionAsync(SafetyId,1,Consumer,stop.Token));
        else Assert.Equal(AccountingApiStatus.Denied,(await service.ReadCancellationDecisionAsync(SafetyId,1,Consumer)).Status);
        Assert.Empty(port.Commands);Assert.Equal(0,port.Effects);
    }
    [Theory]
    [InlineData("company")] [InlineData("outer")] [InlineData("cancel-id")] [InlineData("maker")] [InlineData("pending")] [InlineData("source")] [InlineData("hash")] [InlineData("facts")] [InlineData("time")]
    [InlineData("current-components-null")] [InlineData("current-components-empty")] [InlineData("current-component-null")]
    public async Task Cancellation_owner_malformed_retained_facts_fail_closed(string fault)
    {
        var d=CancellationDecisionDetail();d=fault switch{
            "company"=>d with{OriginalProposal=d.OriginalProposal with{CompanyId=Guid.NewGuid()}},
            "outer"=>d with{OriginalProposal=d.OriginalProposal with{ProposalId=Guid.NewGuid()}},
            "cancel-id"=>d with{Cancellation=d.Cancellation with{Id=Guid.NewGuid()}},
            "maker"=>d with{Cancellation=d.Cancellation with{ApprovedBy=d.PreparedCancellation.PreparedBy}},
            "pending"=>d with{Pending=true},"source"=>d with{SourceCancellation=d.SourceCancellation with{SourceDocumentId=Guid.NewGuid()}},
            "current-components-null"=>d with{Cancellation=d.Cancellation with{Components=null!}},
            "current-components-empty"=>d with{Cancellation=d.Cancellation with{Components=[]}},
            "current-component-null"=>d with{Cancellation=d.Cancellation with{Components=[null!]}},
            "hash"=>d with{PreviewDigest=new string('A',64)},"facts"=>d with{PreparedCancellation=d.PreparedCancellation with{Reason="other"}},
            _=>d with{PreparedRecordedAtUtc=d.PreparedRecordedAtUtc.ToOffset(TimeSpan.FromHours(1))}};
        Assert.Equal(AccountingApiStatus.Unavailable,(await CancellationDecisionService(new(d)).ReadCancellationDecisionAsync(SafetyId,1,_=>Task.CompletedTask)).Status);
    }
    [Theory]
    [InlineData("missing")] [InlineData("key")] [InlineData("actor")] [InlineData("payload")] [InlineData("extra-root")] [InlineData("extra-resource")] [InlineData("private")] [InlineData("version")] [InlineData("correlation")]
    public void Cancellation_original_receipt_requires_exact_native_command_and_complete_result(string fault)
    {
        var d=CancellationDecisionDetail();var answer=CancellationDecisionAnswer(d);var command=CancellationDecisionCommand(answer,d);AccountingCommandRecord? selected=command;
        if(fault=="missing")selected=null;else if(fault=="key")selected=command with{IdempotencyKeyHash=new string('B',64)};
        else if(fault=="actor")selected=command with{ActorId=Guid.NewGuid().ToString("D")};else if(fault=="payload")selected=command with{RequestHash=new string('B',64)};
        else if(fault=="correlation")answer=answer with{IsReplay=true,OriginalCorrelationId="other"};
        else{var json=JsonNode.Parse(command.ResultJson)!.AsObject();if(fault=="extra-root")json["extra"]=true;
            else if(fault=="version")json["resource"]!["version"]=8;else json["resource"]![fault=="private"?"cancellationApprovalReceipt":"extra"]=true;
            selected=command with{ResultJson=json.ToJsonString(DecisionJson)};}
        Assert.Null(AccountingInventoryCostServiceV1.BindCancellationApprovalReceipt(selected,d,answer,SafetyId,new(7,1),"receipt",Actor));
        Assert.NotNull(AccountingInventoryCostServiceV1.BindCancellationApprovalReceipt(command,d,CancellationDecisionAnswer(d),SafetyId,new(7,1),"receipt",Actor));
    }
    [Fact]
    public async Task Cancellation_maker_and_missing_owner_port_cannot_release_human_preview()
    {
        var detail=CancellationDecisionDetail();var maker=new MutableUser{UserId=detail.PreparedCancellation.PreparedBy.ToString("D")};
        Assert.Equal(AccountingApiStatus.Denied,(await CancellationDecisionService(new(detail),user:maker).ReadCancellationDecisionAsync(SafetyId,null,_=>Task.CompletedTask)).Status);
        var old=new AccountingInventoryCostServiceV1(new FailingOptionsStore(),new UnusedCommands(),new SafetyAuthority(),new MutableCompany(),new MutableUser(),SafetyCorrelation());
        Assert.Equal(AccountingApiStatus.Unavailable,(await old.ReadCancellationDecisionAsync(SafetyId,null,_=>Task.CompletedTask)).Status);
    }
    [Fact]
    public void Cancellation_scalar_snapshot_never_serializes_private_human_or_receipt_facts()
    {
        var d=CancellationDecisionDetail();var before=d.PreparedCancellation;
        var item=AccountingInventoryCostCancellationDecisionBindingV1.Item(SafetyId,before.CancellationId,before.PeriodId,before.PreparedBy,d.PreparedRecordedAtUtc)!;
        var snapshot=new WorkApprovalDecisionSnapshotV1(Company,item,"1",false){InventoryCostCancellationProof=new(Company,SafetyId,before.Id,before.CancellationId,7,1,before.PeriodId,before.PreparedBy,before.PreparedAtUtc,d.PreparedRecordedAtUtc,"Approved",d.SourceFactsDigest,d.PreviewDigest)};
        Assert.True(AccountingInventoryCostCancellationDecisionBindingV1.MatchesDecisionProof(item,snapshot.InventoryCostCancellationProof));
        Assert.True(AccountingInventoryCostCancellationDecisionBindingV1.MatchesDecisionItem(item,d));
        var json=JsonSerializer.Serialize(snapshot,DecisionJson);Assert.DoesNotContain(d.SourceFactsDigest,json,StringComparison.Ordinal);Assert.DoesNotContain(before.Reason,json,StringComparison.Ordinal);
        Assert.DoesNotContain("inventoryCostCancellationProof",json,StringComparison.OrdinalIgnoreCase);
    }
    [Theory]
    [InlineData("01")] [InlineData("+1")] [InlineData("1\n")] [InlineData("1\r")] [InlineData("1 ")]
    [InlineData("0")] [InlineData("-1")] [InlineData("9223372036854775808")]
    public async Task Cancellation_version_port_rejects_noncanonical_original_M_before_owner_read(string version)
    {
        var company=new MutableCompany();var user=new MutableUser();var authority=new SafetyAuthority();await using var db=SafetyDb(company);
        var source=new AccountingInventoryCostCancellationWorkInboxSourceV1(db,new(company,user,authority),CancellationDecisionService(new(CancellationDecisionDetail())));
        var context=new WorkInboxSourceContextV1(Company,Actor.ToString("D"),"SENTETIK",null);
        await Assert.ThrowsAsync<InvalidOperationException>(()=>source.ReadDecisionVersionAsync(context,SafetyId,version,default));
    }
    [Fact]
    public async Task Cancellation_version_port_retains_M_N_maker_and_timestamp_but_never_human_JSON()
    {
        var company=new MutableCompany();var user=new MutableUser();var authority=new SafetyAuthority();await using var db=SafetyDb(company);
        var d=CancellationDecisionDetail();var source=new AccountingInventoryCostCancellationWorkInboxSourceV1(db,new(company,user,authority),CancellationDecisionService(new(d)));
        var context=new WorkInboxSourceContextV1(Company,Actor.ToString("D"),"SENTETIK",null);
        var snapshot=Assert.IsType<WorkApprovalDecisionSnapshotV1>(await source.ReadDecisionVersionAsync(context,SafetyId,"1",default));
        Assert.Equal("1",snapshot.SourceVersion);Assert.False(snapshot.Pending);Assert.Equal(7L,snapshot.InventoryCostCancellationProof?.ProposalVersion);
        Assert.Equal(d.PreparedRecordedAtUtc,snapshot.Item.WaitingSinceUtc);Assert.Equal(d.PreparedCancellation.PreparedBy.ToString("D"),snapshot.Item.PreparedBy);
        Assert.DoesNotContain(d.PreparedCancellation.Reason,JsonSerializer.Serialize(snapshot,DecisionJson),StringComparison.Ordinal);
        var missing=new AccountingInventoryCostCancellationWorkInboxSourceV1(db,new(company,user,authority));
        await Assert.ThrowsAsync<InvalidOperationException>(()=>missing.ReadDecisionAsync(context,SafetyId,default));
    }
    [Fact]
    public async Task Cancellation_legacy_bare_original_answer_is_preserved_without_manufacturing_private_receipt()
    {
        var d=CancellationDecisionDetail();var port=new CancellationDecisionProtocol(d);
        var bare=new AccountingApiResult<AccountingInventoryCostProposalV1>(AccountingApiStatus.Ok,null,false,null);
        port.Commands.Add(CancellationDecisionCommand(bare,d));
        var answer=await CancellationDecisionService(port).ApproveCancellationAsync(SafetyId,new(7,1),"receipt");
        Assert.Equal(AccountingApiStatus.Ok,answer.Status);Assert.True(answer.IsReplay);Assert.Null(answer.Resource);
        Assert.Equal("SENTETIK-inventory-approval",answer.OriginalCorrelationId);Assert.Equal(0,port.Effects);Assert.Single(port.Commands);
    }
    [Theory]
    [InlineData(false,"\n")] [InlineData(false,"\r")] [InlineData(false,"\r\n")]
    [InlineData(true,"\n")] [InlineData(true,"\r")] [InlineData(true,"\r\n")]
    public void Cancellation_private_digest_exact_hex_rejects_same_length_line_end_replacements(bool preview,string ending)
    {
        var d=CancellationDecisionDetail();var c=d.PreparedCancellation;var item=AccountingInventoryCostCancellationDecisionBindingV1.Item(SafetyId,c.CancellationId,c.PeriodId,c.PreparedBy,d.PreparedRecordedAtUtc)!;
        var malformed=new string('A',64-ending.Length)+ending;
        var proof=new WorkInventoryCostCancellationProofV1(Company,SafetyId,c.Id,c.CancellationId,7,1,c.PeriodId,c.PreparedBy,c.PreparedAtUtc,d.PreparedRecordedAtUtc,"Approved",preview?d.SourceFactsDigest:malformed,preview?malformed:d.PreviewDigest);
        Assert.False(AccountingInventoryCostCancellationDecisionBindingV1.MatchesDecisionProof(item,proof));
    }
    [Fact]
    public void Cancellation_item_original_result_binding_rejects_changed_human_facts_with_unchanged_receipt()
    {
        var d=CancellationDecisionDetail();var answer=CancellationDecisionAnswer(d);var command=CancellationDecisionCommand(answer,d);
        var receipt=Assert.IsType<AccountingInventoryCostCancellationApprovalReceiptV1>(AccountingInventoryCostServiceV1.BindCancellationApprovalReceipt(command,d,answer,SafetyId,new(7,1),"receipt",Actor));
        var item=AccountingInventoryCostCancellationDecisionBindingV1.Item(SafetyId,d.PreparedCancellation.CancellationId,d.PreparedCancellation.PeriodId,d.PreparedCancellation.PreparedBy,d.PreparedRecordedAtUtc)!;
        Assert.True(AccountingInventoryCostCancellationDecisionBindingV1.MatchesDecisionItem(item,answer.Resource,receipt));
        var altered=answer.Resource! with{ComponentCancellation=d.Cancellation with{Reason="SENTETIK changed after original receipt"}};
        Assert.False(AccountingInventoryCostCancellationDecisionBindingV1.MatchesDecisionItem(item,altered,receipt));
        Assert.False(AccountingInventoryCostCancellationDecisionBindingV1.MatchesDecisionItem(item with{References=[]},answer.Resource,receipt));
    }
    private static AccountingInventoryCostCancellationDecisionDetailV1 CancellationDecisionDetail(long outer=7,long version=1)
    {
        var p=DecisionSource(AccountingInventoryCostKindV1.SalesInvoicedShipment,"Posted").Proposal with{Version=outer};var currency=Guid.NewGuid();
        if(outer==1)p=p with{Status="AlreadyAccounted",Preparation=null,ApprovedBy=null,ApprovedAtUtc=null,JournalId=null};
        var before=new InventoryCostCancellationV1(Guid.NewGuid(),Company,SafetyId,Guid.NewGuid(),version,"Prepared",1,Guid.NewGuid(),new(2026,11,28),new(2026,11,27),"SENTETIK retained cancellation reason",Guid.NewGuid(),DecisionAt,
            [new(new string('B',64),"SENTETIK",Guid.NewGuid(),Guid.NewGuid(),"SENTETIK purpose",currency,"600","600",Guid.NewGuid(),Guid.NewGuid(),new string('A',64))],[new(currency,Guid.NewGuid(),1,2)]);
        var approved=before with{Version=version+1,Status="Approved",ApprovedBy=Actor,ApprovedAtUtc=DecisionAt.AddMinutes(1),Components=before.Components.Select(x=>x with{ReversingJournalId=Guid.NewGuid()}).ToArray()};
        var source=new AccountingInventoryCostCancellationSourceV1(Company,before.CancellationId,AccountingSourceDocumentKindsV1.SalesInvoice,p.AdmissionId,p.InvoiceId,p.InvoiceVersion,Guid.NewGuid(),DecisionAt.AddDays(-1),"SENTETIK source cancellation",new string('A',64),"SENTETIK-source",DecisionAt.AddDays(-1).AddSeconds(1));
        var d=new AccountingInventoryCostCancellationDecisionDetailV1(p,before,approved,DecisionAt.AddSeconds(5),source,"",""){Pending=false};
        return d with{SourceFactsDigest=AccountingInventoryCostServiceV1.CancellationSourceFactsDigest(d),PreviewDigest=AccountingInventoryCostServiceV1.CancellationPreviewDigest(d)};
    }
    private static AccountingApiResult<AccountingInventoryCostProposalV1> CancellationDecisionAnswer(AccountingInventoryCostCancellationDecisionDetailV1 d)=>new(AccountingApiStatus.Ok,d.OriginalProposal with{Status="CancelledByComponentReversal",CancellationId=d.Cancellation.CancellationId,ComponentCancellation=d.Cancellation},false,null);
    private static AccountingCommandRecord CancellationDecisionCommand(AccountingApiResult<AccountingInventoryCostProposalV1> answer,AccountingInventoryCostCancellationDecisionDetailV1 d)=>new(AccountingInventoryCostServiceV1.InventoryCostCancellationApproveCommand,AccountingInventoryCostServiceV1.InventoryCostApprovalKeyHash("receipt"),Actor.ToString("D"),AccountingInventoryCostServiceV1.CancellationApprovalPayloadHash(SafetyId,new(d.OriginalProposal.Version,d.PreparedCancellation.Version)),JsonSerializer.Serialize(answer,DecisionJson),"SENTETIK-inventory-approval");
    private static AccountingInventoryCostServiceV1 CancellationDecisionService(CancellationDecisionProtocol port,MutableCompany? company=null,MutableUser? user=null,SafetyAuthority? authority=null)=>new(port,port,authority??new SafetyAuthority(),company??new MutableCompany(),user??new MutableUser(),SafetyCorrelation());
    // Synthetic native command protocol, not a relational transaction or durable audit acceptance.
    private sealed class CancellationDecisionProtocol(AccountingInventoryCostCancellationDecisionDetailV1 detail):IAccountingInventoryCostStoreV1,IAccountingInventoryCostCancellationDecisionStoreV1,IAccountingCommandLog
    {
        internal AccountingInventoryCostCancellationDecisionDetailV1? Current;internal long? Selected;internal int Effects;internal List<AccountingCommandRecord> Commands{get;}=[];
        public async Task<AccountingInventoryCostCancellationDecisionDetailV1?> ReadCancellationDecisionAsync(Guid id,long? version,Func<CancellationToken,Task> fence,CancellationToken token=default)
        {await fence(token);Selected=version;await fence(token);return version is null?Current??detail:detail;}
        public async Task<AccountingApiResult<AccountingInventoryCostProposalV1>> RunCancellationApprovalAsync(Func<bool,Task<AccountingApiResult<AccountingInventoryCostProposalV1>>> attempt,Func<CancellationToken,Task> fence,CancellationToken token=default)
        {await fence(token);var answer=await attempt(false);await fence(token);return answer;}
        public async Task<AccountingInventoryCostProposalV1> ApproveCancellationAsync(Guid id,ApproveInventoryCostCancellationV1 request,Guid actor,Func<CancellationToken,Task> fence,AccountingCommandMetadata metadata,CancellationToken token=default)
        {await fence(token);Effects++;var p=CancellationDecisionAnswer(detail).Resource!;Commands.Add(metadata.Completion!(p));return p;}
        public Task<AccountingCommandRecord?> FindCommandAsync(string command,string key,CancellationToken token=default)=>Task.FromResult(Commands.SingleOrDefault(x=>x.CommandName==command&&x.IdempotencyKeyHash==key));
        public Task<AccountingInventoryCostProposalV1> ApproveAsync(Guid id,AccountingInventoryCostKindV1 kind,long version,Guid actor,Func<CancellationToken,Task> fence,AccountingCommandMetadata metadata,CancellationToken token=default)=>throw new NotSupportedException();
        public Task RecordCommandAsync(AccountingCommandRecord record, CancellationToken token = default) => throw new NotSupportedException();
        public Task RecordAttemptAsync(AccountingCommandRecord record, Func<CancellationToken, Task> fence, CancellationToken token = default) => throw new NotSupportedException();
        public Task AcceptAsync(WorkInboxEventV1 envelope, CancellationToken token = default) => throw new NotSupportedException();
        public Task<IReadOnlyList<AccountingInventoryCostProposalV1>> ListAsync(AccountingInventoryCostKindV1 kind, CancellationToken token = default) => throw new NotSupportedException();
        public Task<InvoiceLineCogsOptionsV1> OptionsAsync(CancellationToken token = default) => throw new NotSupportedException();
        public Task<AccountingInventoryCostProposalV1> PrepareAsync(Guid id, AccountingInventoryCostKindV1 kind, PrepareAccountingInventoryCostV1 request, Guid actor, Func<CancellationToken, Task> fence, AccountingCommandMetadata metadata, CancellationToken token = default) => throw new NotSupportedException();
        public Task<AccountingInventoryCostProposalV1> ProposeReversalAsync(Guid id, ProposeAccountingInventoryCostReversalV1 request, Guid actor, Func<CancellationToken, Task> fence, AccountingCommandMetadata metadata, CancellationToken token = default) => throw new NotSupportedException();
    }
}
