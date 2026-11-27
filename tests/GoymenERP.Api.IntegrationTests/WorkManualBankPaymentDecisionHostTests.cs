using GoymenERP.Api.WorkRuntime;
using GoymenERP.BuildingBlocks.Multitenancy;
using GoymenERP.BuildingBlocks.Observability;
using GoymenERP.BuildingBlocks.Security;
using GoymenERP.BuildingBlocks.Work;
using GoymenERP.Modules.Finance.Contracts;
using GoymenERP.Modules.Work.Infrastructure.Delegations;

namespace GoymenERP.Api.IntegrationTests;

// Synthetic owner/authority composition, not SQL or HTTP acceptance.
public sealed class WorkManualBankPaymentDecisionHostTests
{
    private static readonly Guid Company=Guid.NewGuid(),Id=Guid.NewGuid(),Party=Guid.NewGuid(),Account=Guid.NewGuid(),Bank=Guid.NewGuid(),Currency=Guid.NewGuid();
    private static readonly DateTimeOffset At=new(2026,11,27,0,0,0,TimeSpan.Zero);
    private const string Actor="SENTETIK-checker",Maker="SENTETIK-maker",Reviser="SENTETIK-reviser";
    private static ManualBankPaymentView Value()=>new(Id,Company,ManualBankPaymentStatus.Approved,
        new(Party,Account,Bank,new("125.00",Currency),new("SENTETIK","1"),new(2026,11,27),null,null,null),
        null,Maker,At,Actor,At.AddHours(1),null,null,false,"2");

    [Theory]
    [InlineData(false)] [InlineData(true)]
    public async Task Approval_and_original_key_replay_only_call_native_approval_not_record_or_draft(bool replay)
    {
        var run=new Rig();run.Source.Pending=!replay;run.Source.Version=replay?"3":"1";
        run.Owner.Answer=new(CommandOutcome.Succeeded,Value(),replay,replay?"SENTETIK-original":"SENTETIK-work",[]);
        var preview=await run.Host.PreviewAsync(run.Source.Kind,Id,default);
        Assert.False(preview.ApprovalReasonRequired);Assert.False(preview.RejectionSupported);
        var result=await run.Host.DecideAsync(run.Source.Kind,Id,new("1",true,null),"original-key",default);
        Assert.Equal(("Approved",replay),(result.State,result.Replayed));Assert.Equal(new ApproveManualBankPaymentRequest(Id,"1"),run.Owner.Captured);
        Assert.Equal("original-key",run.Owner.Key);Assert.Equal(replay?"SENTETIK-original":"SENTETIK-work",result.OriginalCorrelationId);
    }
    [Theory]
    [InlineData("company")] [InlineData("id")] [InlineData("status")] [InlineData("actor")] [InlineData("time")]
    [InlineData("maker")] [InlineData("prepared-time")] [InlineData("recorded")] [InlineData("confirmed")] [InlineData("version")]
    [InlineData("party")] [InlineData("bank")] [InlineData("currency")] [InlineData("amount")] [InlineData("correlation")]
    public async Task Unproven_or_recorded_result_never_becomes_a_work_approval_success(string fault)
    {
        var run=new Rig();var v=Value();v=fault switch
        {
            "company"=>v with{CompanyId=Guid.NewGuid()},"id"=>v with{Id=Guid.NewGuid()},"status"=>v with{Status=ManualBankPaymentStatus.Recorded},
            "actor"=>v with{ApprovedBy=Maker},"time"=>v with{ApprovedAtUtc=At.AddHours(-1)},"maker"=>v with{PreparedBy=Actor},
            "prepared-time"=>v with{PreparedAtUtc=At.AddDays(-1)},"recorded"=>v with{RecordedBy=Actor},"confirmed"=>v with{BankResultConfirmed=true},
            "version"=>v with{Version="3"},"party"=>v with{Preparation=v.Preparation with{PartyId=Guid.NewGuid()}},
            "bank"=>v with{Preparation=v.Preparation with{TreasuryAccountId=Guid.NewGuid()}},
            "currency"=>v with{Preparation=v.Preparation with{Money=v.Preparation.Money with{CurrencyId=Guid.NewGuid()}}},
            "amount"=>v with{Preparation=v.Preparation with{Money=v.Preparation.Money with{Amount="126"}}},_=>v,
        };
        run.Owner.Answer=new(CommandOutcome.Succeeded,v,false,fault=="correlation"?"wrong":"SENTETIK-work",[]);
        Assert.Equal(503,(await Assert.ThrowsAsync<WorkDecisionException>(()=>run.Host.DecideAsync(run.Source.Kind,Id,new("1",true,null),"key",default))).Status);
    }
    [Theory]
    [InlineData(false,null,"1")] [InlineData(true,"note","1")] [InlineData(true,null,"01")]
    [InlineData(true,null,"0")] [InlineData(true,null,"9223372036854775807")]
    public async Task Unsupported_reject_reason_or_version_does_not_call_any_owner(bool approve,string? reason,string version)
    {
        var run=new Rig();Assert.Equal(400,(await Assert.ThrowsAsync<WorkDecisionException>(()=>run.Host.DecideAsync(run.Source.Kind,Id,new(version,approve,reason),"key",default))).Status);Assert.Null(run.Owner.Captured);
    }
    [Theory]
    [InlineData(Maker)] [InlineData(Reviser)]
    public async Task Original_maker_and_successful_draft_reviser_cannot_approve(string actor)
    {
        var run=new Rig(actor);Assert.Equal(403,(await Assert.ThrowsAsync<WorkDecisionException>(()=>run.Host.PreviewAsync(run.Source.Kind,Id,default))).Status);Assert.Null(run.Owner.Captured);
    }
    [Fact]
    public async Task Revoked_delegation_extra_payload_or_wrong_source_authority_never_calls_owner()
    {
        var run=new Rig();run.Delegations.Live=false;
        Assert.Equal(403,(await Assert.ThrowsAsync<WorkDecisionException>(()=>run.Host.DecideAsync(run.Source.Kind,Id,new("1",true,null,Guid.NewGuid()),"key",default))).Status);
        Assert.Equal(400,(await Assert.ThrowsAsync<WorkDecisionException>(()=>run.Host.DecideAsync(run.Source.Kind,Id,new("1",true,null,SupplierReturnCostException:new()),"key",default))).Status);
        run.Source.Permission=FinancePermissionNames.ManualBankPaymentRecord;
        Assert.Equal(503,(await Assert.ThrowsAsync<WorkDecisionException>(()=>run.Host.PreviewAsync(run.Source.Kind,Id,default))).Status);Assert.Null(run.Owner.Captured);
    }
    [Fact]
    public async Task Native_terminal_refusal_preserves_original_correlation_and_replay()
    {
        var run=new Rig();run.Owner.Answer=new(CommandOutcome.VersionConflict,null,true,"SENTETIK-terminal",[new("expectedVersion","version_conflict")]);
        var result=await Assert.ThrowsAsync<WorkDecisionException>(()=>run.Host.DecideAsync(run.Source.Kind,Id,new("1",true,null),"key",default));
        Assert.Equal((409,true,"SENTETIK-terminal"),(result.Status,result.Replayed,result.OriginalCorrelationId));
    }
    private sealed class Rig
    {
        public readonly Source Source=new();public readonly Delegations Delegations=new();public readonly Owner Owner=new();public readonly WorkDecisionHostV1 Host;
        public Rig(string actor=Actor)
        {
            var company=new CompanyContext();company.Set(Company);var correlation=new CorrelationContext();correlation.Set("SENTETIK-work",null,"Web",null,null);
            var fence=new WorkDecisionExecutionFenceV1();var authority=new WorkDecisionAuthorityV1(new WorkInventoryTransferLossDecisionHostTests.LossAuthority(),fence);
            Host=new(company,new WorkInventoryTransferLossDecisionHostTests.LossUser(Company,actor),correlation,authority,[Source],Delegations,fence,null!,null!,manualBankPayments:Owner);
        }
    }
    private sealed class Source:IWorkApprovalDecisionSourceV1
    {
        public bool Pending=true;public string Version="1";public string Permission=FinancePermissionNames.ManualBankPaymentApprove;
        public string Module=>"finance";public string Kind=>WorkDecisionHostV1.ManualBankPayment;public string ApproverPermission=>Permission;
        public Task<WorkApprovalDecisionSnapshotV1?> ReadDecisionAsync(WorkInboxSourceContextV1 c,Guid id,CancellationToken t)=>Task.FromResult<WorkApprovalDecisionSnapshotV1?>(new(Company,
            new(Module,Kind,Id,"SENTETIK",At,"/onaylarim/finance.manual-bank-payment.v1/"+Id,Maker,
                [new("finance.manual-bank-payment-approval-required",null,"125",null,null)],
                [new("party","SENTETIK",Party,null),new("company-account","SENTETIK",Account,null),new("bank-account","SENTETIK",Bank,null),new("currency","SENTETIK",Currency,null)],[Reviser]),Version,Pending));
        public Task<WorkInboxSourceResultV1> ListPendingAsync(WorkInboxSourceContextV1 c,int max,CancellationToken t)=>throw new NotSupportedException();
    }
    private sealed class Owner:IManualBankPaymentService
    {
        public ApproveManualBankPaymentRequest? Captured;public string? Key;public ManualBankPaymentCommandResult Answer=new(CommandOutcome.Succeeded,Value(),false,"SENTETIK-work",[]);
        public Task<ManualBankPaymentCommandResult> ApproveAsync(FinanceContext c,CommandMetadata m,ApproveManualBankPaymentRequest r,CancellationToken t){Captured=r;Key=m.IdempotencyKey;return Task.FromResult(Answer);}
        public Task<ManualBankPaymentCommandResult> RecordAsync(FinanceContext c,CommandMetadata m,RecordManualBankPaymentRequest r,CancellationToken t)=>throw new NotSupportedException("Work cannot record a payment.");
        public Task<ManualBankPaymentCommandResult> DraftSaveAsync(FinanceContext c,CommandMetadata m,ManualBankPaymentDraftSaveRequest r,CancellationToken t)=>throw new NotSupportedException();
        public Task<ReadResult<ManualBankPaymentPage>> ListAuthorizedAsync(FinanceContext c,string? cursor,CancellationToken t)=>throw new NotSupportedException();
        public Task<ReadResult<ManualBankPaymentView>> DetailAuthorizedAsync(FinanceContext c,Guid id,CancellationToken t)=>throw new NotSupportedException();
    }
    private sealed class Delegations:IWorkDelegationReaderV1
    {
        public bool Live=true;
        public Task<WorkDelegationViewV1?> FindForActorAsync(Guid id,WorkInboxItemV1 item,CancellationToken t=default)=>Task.FromResult<WorkDelegationViewV1?>(Live?new(id,Company,item.Kind,FinancePermissionNames.ManualBankPaymentApprove,Guid.NewGuid(),Guid.NewGuid(),At,DateTimeOffset.UtcNow.AddDays(1),"1",Guid.NewGuid(),At,"SENTETIK",null,null,null):null);
    }
}
