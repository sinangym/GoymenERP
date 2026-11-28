using System.Globalization;
using System.Text.Json;
using GoymenERP.Api.WorkRuntime;
using GoymenERP.BuildingBlocks.Messaging;
using GoymenERP.BuildingBlocks.Multitenancy;
using GoymenERP.BuildingBlocks.Observability;
using GoymenERP.BuildingBlocks.Security;
using GoymenERP.BuildingBlocks.Work;
using GoymenERP.Modules.Accounting.Application;
using GoymenERP.Modules.Accounting.Application.Contracts;
using GoymenERP.Modules.Accounting.Infrastructure.Persistence;
using GoymenERP.Modules.Accounting.Infrastructure.WorkInbox;
using GoymenERP.Modules.Work.Infrastructure.Delegations;
using Microsoft.EntityFrameworkCore;
using static GoymenERP.Api.IntegrationTests.InventoryCostWorkDecisionHostTests;
namespace GoymenERP.Api.IntegrationTests;

// Real Application + real versioned source, synthetic store/Commands protocol. Not SQL, Audit or rollback proof.
public sealed class InventoryCostCancellationWorkDecisionHostTests
{
    private const string Kind="accounting.inventory-cost-cancellation.v1",Key="SENTETIK-cancellation-work",Correlation="SENTETIK-cancellation-work-correlation";
    [Theory]
    [InlineData(7L,1L)] [InlineData(9007199254740993L,9007199254740993L)] [InlineData(long.MaxValue,long.MaxValue-1)]
    public async Task Native_two_versions_and_five_field_original_request_survive_later_reprepare_and_outer_state(long outer,long cancellation)
    {
        using var rig=new Rig(outer,cancellation);
        var preview=await rig.Preview();var human=Assert.IsType<WorkInventoryCostCancellationPreviewV1>(preview.InventoryCostCancellation);
        Assert.True(preview.Pending);Assert.False(preview.ApprovalReasonRequired);Assert.False(preview.RejectionSupported);
        Assert.Equal(cancellation.ToString(CultureInfo.InvariantCulture),preview.SourceVersion);
        Assert.Equal(outer.ToString(CultureInfo.InvariantCulture),human.OriginalProposal.Version);Assert.Equal(preview.SourceVersion,human.PreparedCancellation.Version);
        Assert.Equal(rig.Port.Original.PreparedRecordedAtUtc,human.PreparedRecordedAtUtc);Assert.Equal(2,rig.Port.Reads);
        var reads=rig.Port.Reads;var first=await rig.Decide();Assert.Equal("CancelledByComponentReversal",first.State);Assert.False(first.Replayed);
        Assert.Equal(reads+2,rig.Port.Reads);Assert.Equal(outer,rig.Port.Expected?.ExpectedVersion);Assert.Equal(cancellation,rig.Port.Expected?.ExpectedCancellationVersion);
        Assert.Equal(Key,rig.Port.Key);Assert.Equal(1,rig.Port.Effects);Assert.Equal(1,rig.Port.Calls);Assert.Single(rig.Port.Commands);
        Assert.Equal(cancellation.ToString(CultureInfo.InvariantCulture),rig.Source.Selected);
        var frozen=JsonSerializer.Serialize(rig.Request,Json);Assert.Equal(5,JsonSerializer.SerializeToElement(rig.Request,Json).EnumerateObject().Count());
        Assert.DoesNotContain(rig.Port.Original.PreparedCancellation.Reason,frozen,StringComparison.Ordinal);Assert.DoesNotContain("sourceFactsDigest",frozen,StringComparison.OrdinalIgnoreCase);
        rig.Port.Current=Facts(99,77); // Separate latest preparation/outer N must never reconstruct an old key's native body.
        reads=rig.Port.Reads;var replay=await rig.Decide();Assert.True(replay.Replayed);Assert.Equal(Correlation,replay.OriginalCorrelationId);
        Assert.Equal(reads+2,rig.Port.Reads);Assert.Equal(cancellation.ToString(CultureInfo.InvariantCulture),rig.Source.Selected);
        Assert.Equal(1,rig.Port.Effects);Assert.Single(rig.Port.Commands);Assert.Equal(frozen,JsonSerializer.Serialize(rig.Request,Json));
        Assert.All(rig.Authority.Permissions,p=>Assert.Equal(AccountingJournalReversalPermissionNamesV1.Approve,p));
        Assert.DoesNotContain("cancellationApprovalReceipt",rig.Port.Commands[0].ResultJson,StringComparison.OrdinalIgnoreCase);
        Assert.Equal(8,JsonSerializer.Deserialize<JsonElement>(rig.Port.Commands[0].ResultJson,Json).EnumerateObject().Count());
        var response=JsonSerializer.Serialize(first,Json);Assert.Equal(6,JsonSerializer.SerializeToElement(first,Json).EnumerateObject().Count());
        Assert.DoesNotContain(rig.Port.Original.SourceFactsDigest,response,StringComparison.Ordinal);Assert.DoesNotContain(rig.Port.Original.PreparedCancellation.Reason,response,StringComparison.Ordinal);
    }
    [Theory]
    [InlineData("0")] [InlineData("01")] [InlineData("+1")] [InlineData("1\n")] [InlineData("1\r")]
    [InlineData("9223372036854775807")] [InlineData("9223372036854775808")] [InlineData("")]
    public async Task Cancellation_M_must_be_canonical_positive_incrementable_before_source_read(string version)
    {using var rig=new Rig();Assert.Equal(400,(await Assert.ThrowsAsync<WorkDecisionException>(()=>rig.Decide(rig.Request with{ExpectedVersion=version}))).Status);Assert.Equal(0,rig.Source.Reads);Assert.Equal(0,rig.Port.Calls);}
    [Theory]
    [InlineData("0")] [InlineData("07")] [InlineData("+7")] [InlineData("7\n")] [InlineData("7\r")]
    [InlineData("9223372036854775808")] [InlineData("")]
    public async Task Outer_N_is_a_separate_canonical_positive_identity_not_an_increment(string version)
    {using var rig=new Rig();Assert.Equal(400,(await Assert.ThrowsAsync<WorkDecisionException>(()=>rig.Decide(rig.Request with{InventoryCostCancellation=new(version)}))).Status);Assert.Equal(0,rig.Source.Reads);Assert.Equal(0,rig.Port.Calls);}
    [Theory]
    [InlineData("reject")] [InlineData("reason")] [InlineData("missing")] [InlineData("normal")] [InlineData("period")] [InlineData("mapping")]
    [InlineData("catalog")] [InlineData("package")] [InlineData("manufacturing")] [InlineData("loss")] [InlineData("overage")] [InlineData("plan")] [InlineData("supplier")] [InlineData("return")]
    public async Task Approve_only_no_reason_requires_N_and_rejects_all_foreign_request_selections(string fault)
    {
        using var rig=new Rig();var r=fault switch{
            "reject"=>rig.Request with{Approve=false},"reason"=>rig.Request with{Reason=""},"missing"=>rig.Request with{InventoryCostCancellation=null},
            "normal"=>rig.Request with{NormalJournalDraft=new(Id)},"period"=>rig.Request with{AccountingPeriodReopening=new("1")},"mapping"=>rig.Request with{PostingAccountMappingSelection=new("0")},
            "catalog"=>rig.Request with{ApprovedSourceCatalogSelection=new("0")},"package"=>rig.Request with{ProductPackageStandard=new("2026-11-28")},
            "manufacturing"=>rig.Request with{ManufacturingPlanReapproval=new([])},"loss"=>rig.Request with{InventoryTransferLoss=new(1,At)},"overage"=>rig.Request with{InventoryTransferOverage=new(1,At)},
            "plan"=>rig.Request with{LogisticsPlanExcess=new("1")},"supplier"=>rig.Request with{SupplierReturnCostException=new()},_=>rig.Request with{LogisticsCustomerReturn=new(Id)}};
        Assert.Equal(400,(await Assert.ThrowsAsync<WorkDecisionException>(()=>rig.Decide(r))).Status);Assert.Equal(0,rig.Source.Reads);Assert.Equal(0,rig.Port.Calls);
    }
    [Fact]
    public async Task Cancellation_selection_is_rejected_on_every_other_Work_kind_before_any_source_or_native_call()
    {
        using var rig=new Rig();foreach(var kind in WorkDecisionHostV1.Kinds.Where(k=>k!=Kind))
            Assert.Equal(400,(await Assert.ThrowsAsync<WorkDecisionException>(()=>rig.Host.DecideAsync(kind,Id,rig.Request,Key,default))).Status);
        Assert.Equal(0,rig.Source.Reads);Assert.Equal(0,rig.Port.Calls);
    }
    [Theory]
    [InlineData("missing")] [InlineData("company")] [InlineData("id")] [InlineData("component")] [InlineData("source")] [InlineData("maker")]
    [InlineData("period")] [InlineData("version")] [InlineData("caption")] [InlineData("reference")] [InlineData("rule")] [InlineData("scope")]
    public async Task Bound_scalar_identity_and_native_formatter_tampering_are_refused_before_dispatch(string fault)
    {
        using var rig=new Rig();rig.Source.Change=s=>s with{InventoryCostCancellationProof=fault switch{
            "missing"=>null,"company"=>s.InventoryCostCancellationProof! with{CompanyId=Guid.NewGuid()},"id"=>s.InventoryCostCancellationProof! with{InventoryCostProposalId=Guid.NewGuid()},
            "component"=>s.InventoryCostCancellationProof! with{ComponentCancellationId=Guid.Empty},"source"=>s.InventoryCostCancellationProof! with{SourceCancellationId=Guid.Empty},
            "maker"=>s.InventoryCostCancellationProof! with{PreparedBy=Actor},"period"=>s.InventoryCostCancellationProof! with{PeriodId=Guid.NewGuid()},_=>s.InventoryCostCancellationProof},
            SourceVersion=fault=="version"?"01":s.SourceVersion,Item=s.Item with{DocumentLabel=fault=="caption"?"other":s.Item.DocumentLabel,
                References=fault=="reference"?[]:s.Item.References,Rules=fault=="rule"?[s.Item.Rules[0] with{Actual="1"}]:s.Item.Rules,Scope=fault=="scope"?new(Id,null,null):s.Item.Scope}};
        Assert.Equal(503,(await Assert.ThrowsAsync<WorkDecisionException>(()=>rig.Decide())).Status);Assert.Equal(0,rig.Port.Calls);
    }
    [Theory]
    [InlineData("company")] [InlineData("proposal")] [InlineData("component")] [InlineData("source")] [InlineData("command")] [InlineData("key")]
    [InlineData("payload")] [InlineData("actor")] [InlineData("expected-N")] [InlineData("expected-M")] [InlineData("result-N")] [InlineData("result-M")]
    [InlineData("maker")] [InlineData("prepared-at")] [InlineData("recorded-at")] [InlineData("decided-at")] [InlineData("correlation")] [InlineData("facts")]
    [InlineData("digest")] [InlineData("missing")]
    public async Task All_nineteen_original_receipt_bindings_are_required_after_one_native_effect(string fault)
    {
        using var rig=new Rig();rig.Port.AfterAnswer=a=>{var v=a.Resource!;var r=v.CancellationApprovalReceipt!;return a with{Resource=v with{CancellationApprovalReceipt=fault switch{
            "company"=>r with{CompanyId=Guid.NewGuid()},"proposal"=>r with{InventoryCostProposalId=Guid.NewGuid()},"component"=>r with{ComponentCancellationId=Guid.NewGuid()},"source"=>r with{SourceCancellationId=Guid.NewGuid()},
            "command"=>r with{CommandName="other"},"key"=>r with{IdempotencyKeyHash=new('B',64)},"payload"=>r with{PayloadHash=new('B',64)},"actor"=>r with{ActorId=Maker},
            "expected-N"=>r with{ExpectedProposalVersion=8},"expected-M"=>r with{ExpectedCancellationVersion=2},"result-N"=>r with{ResultProposalVersion=8},"result-M"=>r with{ResultCancellationVersion=3},
            "maker"=>r with{PreparedBy=Actor},"prepared-at"=>r with{PreparedAtUtc=At.AddTicks(1)},"recorded-at"=>r with{PreparedRecordedAtUtc=At},"decided-at"=>r with{DecidedAtUtc=At},
            "correlation"=>r with{OriginalCorrelationId="other"},"facts"=>r with{SourceFactsDigest=new('B',64)},"digest"=>r with{OriginalResultDigest=new('B',64)},_=>null}}};};
        Assert.Equal(503,(await Assert.ThrowsAsync<WorkDecisionException>(()=>rig.Decide())).Status);Assert.Equal(1,rig.Port.Effects);Assert.Single(rig.Port.Commands);
    }
    [Fact]
    public async Task Canonical_source_digest_mismatch_is_post_native_unknown_then_original_key_reconciles_without_second_effect()
    {
        using var rig=new Rig();rig.Source.Change=s=>s with{InventoryCostCancellationProof=s.InventoryCostCancellationProof! with{SourceFactsDigest=new('B',64)}};
        Assert.Equal(503,(await Assert.ThrowsAsync<WorkDecisionException>(()=>rig.Preview())).Status);Assert.Equal(0,rig.Port.Effects);
        Assert.Equal(503,(await Assert.ThrowsAsync<WorkDecisionException>(()=>rig.Decide())).Status);Assert.Equal(1,rig.Port.Effects);
        rig.Source.Change=null;Assert.True((await rig.Decide()).Replayed);Assert.Equal(1,rig.Port.Effects);Assert.Single(rig.Port.Commands);
    }
    [Theory] [InlineData("maker")] [InlineData("permission")] [InlineData("company")] [InlineData("member")]
    public async Task Current_owner_maker_permission_and_company_membership_remain_required(string fault)
    {
        using var rig=new Rig();if(fault=="maker")rig.User.Actor=Maker;if(fault=="permission")rig.Authority.Allowed=false;if(fault=="company")rig.Company.Set(Guid.NewGuid());if(fault=="member")rig.User.Member=false;
        Assert.Equal(403,(await Assert.ThrowsAsync<WorkDecisionException>(()=>rig.Decide())).Status);Assert.Equal(0,rig.Port.Effects);Assert.Empty(rig.Port.Commands);
    }
    [Theory] [InlineData("permission")] [InlineData("delegation")] [InlineData("cancel")]
    public async Task Native_fence_revalidates_authority_and_delegation_before_the_synthetic_effect(string fault)
    {
        using var rig=new Rig();using var stop=new CancellationTokenSource();var request=rig.Request with{DelegationId=fault=="delegation"?Id:null};
        rig.Port.BeforeMutation=()=>{if(fault=="permission")rig.Authority.Allowed=false;if(fault=="delegation")rig.Delegations.Live=false;if(fault=="cancel")stop.Cancel();};
        if(fault=="cancel")await Assert.ThrowsAnyAsync<OperationCanceledException>(()=>rig.Decide(request,stop.Token));
        else Assert.Equal(403,(await Assert.ThrowsAsync<WorkDecisionException>(()=>rig.Decide(request))).Status);
        Assert.Equal(1,rig.Port.Calls);Assert.Equal(0,rig.Port.Effects);Assert.Empty(rig.Port.Commands);
    }
    [Theory] [InlineData("permission")] [InlineData("delegation")] [InlineData("cancel")]
    public async Task Post_effect_release_denial_is_not_a_rollback_and_same_key_reconciles_once(string fault)
    {
        using var rig=new Rig();using var stop=new CancellationTokenSource();var request=rig.Request with{DelegationId=fault=="delegation"?Id:null};
        rig.Port.AfterAnswer=a=>{if(fault=="permission")rig.Authority.Allowed=false;if(fault=="delegation")rig.Delegations.Live=false;if(fault=="cancel")stop.Cancel();return a;};
        if(fault=="cancel")await Assert.ThrowsAnyAsync<OperationCanceledException>(()=>rig.Decide(request,stop.Token));
        else Assert.Equal(403,(await Assert.ThrowsAsync<WorkDecisionException>(()=>rig.Decide(request))).Status);
        Assert.Equal(1,rig.Port.Effects);Assert.Single(rig.Port.Commands);
        rig.Port.AfterAnswer=null;rig.Authority.Allowed=true;rig.Delegations.Live=true;Assert.True((await rig.Decide(request)).Replayed);Assert.Equal(1,rig.Port.Effects);
    }
    [Theory] [InlineData("permission")] [InlineData("company")] [InlineData("actor")] [InlineData("cancel")]
    public async Task Human_GET_final_owner_read_cannot_release_after_normal_completed_context_change(string fault)
    {
        using var rig=new Rig();using var stop=new CancellationTokenSource();rig.Port.BeforeRead=count=>{if(count!=2)return;
            if(fault=="permission")rig.Authority.Allowed=false;if(fault=="company")rig.Company.Set(Guid.NewGuid());if(fault=="actor")rig.User.Actor=Maker;if(fault=="cancel")stop.Cancel();};
        if(fault=="cancel")await Assert.ThrowsAnyAsync<OperationCanceledException>(()=>rig.Preview(stop.Token));else Assert.Equal(403,(await Assert.ThrowsAsync<WorkDecisionException>(()=>rig.Preview())).Status);
        Assert.Equal(0,rig.Port.Effects);Assert.Empty(rig.Port.Commands);
    }
    [Fact]
    public async Task Missing_owner_and_source_Unauthorized_never_start_native_cancellation()
    {
        using(var missing=new Rig(inject:false)){Assert.Equal(503,(await Assert.ThrowsAsync<WorkDecisionException>(()=>missing.Preview())).Status);Assert.Equal(503,(await Assert.ThrowsAsync<WorkDecisionException>(()=>missing.Decide())).Status);Assert.Equal(0,missing.Port.Calls);}
        using var rig=new Rig();rig.Source.Unauthorized=true;Assert.Equal(403,(await Assert.ThrowsAsync<WorkDecisionException>(()=>rig.Decide())).Status);Assert.Equal(0,rig.Port.Calls);
    }
    [Fact]
    public async Task Valid_generic_sales_preview_rejects_cancellation_private_proof_on_the_wrong_kind()
    {
        using var rig=new Rig();var native=(await rig.Source.ReadDecisionAsync(new(Company,Actor.ToString("D"),Correlation,null),Id,default))!;
        var fixedSource=new SalesSource(native.InventoryCostCancellationProof!);
        Assert.Equal(503,(await Assert.ThrowsAsync<WorkDecisionException>(()=>rig.WithSource(fixedSource).PreviewAsync(fixedSource.Kind,Id,default))).Status);
        fixedSource.IncludeProof=false;Assert.Equal(fixedSource.Kind,(await rig.WithSource(fixedSource).PreviewAsync(fixedSource.Kind,Id,default)).Kind);
        Assert.Equal(0,rig.Port.Calls);
    }
    public static IEnumerable<object[]> ForeignCarriers => new object[][] {["ManufacturingPlanReapproval"],["InventoryTransferLoss"],["CounterpartyTaxIdentityChange"],["CounterpartyLegalNameChange"],["LogisticsPlanExcess"],["InventoryTransferLossProof"],["LogisticsPlanExcessProof"],["InventoryTransferOverageProof"],["LogisticsWeighingDifferenceProof"],["SupplierReturnCostExceptionProof"],["InventoryStockCountProof"],["BankAccountProof"],["InventoryStockScrapProof"],["LogisticsDispatchNoteProof"],["LogisticsCustomerReturnProof"],["BankAccountIbanChangeProof"],["ProductPackageStandardProof"],["ProductPriceListProof"],["PartyCreditLimitProof"],["OfficialInvoiceImportProof"],["OfficialSalesInvoiceProof"],["ManualBankPaymentReversalProof"],["ManualReceiptReversalProof"],["ExpensePaymentProof"],["BankCreditLimitProof"],["OpeningBalanceProof"],["CreditMovementProof"],["CashExchangeBookValueProof"],["NormalJournalDraftProof"],["AccountingPeriodOpeningProof"],["AccountingPeriodReopeningProof"],["JournalReversalProof"],["ApprovedSourceCatalogProof"],["PostingAccountMappingProof"],["ManufacturingCostDifferenceProof"],["InvoiceLineCogsProof"],["InventoryCostProof"] };
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
            _=>throw new ArgumentOutOfRangeException(nameof(name))};
        Assert.Equal(503,(await Assert.ThrowsAsync<WorkDecisionException>(()=>rig.Decide())).Status);Assert.Equal(0,rig.Port.Calls);
    }
    // Adversarial empty foreign carriers are intentionally not valid owner certificates.
    private static T Foreign<T>() where T:class=>JsonSerializer.Deserialize<T>("{}",Json)!;
    [Fact]
    public async Task Canonical_but_different_outer_N_cannot_substitute_the_version_selected_by_prepared_M()
    {
        using var rig=new Rig();Assert.Equal(409,(await Assert.ThrowsAsync<WorkDecisionException>(()=>rig.Decide(rig.Request with{InventoryCostCancellation=new("8")}))).Status);
        Assert.Equal("1",rig.Source.Selected);Assert.Equal(0,rig.Port.Calls);Assert.Empty(rig.Port.Commands);
    }
    internal static AccountingInventoryCostCancellationDecisionDetailV1 Facts(long outer=7,long cancellation=1)
    {
        var original=NewFacts(AccountingInventoryCostKindV1.SalesInvoicedShipment,"Posted",outer-1).Proposal;
        if(outer==1)original=original with{Status="AlreadyAccounted",Preparation=null,ApprovedBy=null,ApprovedAtUtc=null,JournalId=null};
        var before=new InventoryCostCancellationV1(Guid.NewGuid(),Company,Id,Guid.NewGuid(),cancellation,"Prepared",9007199254740993L,Guid.NewGuid(),new(2026,11,28),new(2026,11,27),"SENTETIK retained cancellation reason",Maker,At,
            [new(new('A',64),"Known",Guid.NewGuid(),Guid.NewGuid(),"SENTETIK-purpose",Guid.NewGuid(),"600","600",Guid.NewGuid(),Guid.NewGuid(),new('B',64))],[]);
        before=before with{MoneyProfiles=[new(before.Components[0].CurrencyId,Guid.NewGuid(),9007199254740993L,2)]};
        var approved=before with{Version=cancellation+1,Status="Approved",ApprovedBy=Actor,ApprovedAtUtc=At.AddMinutes(1),Components=before.Components.Select(x=>x with{ReversingJournalId=Guid.NewGuid()}).ToArray()};
        var source=new AccountingInventoryCostCancellationSourceV1(Company,before.CancellationId,AccountingSourceDocumentKindsV1.SalesInvoice,original.AdmissionId,original.InvoiceId,original.InvoiceVersion,Maker,At.AddDays(-1),"SENTETIK source cancellation reason",new('C',64),"SENTETIK PRIVATE SOURCE CORRELATION",At.AddDays(-1).AddTicks(1));
        var detail=new AccountingInventoryCostCancellationDecisionDetailV1(original,before,approved,At.AddTicks(17),source,"",""){Pending=false};
        return Digests(detail);
    }
    private static AccountingInventoryCostCancellationDecisionDetailV1 Digests(AccountingInventoryCostCancellationDecisionDetailV1 d)=>d with{SourceFactsDigest=AccountingInventoryCostServiceV1.CancellationSourceFactsDigest(d),PreviewDigest=AccountingInventoryCostServiceV1.CancellationPreviewDigest(d)};
    private sealed class Rig:IDisposable
    {
        public MutableCompany Company{get;}=new();public MutableUser User{get;}=new();public Authority Authority{get;}=new();public Delegations Delegations{get;}=new();
        public Protocol Port{get;}public SourceProxy Source{get;}public WorkDecisionHostV1 Host{get;}
        private readonly AccountingDbContext db;private readonly CorrelationContext correlation=new();private readonly WorkDecisionExecutionFenceV1 execution=new();private readonly WorkDecisionAuthorityV1 authority;
        private readonly AccountingInventoryCostServiceV1 owner;
        public Rig(long outer=7,long cancellation=1,bool inject=true)
        {
            Company.Set(InventoryCostWorkDecisionHostTests.Company);correlation.Set(Correlation,null,"Web",null,null);authority=new(Authority,execution);
            Port=new(Facts(outer,cancellation));owner=new(Port,Port,authority,Company,User,correlation);
            db=new(new DbContextOptionsBuilder<AccountingDbContext>().UseInMemoryDatabase("cancellation-host-"+Guid.NewGuid()).Options,Company);
            Source=new(new AccountingInventoryCostCancellationWorkInboxSourceV1(db,new(Company,User,authority),owner));
            Host=new(Company,User,correlation,authority,[Source],Delegations,execution,null!,null!,inventoryCosts:inject?owner:null);
        }
        public WorkDecisionRequestV1 Request=>new(Port.Original.PreparedCancellation.Version.ToString(CultureInfo.InvariantCulture),true,null){InventoryCostCancellation=new(Port.Original.OriginalProposal.Version.ToString(CultureInfo.InvariantCulture))};
        public Task<WorkDecisionPreviewV1> Preview(CancellationToken token=default)=>Host.PreviewAsync(Kind,Id,token);
        public Task<WorkDecisionResultV1> Decide(WorkDecisionRequestV1? request=null,CancellationToken token=default)=>Host.DecideAsync(Kind,Id,request??Request,Key,token);
        public WorkDecisionHostV1 WithSource(IWorkInboxSourceV1 source)=>new(Company,User,correlation,authority,[source],Delegations,execution,null!,null!,inventoryCosts:owner);
        public void Dispose()=>db.Dispose();
    }
    private sealed class Protocol(AccountingInventoryCostCancellationDecisionDetailV1 original):IAccountingInventoryCostStoreV1,IAccountingInventoryCostCancellationDecisionStoreV1,IAccountingCommandLog
    {
        public AccountingInventoryCostCancellationDecisionDetailV1 Original=original,Current=Digests(original with{Cancellation=original.PreparedCancellation,Pending=true});
        public int Reads,Effects,Calls;public ApproveInventoryCostCancellationV1? Expected;public string? Key;public Action<int>? BeforeRead;public Action? BeforeMutation;
        public Func<AccountingApiResult<AccountingInventoryCostProposalV1>,AccountingApiResult<AccountingInventoryCostProposalV1>>? AfterAnswer;
        public List<AccountingCommandRecord> Commands{get;}=[];
        public async Task<AccountingInventoryCostCancellationDecisionDetailV1?> ReadCancellationDecisionAsync(Guid id,long? version,Func<CancellationToken,Task> fence,CancellationToken token=default)
        {await fence(token);Reads++;BeforeRead?.Invoke(Reads);await fence(token);return id!=Id?null:version is null?Current:version==Original.PreparedCancellation.Version?(Effects==0?Digests(Original with{Cancellation=Original.PreparedCancellation,Pending=true}):Original):null;}
        public async Task<AccountingApiResult<AccountingInventoryCostProposalV1>> RunCancellationApprovalAsync(Func<bool,Task<AccountingApiResult<AccountingInventoryCostProposalV1>>> attempt,Func<CancellationToken,Task> fence,CancellationToken token=default)
        {try{await fence(token);var result=await attempt(false);await fence(token);return AfterAnswer?.Invoke(result)??result;}catch(NormalJournalAuthorityDeniedException){return new(AccountingApiStatus.Denied,null,false,AccountingApiCodes.AuthorityDenied);}}
        public async Task<AccountingInventoryCostProposalV1> ApproveCancellationAsync(Guid id,ApproveInventoryCostCancellationV1 request,Guid actor,Func<CancellationToken,Task> fence,AccountingCommandMetadata metadata,CancellationToken token=default)
        {
            Calls++;BeforeMutation?.Invoke();await fence(token);Expected=request;Key=metadata.IdempotencyKey;
            if(actor==Original.PreparedCancellation.PreparedBy)throw new AccountingStoreException("inventory_cost_approval_refused","SENTETIK");
            if(request.ExpectedVersion!=Original.OriginalProposal.Version)throw new AccountingVersionConflictException(request.ExpectedVersion,Original.OriginalProposal.Version);
            if(request.ExpectedCancellationVersion!=Original.PreparedCancellation.Version)throw new AccountingVersionConflictException(request.ExpectedCancellationVersion,Original.PreparedCancellation.Version);
            Effects++;Current=Original;var value=Original.OriginalProposal with{Status="CancelledByComponentReversal",CancellationId=Original.Cancellation.CancellationId,ComponentCancellation=Original.Cancellation};
            Commands.Add(metadata.Completion!(value));return value;
        }
        public Task<AccountingCommandRecord?> FindCommandAsync(string name,string key,CancellationToken token=default)=>Task.FromResult(Commands.SingleOrDefault(c=>c.CommandName==name&&c.IdempotencyKeyHash==key));
        public Task RecordCommandAsync(AccountingCommandRecord record,CancellationToken token=default)=>throw new NotSupportedException();
        public async Task RecordAttemptAsync(AccountingCommandRecord record,Func<CancellationToken,Task> fence,CancellationToken token=default){await fence(token);Commands.Add(record);}
        public Task<AccountingInventoryCostProposalV1> ApproveAsync(Guid id,AccountingInventoryCostKindV1 kind,long version,Guid actor,Func<CancellationToken,Task> fence,AccountingCommandMetadata metadata,CancellationToken token=default)=>throw new NotSupportedException();
        public Task AcceptAsync(WorkInboxEventV1 envelope,CancellationToken token=default)=>throw new InvalidOperationException("No source execution");
        public Task<IReadOnlyList<AccountingInventoryCostProposalV1>> ListAsync(AccountingInventoryCostKindV1 kind,CancellationToken token=default)=>throw new InvalidOperationException("No general list");
        public Task<InvoiceLineCogsOptionsV1> OptionsAsync(CancellationToken token=default)=>throw new InvalidOperationException("No options");
        public Task<AccountingInventoryCostProposalV1> PrepareAsync(Guid id,AccountingInventoryCostKindV1 kind,PrepareAccountingInventoryCostV1 request,Guid actor,Func<CancellationToken,Task> fence,AccountingCommandMetadata metadata,CancellationToken token=default)=>throw new InvalidOperationException("No prepare");
        public Task<AccountingInventoryCostProposalV1> ProposeReversalAsync(Guid id,ProposeAccountingInventoryCostReversalV1 request,Guid actor,Func<CancellationToken,Task> fence,AccountingCommandMetadata metadata,CancellationToken token=default)=>throw new InvalidOperationException("No reversal");

    }
    private sealed class SourceProxy(IWorkApprovalDecisionVersionSourceV1 inner):IWorkApprovalDecisionVersionSourceV1
    {
        public int Reads;public string? Selected;public bool Unauthorized;public Func<WorkApprovalDecisionSnapshotV1,WorkApprovalDecisionSnapshotV1>? Change;
        public string Module=>inner.Module;public string Kind=>inner.Kind;public string ApproverPermission=>inner.ApproverPermission;
        public Task<WorkInboxSourceResultV1> ListPendingAsync(WorkInboxSourceContextV1 c,int max,CancellationToken t)=>inner.ListPendingAsync(c,max,t);
        public async Task<WorkApprovalDecisionSnapshotV1?> ReadDecisionAsync(WorkInboxSourceContextV1 c,Guid id,CancellationToken t)
        {Reads++;if(Unauthorized)throw new UnauthorizedAccessException();var s=await inner.ReadDecisionAsync(c,id,t);return s is null?null:Change?.Invoke(s)??s;}
        public async Task<WorkApprovalDecisionSnapshotV1?> ReadDecisionVersionAsync(WorkInboxSourceContextV1 c,Guid id,string version,CancellationToken t)
        {Reads++;Selected=version;if(Unauthorized)throw new UnauthorizedAccessException();var s=await inner.ReadDecisionVersionAsync(c,id,version,t);return s is null?null:Change?.Invoke(s)??s;}
    }
    private sealed class SalesSource(WorkInventoryCostCancellationProofV1 proof):IWorkApprovalDecisionSourceV1
    {
        public bool IncludeProof=true;public string Module=>"sales";public string Kind=>WorkDecisionHostV1.SalesOrder;public string ApproverPermission=>ErpPermissions.SalesOrdersApprove;
        public Task<WorkInboxSourceResultV1> ListPendingAsync(WorkInboxSourceContextV1 c,int max,CancellationToken t)=>throw new NotSupportedException();
        public Task<WorkApprovalDecisionSnapshotV1?> ReadDecisionAsync(WorkInboxSourceContextV1 c,Guid id,CancellationToken t)=>Task.FromResult<WorkApprovalDecisionSnapshotV1?>(new(Company,new(Module,Kind,id,"SENTETIK generic sales",At,"/satis/siparisler",Maker.ToString("D"),[],[]),"1",true){InventoryCostCancellationProof=IncludeProof?proof:null});
    }
    private sealed class MutableCompany:ICompanyContext{public Guid CompanyId{get;private set;}public bool IsResolved=>CompanyId!=Guid.Empty;public void Set(Guid id)=>CompanyId=id;}
    private sealed class MutableUser:ICurrentUser{public Guid Actor=InventoryCostWorkDecisionHostTests.Actor;public bool Member=true;public bool IsAuthenticated=>true;public string UserId=>Actor.ToString("D");public IReadOnlySet<Guid> AllowedCompanyIds{get;}=new HashSet<Guid>{Company};public bool CanAccessCompany(Guid id)=>Member&&id==Company;}
    private sealed class Authority:IErpAuthorityRevalidationService{public bool Allowed=true;public List<string> Permissions{get;}=[];public Task<PermissionScope> RevalidatePermissionScopeAsync(string permission,Guid company,CancellationToken token=default){Permissions.Add(permission);return Task.FromResult(new PermissionScope(Allowed,true,new HashSet<string>(),new HashSet<Guid>(),new HashSet<Guid>(),new HashSet<Guid>()));}}
    private sealed class Delegations:IWorkDelegationReaderV1{public bool Live=true;public Task<WorkDelegationViewV1?> FindForActorAsync(Guid id,WorkInboxItemV1 item,CancellationToken token=default)=>Task.FromResult<WorkDelegationViewV1?>(Live?new(id,Company,item.Kind,AccountingJournalReversalPermissionNamesV1.Approve,Maker,Actor,At.AddDays(-2),At.AddYears(1),"1",Maker,At,"SENTETIK",null,null,null):null);}
}
