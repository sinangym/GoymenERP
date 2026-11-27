using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using GoymenERP.Api.WorkRuntime;
using GoymenERP.BuildingBlocks.Messaging;
using GoymenERP.BuildingBlocks.Multitenancy;
using GoymenERP.BuildingBlocks.Observability;
using GoymenERP.BuildingBlocks.Security;
using GoymenERP.BuildingBlocks.Work;
using GoymenERP.Modules.Accounting.Application;
using GoymenERP.Modules.Accounting.Infrastructure.Persistence;
using GoymenERP.Modules.Accounting.Infrastructure.WorkInbox;
using GoymenERP.Modules.Work.Infrastructure.Delegations;
using Microsoft.EntityFrameworkCore;

namespace GoymenERP.Api.IntegrationTests;

// Real native Application/source over an explicit synthetic command protocol: not SQL, Audit or rollback acceptance.
public sealed class InventoryCostWorkDecisionHostTests
{
    internal static readonly Guid Company = Guid.Parse("ac550000-0000-4000-8000-000000000001"), Id = Guid.Parse("ac550000-0000-4000-8000-000000000002"),
        Actor = Guid.Parse("ac550000-0000-4000-8000-000000000003"), Maker = Guid.Parse("ac550000-0000-4000-8000-000000000004");
    internal static readonly DateTimeOffset At = new(2026,11,28,9,0,0,TimeSpan.Zero);
    internal static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    private const string Key = "SENTETIK-work-inventory-cost", Correlation = "SENTETIK-work-inventory-cost-correlation";
    [Theory] [InlineData(false,"Posted")] [InlineData(true,"Posted")] [InlineData(false,"RoundedNoCost")]
    [InlineData(true,"RoundedNoCost")] [InlineData(false,"Stale")] [InlineData(true,"Stale")]
    public async Task Both_native_kinds_keep_full_human_versions_and_original_Posted_zero_or_Stale_outcomes(bool purchase,string status)
    {
        using var rig = new Rig(purchase,status);
        var preview = await rig.Preview(); var human = Assert.IsType<WorkInventoryCostPreviewV1>(preview.InventoryCost);
        Assert.True(preview.Pending); Assert.False(preview.ApprovalReasonRequired); Assert.False(preview.RejectionSupported);
        Assert.Equal("7",human.Proposal.Version); Assert.Equal("9007199254740993",human.Proposal.InvoiceVersion);
        Assert.Equal(rig.Port.Original.Prepared.Preparation!.Reason,human.Proposal.Preparation!.Reason);
        Assert.Equal(rig.Port.Original.Prepared.Component.SignedTlAmount,human.Proposal.Component.SignedTlAmount);
        Assert.All(rig.Authority.Permissions,permission=>Assert.Equal(rig.Permission,permission));
        if(status=="Stale")
        {
            var failure=await Assert.ThrowsAsync<WorkDecisionException>(()=>rig.Decide());
            Assert.Equal(409,failure.Status);Assert.Equal(AccountingCostProposalStaleEventV1.ReasonCode,failure.Code);Assert.False(failure.Replayed);
        }
        else { var first=await rig.Decide();Assert.Equal(status,first.State);Assert.False(first.Replayed); }
        Assert.Equal(1,rig.Port.Effects);Assert.Single(rig.Port.Commands);Assert.Equal(7L,rig.Port.Expected);Assert.Equal(Key,rig.Port.Key);
        // A later current cancellation is separate from the original command/result and its frozen facts.
        rig.Port.Current=rig.Port.Current with{SourceCancelled=true};
        var reads=rig.Port.Reads;
        if(status=="Stale") { var replay=await Assert.ThrowsAsync<WorkDecisionException>(()=>rig.Decide());Assert.Equal(409,replay.Status);Assert.True(replay.Replayed);Assert.Equal(Correlation,replay.OriginalCorrelationId); }
        else { var replay=await rig.Decide();Assert.True(replay.Replayed);Assert.Equal(Correlation,replay.OriginalCorrelationId); }
        Assert.Equal(reads+2,rig.Port.Reads); // Snapshot + genuine exact-result receipt, no second POST human GET.
        Assert.Equal(1,rig.Port.Effects);Assert.Single(rig.Port.Commands);
        Assert.Equal(4,JsonSerializer.SerializeToElement(rig.Request,Json).EnumerateObject().Count());
        Assert.DoesNotContain("approvalReceipt",rig.Port.Commands[0].ResultJson,StringComparison.OrdinalIgnoreCase);
    }
    [Theory] [InlineData("0")] [InlineData("1")] [InlineData("07")] [InlineData("+7")] [InlineData("7\n")] [InlineData("7\r")]
    [InlineData("9223372036854775807")] [InlineData("9223372036854775808")] [InlineData("")]
    public async Task Only_canonical_incrementable_native_versions_are_commands(string version)
    {
        using var rig=new Rig();Assert.Equal(400,(await Assert.ThrowsAsync<WorkDecisionException>(()=>rig.Decide(rig.Request with{ExpectedVersion=version}))).Status);
        Assert.Equal(0,rig.Source.Reads);Assert.Equal(0,rig.Port.Effects);
    }
    [Theory] [InlineData("reject")] [InlineData("reason")] [InlineData("normal")] [InlineData("period")] [InlineData("mapping")]
    [InlineData("catalog")] [InlineData("package")] [InlineData("manufacturing")] [InlineData("loss")] [InlineData("overage")]
    [InlineData("plan")] [InlineData("supplier")] [InlineData("return")]
    public async Task Four_field_approve_only_contract_rejects_every_foreign_selector_before_snapshot(string fault)
    {
        using var rig=new Rig();var request=fault switch {
            "reject"=>rig.Request with{Approve=false},"reason"=>rig.Request with{Reason=""},"normal"=>rig.Request with{NormalJournalDraft=new(Id)},
            "period"=>rig.Request with{AccountingPeriodReopening=new("1")},"mapping"=>rig.Request with{PostingAccountMappingSelection=new("0")},
            "catalog"=>rig.Request with{ApprovedSourceCatalogSelection=new("0")},"package"=>rig.Request with{ProductPackageStandard=new("2026-11-28")},
            "manufacturing"=>rig.Request with{ManufacturingPlanReapproval=new([])},"loss"=>rig.Request with{InventoryTransferLoss=new(1,At)},
            "overage"=>rig.Request with{InventoryTransferOverage=new(1,At)},"plan"=>rig.Request with{LogisticsPlanExcess=new("1")},
            "supplier"=>rig.Request with{SupplierReturnCostException=new()},_=>rig.Request with{LogisticsCustomerReturn=new(Id)}};
        Assert.Equal(400,(await Assert.ThrowsAsync<WorkDecisionException>(()=>rig.Decide(request))).Status);Assert.Equal(0,rig.Source.Reads);Assert.Equal(0,rig.Port.Effects);
    }
    [Theory] [InlineData("missing")] [InlineData("kind")] [InlineData("company")] [InlineData("id")] [InlineData("event")]
    [InlineData("maker")] [InlineData("period")] [InlineData("status")] [InlineData("version")] [InlineData("digest")]
    [InlineData("caption")] [InlineData("reference")] [InlineData("rule")] [InlineData("unit")] [InlineData("scope")] [InlineData("cross-proof")]
    public async Task Scalar_identity_reference_rule_and_cross_kind_proof_fail_closed_before_native_dispatch(string fault)
    {
        using var rig=new Rig();rig.Source.Change=s=>s with{InventoryCostProof=fault switch{
            "missing"=>null,"kind"=>s.InventoryCostProof! with{CostKind=2},"company"=>s.InventoryCostProof! with{CompanyId=Guid.NewGuid()},
            "id"=>s.InventoryCostProof! with{ProposalId=Guid.NewGuid()},"event"=>s.InventoryCostProof! with{EventId=Guid.Empty},
            "maker"=>s.InventoryCostProof! with{PreparedBy=Actor},"period"=>s.InventoryCostProof! with{PeriodId=Guid.NewGuid()},
            "status"=>s.InventoryCostProof! with{Status="Posted"},"digest"=>s.InventoryCostProof! with{SourceFactsDigest="bad"},_=>s.InventoryCostProof},
            SourceVersion=fault=="version"?"07":s.SourceVersion,
            InvoiceLineCogsProof=fault=="cross-proof"?new(Company,Id,Id,Id,1,0,Maker,At,"Prepared",new('A',64),new('A',64)):null,
            Item=s.Item with{DocumentLabel=fault=="caption"?"other":s.Item.DocumentLabel,
                References=fault=="reference"?[s.Item.References[0] with{RecordId=Guid.NewGuid()},s.Item.References[1]]:s.Item.References,
                Rules=fault=="rule"?[s.Item.Rules[0] with{Threshold="1"}]:fault=="unit"?[s.Item.Rules[0] with{Unit="USD"}]:s.Item.Rules,
                Scope=fault=="scope"?new(Id,null,null):s.Item.Scope}};
        Assert.Equal(503,(await Assert.ThrowsAsync<WorkDecisionException>(()=>rig.Decide())).Status);Assert.Equal(0,rig.Port.Effects);
    }
    [Theory] [InlineData("missing")] [InlineData("company")] [InlineData("kind")] [InlineData("actor")] [InlineData("key")]
    [InlineData("payload")] [InlineData("expected")] [InlineData("result")] [InlineData("source")] [InlineData("digest")] [InlineData("correlation")] [InlineData("view")]
    public async Task Forged_original_receipt_never_releases_Work_success_after_native_effect(string fault)
    {
        using var rig=new Rig();rig.Port.AfterAnswer=a=> {var v=a.Resource!;var r=v.ApprovalReceipt!;return a with{Resource=v with{
            EventId=fault=="view"?Guid.NewGuid():v.EventId,ApprovalReceipt=fault switch{
                "missing"=>null,"company"=>r with{CompanyId=Guid.NewGuid()},"kind"=>r with{Kind=AccountingInventoryCostKindV1.PurchaseInvoiceDifference},
                "actor"=>r with{ActorId=Maker},"key"=>r with{IdempotencyKeyHash=new('B',64)},"payload"=>r with{PayloadHash=new('B',64)},
                "expected"=>r with{ExpectedVersion=8},"result"=>r with{ResultVersion=9},"source"=>r with{SourceFactsDigest=new('B',64)},
                "digest"=>r with{OriginalResultDigest=new('B',64)},"correlation"=>r with{OriginalCorrelationId="other"},_=>r}}};};
        Assert.Equal(503,(await Assert.ThrowsAsync<WorkDecisionException>(()=>rig.Decide())).Status);Assert.Equal(1,rig.Port.Effects);Assert.Single(rig.Port.Commands);
    }
    [Fact]
    public async Task Post_native_valid_shape_digest_mismatch_is_unknown_then_same_key_reconciles_once()
    {
        using var rig=new Rig();rig.Source.Change=s=>s with{InventoryCostProof=s.InventoryCostProof! with{SourceFactsDigest=new('B',64)}};
        Assert.Equal(503,(await Assert.ThrowsAsync<WorkDecisionException>(()=>rig.Decide())).Status);Assert.Equal(1,rig.Port.Effects);
        rig.Source.Change=null;Assert.True((await rig.Decide()).Replayed);Assert.Equal(1,rig.Port.Effects);Assert.Single(rig.Port.Commands);
    }
    [Fact]
    public async Task Canonical_but_false_rule_amount_cannot_release_human_or_post_native_success()
    {
        using var rig=new Rig();rig.Source.Change=s=>s with{Item=s.Item with{Rules=[s.Item.Rules[0] with{Actual="601"}]}};
        Assert.Equal(503,(await Assert.ThrowsAsync<WorkDecisionException>(()=>rig.Preview())).Status);Assert.Equal(0,rig.Port.Effects);
        // Scalar-only proof cannot authenticate money before execution; the real original receipt suppresses the false rule after commit.
        Assert.Equal(503,(await Assert.ThrowsAsync<WorkDecisionException>(()=>rig.Decide())).Status);Assert.Equal(1,rig.Port.Effects);Assert.Single(rig.Port.Commands);
        rig.Source.Change=null;Assert.True((await rig.Decide()).Replayed);Assert.Equal(1,rig.Port.Effects);
    }
    [Theory] [InlineData(false)] [InlineData(true)]
    public async Task Native_version_conflict_is_durable_and_replays_original_refusal_without_cost_effect(bool purchase)
    {
        using var rig=new Rig(purchase);var request=rig.Request with{ExpectedVersion="6"};
        var first=await Assert.ThrowsAsync<WorkDecisionException>(()=>rig.Decide(request));Assert.Equal(409,first.Status);Assert.False(first.Replayed);
        var replay=await Assert.ThrowsAsync<WorkDecisionException>(()=>rig.Decide(request));Assert.Equal(409,replay.Status);Assert.True(replay.Replayed);
        Assert.Equal(first.Code,replay.Code);Assert.Equal(Correlation,replay.OriginalCorrelationId);Assert.Equal(0,rig.Port.Effects);Assert.Single(rig.Port.Commands);
    }
    [Theory] [InlineData("permission")] [InlineData("company")] [InlineData("actor")] [InlineData("cancel")]
    public async Task Human_final_read_revalidates_current_authority_and_cancellation(string fault)
    {
        using var rig=new Rig();using var cts=new CancellationTokenSource();rig.Port.BeforeRead=count=>{if(count!=2)return;
            if(fault=="permission")rig.Authority.Allowed=false;if(fault=="company")rig.Company.Set(Guid.NewGuid());if(fault=="actor")rig.User.Actor=Maker;if(fault=="cancel")cts.Cancel();};
        if(fault=="cancel")await Assert.ThrowsAnyAsync<OperationCanceledException>(()=>rig.Preview(cts.Token));
        else Assert.Equal(403,(await Assert.ThrowsAsync<WorkDecisionException>(()=>rig.Preview())).Status);
        Assert.Equal(0,rig.Port.Effects);Assert.Empty(rig.Port.Commands);
    }
    [Theory] [InlineData("permission")] [InlineData("delegation")] [InlineData("cancel")]
    public async Task Native_committed_response_release_remains_fresh_and_original_key_reconciles_without_second_effect(string fault)
    {
        using var rig=new Rig();using var cts=new CancellationTokenSource();var request=rig.Request with{DelegationId=fault=="delegation"?Id:null};
        rig.Port.AfterAnswer=a=>{if(fault=="permission")rig.Authority.Allowed=false;if(fault=="delegation")rig.Delegations.Live=false;if(fault=="cancel")cts.Cancel();return a;};
        if(fault=="cancel")await Assert.ThrowsAnyAsync<OperationCanceledException>(()=>rig.Decide(request,cts.Token));
        else Assert.Equal(403,(await Assert.ThrowsAsync<WorkDecisionException>(()=>rig.Decide(request))).Status);
        Assert.Equal(1,rig.Port.Effects);Assert.Single(rig.Port.Commands);
        rig.Port.AfterAnswer=null;rig.Authority.Allowed=true;rig.Delegations.Live=true;Assert.True((await rig.Decide(request)).Replayed);Assert.Equal(1,rig.Port.Effects);
    }
    [Theory] [InlineData("maker")] [InlineData("permission")]
    public async Task Existing_native_maker_and_current_approval_permission_are_required_before_execution(string fault)
    {
        using var rig=new Rig();if(fault=="maker")rig.User.Actor=Maker;else rig.Authority.Allowed=false;
        Assert.Equal(403,(await Assert.ThrowsAsync<WorkDecisionException>(()=>rig.Decide())).Status);
        Assert.Equal(0,rig.Port.Effects);Assert.Empty(rig.Port.Commands);
    }
    [Fact]
    public async Task Missing_owner_is_unavailable_without_native_effect_and_foreign_kind_cannot_carry_cost_proof()
    {
        using var rig=new Rig(inject:false);Assert.Equal(503,(await Assert.ThrowsAsync<WorkDecisionException>(()=>rig.Decide())).Status);Assert.Equal(0,rig.Port.Effects);
        var snapshot=(await rig.Source.ReadDecisionAsync(new(Company,Actor.ToString("D"),Correlation,null),Id,default))!;
        var source=new FixedSource(snapshot);var host=rig.WithSource(source);
        Assert.Equal(503,(await Assert.ThrowsAsync<WorkDecisionException>(()=>host.PreviewAsync(source.Kind,Id,default))).Status);
        Assert.DoesNotContain("inventoryCost",JsonSerializer.Serialize(new WorkDecisionPreviewV1(Company,WorkDecisionHostV1.BankAccount,Id,"SENTETIK","1",true,false,[],[]),Json));
    }

    internal static AccountingInventoryCostDecisionSourceV1 NewFacts(AccountingInventoryCostKindV1 kind, string status, long version = 7)
    {
        var zero = status == "RoundedNoCost"; var currency = Guid.NewGuid(); var ratio = zero ? new AccountingInventoryCostRatioV1("1", "1000") : new("600", "1");
        var component = new AccountingInventoryCostComponentV1("SENTETIK component", Guid.NewGuid(), Guid.NewGuid(), 1, null, ratio, "{}", InventoryCostDigest("{}"));
        var profile = Guid.NewGuid(); AccountingInventoryCostRoundingV1? rounding = null;
        if (zero) { Assert.True(AccountingInventoryCostRoundingV1Rules.TryResolve(component, currency, profile, 1, 2, out _, out rounding)); }
        var prep = new AccountingInventoryCostPreparationV1(Guid.NewGuid(), new(2026, 11, 28), new(2026, 11, 27), currency, zero ? "0" : "600", profile, 1, 2,
            new(InvoiceLineCogsClassificationV1.TradeGoods, Guid.NewGuid(), "SENTETIK", null, null, null, 0), new(Guid.NewGuid(), "151", "SENTETIK counter"),
            Guid.NewGuid(), 9, null, "SENTETIK plan", new string('A', 64), Maker, At, "SENTETIK reason", rounding);
        var source = new AccountingInventoryCostSourceV1(Company, Guid.NewGuid(), kind, Guid.NewGuid(), 9007199254740993L, Guid.NewGuid(), new(2026, 11, 27), "SENTETIK invoice", null, new string('B', 64), [component]);
        var prepared = new AccountingInventoryCostProposalV1(Id, Company, source.EventId, kind, source.InvoiceId, source.InvoiceVersion, source.AdmissionId, source.InvoiceDate, source.OfficialInvoiceNumber,
            component, version, "Prepared", At.AddDays(-1), prep);
        var proposal = prepared with { Status = status, Version = version + 1, ApprovedBy = status == "Stale" ? null : Actor, ApprovedAtUtc = status == "Stale" ? null : At.AddMinutes(1),
            JournalId = status == "Posted" ? Guid.NewGuid() : null, StaleClosure = status == "Stale" ? new(Guid.NewGuid(), version, prep.PreparedBy, At.AddMinutes(1),
                AccountingCostProposalStaleEventV1.ReasonCode, AccountingCostProposalStaleEventV1.ReasonText, Correlation, null) : null };
        var raw = JsonSerializer.Serialize(source, Json);
        return new(prepared, proposal, At.AddSeconds(1), raw, InventoryCostDigest(raw), false, status == "Posted", status == "Posted" ? Correlation : null);
    }
    private static string InventoryCostDigest(string value) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value)));
    private sealed class Rig : IDisposable
    {
        public MutableCompany Company {get;}=new();public MutableUser User {get;}=new();public Authority Authority {get;}=new();public Delegations Delegations {get;}
        public Protocol Port {get;} public SourceProxy Source {get;} public WorkDecisionHostV1 Host {get;}
        private readonly AccountingDbContext db;private readonly CorrelationContext correlation=new();private readonly WorkDecisionExecutionFenceV1 execution=new();
        private readonly WorkDecisionAuthorityV1 authority;public AccountingInventoryCostServiceV1 Owner {get;}
        public string Kind {get;} public string Permission {get;}
        public Rig(bool purchase=false,string status="Posted",bool inject=true)
        {
            var native=purchase?AccountingInventoryCostKindV1.PurchaseInvoiceDifference:AccountingInventoryCostKindV1.SalesInvoicedShipment;
            Kind=AccountingInventoryCostDecisionBindingV1.KindName(native);
            Permission=purchase?AccountingJournalReversalPermissionNamesV1.Approve:NormalJournalPermissionNamesV1.DraftApprove;
            Company.Set(InventoryCostWorkDecisionHostTests.Company);correlation.Set(Correlation,null,"Web",null,null);authority=new(Authority,execution);
            Port=new(NewFacts(native,status));Owner=new(Port,Port,authority,Company,User,correlation);Delegations=new(Permission);
            db=new(new DbContextOptionsBuilder<AccountingDbContext>().UseInMemoryDatabase("inventory-cost-host-"+Guid.NewGuid()).Options,Company);
            IWorkApprovalDecisionSourceV1 source=purchase?new AccountingPurchaseCostDifferenceWorkInboxSourceV1(db,new(Company,User,authority),Owner)
                :new AccountingInvoicedShipmentCostWorkInboxSourceV1(db,new(Company,User,authority),Owner);
            Source=new(source);Host=new(Company,User,correlation,authority,[Source],Delegations,execution,null!,null!,inventoryCosts:inject?Owner:null);
        }
        public WorkDecisionRequestV1 Request=>new("7",true,null);
        public Task<WorkDecisionPreviewV1> Preview(CancellationToken token=default)=>Host.PreviewAsync(Kind,Id,token);
        public Task<WorkDecisionResultV1> Decide(WorkDecisionRequestV1? request=null,CancellationToken token=default)=>Host.DecideAsync(Kind,Id,request??Request,Key,token);
        public WorkDecisionHostV1 WithSource(IWorkInboxSourceV1 source)=>new(Company,User,correlation,authority,[source],Delegations,execution,null!,null!,inventoryCosts:Owner);
        public void Dispose()=>db.Dispose();
    }
    private sealed class Protocol(AccountingInventoryCostDecisionSourceV1 original) : IAccountingInventoryCostStoreV1,IAccountingInventoryCostDecisionStoreV1,IAccountingCommandLog
    {
        public AccountingInventoryCostDecisionSourceV1 Original=original,Current=original with{Proposal=original.Prepared};
        public int Effects,Reads;public long Expected;public string? Key;public Action<int>? BeforeRead;
        public Func<AccountingApiResult<AccountingInventoryCostProposalV1>,AccountingApiResult<AccountingInventoryCostProposalV1>>? AfterAnswer;
        public List<AccountingCommandRecord> Commands {get;}=[];
        public async Task<AccountingInventoryCostDecisionSourceV1?> ReadInventoryCostDecisionAsync(Guid id,AccountingInventoryCostKindV1 kind,long? version,Func<CancellationToken,Task> fence,CancellationToken token=default)
        {await fence(token);BeforeRead?.Invoke(++Reads);await fence(token);return id!=Id||kind!=Original.Proposal.Kind?null:version is null?Current:version==Original.Proposal.Version?Original:null;}
        public async Task<AccountingApiResult<AccountingInventoryCostProposalV1>> RunApprovalAsync(Func<bool,Task<AccountingApiResult<AccountingInventoryCostProposalV1>>> attempt,Func<CancellationToken,Task> fence,CancellationToken token=default)
        {try{await fence(token);var answer=await attempt(false);await fence(token);return AfterAnswer?.Invoke(answer)??answer;}catch(NormalJournalAuthorityDeniedException){return new(AccountingApiStatus.Denied,null,false,AccountingApiCodes.AuthorityDenied);}}
        public async Task<AccountingInventoryCostProposalV1> ApproveAsync(Guid id,AccountingInventoryCostKindV1 kind,long version,Guid actor,Func<CancellationToken,Task> fence,AccountingCommandMetadata metadata,CancellationToken token=default)
        {await fence(token);Expected=version;Key=metadata.IdempotencyKey;if(actor==Maker)throw new AccountingStoreException("inventory_cost_approval_refused","SENTETIK");
            if(version!=Original.Prepared.Version)throw new AccountingVersionConflictException(version,Original.Prepared.Version);
            Effects++;Current=Original;Commands.Add(metadata.Completion!(Original.Proposal));return Original.Proposal;}
        public Task<AccountingCommandRecord?> FindCommandAsync(string command,string key,CancellationToken token=default)=>Task.FromResult(Commands.SingleOrDefault(c=>c.CommandName==command&&c.IdempotencyKeyHash==key));
        public Task RecordCommandAsync(AccountingCommandRecord record,CancellationToken token=default)=>throw new NotSupportedException();
        public async Task RecordAttemptAsync(AccountingCommandRecord record,Func<CancellationToken,Task> fence,CancellationToken token=default){await fence(token);Commands.Add(record);}
        public Task AcceptAsync(WorkInboxEventV1 envelope,CancellationToken token=default)=>throw new InvalidOperationException("No source execution");
        public Task<IReadOnlyList<AccountingInventoryCostProposalV1>> ListAsync(AccountingInventoryCostKindV1 kind,CancellationToken token=default)=>throw new InvalidOperationException("No general list");
        public Task<InvoiceLineCogsOptionsV1> OptionsAsync(CancellationToken token=default)=>throw new InvalidOperationException("No options");
        public Task<AccountingInventoryCostProposalV1> PrepareAsync(Guid id,AccountingInventoryCostKindV1 kind,PrepareAccountingInventoryCostV1 request,Guid actor,Func<CancellationToken,Task> fence,AccountingCommandMetadata metadata,CancellationToken token=default)=>throw new InvalidOperationException("No prepare");
        public Task<AccountingInventoryCostProposalV1> ProposeReversalAsync(Guid id,ProposeAccountingInventoryCostReversalV1 request,Guid actor,Func<CancellationToken,Task> fence,AccountingCommandMetadata metadata,CancellationToken token=default)=>throw new InvalidOperationException("No reversal");
        public Task<AccountingInventoryCostProposalV1> ApproveCancellationAsync(Guid id,ApproveInventoryCostCancellationV1 request,Guid actor,Func<CancellationToken,Task> fence,AccountingCommandMetadata metadata,CancellationToken token=default)=>throw new InvalidOperationException("No cancellation");
    }
    private sealed class SourceProxy(IWorkApprovalDecisionSourceV1 inner):IWorkApprovalDecisionSourceV1
    {
        public int Reads;public Func<WorkApprovalDecisionSnapshotV1,WorkApprovalDecisionSnapshotV1>? Change;
        public string Module=>inner.Module;public string Kind=>inner.Kind;public string ApproverPermission=>inner.ApproverPermission;
        public Task<WorkInboxSourceResultV1> ListPendingAsync(WorkInboxSourceContextV1 c,int max,CancellationToken t)=>inner.ListPendingAsync(c,max,t);
        public async Task<WorkApprovalDecisionSnapshotV1?> ReadDecisionAsync(WorkInboxSourceContextV1 c,Guid id,CancellationToken t){Reads++;var answer=await inner.ReadDecisionAsync(c,id,t);return answer is null?null:Change?.Invoke(answer)??answer;}
    }
    private sealed class FixedSource(WorkApprovalDecisionSnapshotV1 snapshot):IWorkApprovalDecisionSourceV1
    {
        public string Module=>"finance";public string Kind=>WorkDecisionHostV1.BankAccount;public string ApproverPermission=>AccountingJournalReversalPermissionNamesV1.Approve;
        public Task<WorkInboxSourceResultV1> ListPendingAsync(WorkInboxSourceContextV1 c,int max,CancellationToken t)=>throw new NotSupportedException();
        public Task<WorkApprovalDecisionSnapshotV1?> ReadDecisionAsync(WorkInboxSourceContextV1 c,Guid id,CancellationToken t)=>Task.FromResult<WorkApprovalDecisionSnapshotV1?>(snapshot with{Item=snapshot.Item with{Module=Module,Kind=Kind}});
    }
    private sealed class MutableCompany:ICompanyContext{public Guid CompanyId{get;private set;}public bool IsResolved=>CompanyId!=Guid.Empty;public void Set(Guid id)=>CompanyId=id;}
    private sealed class MutableUser:ICurrentUser{public Guid Actor=InventoryCostWorkDecisionHostTests.Actor;public bool IsAuthenticated=>true;public string UserId=>Actor.ToString("D");public IReadOnlySet<Guid> AllowedCompanyIds{get;}=new HashSet<Guid>{Company};public bool CanAccessCompany(Guid id)=>id==Company;}
    private sealed class Authority:IErpAuthorityRevalidationService{public bool Allowed=true;public List<string> Permissions{get;}=[];public Task<PermissionScope> RevalidatePermissionScopeAsync(string permission,Guid company,CancellationToken token=default){Permissions.Add(permission);return Task.FromResult(new PermissionScope(Allowed,true,new HashSet<string>(),new HashSet<Guid>(),new HashSet<Guid>(),new HashSet<Guid>()));}}
    private sealed class Delegations(string permission):IWorkDelegationReaderV1{public bool Live=true;public Task<WorkDelegationViewV1?> FindForActorAsync(Guid id,WorkInboxItemV1 item,CancellationToken token=default)=>Task.FromResult<WorkDelegationViewV1?>(Live?new(id,Company,item.Kind,permission,Maker,Actor,At.AddDays(-2),At.AddYears(1),"1",Maker,At,"SENTETIK",null,null,null):null);}
}
