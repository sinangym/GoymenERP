using System.Globalization;
using System.Reflection;
using GoymenERP.Api.WorkRuntime;
using GoymenERP.BuildingBlocks.Multitenancy;
using GoymenERP.BuildingBlocks.Observability;
using GoymenERP.BuildingBlocks.Security;
using GoymenERP.BuildingBlocks.Work;
using GoymenERP.Modules.Sales.Application;
using GoymenERP.Modules.Sales.Application.Contracts;
using GoymenERP.Modules.Sales.Infrastructure.Persistence;
using GoymenERP.Modules.Sales.Infrastructure.WorkInbox;
using GoymenERP.Modules.Work.Infrastructure.Delegations;
using static GoymenERP.Api.IntegrationTests.WorkSalesCustomerReturnDecisionPreviewV1Tests;

namespace GoymenERP.Api.IntegrationTests;

// Actual source/Host/WP3, controlled native protocol. No SQL, Audit integrity or owned-transaction acceptance claim.
public sealed class WorkSalesCustomerReturnDecisionHostTests
{
    private const string Kind = WorkDecisionHostV1.SalesCustomerReturn, Key = "SENTETIK-return-work", Corr = "SENTETIK-return-original";
    private static WorkDecisionRequestV1 Request(bool approve = true, string? reason = null) => new("2", approve, reason);
    [Theory] [InlineData(true, null)] [InlineData(false, null)] [InlineData(false, "")] [InlineData(false, "   ")] [InlineData(false, "  SENTETIK ret  ")]
    public async Task Two_current_human_reads_and_one_selected_command_keep_both_native_permissions_literal_body_and_original_receipt(bool approve, string? reason)
    {
        var r = new Rig(); var preview = await r.Preview(); var human = Assert.IsType<WorkSalesCustomerReturnDecisionPreviewV1>(preview.SalesCustomerReturn);
        Assert.Equal(("2", true, true, true), (preview.SourceVersion, preview.Pending, preview.ApprovalSupported, preview.RejectionSupported));
        Assert.Equal(r.Native.Current.CustomerReturn, human.CustomerReturn with { Lines = r.Native.Current.CustomerReturn.Lines }); Assert.Equal(2, r.Native.Reads);
        var reads = r.Native.Reads; var q = Request(approve, reason); var result = await r.Decide(q);
        Assert.Equal((approve ? "Approved" : "Rejected", false, Corr), (result.State, result.Replayed, result.OriginalCorrelationId));
        Assert.Equal(reads + 1, r.Native.Reads); Assert.Equal(1, r.Native.HistoryReads); Assert.Equal("2", r.Source.Selected);
        Assert.Equal(new SalesDraftStateCommandRequest("2", reason), r.Native.LastBody); Assert.Equal((Key, Corr, "SENTETIK-cause"),
            (r.Native.LastMetadata!.IdempotencyKey, r.Native.LastMetadata.CorrelationId, r.Native.LastMetadata.CausationId));
        Assert.Equal(1, r.Native.Effects); Assert.Single(r.Native.Commands);
        Assert.Contains(SalesPermissionManifestV1.ReturnsApprove, r.Authority.Permissions); Assert.Contains(SalesPermissionManifestV1.OrdersPriceRead, r.Authority.Permissions);
        Assert.DoesNotContain(SalesPermissionManifestV1.ReturnsRead, r.Authority.Permissions); Assert.DoesNotContain(SalesPermissionManifestV1.DraftsRead, r.Authority.Permissions);
    }
    [Theory] [InlineData(true)] [InlineData(false)]
    public async Task Native_preparer_and_submitter_are_both_refused_before_command_without_a_persona_exception(bool preparer)
    {
        var r = new Rig(); var v = r.Native.Current.CustomerReturn;
        r.Native.Current = Seal(r.Native.Current with { CustomerReturn = preparer ? v with { CreatedBy = Actor.ToString("D") } : v with { SubmittedBy = Actor.ToString("D") } });
        Assert.Equal(403, (await Assert.ThrowsAsync<WorkDecisionException>(() => r.Decide(Request()))).Status); Assert.Equal(0, r.Native.Calls); Assert.Equal(0, r.Native.Effects);
    }
    [Fact]
    public async Task Original_approved3_replay_returns_current_finalized4_natively_without_new_effect_or_human_GET()
    {
        var r = new Rig(); var q = Request(); await r.Decide(q); var original = r.Native.Commands[Key];
        r.Native.Current = Seal(r.Native.Current with { CustomerReturn = r.Native.Current.CustomerReturn with { State = "Finalized", Version = "4", FinalizedBy = Maker.ToString("D"), FinalizedAtUtc = At.AddHours(1), UpdatedAtUtc = At.AddHours(1) } });
        var reads = r.Native.Reads; r.SetCorrelation("SENTETIK-retry"); var result = await r.Decide(q);
        Assert.Equal(("Approved", true, Corr), (result.State, result.Replayed, result.OriginalCorrelationId)); Assert.Equal(reads, r.Native.Reads);
        Assert.Equal((2L, 3L, "4"), (original.ExpectedVersion, original.ResultVersion, r.Native.LastAnswer!.CustomerReturn.Version)); Assert.Equal(1, r.Native.Effects); Assert.Single(r.Native.Commands);
    }
    [Theory] [InlineData("missing")] [InlineData("actor")] [InlineData("company")] [InlineData("key")] [InlineData("payload")]
    [InlineData("version")] [InlineData("source")] [InlineData("digest")] [InlineData("response")] [InlineData("correlation")]
    public async Task Missing_or_forged_postnative_receipt_is_unknown_then_same_key_reconciles_the_retained_single_effect(string fault)
    {
        var r = new Rig(); r.Native.ChangeReceipt = p => fault switch { "missing" => null, "actor" => p with { ActorId = Maker.ToString("D") }, "company" => p with { CompanyId = Guid.NewGuid() },
            "key" => p with { IdempotencyKeyHash = new('A',64) }, "payload" => p with { PayloadHash = new('a',64) }, "version" => p with { ResultVersion = 4 }, "source" => p with { SourceFactsDigest = new('b',64) },
            "digest" => p with { OriginalDecisionDigest = new('c',64) }, "response" => p with { CurrentResponseDigest = new('d',64) }, _ => p with { OriginalCorrelationId = "SENTETIK-other" } };
        Assert.Equal(503, (await Assert.ThrowsAsync<WorkDecisionException>(() => r.Decide(Request()))).Status); Assert.Equal(1, r.Native.Effects); Assert.Single(r.Native.Commands);
        r.Native.ChangeReceipt = null; r.SetCorrelation("SENTETIK-reconcile"); var replay = await r.Decide(Request()); Assert.True(replay.Replayed); Assert.Equal(Corr,replay.OriginalCorrelationId); Assert.Equal(1,r.Native.Effects);
    }
    [Theory] [InlineData("approve-reason")] [InlineData("reject-long")] [InlineData("zero")] [InlineData("padded")] [InlineData("max")]
    [InlineData("floor")] [InlineData("quote")] [InlineData("cancel")]
    public async Task Invalid_or_foreign_body_is_refused_before_source_or_native_IO(string fault)
    {
        var r = new Rig(); var q = fault switch { "approve-reason" => Request(true,""), "reject-long" => Request(false,new('x',4001)), "zero" => Request() with { ExpectedVersion="0" },
            "padded" => Request() with { ExpectedVersion="02" }, "max" => Request() with { ExpectedVersion=long.MaxValue.ToString(CultureInfo.InvariantCulture) },
            "floor" => Request() with { SalesPriceFloor=new([]) }, "quote" => Request() with { SalesQuoteConversion=new(Id,1,0,"2") }, _ => Request() with { InventoryCostCancellation=new("1") } };
        Assert.Equal(400,(await Assert.ThrowsAsync<WorkDecisionException>(()=>r.Decide(q))).Status); Assert.Equal(0,r.Source.Reads); Assert.Equal(0,r.Native.Reads); Assert.Equal(0,r.Native.HistoryReads); Assert.Equal(0,r.Native.Calls);
    }
    [Theory] [InlineData("proof")] [InlineData("caption")] [InlineData("reference")] [InlineData("rule")] [InlineData("version")] [InlineData("company")] [InlineData("foreign")]
    public async Task Source_identity_rules_reference_company_and_foreign_carrier_must_bind_before_native_mutation(string fault)
    {
        var r = new Rig(); r.Source.Change = s => fault switch { "proof" => s with { SalesCustomerReturnProof=null }, "caption" => s with { Item=s.Item with { DocumentLabel="SENTETIK-other" } },
            "reference" => s with { Item=s.Item with { References=[] } }, "rule" => s with { Item=s.Item with { Rules=[new("sales.customer-return-approval-required",null,"1",null,null)] } },
            "version" => s with { SalesCustomerReturnProof=s.SalesCustomerReturnProof! with { SelectedExpectedVersion=3 } }, "company" => s with { CompanyId=Guid.NewGuid() },
            _ => s with { SalesPriceFloorProof=new(Company,Id,"SENTETIK","2",true,false,false,[],"", "",null,null) } };
        Assert.Equal(503,(await Assert.ThrowsAsync<WorkDecisionException>(()=>r.Decide(Request()))).Status); Assert.Equal(0,r.Native.Calls); Assert.Equal(0,r.Native.Effects);
    }
    [Theory] [InlineData(false,"approve")] [InlineData(true,"approve")] [InlineData(false,"price")] [InlineData(true,"price")]
    [InlineData(false,"delegation")] [InlineData(true,"delegation")] [InlineData(false,"actor")] [InlineData(true,"actor")]
    [InlineData(false,"company")] [InlineData(true,"company")] [InlineData(false,"member")] [InlineData(true,"member")] [InlineData(false,"cancel")] [InlineData(true,"cancel")]
    public async Task Current_authority_and_delegation_loss_before_or_after_retained_effect_never_releases_an_unauthorized_reply(bool retained,string fault)
    {
        var r=new Rig(); using var cts=new CancellationTokenSource(); void Revoke() { if(fault=="approve")r.Authority.Denied.Add(SalesPermissionManifestV1.ReturnsApprove); if(fault=="price")r.Authority.Denied.Add(SalesPermissionManifestV1.OrdersPriceRead);
            if(fault=="delegation")r.Delegations.Live=false; if(fault=="actor")r.User.ActorId=Maker; if(fault=="company")r.Company.Set(Guid.NewGuid()); if(fault=="member")r.User.Member=false; if(fault=="cancel")cts.Cancel(); }
        if(retained)r.Native.AfterEffect=Revoke;else r.Native.BeforeEffect=Revoke; var q=Request() with { DelegationId=Guid.NewGuid() };
        if(fault=="cancel")await Assert.ThrowsAnyAsync<OperationCanceledException>(()=>r.Decide(q,token:cts.Token)); else Assert.Equal(403,(await Assert.ThrowsAsync<WorkDecisionException>(()=>r.Decide(q,token:cts.Token))).Status);
        Assert.Equal(retained?1:0,r.Native.Effects); Assert.Equal(retained?1:0,r.Native.Commands.Count);
    }
    [Theory] [InlineData("facts")] [InlineData("price")] [InlineData("actor")] [InlineData("company")] [InlineData("cancel")]
    public async Task Second_human_owner_read_suppresses_changed_content_or_late_price_and_context_loss(string fault)
    {
        var r=new Rig();using var cts=new CancellationTokenSource(); r.Native.OnRead=n=>{if(n!=2)return; if(fault=="facts")r.Native.Current=Seal(r.Native.Current with { CustomerReturn=r.Native.Current.CustomerReturn with { CustomerDocumentNo="SENTETIK-other" } });
            if(fault=="price")r.Authority.Denied.Add(SalesPermissionManifestV1.OrdersPriceRead); if(fault=="actor")r.User.ActorId=Maker;if(fault=="company")r.Company.Set(Guid.NewGuid());if(fault=="cancel")cts.Cancel();};
        if(fault=="cancel")await Assert.ThrowsAnyAsync<OperationCanceledException>(()=>r.Host.PreviewAsync(Kind,Id,cts.Token));else Assert.Equal(fault=="facts"?503:403,(await Assert.ThrowsAsync<WorkDecisionException>(()=>r.Preview())).Status);
        Assert.Equal(2,r.Native.Reads);Assert.Equal(0,r.Native.Calls);
    }
    [Theory] [InlineData(404,"not_found")] [InlineData(409,"version_conflict")] [InlineData(422,"validation_failed")]
    public async Task Native_terminal_refusal_status_code_replay_and_original_correlation_are_preserved(int status,string code)
    {
        var r=new Rig();r.Native.Refusal=new(status,code,isReplay:true,originalCorrelationId:"SENTETIK-refusal");var e=await Assert.ThrowsAsync<WorkDecisionException>(()=>r.Decide(Request()));
        Assert.Equal((status,code,true,"SENTETIK-refusal"),(e.Status,e.Code,e.Replayed,e.OriginalCorrelationId));Assert.Equal(0,r.Native.Effects);
    }
    [Theory] [InlineData("approve")] [InlineData("price")] [InlineData("scope")]
    public async Task Both_companywide_native_permissions_are_required_before_any_source_IO(string fault)
    {
        var r=new Rig();if(fault=="scope")r.Authority.CompanyWide=false;else r.Authority.Denied.Add(fault=="approve"?SalesPermissionManifestV1.ReturnsApprove:SalesPermissionManifestV1.OrdersPriceRead);
        Assert.Equal(403,(await Assert.ThrowsAsync<WorkDecisionException>(()=>r.Decide(Request()))).Status);Assert.Equal(0,r.Source.Reads);Assert.Equal(0,r.Native.Reads);Assert.Equal(0,r.Native.Calls);
    }
    [Theory] [InlineData("missing-owner")] [InlineData("no-read")] [InlineData("legacy")]
    public async Task Missing_same_configured_read_owner_or_legacy_provenance_remains_unavailable(string fault)
    {
        var r=new Rig();if(fault=="legacy"){await r.Decide(Request());r.Native.Legacy=true;}else r.Host=r.BuildHost(fault=="missing-owner"?null:DispatchProxy.Create<ISalesCustomerReturnService,OwnerProxy>());
        Assert.Equal(503,(await Assert.ThrowsAsync<WorkDecisionException>(()=>r.Decide(Request()))).Status);Assert.Equal(fault=="legacy"?1:0,r.Native.Effects);
    }
    [Fact]
    public async Task Unknown_rejection_retains_exact_raw_body_for_original_key_and_rejects_trimmed_replacement()
    {
        var r=new Rig();var q=Request(false,"  SENTETIK ret  ");r.Native.ChangeReceipt=_=>null;Assert.Equal(503,(await Assert.ThrowsAsync<WorkDecisionException>(()=>r.Decide(q))).Status);
        r.Native.ChangeReceipt=null;Assert.Equal(409,(await Assert.ThrowsAsync<WorkDecisionException>(()=>r.Decide(q with { Reason=q.Reason!.Trim() }))).Status);
        Assert.True((await r.Decide(q)).Replayed);Assert.Equal(q.Reason,r.Native.LastBody!.Reason);Assert.Equal(1,r.Native.Effects);
    }
    [Theory] [InlineData(true)] [InlineData(false)]
    public async Task Granted_but_wrong_source_module_or_permission_is_broken_metadata_not_an_owner_command(bool wrongModule)
    {
        var r = new Rig(); if (wrongModule) r.Source.ModuleOverride = "purchasing";
        else { r.Source.PermissionOverride = SalesPermissionManifestV1.ReturnsWrite; r.Authority.Extra.Add(SalesPermissionManifestV1.ReturnsWrite); }
        Assert.Equal(503, (await Assert.ThrowsAsync<WorkDecisionException>(() => r.Decide(Request()))).Status);
        Assert.Equal(0, r.Native.Calls); Assert.Equal(0, r.Native.Effects);
    }
    [Fact]
    public async Task Current_native_replay_immutable_facts_must_match_original_source_even_if_a_bad_owner_rehashes_the_observed_reply()
    {
        var r = new Rig(); r.Native.ChangeAnswer = answer =>
        {
            var altered = answer with { CustomerReturn = answer.CustomerReturn with { CustomerDocumentNo = "SENTETIK-forged" } };
            return altered with { OriginalDecisionReceipt = altered.OriginalDecisionReceipt! with { CurrentResponseDigest = SalesCustomerReturnDecisionDigestsV1.CurrentResponseDigest(altered) } };
        };
        Assert.Equal(503, (await Assert.ThrowsAsync<WorkDecisionException>(() => r.Decide(Request()))).Status);
        Assert.Equal(1, r.Native.Effects); Assert.Single(r.Native.Commands);
    }
    [Fact]
    public async Task Null_native_current_view_after_a_retained_decision_is_unknown_not_an_unhandled_parser_failure()
    {
        var r = new Rig(); r.Native.ChangeAnswer = answer => answer with { CustomerReturn = null! };
        Assert.Equal(503, (await Assert.ThrowsAsync<WorkDecisionException>(() => r.Decide(Request()))).Status); Assert.Equal(1, r.Native.Effects);
    }
    public interface IReturnOwner:ISalesCustomerReturnService,ISalesCustomerReturnDecisionReadV1{}
    public class OwnerProxy:DispatchProxy { internal Func<MethodInfo,object?[],object?> Handler=(_,_)=>throw new NotSupportedException("No unrelated native command/general read");protected override object? Invoke(MethodInfo? m,object?[]? a)=>Handler(m!,a!); }
    private sealed class Rig
    {
        internal readonly MutableCompany Company=new();internal readonly MutableUser User=new();internal readonly Authority Authority=new();internal readonly Delegations Delegations=new();internal readonly Protocol Native;internal readonly SourceProxy Source;internal readonly IReturnOwner Owner;internal WorkDecisionHostV1 Host;
        private CorrelationContext correlation=new();private readonly WorkDecisionExecutionFenceV1 execution=new();private readonly WorkDecisionAuthorityV1 decorated;
        internal Rig(){Company.Set(WorkSalesCustomerReturnDecisionPreviewV1Tests.Company);correlation.Set(Corr,"SENTETIK-cause","Web",null,null);decorated=new(Authority,execution);Native=new(this,decorated);Owner=DispatchProxy.Create<IReturnOwner,OwnerProxy>();
            ((OwnerProxy)(object)Owner).Handler=(m,a)=>m.Name switch { nameof(ISalesCustomerReturnDecisionReadV1.ReadCustomerReturnDecisionAsync)=>Native.Read((Guid)a[0]!, (Func<CancellationToken,Task>)a[1]!, (CancellationToken)a[2]!),
                nameof(ISalesCustomerReturnDecisionReadV1.ReadCustomerReturnOriginalVersionAsync)=>Native.History((Guid)a[0]!, (string)a[1]!, (Func<CancellationToken,Task>)a[2]!, (CancellationToken)a[3]!),
                nameof(ISalesCustomerReturnService.ApproveCustomerReturnAsync)=>Native.Decide((Guid)a[0]!, (SalesDraftStateCommandRequest)a[1]!, (SalesCommandMetadata)a[2]!,true,(CancellationToken)a[3]!),
                nameof(ISalesCustomerReturnService.RejectCustomerReturnAsync)=>Native.Decide((Guid)a[0]!, (SalesDraftStateCommandRequest)a[1]!, (SalesCommandMetadata)a[2]!,false,(CancellationToken)a[3]!), _=>throw new NotSupportedException(m.Name)};
            Source=new(new SalesCustomerReturnWorkInboxSourceV1(null!,Company,User,decorated,Owner));Host=BuildHost(Owner);}
        internal WorkDecisionHostV1 BuildHost(ISalesCustomerReturnService? owner)=>new(Company,User,correlation,decorated,[Source],Delegations,execution,null!,null!,salesCustomerReturns:owner);
        internal void SetCorrelation(string c){correlation=new();correlation.Set(c,"SENTETIK-cause","Web",null,null);Host=BuildHost(Owner);}
        internal Task<WorkDecisionPreviewV1> Preview()=>Host.PreviewAsync(Kind,Id,default);
        internal Task<WorkDecisionResultV1> Decide(WorkDecisionRequestV1 q,string key=Key,CancellationToken token=default)=>Host.DecideAsync(Kind,Id,q,key,token);
    }
    private sealed class Protocol(Rig rig,IErpAuthorityRevalidationService authority)
    {
        internal SalesCustomerReturnDecisionDetailV1 Current=Facts();internal int Reads,HistoryReads,Calls,Effects;internal bool Legacy;internal Action<int>? OnRead;internal Action? BeforeEffect,AfterEffect;internal SalesDraftServiceException? Refusal;
        internal Func<SalesCustomerReturnCommandResult,SalesCustomerReturnCommandResult>? ChangeAnswer; internal SalesDraftStateCommandRequest? LastBody;internal SalesCommandMetadata? LastMetadata;internal SalesCustomerReturnCommandResult? LastAnswer;internal Func<SalesCustomerReturnOriginalReceiptV1,SalesCustomerReturnOriginalReceiptV1?>? ChangeReceipt;
        internal readonly Dictionary<string,SalesCustomerReturnOriginalReceiptV1> Commands=[];
        internal async Task<SalesCustomerReturnDecisionReadResultV1> Read(Guid id,Func<CancellationToken,Task> fence,CancellationToken ct)
        {await fence(ct);Reads++;OnRead?.Invoke(Reads);await fence(ct);if(id!=Id)return new(SalesCustomerReturnDecisionReadStateV1.NotFound,null);if(Current.CustomerReturn.CreatedBy==rig.User.UserId||Current.CustomerReturn.SubmittedBy==rig.User.UserId)return new(SalesCustomerReturnDecisionReadStateV1.Denied,null);return new(SalesCustomerReturnDecisionReadStateV1.Found,Current);}
        internal async Task<SalesCustomerReturnOriginalReadResultV1> History(Guid id,string version,Func<CancellationToken,Task> fence,CancellationToken ct)
        {await fence(ct);HistoryReads++;await fence(ct);if(Legacy)return new(SalesCustomerReturnDecisionReadStateV1.Unavailable,null);var rows=Commands.Values.Where(x=>x.ReturnId==id&&x.ActorId==rig.User.UserId&&x.ExpectedVersion.ToString(CultureInfo.InvariantCulture)==version).ToArray();
            return rows.Length==1?new(SalesCustomerReturnDecisionReadStateV1.Found,rows[0]){CurrentVersion=long.Parse(Current.CustomerReturn.Version,CultureInfo.InvariantCulture)}:new(SalesCustomerReturnDecisionReadStateV1.NotFound,null){CurrentVersion=long.Parse(Current.CustomerReturn.Version,CultureInfo.InvariantCulture)};}
        internal async Task<SalesCustomerReturnCommandResult> Decide(Guid id,SalesDraftStateCommandRequest body,SalesCommandMetadata m,bool approve,CancellationToken ct)
        {
            Calls++;LastBody=body;LastMetadata=m;var actor=rig.User.UserId;BeforeEffect?.Invoke();await Fence(actor,ct);if(Refusal is not null)throw Refusal;
            var command=approve?SalesCustomerReturnCommands.ApproveV1:SalesCustomerReturnCommands.RejectV1;var hash=approve?SalesCustomerReturnHash.Compute(command,id,body):SalesCustomerReturnHash.ComputeDecision(command,id,body);var replay=Commands.TryGetValue(m.IdempotencyKey,out var saved);
            if(replay&&(saved!.ActorId!=actor||saved.PayloadHash!=hash||saved.CommandName!=command))throw new SalesDraftServiceException(409,SalesDraftProblemCodesV1.SameKeyPayloadMismatch);
            if(!replay){if(Current.CustomerReturn.Version!=body.ExpectedVersion)throw new SalesDraftServiceException(409,SalesDraftProblemCodesV1.VersionConflict);var n=long.Parse(body.ExpectedVersion,CultureInfo.InvariantCulture);var v=Current.CustomerReturn;
                if(v.CreatedBy==actor||v.SubmittedBy==actor)throw new SalesDraftServiceException(409,SalesCustomerReturnProblemCodesV1.SelfApproval);
                var decided=v with {State=approve?"Approved":"Rejected",Version=(n+1).ToString(CultureInfo.InvariantCulture),ApprovedBy=approve?actor:null,ApprovedAtUtc=approve?At.AddMinutes(2):null,
                    RejectedBy=approve?null:actor,RejectedAtUtc=approve?null:At.AddMinutes(2),RejectionReason=approve||string.IsNullOrWhiteSpace(body.Reason)?null:body.Reason.Trim(),UpdatedAtUtc=At.AddMinutes(2)};
                Current=Seal(Current with {CustomerReturn=decided,ApprovalSupported=false,RejectionSupported=false});var p=new SalesCustomerReturnOriginalReceiptV1(WorkSalesCustomerReturnDecisionPreviewV1Tests.Company,id,command,actor,SalesStore.IdempotencyKeyHash(m.IdempotencyKey),hash,n,n+1,m.CorrelationId,At.AddMinutes(2),decided,Guid.NewGuid(),new('A',64),SalesCustomerReturnDecisionDigestsV1.SourceFactsDigest(WorkSalesCustomerReturnDecisionPreviewV1Tests.Company,decided),"");
                saved=p with {OriginalDecisionDigest=SalesCustomerReturnDecisionDigestsV1.OriginalDecisionDigest(p)};Commands.Add(m.IdempotencyKey,saved);Effects++;AfterEffect?.Invoke();}
            await Fence(actor,ct);var answer=new SalesCustomerReturnCommandResult(Current.CustomerReturn,replay,saved!.OriginalCorrelationId);var receipt=saved with {CurrentResponseDigest=SalesCustomerReturnDecisionDigestsV1.CurrentResponseDigest(answer)};
            var proven=answer with {OriginalDecisionReceipt=ChangeReceipt is null?receipt:ChangeReceipt(receipt)};LastAnswer=ChangeAnswer is null?proven:ChangeAnswer(proven);return LastAnswer;
        }
        private async Task Fence(string actor,CancellationToken ct)
        {foreach(var permission in new[]{SalesPermissionManifestV1.ReturnsApprove,SalesPermissionManifestV1.OrdersPriceRead}){ct.ThrowIfCancellationRequested();var p=await authority.RevalidatePermissionScopeAsync(permission,WorkSalesCustomerReturnDecisionPreviewV1Tests.Company,ct);ct.ThrowIfCancellationRequested();
            if(!p.IsGranted||!p.IsCompanyWide||rig.User.UserId!=actor||!rig.User.Member||rig.Company.CompanyId!=WorkSalesCustomerReturnDecisionPreviewV1Tests.Company)throw new SalesDraftServiceException(403,"forbidden");}}
    }
    private sealed class SourceProxy(IWorkApprovalDecisionVersionSourceV1 inner):IWorkApprovalDecisionVersionSourceV1
    {
        internal int Reads;internal string? Selected,ModuleOverride,PermissionOverride;internal Func<WorkApprovalDecisionSnapshotV1,WorkApprovalDecisionSnapshotV1>? Change;public string Module=>ModuleOverride??inner.Module;public string Kind=>inner.Kind;public string ApproverPermission=>PermissionOverride??inner.ApproverPermission;
        public Task<WorkInboxSourceResultV1> ListPendingAsync(WorkInboxSourceContextV1 c,int n,CancellationToken ct)=>throw new NotSupportedException();
        public async Task<WorkApprovalDecisionSnapshotV1?> ReadDecisionAsync(WorkInboxSourceContextV1 c,Guid id,CancellationToken ct){Reads++;var s=await inner.ReadDecisionAsync(c,id,ct);return s is null?null:Change?.Invoke(s)??s;}
        public async Task<WorkApprovalDecisionSnapshotV1?> ReadDecisionVersionAsync(WorkInboxSourceContextV1 c,Guid id,string v,CancellationToken ct){Reads++;Selected=v;var s=await inner.ReadDecisionVersionAsync(c,id,v,ct);return s is null?null:Change?.Invoke(s)??s;}
    }
    private sealed class MutableCompany:ICompanyContext{public Guid CompanyId{get;private set;}public bool IsResolved=>CompanyId!=Guid.Empty;public void Set(Guid id)=>CompanyId=id;}
    private sealed class MutableUser:ICurrentUser{internal Guid ActorId=Actor;internal bool Member=true;public bool IsAuthenticated=>true;public string UserId=>ActorId.ToString("D");public IReadOnlySet<Guid> AllowedCompanyIds{get;}=new HashSet<Guid>([WorkSalesCustomerReturnDecisionPreviewV1Tests.Company]);public bool CanAccessCompany(Guid c)=>Member&&c==WorkSalesCustomerReturnDecisionPreviewV1Tests.Company;}
    private sealed class Authority:IErpAuthorityRevalidationService{internal bool CompanyWide=true;internal readonly HashSet<string> Denied=[],Extra=[];internal readonly List<string> Permissions=[];
        public Task<PermissionScope> RevalidatePermissionScopeAsync(string p,Guid c,CancellationToken ct=default){Permissions.Add(p);return Task.FromResult(new PermissionScope(!Denied.Contains(p)&&(Extra.Contains(p)||p is SalesPermissionManifestV1.ReturnsApprove or SalesPermissionManifestV1.OrdersPriceRead),CompanyWide,new HashSet<string>(),new HashSet<Guid>(),new HashSet<Guid>(),new HashSet<Guid>()));}}
    private sealed class Delegations:IWorkDelegationReaderV1{internal bool Live=true;public Task<WorkDelegationViewV1?> FindForActorAsync(Guid id,WorkInboxItemV1 item,CancellationToken ct=default)=>Task.FromResult<WorkDelegationViewV1?>(Live?new(id,Company,Kind,SalesPermissionManifestV1.ReturnsApprove,Maker,Actor,At.AddYears(-1),At.AddYears(1),"1",Maker,At,"SENTETIK",null,null,null):null);}
}
