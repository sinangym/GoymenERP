using System.Globalization;
using System.Text.Json;
using GoymenERP.Api.WorkRuntime;
using GoymenERP.BuildingBlocks.Multitenancy;
using GoymenERP.BuildingBlocks.Observability;
using GoymenERP.BuildingBlocks.Security;
using GoymenERP.BuildingBlocks.Work;
using GoymenERP.Modules.Sales.Application.Contracts;
using GoymenERP.Modules.Sales.Domain.SalesContracts;
using GoymenERP.Modules.Sales.Infrastructure.Persistence;
using GoymenERP.Modules.Sales.Infrastructure.WorkInbox;
using GoymenERP.Modules.Work.Infrastructure.Delegations;
using static GoymenERP.Api.IntegrationTests.WorkSalesContractDiscountDecisionPreviewV1Tests;

namespace GoymenERP.Api.IntegrationTests;

// Real Work source/host/WP3 decorator with a controlled native protocol. No SQL, actual Audit integrity or transaction acceptance claim.
public sealed class WorkSalesContractDiscountDecisionHostTests
{
    private const string Kind = "sales.contract-discount.v1", Key = "SENTETIK-contract-work-key", Corr = "SENTETIK-contract-work-original";
    private static WorkDecisionRequestV1 Request(bool approve = true) => new("1", approve, approve ? null : "SENTETIK ret");
    [Theory] [InlineData(true, true)] [InlineData(true, false)] [InlineData(false, true)] [InlineData(false, false)]
    public async Task Human_GET_preserves_native_facts_and_optional_disclosure_POST_has_exact_native_body_and_no_second_human_read(bool approve, bool prices)
    {
        var r = new Rig(); r.Authority.Prices = prices; var preview = await r.Preview(); var human = Assert.IsType<WorkSalesContractDiscountDecisionPreviewV1>(preview.SalesContractDiscount);
        Assert.Equal(("1", true, true, true), (preview.SourceVersion, preview.Pending, preview.ApprovalSupported, preview.RejectionSupported)); Assert.Equal(2, r.Native.Reads);
        Assert.Equal(2, human.Contract.Installments.Count); Assert.Equal(2, human.Contract.DiscountRules.Count); Assert.Equal(prices ? "3000.25" : null, human.Contract.PlannedTotalAmount);
        Assert.Equal(prices ? SalesPriceDisclosure.Visible : SalesPriceDisclosure.Withheld, human.Contract.AmountDisclosure);
        var reads = r.Native.Reads; var q = Request(approve); var answer = await r.Decide(q);
        Assert.Equal((approve ? "Active" : "DiscountRejected", false, Corr), (answer.State, answer.Replayed, answer.OriginalCorrelationId)); Assert.Equal(reads + 1, r.Native.Reads); Assert.Equal(0, r.Native.HistoryReads);
        Assert.Equal(new SalesDraftStateCommandRequest(q.ExpectedVersion, q.Reason), r.Native.LastBody); Assert.Equal(Id, r.Native.LastId); Assert.Equal(approve, r.Native.LastApprove);
        Assert.Equal((Key, Corr, "SENTETIK-cause"), (r.Native.LastMetadata!.IdempotencyKey, r.Native.LastMetadata.CorrelationId, r.Native.LastMetadata.CausationId));
        Assert.Equal(1, r.Native.Effects); var receipt = Assert.Single(r.Native.Commands).Value;
        Assert.Equal(SalesContractHash.Compute(approve ? SalesContractCommands.ApproveDiscountV1 : SalesContractCommands.RejectDiscountV1, Id, r.Native.LastBody!), receipt.PayloadHash);
        Assert.DoesNotContain(SalesPermissionManifestV1.ContractsRead, r.Authority.Permissions);
        using var work = JsonDocument.Parse(JsonSerializer.Serialize(answer, Json)); Assert.Equal(6, work.RootElement.EnumerateObject().Count());
        using var wire = JsonDocument.Parse(JsonSerializer.Serialize(preview, Json)); Assert.True(wire.RootElement.TryGetProperty("salesContractDiscount", out var typed)); Assert.Equal(4, typed.EnumerateObject().Count());
        foreach (var hidden in new[] { "sourceFactsDigest", "previewDigest", "salesContractDiscountProof", "originalDecisionReceipt", "payloadHash" }) Assert.DoesNotContain(hidden, wire.RootElement.GetRawText(), StringComparison.OrdinalIgnoreCase);
    }
    [Theory] [InlineData(true)] [InlineData(false)]
    public async Task Canonical_original_key_recovery_uses_original_decision_version_and_correlation_after_current_terminal_preview(bool approve)
    {
        var r = new Rig(); var q = Request(approve); await r.Decide(q); var original = Assert.Single(r.Native.Commands).Value;
        r.Native.Current = Seal(r.Native.Current.Source!, Today.AddYears(-1)); var current = await r.Preview();
        Assert.False(current.Pending); Assert.False(current.ApprovalSupported); Assert.False(current.RejectionSupported); Assert.False(current.SalesContractDiscount!.Contract.IsInEffectToday);
        Assert.Equal("2", current.SourceVersion); r.SetCorrelation("SENTETIK-current-retry"); var reads = r.Native.Reads;
        var answer = await r.Decide(q); Assert.Equal((true, approve ? "Active" : "DiscountRejected", Corr), (answer.Replayed, answer.State, answer.OriginalCorrelationId));
        Assert.Equal(reads + 1, r.Native.Reads); Assert.Equal(1, r.Native.HistoryReads); Assert.Equal(1, r.Native.Effects); Assert.Single(r.Native.Commands);
        Assert.Equal(original.OriginalDecisionDigest, Assert.Single(r.Native.Commands).Value.OriginalDecisionDigest);
        Assert.Equal(("1", "2", true, false), (r.Source.Last!.SourceVersion, r.Source.Last.SalesContractDiscountProof!.CurrentVersion, r.Source.Last.SalesContractDiscountProof.Historical, r.Source.Last.Pending));
    }
    [Theory] [InlineData(9L)] [InlineData(9007199254740993L)] [InlineData(long.MaxValue-1)]
    public async Task Native_Int64_expected_and_result_versions_are_exact_and_terminal_Max_replays_the_old_key_without_current_version_substitution(long version)
    {
        var r=new Rig();r.Native.Current=Facts(version);var text=version.ToString(CultureInfo.InvariantCulture);var q=Request() with {ExpectedVersion=text};
        Assert.Equal(text,(await r.Preview()).SalesContractDiscount!.Contract.Version);var first=await r.Decide(q);Assert.Equal("Active",first.State);
        var receipt=Assert.Single(r.Native.Commands).Value;Assert.Equal(text,receipt.ExpectedVersion);Assert.Equal((version+1).ToString(CultureInfo.InvariantCulture),receipt.ResultVersion);
        r.SetCorrelation("SENTETIK-large-retry");Assert.True((await r.Decide(q)).Replayed);Assert.Equal(1,r.Native.Effects);Assert.Equal(text,r.Native.LastBody!.ExpectedVersion);
    }
    [Theory]
    [InlineData("missing")] [InlineData("actor")] [InlineData("company")] [InlineData("key")] [InlineData("payload")] [InlineData("expected")]
    [InlineData("result")] [InlineData("before")] [InlineData("after")] [InlineData("digest")] [InlineData("response")] [InlineData("correlation")]
    public async Task Bad_postnative_receipt_suppresses_answer_then_same_key_reconciles_exactly_one_durable_effect(string fault)
    {
        var r = new Rig(); r.Native.ChangeReceipt = p => fault switch
        {
            "missing" => null, "actor" => p with { ActorId = Maker.ToString("D") }, "company" => p with { CompanyId = Guid.NewGuid() }, "key" => p with { IdempotencyKeyHash = new('A',64) },
            "payload" => p with { PayloadHash = new('b',64) }, "expected" => p with { ExpectedVersion = "2" }, "result" => p with { ResultVersion = "3" },
            "before" => p with { Before = Facts(2).Source! }, "after" => p with { After = p.Before }, "digest" => p with { OriginalDecisionDigest = new('c',64) },
            "response" => p with { CurrentResponseDigest = new('d',64) }, _ => p with { OriginalCorrelationId = "other" }
        };
        Assert.Equal(503, (await Assert.ThrowsAsync<WorkDecisionException>(() => r.Decide(Request()))).Status); Assert.Equal(1,r.Native.Effects); Assert.Single(r.Native.Commands);
        r.Native.ChangeReceipt = null; r.SetCorrelation("SENTETIK-reconcile"); var replay = await r.Decide(Request());
        Assert.True(replay.Replayed); Assert.Equal(Corr,replay.OriginalCorrelationId); Assert.Equal(1,r.Native.Effects); Assert.Single(r.Native.Commands);
    }
    [Theory] [InlineData("source-digest")] [InlineData("amount-rule")]
    public async Task Valid_shape_monetary_or_source_tamper_is_postnative_unknown_and_reconciliation_does_not_claim_rollback(string fault)
    {
        var r = new Rig(); r.Source.Change = s => fault == "source-digest"
            ? s with { SalesContractDiscountProof = s.SalesContractDiscountProof! with { SourceFactsDigest = new('a',64) } }
            : s with { Item = s.Item with { Rules = s.Item.Rules.Select(x => x with { Actual = "6" }).ToArray() } };
        Assert.Equal(503, (await Assert.ThrowsAsync<WorkDecisionException>(() => r.Decide(Request()))).Status); Assert.Equal(1,r.Native.Effects); Assert.Single(r.Native.Commands);
        r.Source.Change = null; r.SetCorrelation("SENTETIK-reconcile"); Assert.True((await r.Decide(Request())).Replayed); Assert.Equal(1,r.Native.Effects); Assert.Single(r.Native.Commands);
    }
    [Theory] [InlineData("zero")] [InlineData("padded-version")] [InlineData("max")] [InlineData("approve-reason")]
    [InlineData("reject-null")] [InlineData("reject-blank")] [InlineData("reject-long")] [InlineData("reject-leading")]
    [InlineData("reject-trailing")] [InlineData("quote")] [InlineData("discount")] [InlineData("floor")] [InlineData("loss")] [InlineData("period")] [InlineData("cancellation")]
    public async Task Malformed_or_noncanonical_Work_request_is_400_before_source_and_native_IO(string fault)
    {
        var r = new Rig(); var q = Request(); q = fault switch
        {
            "zero" => q with { ExpectedVersion="0" }, "padded-version" => q with { ExpectedVersion="01" }, "max" => q with { ExpectedVersion=long.MaxValue.ToString(CultureInfo.InvariantCulture) },
            "approve-reason" => q with { Reason="SENTETIK" }, "reject-null" => q with { Approve=false }, "reject-blank" => q with { Approve=false,Reason=" \t " },
            "reject-long" => q with { Approve=false,Reason=new('x',SalesContract.MaximumReasonLength+1) }, "reject-leading" => q with { Approve=false,Reason=" SENTETIK ret" }, "reject-trailing" => q with { Approve=false,Reason="SENTETIK ret\n" },
            "quote" => q with { SalesQuoteConversion=new(Id,1,0,"1") }, "discount" => q with { SalesDiscount=new([]) }, "floor" => q with { SalesPriceFloor=new([]) },
            "loss" => q with { InventoryTransferLoss=new(1,At) }, "period" => q with { NormalJournalDraft=new(Id) }, _ => q with { InventoryCostCancellation=new("1") }
        };
        Assert.Equal(400,(await Assert.ThrowsAsync<WorkDecisionException>(()=>r.Decide(q))).Status); Assert.Equal((0,0,0,0),(r.Source.Reads,r.Native.Reads,r.Native.HistoryReads,r.Native.Calls));
    }
    [Theory] [InlineData("missing")] [InlineData("caption")] [InlineData("reference")] [InlineData("rule-shape")] [InlineData("maker")]
    [InlineData("version")] [InlineData("company")] [InlineData("permission")] [InlineData("digest-shape")] [InlineData("foreign")]
    public async Task Identity_formatter_permission_pin_or_private_proof_mismatch_has_zero_command_effects(string fault)
    {
        var r = new Rig(); r.Source.Change = s => fault switch
        {
            "missing" => s with { SalesContractDiscountProof=null }, "caption" => s with { Item=s.Item with { DocumentLabel="other" } },
            "reference" => s with { Item=s.Item with { References=s.Item.References.Select(x=>x with { RecordId=Guid.NewGuid() }).ToArray() } },
            "rule-shape" => s with { Item=s.Item with { Rules=s.Item.Rules.Select(x=>x with { Unit="kg" }).ToArray() } },
            "maker" => s with { SalesContractDiscountProof=s.SalesContractDiscountProof! with { CreatedBy="other" } }, "version" => s with { SourceVersion="2" },
            "company" => s with { CompanyId=Guid.NewGuid() }, "digest-shape" => s with { SalesContractDiscountProof=s.SalesContractDiscountProof! with { PreviewDigest="bad" } },
            "foreign" => s with { SalesRiskExceptionProof=SalesRiskExceptionDecisionBindingV1.Proof(WorkSalesRiskExceptionDecisionPreviewV1Tests.Facts()) }, _ => s
        };
        if(fault=="permission") { r.Source.Permission=SalesPermissionManifestV1.OrdersApprove; r.Authority.Additional.Add(SalesPermissionManifestV1.OrdersApprove); }
        Assert.Equal(503,(await Assert.ThrowsAsync<WorkDecisionException>(()=>r.Decide(Request()))).Status); Assert.Equal(0,r.Native.Calls); Assert.Empty(r.Native.Commands);
    }
    [Theory] [InlineData("facts")] [InlineData("permission")] [InlineData("actor")] [InlineData("company")] [InlineData("membership")] [InlineData("cancel")]
    public async Task Second_human_read_rechecks_facts_and_current_context_before_any_human_response(string fault)
    {
        var r=new Rig(); using var cts=new CancellationTokenSource(); r.Native.OnRead=n=>
        {
            if(n!=2)return; if(fault=="facts")r.Native.Current=Facts(2); if(fault=="permission")r.Authority.Allowed=false;
            if(fault=="actor")r.User.ActorId=Maker; if(fault=="company")r.Company.Set(Guid.NewGuid()); if(fault=="membership")r.User.Member=false; if(fault=="cancel")cts.Cancel();
        };
        if(fault=="cancel")await Assert.ThrowsAnyAsync<OperationCanceledException>(()=>r.Host.PreviewAsync(Kind,Id,cts.Token));
        else Assert.Equal(fault=="facts"?503:403,(await Assert.ThrowsAsync<WorkDecisionException>(()=>r.Preview())).Status);
        Assert.Equal(2,r.Native.Reads); Assert.Equal(0,r.Native.Calls);
    }
    [Theory] [InlineData(false,"permission")] [InlineData(true,"permission")] [InlineData(false,"delegation")] [InlineData(true,"delegation")]
    [InlineData(false,"actor")] [InlineData(true,"actor")] [InlineData(false,"company")] [InlineData(true,"company")]
    [InlineData(false,"membership")] [InlineData(true,"membership")] [InlineData(false,"cancel")] [InlineData(true,"cancel")]
    public async Task Native_and_release_fences_distinguish_zero_effect_denial_from_retained_response_suppression(bool retained,string fault)
    {
        var r=new Rig();using var cts=new CancellationTokenSource();void Revoke()
        { if(fault=="permission")r.Authority.Allowed=false;if(fault=="delegation")r.Delegations.Live=false;if(fault=="actor")r.User.ActorId=Maker;if(fault=="company")r.Company.Set(Guid.NewGuid());if(fault=="membership")r.User.Member=false;if(fault=="cancel")cts.Cancel(); }
        if(retained)r.Native.AfterEffect=Revoke;else r.Native.BeforeEffect=Revoke;
        var q=Request() with { DelegationId=Guid.NewGuid() };
        if(fault=="cancel")await Assert.ThrowsAnyAsync<OperationCanceledException>(()=>r.Decide(q,token:cts.Token));else Assert.Equal(403,(await Assert.ThrowsAsync<WorkDecisionException>(()=>r.Decide(q))).Status);
        Assert.Equal(retained?1:0,r.Native.Effects);Assert.Equal(retained?1:0,r.Native.Commands.Count);
    }
    [Theory] [InlineData(true)] [InlineData(false)]
    public async Task Creator_cannot_decide_either_way_and_canonical_reason_at_native_max_is_preserved(bool approve)
    {
        var r=new Rig();r.User.ActorId=Maker;Assert.Equal(403,(await Assert.ThrowsAsync<WorkDecisionException>(()=>r.Decide(Request(approve)))).Status);Assert.Equal(0,r.Native.Calls);
        var other=new Rig();var reason=new string('x',SalesContract.MaximumReasonLength);var answer=await other.Decide(Request(false) with { Reason=reason });Assert.Equal("DiscountRejected",answer.State);Assert.Equal(reason,other.Native.LastBody!.Reason);
    }
    [Theory] [InlineData(404,"not_found")] [InlineData(409,"sales_version_conflict")] [InlineData(422,"validation_failed")]
    public async Task Native_terminal_status_code_replay_and_original_correlation_are_not_mapped_to_Work_success(int status,string code)
    {
        var r=new Rig();r.Native.Refusal=new(status,code,isReplay:true,originalCorrelationId:"SENTETIK-original-refusal");
        var error=await Assert.ThrowsAsync<WorkDecisionException>(()=>r.Decide(Request()));Assert.Equal((status,code,true,"SENTETIK-original-refusal"),(error.Status,error.Code,error.Replayed,error.OriginalCorrelationId));Assert.Empty(r.Native.Commands);
    }
    [Theory] [InlineData("unauthenticated")] [InlineData("company")] [InlineData("membership")] [InlineData("permission")] [InlineData("scoped")]
    public async Task Initial_context_and_company_wide_permission_denial_precede_source_reads(string fault)
    {
        var r=new Rig();if(fault=="unauthenticated")r.User.Authenticated=false;if(fault=="company")r.Company.Set(Guid.NewGuid());if(fault=="membership")r.User.Member=false;if(fault=="permission")r.Authority.Allowed=false;if(fault=="scoped")r.Authority.CompanyWide=false;
        Assert.Equal(fault=="unauthenticated"?401:403,(await Assert.ThrowsAsync<WorkDecisionException>(()=>r.Decide(Request()))).Status);Assert.Equal(0,r.Source.Reads);Assert.Equal(0,r.Native.Calls);
    }
    [Fact]
    public async Task Final_optional_price_read_loss_suppresses_a_stale_visible_human_card_without_requiring_price_co_grant()
    {
        var r=new Rig();r.Authority.OnPriceRead=n=>{if(n==4)r.Authority.Prices=false;};
        Assert.Equal(503,(await Assert.ThrowsAsync<WorkDecisionException>(()=>r.Preview())).Status);Assert.Equal(0,r.Native.Calls);
        r.Authority.OnPriceRead=null;var preview=await r.Preview();Assert.Equal(SalesPriceDisclosure.Withheld,preview.SalesContractDiscount!.Contract.AmountDisclosure);
        Assert.All(preview.SalesContractDiscount.Contract.Installments,x=>Assert.Null(x.Amount));Assert.All(preview.SalesContractDiscount.Contract.DiscountRules,x=>Assert.Null(x.RatePercent));
        Assert.Equal("Active",(await r.Decide(Request())).State);
    }
    [Fact]
    public async Task Terminal_current_card_cannot_repeat_a_decision_and_missing_legacy_provenance_never_forges_original_success()
    {
        var r=new Rig();await r.Decide(Request());var current=await r.Preview();Assert.False(current.Pending);Assert.False(current.ApprovalSupported);Assert.False(current.RejectionSupported);
        Assert.Equal(409,(await Assert.ThrowsAsync<WorkDecisionException>(()=>r.Decide(new("2",false,"SENTETIK later"),"new-key"))).Status);Assert.Single(r.Native.Commands);Assert.Equal(1,r.Native.Effects);
        r.Native.Legacy=true;var calls=r.Native.Calls;Assert.Equal(503,(await Assert.ThrowsAsync<WorkDecisionException>(()=>r.Decide(Request()))).Status);Assert.Equal(calls,r.Native.Calls);Assert.Equal(1,r.Native.Effects);
        Assert.Equal(503,(await Assert.ThrowsAsync<WorkDecisionException>(()=>r.BuildHost(null).PreviewAsync(Kind,Id,default))).Status);
    }
    [Fact]
    public async Task Max_version_pending_contract_is_readable_with_both_action_flags_off_and_cannot_dispatch_an_overflowing_decision()
    {
        var r=new Rig();r.Native.Current=Facts(long.MaxValue);var preview=await r.Preview();Assert.True(preview.Pending);Assert.False(preview.ApprovalSupported);Assert.False(preview.RejectionSupported);
        Assert.Equal(long.MaxValue.ToString(CultureInfo.InvariantCulture),preview.SourceVersion);
        Assert.Equal(400,(await Assert.ThrowsAsync<WorkDecisionException>(()=>r.Decide(Request() with {ExpectedVersion=preview.SourceVersion}))).Status);Assert.Equal(0,r.Native.Calls);
    }
    [Fact]
    public async Task Changed_canonical_body_under_the_original_key_is_native_payload_conflict_and_never_relabels_the_retained_decision()
    {
        var r=new Rig();var original=Request(false);await r.Decide(original);r.SetCorrelation("SENTETIK changed retry");
        var error=await Assert.ThrowsAsync<WorkDecisionException>(()=>r.Decide(original with {Reason="SENTETIK different"}));
        Assert.Equal((409,SalesDraftProblemCodesV1.SameKeyPayloadMismatch),(error.Status,error.Code));Assert.Equal(1,r.Native.Effects);Assert.Single(r.Native.Commands);
        Assert.Equal("SENTETIK ret",Assert.Single(r.Native.Commands).Value.After.Contract.Decision!.RejectionReason);
    }
    [Theory] [InlineData("finance.cash-account.v1")] [InlineData(WorkDecisionHostV1.OpeningCost)]
    public async Task Existing_generic_kinds_keep_their_preview_but_reject_foreign_private_contract_proof(string kind)
    {
        var r=new Rig();var source=new GenericSource(kind);r.Authority.Additional.Add(source.ApproverPermission);var host=r.BuildHost(r.Native,[source]);Assert.Equal(kind,(await host.PreviewAsync(kind,Id,default)).Kind);
        source.Foreign=SalesContractDiscountDecisionBindingV1.Proof(Facts());Assert.Equal(503,(await Assert.ThrowsAsync<WorkDecisionException>(()=>host.PreviewAsync(kind,Id,default))).Status);Assert.Equal(0,r.Native.Calls);
    }

    private sealed class Rig
    {
        internal readonly MutableCompany Company=new();internal readonly MutableUser User=new();internal readonly Authority Authority=new();internal readonly Delegations Delegations=new();
        internal readonly Native Native;internal readonly SourceProxy Source;internal WorkDecisionHostV1 Host;private CorrelationContext correlation=new();private readonly WorkDecisionExecutionFenceV1 execution=new();private readonly WorkDecisionAuthorityV1 decorated;
        internal Rig(){Company.Set(WorkSalesContractDiscountDecisionPreviewV1Tests.Company);correlation.Set(Corr,"SENTETIK-cause","Web",null,null);decorated=new(Authority,execution);Native=new(this,decorated);Source=new(new SalesContractDiscountWorkInboxSourceV1(null!,Company,User,decorated,Native));Host=BuildHost(Native);}
        internal WorkDecisionHostV1 BuildHost(ISalesContractService? owner,IEnumerable<IWorkInboxSourceV1>? sources=null)=>new(Company,User,correlation,decorated,sources??[Source],Delegations,execution,null!,null!,salesContracts:owner);
        internal void SetCorrelation(string value){correlation=new();correlation.Set(value,"SENTETIK-cause","Web",null,null);Host=BuildHost(Native);}
        internal Task<WorkDecisionPreviewV1> Preview()=>Host.PreviewAsync(Kind,Id,default);
        internal Task<WorkDecisionResultV1> Decide(WorkDecisionRequestV1 q,string key=Key,CancellationToken token=default)=>Host.DecideAsync(Kind,Id,q,key,token);
    }
    private sealed class Native(Rig rig,IErpAuthorityRevalidationService authority):ISalesContractService,ISalesContractDiscountDecisionReadV1
    {
        internal SalesContractDiscountDecisionDetailV1 Current=Facts();internal int Reads,HistoryReads,Calls,Effects;internal Guid LastId;internal bool LastApprove,Legacy;
        internal Action<int>? OnRead;internal Action? BeforeEffect,AfterEffect;internal SalesDraftStateCommandRequest? LastBody;internal SalesCommandMetadata? LastMetadata;internal SalesDraftServiceException? Refusal;
        internal Func<SalesContractDiscountOriginalReceiptV1,SalesContractDiscountOriginalReceiptV1?>? ChangeReceipt;internal readonly Dictionary<(string Command,string Key),SalesContractDiscountOriginalReceiptV1> Commands=[];
        public async Task<SalesContractDiscountDecisionReadResultV1> ReadContractDiscountDecisionAsync(Guid id,Func<CancellationToken,Task> fence,CancellationToken ct=default)
        {
            await fence(ct);Reads++;OnRead?.Invoke(Reads);await fence(ct);
            if(id!=Id)return new(SalesContractDiscountDecisionReadStateV1.NotFound,null);if(Current.Contract.CreatedBy==rig.User.UserId)return new(SalesContractDiscountDecisionReadStateV1.Denied,null);
            var disclosure=await Price(ct);return new(SalesContractDiscountDecisionReadStateV1.Found,Seal(Current.Source!,Current.ObservedBusinessDate,disclosure));
        }
        public async Task<SalesContractDiscountOriginalVersionReadResultV1> ReadContractDiscountOriginalVersionAsync(Guid id,string version,Func<CancellationToken,Task> fence,CancellationToken ct=default)
        {
            await fence(ct);HistoryReads++;await fence(ct);if(Legacy)return new(SalesContractDiscountDecisionReadStateV1.Unavailable,null);
            var matches=Commands.Values.Where(x=>x.ContractId==id&&x.ExpectedVersion==version&&x.ActorId==rig.User.UserId).ToArray();
            return matches.Length==1?new(SalesContractDiscountDecisionReadStateV1.Found,Observe(matches[0],true,rig.Authority.Prices?SalesPriceDisclosure.Visible:SalesPriceDisclosure.Withheld).OriginalDecisionReceipt):new(SalesContractDiscountDecisionReadStateV1.NotFound,null){CurrentVersion=Current.Source!.Contract.Version};
        }
        public Task<SalesContractCommandResult> ApproveContractDiscountAsync(Guid id,SalesDraftStateCommandRequest q,SalesCommandMetadata m,CancellationToken ct=default)=>Execute(id,q,m,true,ct);
        public Task<SalesContractCommandResult> RejectContractDiscountAsync(Guid id,SalesDraftStateCommandRequest q,SalesCommandMetadata m,CancellationToken ct=default)=>Execute(id,q,m,false,ct);
        private async Task<SalesContractCommandResult> Execute(Guid id,SalesDraftStateCommandRequest q,SalesCommandMetadata m,bool approve,CancellationToken ct)
        {
            Calls++;LastId=id;LastApprove=approve;LastBody=q;LastMetadata=m;var actor=rig.User.UserId;BeforeEffect?.Invoke();await Fence(actor,ct);if(Refusal is not null)throw Refusal;
            var command=approve?SalesContractCommands.ApproveDiscountV1:SalesContractCommands.RejectDiscountV1;var hash=SalesContractHash.Compute(command,id,q);var replay=Commands.TryGetValue((command,m.IdempotencyKey),out var receipt);
            if(replay&&(receipt!.PayloadHash!=hash||receipt.ActorId!=actor))throw new SalesDraftServiceException(409,SalesDraftProblemCodesV1.SameKeyPayloadMismatch);
            if(!replay)
            {
                if(id!=Id)throw new SalesDraftServiceException(404,"not_found");if(Current.Contract.Version!=q.ExpectedVersion)throw new SalesDraftServiceException(409,SalesDraftProblemCodesV1.VersionConflict);
                if(Current.Contract.State!="PendingDiscountApproval")throw new SalesDraftServiceException(409,SalesDraftProblemCodesV1.StateConflict);
                var before=SalesContractDiscountDecisionDigestsV1.Freeze(Current.Source!);var after=SalesContractDiscountDecisionDigestsV1.Freeze(before);var at=At.AddSeconds(1);
                if(approve)after.Contract.ApproveDiscount(before.Contract.Version,actor,at);else after.Contract.RejectDiscount(before.Contract.Version,actor,q.Reason,at);
                Current=Seal(after);var answer=new SalesContractCommandResult(Current.Contract,false,m.CorrelationId);
                receipt=new(WorkSalesContractDiscountDecisionPreviewV1Tests.Company,Id,command,actor,SalesStore.IdempotencyKeyHash(m.IdempotencyKey),hash,q.ExpectedVersion,Current.Contract.Version,m.CorrelationId,at,Guid.NewGuid(),new('A',64),before,after,"")
                    {CurrentSource=after,CurrentResponseDigest=SalesContractDiscountDecisionDigestsV1.CurrentResponseDigest(answer)};
                receipt=receipt with{OriginalDecisionDigest=SalesContractDiscountDecisionDigestsV1.OriginalDecisionDigest(receipt)};Commands.Add((command,m.IdempotencyKey),receipt);Effects++;AfterEffect?.Invoke();
            }
            await Fence(actor,ct);var disclosure=await Price(ct);var result=Observe(receipt!,replay,disclosure);
            return result with{OriginalDecisionReceipt=Legacy?null:ChangeReceipt is null?result.OriginalDecisionReceipt:ChangeReceipt(result.OriginalDecisionReceipt!)};
        }
        private SalesContractCommandResult Observe(SalesContractDiscountOriginalReceiptV1 receipt,bool replay,SalesPriceDisclosure disclosure)
        {
            var current=Seal(Current.Source!,Current.ObservedBusinessDate,disclosure);var result=new SalesContractCommandResult(current.Contract,replay,receipt.OriginalCorrelationId);
            return result with{OriginalDecisionReceipt=receipt with{CurrentSource=current.Source,CurrentResponseDigest=SalesContractDiscountDecisionDigestsV1.CurrentResponseDigest(result)}};
        }
        private async Task<SalesPriceDisclosure> Price(CancellationToken ct)
        {var p=await authority.RevalidatePermissionScopeAsync(SalesPermissionManifestV1.OrdersPriceRead,WorkSalesContractDiscountDecisionPreviewV1Tests.Company,ct);ct.ThrowIfCancellationRequested();return p.IsGranted&&p.IsCompanyWide?SalesPriceDisclosure.Visible:SalesPriceDisclosure.Withheld;}
        private async Task Fence(string actor,CancellationToken ct)
        {
            ct.ThrowIfCancellationRequested();var p=await authority.RevalidatePermissionScopeAsync(SalesPermissionManifestV1.ContractsDiscountApprove,WorkSalesContractDiscountDecisionPreviewV1Tests.Company,ct);ct.ThrowIfCancellationRequested();
            if(!p.IsGranted||!p.IsCompanyWide||rig.User.UserId!=actor||!rig.User.Member||rig.Company.CompanyId!=WorkSalesContractDiscountDecisionPreviewV1Tests.Company)throw new SalesDraftServiceException(403,"forbidden");
        }
        public Task<SalesContractCommandResult> CreateContractAsync(CreateSalesContractRequest q,SalesCommandMetadata m,CancellationToken ct=default)=>throw new NotSupportedException("No Work create");
        public Task<SalesContractView> GetContractAsync(Guid id,CancellationToken ct=default)=>throw new NotSupportedException("No general contract read");
        public Task<SalesContractListResponse> GetContractsAsync(CancellationToken ct=default)=>throw new NotSupportedException("No general contract list");
    }
    private sealed class SourceProxy(IWorkApprovalDecisionVersionSourceV1 inner):IWorkApprovalDecisionVersionSourceV1
    {
        internal int Reads;internal string? Permission;internal WorkApprovalDecisionSnapshotV1? Last;internal Func<WorkApprovalDecisionSnapshotV1,WorkApprovalDecisionSnapshotV1>? Change;
        public string Module=>"sales";public string Kind=>inner.Kind;public string ApproverPermission=>Permission??inner.ApproverPermission;
        public Task<WorkInboxSourceResultV1> ListPendingAsync(WorkInboxSourceContextV1 c,int take,CancellationToken ct)=>throw new NotSupportedException("No list fallback");
        public async Task<WorkApprovalDecisionSnapshotV1?> ReadDecisionAsync(WorkInboxSourceContextV1 c,Guid id,CancellationToken ct){Reads++;var s=await inner.ReadDecisionAsync(c,id,ct);return Last=s is null?null:Change?.Invoke(s)??s;}
        public async Task<WorkApprovalDecisionSnapshotV1?> ReadDecisionVersionAsync(WorkInboxSourceContextV1 c,Guid id,string version,CancellationToken ct){Reads++;var s=await inner.ReadDecisionVersionAsync(c,id,version,ct);return Last=s is null?null:Change?.Invoke(s)??s;}
    }
    private sealed class GenericSource(string kind):IWorkApprovalDecisionSourceV1
    {
        internal WorkSalesContractDiscountProofV1? Foreign;public string Module=>kind.StartsWith("finance",StringComparison.Ordinal)?"finance":"inventory";public string Kind=>kind;
        public string ApproverPermission=>kind==WorkDecisionHostV1.OpeningCost?ErpPermissions.InventoryOpeningCostApprove:ErpPermissions.FinanceCashAccountApprove;
        public Task<WorkInboxSourceResultV1> ListPendingAsync(WorkInboxSourceContextV1 c,int take,CancellationToken ct)=>throw new NotSupportedException();
        public Task<WorkApprovalDecisionSnapshotV1?> ReadDecisionAsync(WorkInboxSourceContextV1 c,Guid id,CancellationToken ct)=>Task.FromResult<WorkApprovalDecisionSnapshotV1?>(new(WorkSalesContractDiscountDecisionPreviewV1Tests.Company,new(Module,Kind,id,"SENTETIK generic",At,"/onaylarim",Maker.ToString("D"),[],[]),"7",true){SalesContractDiscountProof=Foreign});
    }
    private sealed class MutableCompany:ICompanyContext{public Guid CompanyId{get;private set;}public bool IsResolved=>CompanyId!=Guid.Empty;public void Set(Guid id)=>CompanyId=id;}
    private sealed class MutableUser:ICurrentUser
    {
        internal Guid ActorId=Actor;internal bool Member=true,Authenticated=true;public bool IsAuthenticated=>Authenticated;public string UserId=>ActorId.ToString("D");
        public IReadOnlySet<Guid> AllowedCompanyIds{get;}=new HashSet<Guid>([WorkSalesContractDiscountDecisionPreviewV1Tests.Company]);public bool CanAccessCompany(Guid id)=>Member&&id==WorkSalesContractDiscountDecisionPreviewV1Tests.Company;
    }
    private sealed class Authority:IErpAuthorityRevalidationService
    {
        internal bool Allowed=true,CompanyWide=true,Prices=true;internal int PriceReads;internal Action<int>? OnPriceRead;internal readonly List<string> Permissions=[];internal readonly HashSet<string> Additional=new(StringComparer.Ordinal);
        public Task<PermissionScope> RevalidatePermissionScopeAsync(string p,Guid company,CancellationToken ct=default)
        {Permissions.Add(p);if(p==SalesPermissionManifestV1.OrdersPriceRead){PriceReads++;OnPriceRead?.Invoke(PriceReads);}return Task.FromResult(new PermissionScope(p==SalesPermissionManifestV1.OrdersPriceRead?Prices:Allowed&&(p==SalesPermissionManifestV1.ContractsDiscountApprove||Additional.Contains(p)),CompanyWide,new HashSet<string>(),new HashSet<Guid>(),new HashSet<Guid>(),new HashSet<Guid>()));}
    }
    private sealed class Delegations:IWorkDelegationReaderV1
    {
        internal bool Live=true;
        public Task<WorkDelegationViewV1?> FindForActorAsync(Guid id,WorkInboxItemV1 item,CancellationToken ct=default)=>Task.FromResult<WorkDelegationViewV1?>(Live?new(id,WorkSalesContractDiscountDecisionPreviewV1Tests.Company,Kind,SalesPermissionManifestV1.ContractsDiscountApprove,Maker,Actor,At.AddYears(-1),At.AddYears(1),"1",Maker,At,"SENTETIK",null,null,null):null);
    }
}
