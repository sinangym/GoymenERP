using System.Text.Json;
using GoymenERP.Api.WorkRuntime;
using GoymenERP.BuildingBlocks.Multitenancy;
using GoymenERP.BuildingBlocks.Observability;
using GoymenERP.BuildingBlocks.Security;
using GoymenERP.BuildingBlocks.Work;
using GoymenERP.Modules.Accounting.Application;
using GoymenERP.Modules.Accounting.Infrastructure.Persistence;
using GoymenERP.Modules.Accounting.Infrastructure.WorkInbox;
using GoymenERP.Modules.Work.Infrastructure.Delegations;
using Microsoft.EntityFrameworkCore;
using static GoymenERP.Api.IntegrationTests.InventoryCostWorkDecisionHostTests;
namespace GoymenERP.Api.IntegrationTests;

// Real Application, real source and native Completion/Commands serialization; synthetic owner protocol, not SQL or rollback proof.
public sealed class SourceVoucherWorkDecisionHostTests
{
    private const string Kind="accounting.source-voucher.v1",Key="SENTETIK-source-voucher-work",Correlation="SENTETIK-source-voucher-correlation";
    [Theory] [InlineData(true)] [InlineData(false)]
    public async Task Full_human_GET_and_original_native_approval_survive_restart_closed_period_and_source_cancellation(bool periodOpen)
    {
        using var rig=new Rig();rig.Port.PeriodOpen=periodOpen;
        var preview=await rig.Preview();var human=Assert.IsType<WorkSourceVoucherPreviewV1>(preview.SourceVoucher);
        Assert.True(preview.Pending);Assert.False(preview.ApprovalReasonRequired);Assert.False(preview.RejectionSupported);
        Assert.Equal("1",preview.SourceVersion);Assert.Equal(periodOpen,human.PeriodOpen);Assert.Equal(2,human.Voucher.Lines.Count);
        Assert.Equal(2,rig.Port.Reads);var reads=rig.Port.Reads;
        rig.Port.PeriodOpen=true; // A previously waiting preview is not evidence that a closed period can post.
        var first=await rig.Decide();Assert.Equal("Posted",first.State);Assert.False(first.Replayed);Assert.Equal(Correlation,first.OriginalCorrelationId);
        Assert.Equal(reads+2,rig.Port.Reads);Assert.Equal(1,rig.Port.Effects);Assert.Equal(1L,rig.Port.Expected);Assert.Equal(Key,rig.Port.Key);
        var command=Assert.Single(rig.Port.Commands);Assert.Equal(8,JsonSerializer.Deserialize<JsonElement>(command.ResultJson,Json).EnumerateObject().Count());
        Assert.DoesNotContain("approvalReceipt",command.ResultJson,StringComparison.OrdinalIgnoreCase);
        Assert.Equal(4,JsonSerializer.SerializeToElement(rig.Request,Json).EnumerateObject().Count());
        Assert.Equal(6,JsonSerializer.SerializeToElement(first,Json).EnumerateObject().Count());
        rig.Port.PeriodOpen=false;rig.Port.Cancelled=true;rig.RestartOwner();reads=rig.Port.Reads;
        var replay=await rig.Decide();Assert.True(replay.Replayed);Assert.Equal("Posted",replay.State);Assert.Equal(Correlation,replay.OriginalCorrelationId);
        Assert.Equal(reads+2,rig.Port.Reads);Assert.Equal(1,rig.Port.Effects);Assert.Equal(1,rig.Port.Calls);Assert.Single(rig.Port.Commands);
        Assert.All(rig.Authority.Permissions,p=>Assert.Equal(NormalJournalPermissionNamesV1.DraftApprove,p));
        var wire=JsonSerializer.Serialize(preview,Json);Assert.DoesNotContain("sourceFactsDigest",wire,StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("previewDigest",wire,StringComparison.OrdinalIgnoreCase);Assert.DoesNotContain("sourceVoucherProof",wire,StringComparison.OrdinalIgnoreCase);
    }
    [Theory] [InlineData("0")] [InlineData("01")] [InlineData("2")] [InlineData("+1")] [InlineData("1\n")] [InlineData("1\r")] [InlineData("9223372036854775807")]
    public async Task Only_original_version_one_can_be_dispatched(string version)
    {using var r=new Rig();Assert.Equal(400,(await Assert.ThrowsAsync<WorkDecisionException>(()=>r.Decide(r.Request with{ExpectedVersion=version}))).Status);Assert.Equal(0,r.Source.Reads);Assert.Equal(0,r.Port.Calls);}
    [Theory] [InlineData("reject")] [InlineData("reason")] [InlineData("cancellation")] [InlineData("mapping")] [InlineData("catalog")]
    [InlineData("normal")] [InlineData("period")] [InlineData("package")] [InlineData("manufacturing")] [InlineData("loss")]
    [InlineData("overage")] [InlineData("plan")] [InlineData("supplier")] [InlineData("return")]
    public async Task Approve_only_four_field_command_rejects_every_foreign_selection_before_read(string fault)
    {
        using var r=new Rig();var request=fault switch{
            "reject"=>r.Request with{Approve=false},"reason"=>r.Request with{Reason=""},"cancellation"=>r.Request with{InventoryCostCancellation=new("1")},
            "mapping"=>r.Request with{PostingAccountMappingSelection=new("0")},"catalog"=>r.Request with{ApprovedSourceCatalogSelection=new("0")},
            "normal"=>r.Request with{NormalJournalDraft=new(Id)},"period"=>r.Request with{AccountingPeriodReopening=new("1")},
            "package"=>r.Request with{ProductPackageStandard=new("2026-11-28")},"manufacturing"=>r.Request with{ManufacturingPlanReapproval=new([])},
            "loss"=>r.Request with{InventoryTransferLoss=new(1,At)},"overage"=>r.Request with{InventoryTransferOverage=new(1,At)},
            "plan"=>r.Request with{LogisticsPlanExcess=new("1")},"supplier"=>r.Request with{SupplierReturnCostException=new()},_=>r.Request with{LogisticsCustomerReturn=new(Id)}};
        Assert.Equal(400,(await Assert.ThrowsAsync<WorkDecisionException>(()=>r.Decide(request))).Status);Assert.Equal(0,r.Source.Reads);Assert.Equal(0,r.Port.Calls);
    }
    [Theory] [InlineData("company")] [InlineData("voucher")] [InlineData("source")] [InlineData("invoice")] [InlineData("maker")]
    [InlineData("created")] [InlineData("kind")] [InlineData("finalized")] [InlineData("party")] [InlineData("currency")]
    [InlineData("status")] [InlineData("cancelled")] [InlineData("period")] [InlineData("facts")] [InlineData("preview")] [InlineData("number")] [InlineData("missing")]
    public async Task All_sixteen_scalar_identity_fields_require_the_real_owner_on_GET(string fault)
    {
        using var r=new Rig();r.Source.Change=s=>s with{SourceVoucherProof=ChangeProof(s.SourceVoucherProof!,fault)};
        Assert.Equal(503,(await Assert.ThrowsAsync<WorkDecisionException>(()=>r.Preview())).Status);Assert.Equal(0,r.Port.Effects);
    }
    private static WorkSourceVoucherProofV1? ChangeProof(WorkSourceVoucherProofV1 p,string fault)=>fault switch{
        "company"=>p with{CompanyId=Guid.NewGuid()},"voucher"=>p with{VoucherId=Guid.NewGuid()},"source"=>p with{SourceDocumentId=Guid.NewGuid()},
        "invoice"=>p with{InvoiceId=Guid.NewGuid()},"maker"=>p with{SourceActorId=Actor},"created"=>p with{CreatedAtUtc=At.AddTicks(1)},
        "kind"=>p with{DocumentKind="other"},"finalized"=>p with{FinalizedVersion=5},"party"=>p with{PartyId=Guid.NewGuid()},"currency"=>p with{CurrencyId=Guid.NewGuid()},
        "status"=>p with{Status="Posted"},"cancelled"=>p with{SourceCancelled=true},"period"=>p with{PeriodOpen=false},
        "facts"=>p with{SourceFactsDigest=new('B',64)},"preview"=>p with{PreviewDigest=new('B',64)},"number"=>p with{OfficialInvoiceNo="other"},_=>null};
    [Theory] [InlineData("company")] [InlineData("voucher")] [InlineData("source")] [InlineData("invoice")] [InlineData("maker")] [InlineData("created")]
    [InlineData("command")] [InlineData("key")] [InlineData("payload")] [InlineData("actor")] [InlineData("expected")] [InlineData("result")]
    [InlineData("journal")] [InlineData("correlation")] [InlineData("approved")] [InlineData("facts")] [InlineData("digest")] [InlineData("missing")]
    public async Task Genuine_receipt_cannot_be_released_after_any_of_its_seventeen_bindings_is_changed(string fault)
    {
        using var r=new Rig();r.Port.AfterAnswer=a=>{var v=a.Resource!;var p=v.ApprovalReceipt!;return a with{Resource=v with{ApprovalReceipt=fault switch{
            "company"=>p with{CompanyId=Guid.NewGuid()},"voucher"=>p with{VoucherId=Guid.NewGuid()},"source"=>p with{SourceDocumentId=Guid.NewGuid()},
            "invoice"=>p with{InvoiceId=Guid.NewGuid()},"maker"=>p with{SourceActorId=Actor},"created"=>p with{CreatedAtUtc=At.AddTicks(1)},
            "command"=>p with{CommandName="other"},"key"=>p with{IdempotencyKeyHash=new('B',64)},"payload"=>p with{PayloadHash=new('B',64)},
            "actor"=>p with{ActorId=Maker},"expected"=>p with{ExpectedVersion=2},"result"=>p with{ResultVersion=3},"journal"=>p with{JournalId=Guid.NewGuid()},
            "correlation"=>p with{OriginalCorrelationId="other"},"approved"=>p with{ApprovedAtUtc=At},"facts"=>p with{SourceFactsDigest=new('B',64)},"digest"=>p with{OriginalResultDigest=new('B',64)},_=>null}}};};
        Assert.Equal(503,(await Assert.ThrowsAsync<WorkDecisionException>(()=>r.Decide())).Status);Assert.Equal(1,r.Port.Effects);Assert.Single(r.Port.Commands);
        r.Port.AfterAnswer=null;Assert.True((await r.Decide()).Replayed);Assert.Equal(1,r.Port.Effects);
    }
    [Theory] [InlineData("caption")] [InlineData("rule")] [InlineData("reference")] [InlineData("scope")] [InlineData("link")]
    public async Task Formatter_identity_and_full_payable_binding_are_not_opaque_hash_self_certification(string fault)
    {
        using var r=new Rig();r.Source.Change=s=>s with{Item=s.Item with{DocumentLabel=fault=="caption"?"other":s.Item.DocumentLabel,
            Rules=fault=="rule"?[s.Item.Rules[0] with{Actual="121"}]:s.Item.Rules,References=fault=="reference"?[]:s.Item.References,
            Scope=fault=="scope"?new(Id,null,null):s.Item.Scope,Link=fault=="link"?"/other":s.Item.Link}};
        Assert.Equal(503,(await Assert.ThrowsAsync<WorkDecisionException>(()=>r.Preview())).Status);Assert.Equal(0,r.Port.Effects);
        // Scalar cannot authenticate payable alone: POST validates it against the genuine original native result after one effect.
        if(fault=="rule"){Assert.Equal(503,(await Assert.ThrowsAsync<WorkDecisionException>(()=>r.Decide())).Status);Assert.Equal(1,r.Port.Effects);}
        else{Assert.Equal(503,(await Assert.ThrowsAsync<WorkDecisionException>(()=>r.Decide())).Status);Assert.Equal(0,r.Port.Effects);}
    }
    [Theory] [InlineData("maker")] [InlineData("permission")] [InlineData("company")] [InlineData("member")]
    public async Task Current_identity_membership_and_narrow_permission_are_required(string fault)
    {using var r=new Rig();if(fault=="maker")r.User.Actor=Maker;if(fault=="permission")r.Authority.Allowed=false;if(fault=="company")r.Company.Set(Guid.NewGuid());if(fault=="member")r.User.Member=false;
        Assert.Equal(403,(await Assert.ThrowsAsync<WorkDecisionException>(()=>r.Decide())).Status);Assert.Equal(0,r.Port.Effects);}
    [Theory] [InlineData("permission")] [InlineData("company")] [InlineData("actor")] [InlineData("cancel")]
    public async Task Completed_human_read_rechecks_authority_context_and_cancellation(string fault)
    {using var r=new Rig();using var stop=new CancellationTokenSource();r.Port.BeforeRead=n=>{if(n!=2)return;if(fault=="permission")r.Authority.Allowed=false;if(fault=="company")r.Company.Set(Guid.NewGuid());if(fault=="actor")r.User.Actor=Maker;if(fault=="cancel")stop.Cancel();};
        if(fault=="cancel")await Assert.ThrowsAnyAsync<OperationCanceledException>(()=>r.Preview(stop.Token));else Assert.Equal(403,(await Assert.ThrowsAsync<WorkDecisionException>(()=>r.Preview())).Status);Assert.Equal(0,r.Port.Effects);}
    [Theory] [InlineData("permission")] [InlineData("delegation")] [InlineData("cancel")]
    public async Task Actual_native_fence_denial_prevents_the_synthetic_effect(string fault)
    {using var r=new Rig();using var stop=new CancellationTokenSource();var request=r.Request with{DelegationId=fault=="delegation"?Id:null};r.Port.BeforeMutation=()=>{if(fault=="permission")r.Authority.Allowed=false;if(fault=="delegation")r.Delegations.Live=false;if(fault=="cancel")stop.Cancel();};
        if(fault=="cancel")await Assert.ThrowsAnyAsync<OperationCanceledException>(()=>r.Decide(request,stop.Token));else Assert.Equal(403,(await Assert.ThrowsAsync<WorkDecisionException>(()=>r.Decide(request))).Status);Assert.Equal(0,r.Port.Effects);Assert.Empty(r.Port.Commands);}
    [Theory] [InlineData("permission")] [InlineData("delegation")] [InlineData("cancel")]
    public async Task Post_effect_release_denial_preserves_original_key_reconciliation_without_claiming_rollback(string fault)
    {using var r=new Rig();using var stop=new CancellationTokenSource();var request=r.Request with{DelegationId=fault=="delegation"?Id:null};r.Port.AfterAnswer=a=>{if(fault=="permission")r.Authority.Allowed=false;if(fault=="delegation")r.Delegations.Live=false;if(fault=="cancel")stop.Cancel();return a;};
        if(fault=="cancel")await Assert.ThrowsAnyAsync<OperationCanceledException>(()=>r.Decide(request,stop.Token));else Assert.Equal(403,(await Assert.ThrowsAsync<WorkDecisionException>(()=>r.Decide(request))).Status);Assert.Equal(1,r.Port.Effects);
        r.Port.AfterAnswer=null;r.Authority.Allowed=true;r.Delegations.Live=true;Assert.True((await r.Decide(request)).Replayed);Assert.Equal(1,r.Port.Effects);}
    [Fact]
    public async Task Missing_owner_and_wrong_kind_private_proof_are_closed()
    {
        using var r=new Rig(inject:false);Assert.Equal(503,(await Assert.ThrowsAsync<WorkDecisionException>(()=>r.Preview())).Status);Assert.Equal(503,(await Assert.ThrowsAsync<WorkDecisionException>(()=>r.Decide())).Status);Assert.Equal(0,r.Port.Calls);
        var s=(await r.Source.ReadDecisionAsync(new(Company,Actor.ToString("D"),Correlation,null),Id,default))!;
        var foreign=new SalesSource(s.SourceVoucherProof!);Assert.Equal(503,(await Assert.ThrowsAsync<WorkDecisionException>(()=>r.WithSource(foreign).PreviewAsync(foreign.Kind,Id,default))).Status);
        foreign.Proof=null;Assert.Equal(foreign.Kind,(await r.WithSource(foreign).PreviewAsync(foreign.Kind,Id,default)).Kind);
    }
    public static IEnumerable<object[]> ForeignCarriers => new object[][] {["ManufacturingPlanReapproval"],["InventoryTransferLoss"],["CounterpartyTaxIdentityChange"],["CounterpartyLegalNameChange"],["LogisticsPlanExcess"],["InventoryTransferLossProof"],["LogisticsPlanExcessProof"],["InventoryTransferOverageProof"],["LogisticsWeighingDifferenceProof"],["SupplierReturnCostExceptionProof"],["InventoryStockCountProof"],["BankAccountProof"],["InventoryStockScrapProof"],["LogisticsDispatchNoteProof"],["LogisticsCustomerReturnProof"],["BankAccountIbanChangeProof"],["ProductPackageStandardProof"],["ProductPriceListProof"],["PartyCreditLimitProof"],["OfficialInvoiceImportProof"],["OfficialSalesInvoiceProof"],["ManualBankPaymentReversalProof"],["ManualReceiptReversalProof"],["ExpensePaymentProof"],["BankCreditLimitProof"],["OpeningBalanceProof"],["CreditMovementProof"],["CashExchangeBookValueProof"],["NormalJournalDraftProof"],["AccountingPeriodOpeningProof"],["AccountingPeriodReopeningProof"],["JournalReversalProof"],["ApprovedSourceCatalogProof"],["PostingAccountMappingProof"],["ManufacturingCostDifferenceProof"],["InvoiceLineCogsProof"],["InventoryCostProof"],["InventoryCostCancellationProof"] };
    [Theory] [MemberData(nameof(ForeignCarriers))]
    public async Task Every_other_typed_carrier_is_rejected_before_native_dispatch(string name)
    {
        using var rig=new Rig();rig.Source.Change=s=>name switch{
            "ManufacturingPlanReapproval"=>s with{ManufacturingPlanReapproval=Foreign<WorkManufacturingPlanReapprovalOptionsV1>()},
            "InventoryTransferLoss"=>s with{InventoryTransferLoss=Foreign<WorkInventoryTransferLossOptionsV1>()},
            "CounterpartyTaxIdentityChange"=>s with{CounterpartyTaxIdentityChange=Foreign<WorkCounterpartyTaxIdentityChangePreviewV1>()},
            "CounterpartyLegalNameChange"=>s with{CounterpartyLegalNameChange=Foreign<WorkCounterpartyLegalNameChangePreviewV1>()},
            "LogisticsPlanExcess"=>s with{LogisticsPlanExcess=Foreign<WorkLogisticsPlanExcessOptionsV1>()},
            "InventoryTransferLossProof"=>s with{InventoryTransferLossProof=Foreign<WorkInventoryTransferLossProofV1>()},
            "LogisticsPlanExcessProof"=>s with{LogisticsPlanExcessProof=Foreign<WorkLogisticsPlanExcessProofV1>()},
            "InventoryTransferOverageProof"=>s with{InventoryTransferOverageProof=Foreign<WorkInventoryTransferOverageProofV1>()},
            "LogisticsWeighingDifferenceProof"=>s with{LogisticsWeighingDifferenceProof=Foreign<WorkLogisticsWeighingDifferenceProofV1>()},
            "SupplierReturnCostExceptionProof"=>s with{SupplierReturnCostExceptionProof=Foreign<WorkSupplierReturnCostExceptionProofV1>()},
            "InventoryStockCountProof"=>s with{InventoryStockCountProof=Foreign<WorkInventoryStockCountProofV1>()},
            "BankAccountProof"=>s with{BankAccountProof=Foreign<WorkBankAccountProofV1>()},
            "InventoryStockScrapProof"=>s with{InventoryStockScrapProof=Foreign<WorkInventoryStockScrapProofV1>()},
            "LogisticsDispatchNoteProof"=>s with{LogisticsDispatchNoteProof=Foreign<WorkLogisticsDispatchNoteProofV1>()},
            "LogisticsCustomerReturnProof"=>s with{LogisticsCustomerReturnProof=Foreign<WorkLogisticsCustomerReturnProofV1>()},
            "BankAccountIbanChangeProof"=>s with{BankAccountIbanChangeProof=Foreign<WorkBankAccountIbanChangeProofV1>()},
            "ProductPackageStandardProof"=>s with{ProductPackageStandardProof=Foreign<WorkProductPackageStandardProofV1>()},
            "ProductPriceListProof"=>s with{ProductPriceListProof=Foreign<WorkProductPriceListProofV1>()},
            "PartyCreditLimitProof"=>s with{PartyCreditLimitProof=Foreign<WorkPartyCreditLimitProofV1>()},
            "OfficialInvoiceImportProof"=>s with{OfficialInvoiceImportProof=Foreign<WorkOfficialInvoiceImportProofV1>()},
            "OfficialSalesInvoiceProof"=>s with{OfficialSalesInvoiceProof=Foreign<WorkOfficialSalesInvoiceProofV1>()},
            "ManualBankPaymentReversalProof"=>s with{ManualBankPaymentReversalProof=Foreign<WorkManualBankPaymentReversalProofV1>()},
            "ManualReceiptReversalProof"=>s with{ManualReceiptReversalProof=Foreign<WorkManualReceiptReversalProofV1>()},
            "ExpensePaymentProof"=>s with{ExpensePaymentProof=Foreign<WorkExpensePaymentProofV1>()},
            "BankCreditLimitProof"=>s with{BankCreditLimitProof=Foreign<WorkBankCreditLimitProofV1>()},
            "OpeningBalanceProof"=>s with{OpeningBalanceProof=Foreign<WorkOpeningBalanceProofV1>()},
            "CreditMovementProof"=>s with{CreditMovementProof=Foreign<WorkCreditMovementProofV1>()},
            "CashExchangeBookValueProof"=>s with{CashExchangeBookValueProof=Foreign<WorkCashExchangeBookValueProofV1>()},
            "NormalJournalDraftProof"=>s with{NormalJournalDraftProof=Foreign<WorkNormalJournalDraftProofV1>()},
            "AccountingPeriodOpeningProof"=>s with{AccountingPeriodOpeningProof=Foreign<WorkAccountingPeriodOpeningProofV1>()},
            "AccountingPeriodReopeningProof"=>s with{AccountingPeriodReopeningProof=Foreign<WorkAccountingPeriodReopeningProofV1>()},
            "JournalReversalProof"=>s with{JournalReversalProof=Foreign<WorkJournalReversalProofV1>()},
            "ApprovedSourceCatalogProof"=>s with{ApprovedSourceCatalogProof=Foreign<WorkApprovedSourceCatalogProofV1>()},
            "PostingAccountMappingProof"=>s with{PostingAccountMappingProof=Foreign<WorkPostingAccountMappingProofV1>()},
            "ManufacturingCostDifferenceProof"=>s with{ManufacturingCostDifferenceProof=Foreign<WorkManufacturingCostDifferenceProofV1>()},
            "InvoiceLineCogsProof"=>s with{InvoiceLineCogsProof=Foreign<WorkInvoiceLineCogsProofV1>()},
            "InventoryCostProof"=>s with{InventoryCostProof=Foreign<WorkInventoryCostProofV1>()},
            "InventoryCostCancellationProof"=>s with{InventoryCostCancellationProof=Foreign<WorkInventoryCostCancellationProofV1>()},
            _=>throw new ArgumentOutOfRangeException(nameof(name))};
        Assert.Equal(503,(await Assert.ThrowsAsync<WorkDecisionException>(()=>rig.Decide())).Status);Assert.Equal(0,rig.Port.Calls);
    }
    // Adversarial empty foreign carriers are intentionally not valid owner certificates.
    private static T Foreign<T>() where T:class=>JsonSerializer.Deserialize<T>("{}",Json)!;
    [Theory] [InlineData("lines")] [InlineData("payable")] [InlineData("date")] [InlineData("mapping")]
    public async Task Human_GET_cannot_release_changed_owner_facts_under_the_first_snapshot(string fault)
    {
        using var r=new Rig();r.Port.BeforeRead=n=>{if(n!=2)return;var v=r.Port.Stored.StoredVoucher;v=fault switch{
            "lines"=>v with{Lines=[v.Lines[0] with{FunctionalAmount="121"},v.Lines[1]]},"payable"=>v with{FunctionalPayable="121"},
            "date"=>v with{DocumentDate=v.DocumentDate.AddDays(1)},_=>v with{MappingVersion=2}};r.Port.Stored=r.Port.Stored with{StoredVoucher=v,CurrentVoucher=v};};
        Assert.Equal(503,(await Assert.ThrowsAsync<WorkDecisionException>(()=>r.Preview())).Status);Assert.Equal(0,r.Port.Effects);
    }
    [Theory] [InlineData("root-extra")] [InlineData("resource-extra")] [InlineData("private-extra")] [InlineData("actor")] [InlineData("hash")]
    public async Task Restart_cannot_turn_altered_original_Commands_into_a_genuine_receipt(string fault)
    {
        using var r=new Rig();await r.Decide();var command=Assert.Single(r.Port.Commands);
        if(fault.EndsWith("extra",StringComparison.Ordinal)){var node=System.Text.Json.Nodes.JsonNode.Parse(command.ResultJson)!;
            if(fault=="root-extra")node["extra"]=true;else node["resource"]![fault=="private-extra"?"approvalReceipt":"extra"]=true;
            command=command with{ResultJson=node.ToJsonString()};}
        else if(fault=="actor")command=command with{ActorId=Maker.ToString("D")};else command=command with{RequestHash=new('B',64)};
        r.Port.Commands[0]=command;r.RestartOwner();
        Assert.Equal(fault is "actor" or "hash"?409:503,(await Assert.ThrowsAsync<WorkDecisionException>(()=>r.Decide())).Status);
        Assert.Equal(1,r.Port.Effects);Assert.Single(r.Port.Commands);
    }
    internal static AccountingSourceVoucherDecisionSourceV1 Facts(long finalized=4,long mapping=1)
    {
        var v=new AccountingSourceVoucherViewV1(Id,Company,"sales-invoice",Guid.NewGuid(),Guid.NewGuid(),finalized,Maker,Guid.NewGuid(),Guid.NewGuid(),null,new(2026,11,28),null,"AwaitingApproval",1,mapping,null,null,null,null,null,
            [new(1,"SENTETIK debit",1,Guid.NewGuid(),"120",1,"20","120","SENTETIK account"),new(2,"SENTETIK credit",2,Guid.NewGuid(),"120",null,null,"320","SENTETIK payable")],"SENTETIK-57","120");
        return new(v,v,At,new(100,20,0,120,100,20,0,120),false,true,false,null);
    }
    private sealed class Rig:IDisposable
    {
        public MutableCompany Company{get;}=new();public MutableUser User{get;}=new();public Authority Authority{get;}=new();public Delegations Delegations{get;}=new();public Protocol Port{get;}=new(Facts());
        public SourceProxy Source{get;private set;}=null!;public WorkDecisionHostV1 Host{get;private set;}=null!;
        private readonly AccountingDbContext db;private readonly CorrelationContext correlation=new();private readonly WorkDecisionExecutionFenceV1 execution=new();private readonly WorkDecisionAuthorityV1 authority;private readonly bool inject;
        public Rig(bool inject=true){this.inject=inject;Company.Set(InventoryCostWorkDecisionHostTests.Company);correlation.Set(Correlation,null,"Web",null,null);authority=new(Authority,execution);
            db=new(new DbContextOptionsBuilder<AccountingDbContext>().UseInMemoryDatabase("SENTETIK-voucher-host-"+Guid.NewGuid()).Options,Company);RestartOwner();}
        public void RestartOwner(){var owner=new AccountingSourceVoucherServiceV1(Port,Port,authority,Company,User,correlation);Source=new(new AccountingSourceVoucherWorkInboxSourceV1(db,new(Company,User,authority),owner));Host=Make(Source,inject?owner:null);}
        private WorkDecisionHostV1 Make(IWorkInboxSourceV1 source,AccountingSourceVoucherServiceV1? owner)=>new(Company,User,correlation,authority,[source],Delegations,execution,null!,null!,sourceVouchers:owner);
        public WorkDecisionHostV1 WithSource(IWorkInboxSourceV1 source)=>Make(source,new(Port,Port,authority,Company,User,correlation));
        public WorkDecisionRequestV1 Request=>new("1",true,null);public Task<WorkDecisionPreviewV1> Preview(CancellationToken t=default)=>Host.PreviewAsync(Kind,Id,t);
        public Task<WorkDecisionResultV1> Decide(WorkDecisionRequestV1? r=null,CancellationToken t=default)=>Host.DecideAsync(Kind,Id,r??Request,Key,t);public void Dispose()=>db.Dispose();
    }
    private sealed class Protocol(AccountingSourceVoucherDecisionSourceV1 facts):IAccountingSourceVoucherStoreV1,IAccountingSourceVoucherDecisionStoreV1,IAccountingCommandLog
    {
        public AccountingSourceVoucherDecisionSourceV1 Stored=facts;public bool PeriodOpen=true,Cancelled;public int Reads,Calls,Effects;public long Expected;public string? Key;public Action<int>? BeforeRead;public Action? BeforeMutation;
        public Func<AccountingApiResult<AccountingSourceVoucherViewV1>,AccountingApiResult<AccountingSourceVoucherViewV1>>? AfterAnswer;public List<AccountingCommandRecord> Commands{get;}=[];
        public async Task<AccountingSourceVoucherDecisionSourceV1?> ReadSourceVoucherDecisionAsync(Guid id,Func<CancellationToken,Task> fence,CancellationToken token=default)
        {await fence(token);Reads++;BeforeRead?.Invoke(Reads);await fence(token);if(id!=Id)return null;var v=Stored.StoredVoucher;
            var current=v with{Status=Cancelled?"CancellationReversalPending":v.Status=="Posted"?"Posted":PeriodOpen?"AwaitingApproval":"PeriodWaiting",CancellationId=Cancelled?Id:null,CancellationReason=Cancelled?"SENTETIK cancelled":null};
            return Stored with{CurrentVoucher=current,SourceCancelled=Cancelled,PeriodOpen=PeriodOpen};}
        public async Task<AccountingApiResult<AccountingSourceVoucherViewV1>> RunApprovalAsync(Func<bool,Task<AccountingApiResult<AccountingSourceVoucherViewV1>>> attempt,Func<CancellationToken,Task> fence,CancellationToken token=default)
        {try{await fence(token);var answer=await attempt(false);await fence(token);return AfterAnswer?.Invoke(answer)??answer;}catch(NormalJournalAuthorityDeniedException){return new(AccountingApiStatus.Denied,null,false,null);}}
        public async Task<AccountingSourceVoucherViewV1> ApproveAsync(Guid id,long version,Guid actor,Func<CancellationToken,Task> fence,AccountingCommandMetadata metadata,CancellationToken token=default)
        {Calls++;BeforeMutation?.Invoke();await fence(token);AccountingSourceVoucherRulesV1.RequireApproval(1,Stored.StoredVoucher.SourceActorId,actor,Cancelled);if(!PeriodOpen)throw new AccountingStoreException("source_voucher_period_waiting","SENTETIK");Expected=version;Key=metadata.IdempotencyKey;
            var v=Stored.StoredVoucher with{Status="Posted",Version=2,PeriodId=Guid.NewGuid(),JournalId=id,ApprovedBy=actor,ApprovedAtUtc=At.AddMinutes(1)};
            Effects++;Stored=Stored with{StoredVoucher=v,CurrentVoucher=v,OriginalJournalBound=true,OriginalJournalCorrelationId=metadata.CorrelationId};Commands.Add(metadata.Completion!(v));return v;}
        public Task<AccountingCommandRecord?> FindCommandAsync(string command,string key,CancellationToken token=default)=>Task.FromResult(Commands.SingleOrDefault(c=>c.CommandName==command&&c.IdempotencyKeyHash==key));
        public Task RecordCommandAsync(AccountingCommandRecord command,CancellationToken token=default)=>throw new InvalidOperationException("No unguarded command write");
        public async Task RecordApprovalAttemptAsync(AccountingCommandRecord command,Func<CancellationToken,Task> fence,CancellationToken token=default){await fence(token);Commands.Add(command);}
        public Task<IReadOnlyList<AccountingSourceVoucherViewV1>> ListAsync(CancellationToken token=default)=>throw new InvalidOperationException("No general list");
        public Task<AccountingSourceVoucherViewV1?> FindAsync(Guid id,CancellationToken token=default)=>throw new InvalidOperationException("No general decorated read");
        public Task<AccountingSourceVoucherViewV1> ProposeCancellationReversalAsync(Guid id,ProposeAccountingSourceVoucherReversalV1 request,Guid actor,Func<CancellationToken,Task> fence,AccountingCommandMetadata metadata,CancellationToken token=default)=>throw new InvalidOperationException("No reversal mutation");
    }
    private sealed class SourceProxy(IWorkApprovalDecisionSourceV1 inner):IWorkApprovalDecisionSourceV1
    {public int Reads;public Func<WorkApprovalDecisionSnapshotV1,WorkApprovalDecisionSnapshotV1>? Change;public string Module=>inner.Module;public string Kind=>inner.Kind;public string ApproverPermission=>inner.ApproverPermission;
        public Task<WorkInboxSourceResultV1> ListPendingAsync(WorkInboxSourceContextV1 c,int max,CancellationToken t)=>inner.ListPendingAsync(c,max,t);
        public async Task<WorkApprovalDecisionSnapshotV1?> ReadDecisionAsync(WorkInboxSourceContextV1 c,Guid id,CancellationToken t){Reads++;var s=await inner.ReadDecisionAsync(c,id,t);return s is null?null:Change?.Invoke(s)??s;}}
    private sealed class SalesSource(WorkSourceVoucherProofV1? proof):IWorkApprovalDecisionSourceV1
    {public WorkSourceVoucherProofV1? Proof=proof;public string Module=>"sales";public string Kind=>WorkDecisionHostV1.SalesOrder;public string ApproverPermission=>ErpPermissions.SalesOrdersApprove;
        public Task<WorkInboxSourceResultV1> ListPendingAsync(WorkInboxSourceContextV1 c,int max,CancellationToken t)=>throw new NotSupportedException();
        public Task<WorkApprovalDecisionSnapshotV1?> ReadDecisionAsync(WorkInboxSourceContextV1 c,Guid id,CancellationToken t)=>Task.FromResult<WorkApprovalDecisionSnapshotV1?>(new(Company,new(Module,Kind,id,"SENTETIK sales",At,"/satis/siparisler",Maker.ToString("D"),[],[]),"1",true){SourceVoucherProof=Proof});}
    private sealed class MutableCompany:ICompanyContext{public Guid CompanyId{get;private set;}public bool IsResolved=>CompanyId!=Guid.Empty;public void Set(Guid id)=>CompanyId=id;}
    private sealed class MutableUser:ICurrentUser{public Guid Actor=InventoryCostWorkDecisionHostTests.Actor;public bool Member=true;public bool IsAuthenticated=>true;public string UserId=>Actor.ToString("D");public IReadOnlySet<Guid> AllowedCompanyIds{get;}=new HashSet<Guid>{Company};public bool CanAccessCompany(Guid id)=>Member&&id==Company;}
    private sealed class Authority:IErpAuthorityRevalidationService{public bool Allowed=true;public List<string> Permissions{get;}=[];public Task<PermissionScope> RevalidatePermissionScopeAsync(string permission,Guid company,CancellationToken token=default){Permissions.Add(permission);return Task.FromResult(new PermissionScope(Allowed,true,new HashSet<string>(),new HashSet<Guid>(),new HashSet<Guid>(),new HashSet<Guid>()));}}
    private sealed class Delegations:IWorkDelegationReaderV1{public bool Live=true;public Task<WorkDelegationViewV1?> FindForActorAsync(Guid id,WorkInboxItemV1 item,CancellationToken token=default)=>Task.FromResult<WorkDelegationViewV1?>(Live?new(id,Company,item.Kind,NormalJournalPermissionNamesV1.DraftApprove,Maker,Actor,At.AddDays(-2),At.AddYears(1),"1",Maker,At,"SENTETIK",null,null,null):null);}
}
