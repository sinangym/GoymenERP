using System.Text.Json;
using GoymenERP.Api.WorkRuntime;
using GoymenERP.BuildingBlocks.Multitenancy;
using GoymenERP.BuildingBlocks.Observability;
using GoymenERP.BuildingBlocks.Security;
using GoymenERP.BuildingBlocks.Work;
using GoymenERP.Modules.Purchasing.Application.Contracts;
using GoymenERP.Modules.Purchasing.Infrastructure.Shell;
using GoymenERP.Modules.Work.Infrastructure.Delegations;

namespace GoymenERP.Api.IntegrationTests;

// Synthetic host receipt/authority boundary only. Genuine owner/SQL paths have separate source facts.
public sealed class WorkSupplierReturnCostExceptionDecisionHostTests
{
    private static readonly Guid Company = Guid.NewGuid(), Id = Guid.NewGuid(), Order = Guid.NewGuid(), Line = Guid.NewGuid(), ReturnLine = Guid.NewGuid(), Receipt = Guid.NewGuid(), Movement = Guid.NewGuid();
    private const string Actor = "SENTETIK-checker", Maker = "SENTETIK-maker";
    private static readonly DateTimeOffset At = new(2026,11,27,0,0,0,TimeSpan.Zero);
    private static WorkSupplierReturnCostExceptionProofV1 Proof(bool measured = false) => new(Company, Id, ReturnLine, Order, Line, Receipt, Movement,
        "Free", "1", "ManualCostWithException", false, "SENTETIK return", At, Maker, At, measured ? "3" : null, measured ? "7" : null, measured ? "1" : null);
    private static PurchaseSupplierReturnViewV1 Value(bool approve, bool measured = false) => new(Company, Id, ReturnLine, Order, Line, Receipt, Movement,
        approve ? "Recorded" : "Rejected", "2", "Free", "1", "ManualCostWithException", null, false, "SENTETIK return", At, Maker, At,
        Actor, At.AddHours(1), approve ? null : "SENTETIK reject", measured ? "3" : null, measured ? "1" : null);

    [Theory]
    [InlineData(true,false)] [InlineData(false,false)] [InlineData(true,true)] [InlineData(false,true)]
    public async Task Both_native_commands_and_original_key_replays_preserve_withheld_cost_outcomes(bool approve, bool replay)
    {
        var run = new Rig(); run.Source.Pending = !replay; run.Source.Version = replay ? "2" : "1";
        run.Owner.Answer = new(Value(approve), replay, replay ? "SENTETIK-original" : "SENTETIK-work", true);
        var preview = await run.Host.PreviewAsync(run.Source.Kind, Id, default);
        Assert.True(preview.RejectionSupported); Assert.False(preview.ApprovalReasonRequired);
        Assert.Equal(PurchasingPriceDisclosureV1.Withheld, preview.SupplierReturnCostException!.PriceDisclosure);
        var result = await run.Host.DecideAsync(run.Source.Kind, Id, new("1", approve, approve ? null : " SENTETIK reject "), "key", default);
        Assert.Equal((approve ? "Recorded" : "Rejected", replay), (result.State, result.Replayed));
        Assert.Equal(Order, run.Owner.Order); Assert.Equal(Id, run.Owner.Id); Assert.Equal("1", run.Owner.Captured!.ExpectedVersion);
        Assert.Null(run.Owner.Captured.ExpectedPurchaseOrderVersion);
    }

    [Fact]
    public async Task Measured_approval_requires_a_human_frozen_PO_version_and_never_substitutes_current_or_original()
    {
        var run = new Rig(); run.Source.Proof = Proof(true); run.Owner.Answer = new(Value(true,true),false,"SENTETIK-work",true);
        var preview = await run.Host.PreviewAsync(run.Source.Kind, Id, default);
        Assert.Equal(("3","7"), (preview.SupplierReturnCostException!.RecordedExpectedPurchaseOrderVersion,preview.SupplierReturnCostException.CurrentPurchaseOrderVersion));
        Assert.Equal(400,(await Assert.ThrowsAsync<WorkDecisionException>(() => run.Host.DecideAsync(run.Source.Kind,Id,new("1",true,null),"key",default))).Status);
        Assert.Null(run.Owner.Captured);
        await run.Host.DecideAsync(run.Source.Kind, Id, new("1",true,null,SupplierReturnCostException:new("5")),"key",default);
        Assert.Equal("5",run.Owner.Captured!.ExpectedPurchaseOrderVersion);
        // An old immutable key keeps version 5 even after the preview's live PO version changes.
        run.Source.Proof = Proof(true) with { CurrentPurchaseOrderVersion = "9" }; run.Source.Pending=false; run.Source.Version="2";
        run.Owner.Answer = run.Owner.Answer with { IsReplay=true,OriginalCorrelationId="SENTETIK-original" };
        Assert.True((await run.Host.DecideAsync(run.Source.Kind,Id,new("1",true,null,SupplierReturnCostException:new("5")),"key",default)).Replayed);
        Assert.Equal("5",run.Owner.Captured.ExpectedPurchaseOrderVersion);
    }

    [Theory]
    [InlineData("0")] [InlineData("01")] [InlineData("+1")] [InlineData(" 1")] [InlineData("9223372036854775807")]
    public async Task Noncanonical_or_overflow_versions_do_not_reach_the_native_owner(string version)
    {
        var run = new Rig();
        Assert.Equal(400, (await Assert.ThrowsAsync<WorkDecisionException>(() => run.Host.DecideAsync(run.Source.Kind, Id, new(version, true, null), "key", default))).Status);
        Assert.Null(run.Owner.Captured);
    }

    [Theory]
    [InlineData("flag")] [InlineData("company")] [InlineData("id")] [InlineData("order")] [InlineData("line")]
    [InlineData("receipt")] [InlineData("movement")] [InlineData("quantity")] [InlineData("version")] [InlineData("state")]
    [InlineData("maker")] [InlineData("actor")] [InlineData("time")] [InlineData("reason")] [InlineData("cost-leak")]
    public async Task Malformed_or_unproven_native_answers_are_never_Work_success(string fault)
    {
        var run=new Rig();var value=Value(false); value=fault switch {
            "company"=>value with{CompanyId=Guid.NewGuid()},"id"=>value with{ReturnId=Guid.NewGuid()},"order"=>value with{PurchaseOrderId=Guid.NewGuid()},
            "line"=>value with{ReturnLineId=Guid.NewGuid()},"receipt"=>value with{GoodsAcceptanceId=Guid.NewGuid()},"movement"=>value with{InventoryMovementId=Guid.NewGuid()},
            "quantity"=>value with{QuantityKg="2"},"version"=>value with{Version="3"},"state"=>value with{Status="PendingCostException"},
            "maker"=>value with{RequestedBy=Actor},"actor"=>value with{DecidedBy=Maker},"time"=>value with{DecidedAtUtc=At.ToOffset(TimeSpan.FromHours(3))},
            "reason"=>value with{DecisionReason="different"},"cost-leak"=>value with{UnitCostTryPerKg="99"},_=>value};
        run.Owner.Answer=new(value,false,"SENTETIK-work",fault!="flag");
        Assert.Equal(503,(await Assert.ThrowsAsync<WorkDecisionException>(()=>run.Host.DecideAsync(run.Source.Kind,Id,new("1",false,"SENTETIK reject"),"key",default))).Status);
    }

    [Theory]
    [InlineData(true, "")] [InlineData(false, "")] [InlineData(false, "long")]
    public async Task Native_reason_validation_and_optional_approval_reason_are_preserved(bool approve,string reason)
    {
        var run=new Rig();if(reason=="long")reason=new string('x',501);
        if(approve){await run.Host.DecideAsync(run.Source.Kind,Id,new("1",true,reason),"key",default);Assert.Equal(reason,run.Owner.Captured!.Reason);}
        else{Assert.Equal(400,(await Assert.ThrowsAsync<WorkDecisionException>(()=>run.Host.DecideAsync(run.Source.Kind,Id,new("1",false,reason),"key",default))).Status);Assert.Null(run.Owner.Captured);}
    }

    [Fact]
    public async Task Fresh_price_disclosure_only_changes_human_cost_visibility_and_never_becomes_a_decision_copermission()
    {
        var run = new Rig(); run.Source.Cost = "12.3456";
        Assert.Equal("12.3456", (await run.Host.PreviewAsync(run.Source.Kind, Id, default)).SupplierReturnCostException!.UnitCostTryPerKg);
        run.Raw.Prices = false;
        var hidden = await run.Host.PreviewAsync(run.Source.Kind, Id, default);
        Assert.Null(hidden.SupplierReturnCostException!.UnitCostTryPerKg);
        Assert.Equal(PurchasingPriceDisclosureV1.Withheld, hidden.SupplierReturnCostException.PriceDisclosure);
        Assert.Null(hidden.Rules.Single(r => r.RuleCode == "purchasing.supplier-return-manual-cost").Actual);
        Assert.DoesNotContain("12.3456", JsonSerializer.Serialize(hidden, new JsonSerializerOptions(JsonSerializerDefaults.Web)));
        Assert.Equal("Recorded", (await run.Host.DecideAsync(run.Source.Kind, Id, new("1", true, null), "key", default)).State);
    }

    [Theory]
    [InlineData(false)] [InlineData(true)]
    public async Task Explicit_PO_versions_are_rejected_for_unmeasured_or_rejection_commands(bool approve)
    {
        var run = new Rig();
        Assert.Equal(400, (await Assert.ThrowsAsync<WorkDecisionException>(() => run.Host.DecideAsync(run.Source.Kind, Id,
            new("1", approve, approve ? null : "SENTETIK reject", SupplierReturnCostException: new("7")), "key", default))).Status);
        Assert.Null(run.Owner.Captured);
    }

    [Fact]
    public async Task Foreign_typed_payloads_maker_and_revoked_delegation_fail_without_native_effects()
    {
        var own=new Rig(Maker);Assert.Equal(403,(await Assert.ThrowsAsync<WorkDecisionException>(()=>own.Host.PreviewAsync(own.Source.Kind,Id,default))).Status);
        var run=new Rig(); Assert.Equal(400,(await Assert.ThrowsAsync<WorkDecisionException>(()=>run.Host.DecideAsync(run.Source.Kind,Id,new("1",true,null,InventoryTransferLoss:new(1,At)),"key",default))).Status);
        run.Delegations.Live=false;Assert.Equal(403,(await Assert.ThrowsAsync<WorkDecisionException>(()=>run.Host.DecideAsync(run.Source.Kind,Id,new("1",true,null,Guid.NewGuid()),"key",default))).Status);Assert.Null(run.Owner.Captured);
        run.Source.Kind=WorkDecisionHostV1.OpeningCost;Assert.Equal(503,(await Assert.ThrowsAsync<WorkDecisionException>(()=>run.Host.PreviewAsync(run.Source.Kind,Id,default))).Status);
    }

    [Fact]
    public async Task New_optional_wire_fields_are_absent_for_old_callers_and_internal_proof_never_serializes()
    {
        var options=new JsonSerializerOptions(JsonSerializerDefaults.Web);var run=new Rig();
        using var legacy=JsonDocument.Parse(JsonSerializer.Serialize(new WorkDecisionRequestV1("1",true,null),options));Assert.Equal(4,legacy.RootElement.EnumerateObject().Count());
        Assert.DoesNotContain("supplierReturnCostException",JsonSerializer.Serialize(new WorkDecisionPreviewV1(Company,WorkDecisionHostV1.OpeningCost,Id,"SENTETIK","1",true,true,[],[]),options));
        Assert.DoesNotContain("supplierReturnCostExceptionProof",JsonSerializer.Serialize(await run.Source.ReadDecisionAsync(new(Company,Actor,"SENTETIK",null),Id,default),options));
        var cap=Assert.Single(new PurchasingShellCapabilityProvider().GetDeclarations().Where(c=>c.CapabilityId==PurchasingShellCapabilityProvider.SupplierReturnWorkDecisionCapabilityId));
        Assert.False(cap.ImplementationAvailable);Assert.Null(cap.ParentCapabilityId);Assert.Equal([PurchasingPermissionManifestV1.OrdersApprove],cap.RequiredPermissions);
    }

    private sealed class Rig
    {
        public readonly RawAuthority Raw = new(); public readonly Source Source=new();public readonly Delegations Delegations=new();public readonly Owner Owner;public readonly WorkDecisionHostV1 Host;
        public Rig(string actor=Actor){var company=new CompanyContext();company.Set(Company);var correlation=new CorrelationContext();correlation.Set("SENTETIK-work",null,"Web",null,null);var execution=new WorkDecisionExecutionFenceV1();
            var authority=new WorkDecisionAuthorityV1(Raw,execution);Owner=new(authority);
            Host=new(company,new WorkInventoryTransferLossDecisionHostTests.LossUser(Company,actor),correlation,authority,[Source],Delegations,execution,null!,null!,supplierReturns:Owner);}
    }
    private sealed class Source:IWorkApprovalDecisionSourceV1
    {
        public WorkSupplierReturnCostExceptionProofV1? Proof=WorkSupplierReturnCostExceptionDecisionHostTests.Proof(); public bool Pending=true; public string Version="1"; public string? Cost;
        public string Module=>"purchasing";public string Kind{get;set;}=WorkDecisionHostV1.SupplierReturnCostException;public string ApproverPermission=>PurchasingPermissionManifestV1.OrdersApprove;
        public Task<WorkApprovalDecisionSnapshotV1?> ReadDecisionAsync(WorkInboxSourceContextV1 c,Guid id,CancellationToken t)=>Task.FromResult<WorkApprovalDecisionSnapshotV1?>(new(Company,new(Module,Kind,Id,"SENTETIK-PO",At,"/satinalma/siparisler/"+Order,Maker,[new("purchasing.supplier-return-manual-cost",null,Cost,null,Cost is null ? null : "TRY"),new("purchasing.supplier-return-quantity",null,"1",null,"kg")],[new("purchase-order","SENTETIK-PO",Order,null)]),Version,Pending,SupplierReturnCostExceptionProof:Proof));
        public Task<WorkInboxSourceResultV1> ListPendingAsync(WorkInboxSourceContextV1 c,int n,CancellationToken t)=>throw new NotSupportedException();
    }
    private sealed class RawAuthority : IErpAuthorityRevalidationService
    {
        public bool Prices = true;
        public Task<PermissionScope> RevalidatePermissionScopeAsync(string permission, Guid company, CancellationToken token = default) =>
            Task.FromResult(new PermissionScope(permission != PurchasingPermissionManifestV1.OrdersPriceRead || Prices, true, new HashSet<string>(), new HashSet<Guid>(), new HashSet<Guid>(), new HashSet<Guid>()));
    }
    private sealed class Owner(IErpAuthorityRevalidationService authority):IPurchaseSupplierReturnServiceV1
    {
        public Guid Order,Id;public DecidePurchaseSupplierReturnV1? Captured;public PurchaseSupplierReturnResponseV1 Answer=new(Value(true),false,"SENTETIK-work",true);
        private async Task<PurchaseSupplierReturnResponseV1> Execute(Guid order,Guid id,DecidePurchaseSupplierReturnV1 r,CancellationToken t){Order=order;Id=id;Captured=r;if(!(await authority.RevalidatePermissionScopeAsync(PurchasingPermissionManifestV1.OrdersApprove,Company,t)).IsGranted)throw new PurchasingServiceException(403,"forbidden");return Answer;}
        public Task<PurchaseSupplierReturnResponseV1> ApproveSupplierReturnCostExceptionAsync(Guid o,Guid id,DecidePurchaseSupplierReturnV1 r,PurchasingCommandMetadata m,CancellationToken t=default)=>Execute(o,id,r,t);
        public Task<PurchaseSupplierReturnResponseV1> RejectSupplierReturnCostExceptionAsync(Guid o,Guid id,DecidePurchaseSupplierReturnV1 r,PurchasingCommandMetadata m,CancellationToken t=default)=>Execute(o,id,r,t);
        public Task<PurchaseSupplierReturnResponseV1> CreateSupplierReturnAsync(Guid o,CreatePurchaseSupplierReturnV1 r,PurchasingCommandMetadata m,CancellationToken t=default)=>throw new NotSupportedException();
        public Task<PurchaseSupplierReturnDetailV1> GetSupplierReturnAsync(Guid o,Guid id,CancellationToken t=default)=>throw new NotSupportedException();
        public Task<PurchaseSupplierReturnPageV1> GetSupplierReturnsAsync(Guid o,int limit=50,CancellationToken t=default)=>throw new NotSupportedException();
    }
    private sealed class Delegations:IWorkDelegationReaderV1
    {public bool Live=true;public Task<WorkDelegationViewV1?> FindForActorAsync(Guid id,WorkInboxItemV1 item,CancellationToken t=default)=>Task.FromResult<WorkDelegationViewV1?>(Live?new(id,Company,item.Kind,PurchasingPermissionManifestV1.OrdersApprove,Guid.NewGuid(),Guid.NewGuid(),At,DateTimeOffset.UtcNow.AddDays(1),"1",Guid.NewGuid(),At,"SENTETIK",null,null,null):null);}
}
