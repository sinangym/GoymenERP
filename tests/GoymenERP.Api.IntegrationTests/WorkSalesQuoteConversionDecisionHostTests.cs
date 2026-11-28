using System.Globalization;
using GoymenERP.Api.WorkRuntime;
using GoymenERP.BuildingBlocks.Multitenancy;
using GoymenERP.BuildingBlocks.Observability;
using GoymenERP.BuildingBlocks.Security;
using GoymenERP.BuildingBlocks.Work;
using GoymenERP.Modules.Sales.Application.Contracts;
using GoymenERP.Modules.Sales.Infrastructure.Persistence;
using GoymenERP.Modules.Sales.Infrastructure.WorkInbox;
using GoymenERP.Modules.Work.Infrastructure.Delegations;
using static GoymenERP.Api.IntegrationTests.WorkSalesQuoteConversionDecisionPreviewV1Tests;

namespace GoymenERP.Api.IntegrationTests;

// Real Work host/source/WP3 decorator with a controlled native protocol. Counters below are not SQL/Audit/TX acceptance.
public sealed class WorkSalesQuoteConversionDecisionHostTests
{
    [Theory] [InlineData(true)] [InlineData(false)]
    public async Task Foreign_floor_private_carrier_is_refused_before_any_native_decision(bool post)
    {
        var r = new Rig();
        r.Source.Change = s => s with { SalesPriceFloorProof = new(Company, Id, "SENTETIK-FLOOR", "1",
            true, false, false, [], "", "", null, null) };
        var failure = await Assert.ThrowsAsync<WorkDecisionException>(async () =>
        {
            if (post) _ = await r.Decide(Request(true));
            else _ = await r.Preview();
        });
        Assert.Equal(503, failure.Status);
        Assert.Equal(0, r.Native.Calls);
        Assert.Equal(0, r.Native.Effects);
    }

    private const string Kind="sales.quote-conversion-request.v1",Key="SENTETIK-quote-work-key",Correlation="SENTETIK-quote-work-original",Reason="  SENTETIK ret  ";

    [Theory] [InlineData(true)] [InlineData(false)]
    public async Task GET_two_exact_reads_POST_one_source_read_one_bound_command_and_two_native_version_axes(bool approve)
    {
        var r=new Rig();var preview=await r.Preview();var human=Assert.IsType<WorkSalesQuoteConversionDecisionPreviewV1>(preview.SalesQuoteConversion);
        Assert.Equal(("9","1",true,true,true),(preview.SourceVersion,human.Request.Version,preview.Pending,preview.ApprovalSupported,preview.RejectionSupported));Assert.Equal(2,r.Native.Reads);
        var q=Request(approve);var before=r.Native.Reads;var result=await r.Decide(q);
        Assert.Equal(approve?"Approved":"Rejected",result.State);Assert.False(result.Replayed);Assert.Equal(Correlation,result.OriginalCorrelationId);
        Assert.Equal(before+1,r.Native.Reads);Assert.Equal(1,r.Native.Calls);Assert.Equal(1,r.Native.Effects);Assert.Single(r.Native.Commands);
        Assert.Equal(new SalesQuoteConversionTargetV1(Id,Quote,3,0,"1"),r.Native.LastTarget);Assert.Equal(approve?"9":"1",r.Native.LastVersion);
        Assert.Equal(approve?null:Reason,r.Native.LastReason);Assert.Equal(Key,r.Native.LastMetadata!.IdempotencyKey);Assert.Equal((Correlation,"SENTETIK-cause"),(r.Native.LastMetadata.CorrelationId,r.Native.LastMetadata.CausationId));
        var json=System.Text.Json.JsonSerializer.SerializeToElement(q,Json);Assert.Equal(5,json.EnumerateObject().Count());Assert.True(json.TryGetProperty("salesQuoteConversion",out var selection));Assert.Equal(4,selection.EnumerateObject().Count());
        Assert.All(r.Authority.Permissions,p=>Assert.Equal(SalesPermissionManifestV1.QuotesExpiredConversionApprove,p));
    }

    [Fact]
    public async Task Derived_Approved_rejects_with_request_version_and_old_approval_replay_keeps_original_target_and_decision()
    {
        var r=new Rig();var original=await r.Decide(Request(true));var approved=await r.Preview();
        Assert.False(approved.Pending);Assert.False(approved.ApprovalSupported);Assert.True(approved.RejectionSupported);Assert.Equal(("10","1"),(approved.SourceVersion,approved.SalesQuoteConversion!.Request.Version));
        Assert.Equal("Rejected",(await r.Decide(Request(false),"SENTETIK-other-key")).State);
        var terminal=await r.Preview();Assert.False(terminal.Pending);Assert.False(terminal.ApprovalSupported);Assert.False(terminal.RejectionSupported);
        r.SetCorrelation("SENTETIK-reconcile");var replay=await r.Decide(Request(true));Assert.Equal(("Approved",true,original.OriginalCorrelationId),(replay.State,replay.Replayed,replay.OriginalCorrelationId));
        Assert.Equal(("10","2","Rejected"),(r.Native.Current.Quote.Quote.Version,r.Native.Current.Request.Version,r.Native.Current.Request.Status));Assert.Equal(2,r.Native.Effects);
        Assert.Equal(503,(await Assert.ThrowsAsync<WorkDecisionException>(()=>r.Decide(Request(true) with{SalesQuoteConversion=new(Quote,3,0,"2")}))).Status);
        Assert.Equal(2,r.Native.Effects);Assert.Equal(2,r.Native.Commands.Count);
    }

    [Fact]
    public async Task Native_stale_request_is_not_pending_but_can_reject_and_original_rejection_replay_is_not_a_second_decision()
    {
        var r=new Rig();var d=r.Native.Current;r.Native.Current=Seal(d with{Request=d.Request with{Status="Stale"},Quote=d.Quote with{Quote=d.Quote.Quote with{State="Lost"}},ApprovalSupported=false});
        var preview=await r.Preview();Assert.False(preview.Pending);Assert.False(preview.ApprovalSupported);Assert.True(preview.RejectionSupported);
        var result=await r.Decide(Request(false));Assert.Equal("Rejected",result.State);var humanReads=r.Native.Reads;
        r.SetCorrelation("SENTETIK-rejected-reconcile");var replay=await r.Decide(Request(false));
        Assert.Equal(("Rejected",true,result.OriginalCorrelationId),(replay.State,replay.Replayed,replay.OriginalCorrelationId));
        Assert.Equal(humanReads+1,r.Native.Reads);Assert.Equal(1,r.Native.Effects);Assert.Single(r.Native.Commands);
    }

    [Fact]
    public async Task Nonincrementable_quote_version_does_not_disable_the_separate_incrementable_request_rejection()
    {
        var r=new Rig();r.Native.Current=Facts(quoteVersion:long.MaxValue.ToString(CultureInfo.InvariantCulture));var preview=await r.Preview();
        Assert.False(preview.ApprovalSupported);Assert.True(preview.RejectionSupported);Assert.Equal("1",preview.SalesQuoteConversion!.Request.Version);
        Assert.Equal("Rejected",(await r.Decide(Request(false))).State);Assert.Equal("1",r.Native.LastVersion);Assert.Equal(1,r.Native.Effects);
    }

    [Fact]
    public async Task SAT_D44_preparer_selfapproval_uses_only_existing_approve_permission_and_raw_4000_reason_is_preserved()
    {
        var r=new Rig();r.User.Actor=Maker;Assert.True((await r.Preview()).ApprovalSupported);Assert.Equal("Approved",(await r.Decide(Request(true))).State);
        var reason=" \n"+new string('x',3996)+"  ";Assert.Equal(4000,reason.Length);
        Assert.Equal("Rejected",(await r.Decide(Request(false) with{Reason=reason},"SENTETIK-maker-reject")).State);Assert.Equal(reason,r.Native.LastReason);
        Assert.All(r.Authority.Permissions,p=>Assert.Equal(SalesPermissionManifestV1.QuotesExpiredConversionApprove,p));Assert.Equal(2,r.Native.Effects);
    }

    [Theory] [InlineData("0")] [InlineData("01")] [InlineData("1\n")] [InlineData("+1")] [InlineData("9223372036854775807")] [InlineData("9223372036854775808")]
    public async Task Invalid_or_nonincrementable_action_versions_are_rejected_before_source_or_owner_IO(string version)
    {
        var r=new Rig();foreach(var approve in new[]{true,false})Assert.Equal(400,(await Assert.ThrowsAsync<WorkDecisionException>(()=>r.Decide(Request(approve) with{ExpectedVersion=version}))).Status);
        Assert.Equal(0,r.Source.Reads);Assert.Equal(0,r.Native.Calls);
    }

    [Theory] [InlineData("missing")] [InlineData("quote-id")] [InlineData("number")] [InlineData("revision")] [InlineData("request-version")]
    [InlineData("approve-note")] [InlineData("blank-reject")] [InlineData("long-reject")] [InlineData("reject-quote-version")] [InlineData("foreign-selection")]
    public async Task Strict_frozen_selection_and_native_reason_rules_fail_before_dispatch(string fault)
    {
        var r=new Rig();var q=Request(true);q=fault switch{
            "missing"=>q with{SalesQuoteConversion=null},"quote-id"=>q with{SalesQuoteConversion=new(Guid.Empty,3,0,"1")},"number"=>q with{SalesQuoteConversion=new(Quote,0,0,"1")},
            "revision"=>q with{SalesQuoteConversion=new(Quote,3,-1,"1")},"request-version"=>q with{SalesQuoteConversion=new(Quote,3,0,"01")},"approve-note"=>q with{Reason="note"},
            "blank-reject"=>Request(false) with{Reason=" \t "},"long-reject"=>Request(false) with{Reason=new('x',4001)},"reject-quote-version"=>Request(false) with{ExpectedVersion="9"},
            _=>q with{ApprovedSourceCatalogSelection=new("0")}};
        Assert.Equal(400,(await Assert.ThrowsAsync<WorkDecisionException>(()=>r.Decide(q))).Status);Assert.Equal(0,r.Source.Reads);Assert.Equal(0,r.Native.Calls);
    }

    [Theory] [InlineData("reference")] [InlineData("rule")] [InlineData("company")] [InlineData("source-version")] [InlineData("pending")]
    [InlineData("foreign-proof")] [InlineData("selection-id")]
    public async Task Validatable_private_source_identity_or_frozen_target_faults_never_reach_the_native_command(string fault)
    {
        var r=new Rig();r.Source.Change=s=>fault switch{
            "reference"=>s with{Item=s.Item with{References=[s.Item.References[0] with{RecordId=Guid.NewGuid()}]}},"rule"=>s with{Item=s.Item with{Rules=[s.Item.Rules[0] with{Actual="1"}]}},
            "company"=>s with{SalesQuoteConversionProof=s.SalesQuoteConversionProof! with{CompanyId=Guid.NewGuid()}},"source-version"=>s with{SourceVersion="1"},"pending"=>s with{Pending=false},
            "foreign-proof"=>s with{SalesRequestProof=new(Company,Id,"SENTETIK",Guid.NewGuid(),Guid.NewGuid(),new(2026,11,28),At,"1","Submitted",true,true,new('a',64),new('b',64))},_=>s};
        var q=fault=="selection-id"?Request(true) with{SalesQuoteConversion=new(Guid.NewGuid(),3,0,"1")}:Request(true);
        Assert.Equal(503,(await Assert.ThrowsAsync<WorkDecisionException>(()=>r.Decide(q))).Status);Assert.Equal(0,r.Native.Calls);Assert.Equal(0,r.Native.Effects);
    }

    [Theory] [InlineData("missing")] [InlineData("actor")] [InlineData("key")] [InlineData("payload")] [InlineData("digest")] [InlineData("correlation")]
    public async Task Postnative_receipt_refusal_is_unknown_and_same_frozen_key_reconciles_with_original_correlation_once(string fault)
    {
        var r=new Rig();r.Native.ChangeReceipt=p=>fault switch{"missing"=>null,"actor"=>p with{ActorId=Maker.ToString("D")},"key"=>p with{IdempotencyKeyHash=new('a',64)},
            "payload"=>p with{PayloadHash=new('b',64)},"digest"=>p with{OriginalDecisionDigest=new('c',64)},_=>p with{OriginalCorrelationId="different"}};
        Assert.Equal(503,(await Assert.ThrowsAsync<WorkDecisionException>(()=>r.Decide(Request(true)))).Status);Assert.Equal(1,r.Native.Effects);Assert.Single(r.Native.Commands);
        r.Native.ChangeReceipt=null;r.SetCorrelation("SENTETIK-new-request");var replay=await r.Decide(Request(true));Assert.Equal((true,"Approved",Correlation),(replay.Replayed,replay.State,replay.OriginalCorrelationId));
        Assert.Equal(1,r.Native.Effects);Assert.Single(r.Native.Commands);
    }

    [Theory] [InlineData(false,"permission")] [InlineData(true,"permission")] [InlineData(false,"delegation")] [InlineData(true,"delegation")]
    [InlineData(false,"cancel")] [InlineData(true,"cancel")] [InlineData(false,"actor")] [InlineData(true,"actor")] [InlineData(false,"company")] [InlineData(true,"company")]
    public async Task Current_authority_delegation_actor_company_and_normal_completion_cancel_are_fenced(bool afterEffect,string fault)
    {
        var r=new Rig();using var cancellation=new CancellationTokenSource();Action revoke=()=>{
            if(fault=="permission")r.Authority.Allowed=false;if(fault=="delegation")r.Delegations.Live=false;if(fault=="cancel")cancellation.Cancel();
            if(fault=="actor")r.User.Actor=Maker;if(fault=="company")r.Company.Set(Guid.NewGuid());};
        if(afterEffect)r.Native.AfterEffect=revoke;else r.Native.BeforeAuthority=revoke;
        var q=Request(true) with{DelegationId=Guid.NewGuid()};
        if(fault=="cancel")await Assert.ThrowsAnyAsync<OperationCanceledException>(()=>r.Decide(q,token:cancellation.Token));
        else Assert.Equal(403,(await Assert.ThrowsAsync<WorkDecisionException>(()=>r.Decide(q,token:cancellation.Token))).Status);
        Assert.Equal(afterEffect?1:0,r.Native.Effects);Assert.Equal(afterEffect?1:0,r.Native.Commands.Count);
        // AfterEffect models an already-retained success; this controlled protocol does not claim native SQL rollback.
    }

    [Fact]
    public async Task Optional_owner_absence_foreign_carrier_and_foreign_selection_fail_closed_without_another_human_read()
    {
        var r=new Rig();Assert.Equal(503,(await Assert.ThrowsAsync<WorkDecisionException>(()=>r.BuildHost(null).PreviewAsync(Kind,Id,default))).Status);
        r.Source.OtherKind=WorkDecisionHostV1.SalesOrder;r.Authority.AllowOther=true;
        r.Source.Change=s=>s with{Item=s.Item with{Kind=WorkDecisionHostV1.SalesOrder,PreparedBy=Maker.ToString("D"),Rules=[],References=[]}};
        Assert.Equal(503,(await Assert.ThrowsAsync<WorkDecisionException>(()=>r.Host.PreviewAsync(WorkDecisionHostV1.SalesOrder,Id,default))).Status);
        var reads=r.Source.Reads;Assert.Equal(400,(await Assert.ThrowsAsync<WorkDecisionException>(()=>r.Host.DecideAsync(WorkDecisionHostV1.SalesOrder,Id,Request(true),Key,default))).Status);
        Assert.Equal(reads,r.Source.Reads);Assert.Equal(0,r.Native.Effects);
    }

    [Theory] [InlineData(409,SalesDraftProblemCodesV1.VersionConflict)] [InlineData(409,SalesQuoteProblemCodesV1.ExpiryAlreadyApproved)] [InlineData(422,"validation_failed")]
    public async Task Native_refusals_never_become_Work_success(int status,string code)
    {var r=new Rig();r.Native.Refusal=new(status,code,isReplay:true,originalCorrelationId:"SENTETIK-old-refusal");var e=await Assert.ThrowsAsync<WorkDecisionException>(()=>r.Decide(Request(true)));Assert.Equal((status,code,true,"SENTETIK-old-refusal"),(e.Status,e.Code,e.Replayed,e.OriginalCorrelationId));Assert.Equal(0,r.Native.Effects);}

    [Fact]
    public async Task Every_other_supported_kind_rejects_the_quote_frozen_selection_before_source_IO()
    {
        var r=new Rig();foreach(var kind in WorkDecisionHostV1.Kinds.Where(k=>k!=Kind))
            Assert.Equal(400,(await Assert.ThrowsAsync<WorkDecisionException>(()=>r.Host.DecideAsync(kind,Id,Request(true),Key,default))).Status);
        Assert.Equal(0,r.Source.Reads);Assert.Equal(0,r.Native.Reads);Assert.Equal(0,r.Native.Calls);
    }

    [Theory] [InlineData("facts")] [InlineData("permission")] [InlineData("cancel")]
    public async Task Second_human_read_cannot_release_changed_facts_late_denial_or_normal_completed_cancellation(string fault)
    {
        var r=new Rig();using var cancellation=new CancellationTokenSource();r.Native.OnRead=n=>{if(n!=2)return;
            if(fault=="facts")r.Native.Current=Seal(r.Native.Current with{Quote=r.Native.Current.Quote with{Note="SENTETIK changed human facts"}});
            if(fault=="permission")r.Authority.Allowed=false;if(fault=="cancel")cancellation.Cancel();};
        if(fault=="cancel")await Assert.ThrowsAnyAsync<OperationCanceledException>(()=>r.Host.PreviewAsync(Kind,Id,cancellation.Token));
        else Assert.Equal(fault=="permission"?403:503,(await Assert.ThrowsAsync<WorkDecisionException>(()=>r.Preview())).Status);
        Assert.Equal(2,r.Native.Reads);Assert.Equal(0,r.Native.Calls);Assert.Equal(0,r.Native.Effects);
    }

    [Fact]
    public async Task Optional_price_scope_is_observed_after_the_last_completed_approve_fence_without_requiring_general_read()
    {
        var baseline=new Rig();baseline.Native.Current=VisibleFacts();var visible=await baseline.Preview();
        Assert.Equal("00012.3400",visible.SalesQuoteConversion!.Quote.Lines[0].UnitPrice);
        var lastApprove=baseline.Authority.Permissions.FindLastIndex(p=>p==SalesPermissionManifestV1.QuotesExpiredConversionApprove)+1;
        Assert.True(lastApprove>0);Assert.Equal(SalesPermissionManifestV1.OrdersPriceRead,baseline.Authority.Permissions[^1]);
        var r=new Rig();r.Native.Current=VisibleFacts();r.Authority.OnRead=(permission,n)=>{
            if(permission==SalesPermissionManifestV1.QuotesExpiredConversionApprove && n==lastApprove)r.Authority.Prices=false;};
        Assert.Equal(503,(await Assert.ThrowsAsync<WorkDecisionException>(()=>r.Preview())).Status);
        Assert.Equal(SalesPermissionManifestV1.OrdersPriceRead,r.Authority.Permissions[^1]);Assert.Equal(0,r.Native.Calls);Assert.Equal(0,r.Native.Effects);
    }

    private static WorkDecisionRequestV1 Request(bool approve)=>new(approve?"9":"1",approve,approve?null:Reason){SalesQuoteConversion=new(Quote,3,0,"1")};
    private sealed class Rig
    {
        internal readonly MutableCompany Company=new();internal readonly MutableUser User=new();internal readonly Authority Authority=new();internal readonly Delegations Delegations=new();
        internal readonly Native Native;internal readonly SourceProxy Source;internal WorkDecisionHostV1 Host;private CorrelationContext correlation=new();
        private readonly WorkDecisionExecutionFenceV1 execution=new();private readonly WorkDecisionAuthorityV1 decorated;
        internal Rig(){Company.Set(WorkSalesQuoteConversionDecisionPreviewV1Tests.Company);correlation.Set(Correlation,"SENTETIK-cause","Web",null,null);decorated=new(Authority,execution);
            Native=new(this,decorated);Source=new(new SalesQuoteConversionRequestWorkInboxSourceV1(null!,Company,User,decorated,Native));Host=BuildHost(Native);}
        internal WorkDecisionHostV1 BuildHost(ISalesQuoteConversionDecisionReadV1? owner)=>new(Company,User,correlation,decorated,[Source],Delegations,execution,null!,null!,salesQuoteConversions:owner);
        internal void SetCorrelation(string value){correlation=new();correlation.Set(value,"SENTETIK-cause","Web",null,null);Host=BuildHost(Native);}
        internal Task<WorkDecisionPreviewV1> Preview()=>Host.PreviewAsync(Kind,Id,default);
        internal Task<WorkDecisionResultV1> Decide(WorkDecisionRequestV1 q,string key=Key,CancellationToken token=default)=>Host.DecideAsync(Kind,Id,q,key,token);
    }
    private sealed record Saved(string Hash,string Actor,SalesQuoteOriginalDecisionReceiptV1 Receipt);
    private sealed class Native(Rig rig,IErpAuthorityRevalidationService authority):ISalesQuoteConversionDecisionReadV1
    {
        internal SalesQuoteConversionDecisionDetailV1 Current=Facts();internal int Reads,Calls,Effects;internal Action? BeforeAuthority,AfterEffect;internal Action<int>? OnRead;
        internal Func<SalesQuoteOriginalDecisionReceiptV1,SalesQuoteOriginalDecisionReceiptV1?>? ChangeReceipt;internal SalesDraftServiceException? Refusal;
        internal SalesQuoteConversionTargetV1? LastTarget;internal string? LastVersion,LastReason;internal SalesCommandMetadata? LastMetadata;
        internal readonly Dictionary<(string Command,string Key),Saved> Commands=[];
        public async Task<SalesQuoteDecisionReadResultV1> ReadQuoteConversionDecisionAsync(Guid id,Func<CancellationToken,Task> fence,CancellationToken ct=default)
        {await fence(ct);Reads++;OnRead?.Invoke(Reads);await fence(ct);return id==Id?new(SalesQuoteDecisionReadStateV1.Found,Current):new(SalesQuoteDecisionReadStateV1.NotFound,null);}
        public async Task<SalesQuoteResponse> ApproveSelectedQuoteConversionAsync(SalesQuoteConversionTargetV1 target,SalesQuoteExpiryApprovalRequest body,SalesCommandMetadata m,CancellationToken ct=default)
        {
            var (saved,replay)=await Execute(target,body.ExpectedVersion,body.Note,m,true,ct);var answer=new SalesQuoteResponse(Current.Quote,replay,saved.Receipt.OriginalCorrelationId);
            var p=saved.Receipt with{CurrentResponseDigest=SalesQuoteDecisionDigestsV1.CurrentResponseDigest(answer)};return answer with{OriginalDecisionReceipt=ChangeReceipt is null?p:ChangeReceipt(p)};
        }
        public async Task<SalesApprovalRequestCommandResult> RejectSelectedQuoteConversionAsync(SalesQuoteConversionTargetV1 target,SalesDraftStateCommandRequest body,SalesCommandMetadata m,CancellationToken ct=default)
        {
            var (saved,replay)=await Execute(target,body.ExpectedVersion,body.Reason,m,false,ct);var answer=new SalesApprovalRequestCommandResult(Current.Request,replay,saved.Receipt.OriginalCorrelationId);
            var p=saved.Receipt with{CurrentResponseDigest=SalesQuoteDecisionDigestsV1.CurrentResponseDigest(answer)};return answer with{OriginalDecisionReceipt=ChangeReceipt is null?p:ChangeReceipt(p)};
        }
        private async Task<(Saved Saved,bool Replay)> Execute(SalesQuoteConversionTargetV1 target,string version,string? reason,SalesCommandMetadata m,bool approve,CancellationToken ct)
        {
            Calls++;LastTarget=target;LastVersion=version;LastReason=reason;LastMetadata=m;var actor=rig.User.UserId;BeforeAuthority?.Invoke();await Fence(actor,ct);if(Refusal is not null)throw Refusal;
            var command=approve?SalesQuoteCommands.ApproveExpiredConversionV1:SalesApprovalRequestCommands.RejectQuoteConversionV1;
            var hash=approve?SalesQuoteHash.Compute(target.QuoteId,new SalesQuoteExpiryApprovalRequest(version,reason)):SalesApprovalRequestHash.Compute(command,target.QuoteId,target.RequestNo,new(version,reason));
            var replay=Commands.TryGetValue((command,m.IdempotencyKey),out var saved);
            if(replay && (saved!.Hash!=hash || saved.Actor!=actor))throw new SalesDraftServiceException(409,SalesDraftProblemCodesV1.SameKeyPayloadMismatch);
            if(!replay)
            {
                if(target!=SalesQuoteConversionDecisionBindingV1.Target(SalesQuoteConversionDecisionBindingV1.Proof(Current)))throw new SalesDraftServiceException(503,"service_unavailable");
                if(version!=(approve?Current.Quote.Quote.Version:Current.Request.Version))throw new SalesDraftServiceException(409,SalesDraftProblemCodesV1.VersionConflict);
                if(!(approve?Current.ApprovalSupported:Current.RejectionSupported))throw new SalesDraftServiceException(409,SalesDraftProblemCodesV1.StateConflict);
                var result=(long.Parse(version,CultureInfo.InvariantCulture)+1).ToString(CultureInfo.InvariantCulture);
                if(approve)
                {var quote=Current.Quote with{Quote=Current.Quote.Quote with{Version=result},Expiry=Current.Quote.Expiry with{IsApproved=true},
                    ExpiryApprovals=[new(Current.Request.SubjectRevision,Current.Quote.Quote.ValidUntil!.Value,new(2026,11,28),actor,At,reason)]};
                    Current=Seal(Current with{Quote=quote,Request=Current.Request with{Status="Approved"},ApprovalSupported=false});}
                else Current=Seal(Current with{Request=Current.Request with{Version=result,Status="Rejected",DecidedBy=actor,DecidedAtUtc=At,DecisionReason=reason},ApprovalSupported=false,RejectionSupported=false});
                Effects++;var p=new SalesQuoteOriginalDecisionReceiptV1(WorkSalesQuoteConversionDecisionPreviewV1Tests.Company,target,command,actor,SalesStore.IdempotencyKeyHash(m.IdempotencyKey),hash,version,result,
                    approve?"Approved":"Rejected",At,reason,m.CorrelationId,Guid.NewGuid(),new('a',64),"","");p=p with{OriginalDecisionDigest=SalesQuoteDecisionDigestsV1.OriginalDecisionDigest(p)};
                saved=new(hash,actor,p);Commands.Add((command,m.IdempotencyKey),saved);AfterEffect?.Invoke();
            }
            await Fence(actor,ct);return(saved!,replay);
        }
        private async Task Fence(string actor,CancellationToken ct)
        {ct.ThrowIfCancellationRequested();var p=await authority.RevalidatePermissionScopeAsync(SalesPermissionManifestV1.QuotesExpiredConversionApprove,WorkSalesQuoteConversionDecisionPreviewV1Tests.Company,ct);ct.ThrowIfCancellationRequested();
            if(!p.IsGranted || !p.IsCompanyWide || rig.User.UserId!=actor || !rig.User.Member || rig.Company.CompanyId!=WorkSalesQuoteConversionDecisionPreviewV1Tests.Company)throw new SalesDraftServiceException(403,"forbidden");}
    }
    private sealed class SourceProxy(IWorkApprovalDecisionSourceV1 inner):IWorkApprovalDecisionSourceV1
    {
        internal int Reads;internal string? OtherKind;internal Func<WorkApprovalDecisionSnapshotV1,WorkApprovalDecisionSnapshotV1>? Change;
        public string Module=>"sales";public string Kind=>OtherKind??inner.Kind;public string ApproverPermission=>OtherKind is null?inner.ApproverPermission:SalesPermissionManifestV1.OrdersApprove;
        public Task<WorkInboxSourceResultV1> ListPendingAsync(WorkInboxSourceContextV1 c,int n,CancellationToken ct)=>throw new InvalidOperationException("No pending fallback");
        public async Task<WorkApprovalDecisionSnapshotV1?> ReadDecisionAsync(WorkInboxSourceContextV1 c,Guid id,CancellationToken ct){Reads++;var s=await inner.ReadDecisionAsync(c,id,ct);return s is null?null:Change?.Invoke(s)??s;}
    }
    private sealed class MutableCompany:ICompanyContext{public Guid CompanyId{get;private set;}public bool IsResolved=>CompanyId!=Guid.Empty;public void Set(Guid id)=>CompanyId=id;}
    private sealed class MutableUser:ICurrentUser{internal Guid Actor=WorkSalesQuoteConversionDecisionPreviewV1Tests.Actor;internal bool Member=true;public bool IsAuthenticated=>true;public string UserId=>Actor.ToString("D");public IReadOnlySet<Guid> AllowedCompanyIds{get;}=new HashSet<Guid>([WorkSalesQuoteConversionDecisionPreviewV1Tests.Company]);public bool CanAccessCompany(Guid id)=>Member && id==WorkSalesQuoteConversionDecisionPreviewV1Tests.Company;}
    private sealed class Authority:IErpAuthorityRevalidationService
    {
        internal bool Allowed=true,Prices=true,AllowOther;internal readonly List<string> Permissions=[];internal Action<string,int>? OnRead;
        public Task<PermissionScope> RevalidatePermissionScopeAsync(string p,Guid c,CancellationToken ct=default)
        {
            Permissions.Add(p);var granted=Allowed&&(AllowOther || p==SalesPermissionManifestV1.QuotesExpiredConversionApprove || p==SalesPermissionManifestV1.OrdersPriceRead && Prices);
            OnRead?.Invoke(p,Permissions.Count);return Task.FromResult(new PermissionScope(granted,true,new HashSet<string>(),new HashSet<Guid>(),new HashSet<Guid>(),new HashSet<Guid>()));
        }
    }
    private sealed class Delegations:IWorkDelegationReaderV1
    {internal bool Live=true;public Task<WorkDelegationViewV1?> FindForActorAsync(Guid id,WorkInboxItemV1 item,CancellationToken ct=default)=>Task.FromResult<WorkDelegationViewV1?>(Live?new(id,Company,Kind,SalesPermissionManifestV1.QuotesExpiredConversionApprove,Maker,Actor,At.AddYears(-1),At.AddYears(1),"1",Maker,At,"SENTETIK",null,null,null):null);}
}
