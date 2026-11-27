using System.Text.Json;
using GoymenERP.Api.WorkRuntime;
using GoymenERP.BuildingBlocks.Multitenancy;
using GoymenERP.BuildingBlocks.Observability;
using GoymenERP.BuildingBlocks.Security;
using GoymenERP.BuildingBlocks.Work;
using GoymenERP.Modules.Finance.Contracts;
using GoymenERP.Modules.Finance.Infrastructure.Shell;
using GoymenERP.Modules.Work.Infrastructure.Delegations;

namespace GoymenERP.Api.IntegrationTests;

// Synthetic receipt/authority boundary. Actual native SQL and rollback are covered by separate source facts.
public sealed class WorkBankAccountDecisionHostTests
{
    private static readonly Guid Company=Guid.NewGuid(),Id=Guid.NewGuid(),Currency=Guid.NewGuid();
    private static readonly DateTimeOffset At=new(2026,11,27,0,0,0,TimeSpan.Zero);
    private const string Actor="SENTETIK-checker",Maker="SENTETIK-proposer";
    private static BankAccountView Value()=>new(Id,Company,Currency,"SENTETIK safe display",true,true,"2");
    private static BankAccountApprovalReceiptV1 Receipt()=>new(Company,Id,"finance.bank-account.approve",Actor,At.AddHours(1));
    [Theory]
    [InlineData(false)] [InlineData(true)]
    public async Task Approve_and_old_key_replay_use_native_immutable_command_not_the_current_bank_revision(bool replay)
    {
        var run=new Rig();run.Source.Pending=!replay;run.Source.Version=replay?"4":"1";
        run.Owner.Answer=new(CommandOutcome.Succeeded,Value(),replay,replay?"SENTETIK-original":"SENTETIK-work",[],Receipt());
        var preview=await run.Host.PreviewAsync(run.Source.Kind,Id,default);Assert.False(preview.ApprovalReasonRequired);Assert.False(preview.RejectionSupported);
        Assert.Equal((Maker,At),(preview.BankAccount!.ProposedBy,preview.BankAccount.ProposedAtUtc));Assert.Equal(run.Source.Version,preview.BankAccount.Account.Version);
        var result=await run.Host.DecideAsync(run.Source.Kind,Id,new("1",true,null),"original-key",default);
        Assert.Equal(("Approved",replay),(result.State,result.Replayed));Assert.Equal(new BankAccountApproveRequest(Id,"1"),run.Owner.Captured);
        Assert.Equal("original-key",run.Owner.Key);Assert.Equal(replay?"SENTETIK-original":"SENTETIK-work",result.OriginalCorrelationId);
    }
    [Theory]
    [InlineData("proof")] [InlineData("company")] [InlineData("id")] [InlineData("currency")] [InlineData("name")] [InlineData("active")] [InlineData("version")]
    [InlineData("receipt-company")] [InlineData("receipt-id")] [InlineData("command")] [InlineData("actor")] [InlineData("time")] [InlineData("correlation")]
    public async Task Malformed_or_unproven_success_is_unavailable(string fault)
    {
        var run=new Rig();var value=Value();var proof=Receipt();
        value=fault switch{"company"=>value with{CompanyId=Guid.NewGuid()},"id"=>value with{Id=Guid.NewGuid()},"currency"=>value with{CurrencyId=Guid.NewGuid()},"name"=>value with{DisplayName="different"},"active"=>value with{IsActive=false},"version"=>value with{Version="4"},_=>value};
        proof=fault switch{"receipt-company"=>proof with{CompanyId=Guid.NewGuid()},"receipt-id"=>proof with{AccountId=Guid.NewGuid()},"command"=>proof with{CommandName="finance.bank-account.iban-change.approve"},"actor"=>proof with{ActorId=Maker},"time"=>proof with{RecordedAtUtc=At.ToOffset(TimeSpan.FromHours(3))},_=>proof};
        run.Owner.Answer=new(CommandOutcome.Succeeded,value,false,fault=="correlation"?"wrong":"SENTETIK-work",[],fault=="proof"?null:proof);
        Assert.Equal(503,(await Assert.ThrowsAsync<WorkDecisionException>(()=>run.Host.DecideAsync(run.Source.Kind,Id,new("1",true,null),"key",default))).Status);
    }
    [Theory]
    [InlineData(false,null,"1")] [InlineData(true,"note","1")] [InlineData(true,null,"01")] [InlineData(true,null,"0")] [InlineData(true,null,"9223372036854775807")]
    public async Task Unsupported_reject_reason_or_version_never_calls_native_owner(bool approve,string? reason,string version)
    {var run=new Rig();Assert.Equal(400,(await Assert.ThrowsAsync<WorkDecisionException>(()=>run.Host.DecideAsync(run.Source.Kind,Id,new(version,approve,reason),"key",default))).Status);Assert.Null(run.Owner.Captured);}
    [Fact]
    public async Task Wrong_kind_payload_maker_and_revoked_delegation_never_call_native_owner()
    {
        var maker=new Rig(Maker);Assert.Equal(403,(await Assert.ThrowsAsync<WorkDecisionException>(()=>maker.Host.PreviewAsync(maker.Source.Kind,Id,default))).Status);
        var run=new Rig();Assert.Equal(400,(await Assert.ThrowsAsync<WorkDecisionException>(()=>run.Host.DecideAsync(run.Source.Kind,Id,new("1",true,null,SupplierReturnCostException:new("3")),"key",default))).Status);
        run.Delegations.Live=false;Assert.Equal(403,(await Assert.ThrowsAsync<WorkDecisionException>(()=>run.Host.DecideAsync(run.Source.Kind,Id,new("1",true,null,Guid.NewGuid()),"key",default))).Status);Assert.Null(run.Owner.Captured);
        run.Source.Kind=WorkDecisionHostV1.OpeningCost;Assert.Equal(503,(await Assert.ThrowsAsync<WorkDecisionException>(()=>run.Host.PreviewAsync(run.Source.Kind,Id,default))).Status);
    }
    [Fact]
    public async Task Native_recorded_refusal_preserves_its_outcome_and_original_correlation_without_success_projection()
    {
        var run=new Rig();run.Owner.Answer=new(CommandOutcome.VersionConflict,null,true,"SENTETIK-terminal",[new("expectedVersion","version_conflict")]);
        var result=await Assert.ThrowsAsync<WorkDecisionException>(()=>run.Host.DecideAsync(run.Source.Kind,Id,new("1",true,null),"key",default));
        Assert.Equal((409,true,"SENTETIK-terminal"),(result.Status,result.Replayed,result.OriginalCorrelationId));
    }
    [Fact]
    public async Task Human_safe_projection_and_staged_capability_never_disclose_bank_card_or_change_legacy_wire()
    {
        var run=new Rig();var options=new JsonSerializerOptions(JsonSerializerDefaults.Web);
        var wire=JsonSerializer.Serialize(await run.Host.PreviewAsync(run.Source.Kind,Id,default),options);
        foreach(var name in new[]{"iban","accountNumber","bankName","accountType","bankAccountProof","approvalReceipt"})Assert.DoesNotContain(name,wire);
        Assert.DoesNotContain("bankAccountProof",JsonSerializer.Serialize(await run.Source.ReadDecisionAsync(new(Company,Actor,"SENTETIK",null),Id,default),options));
        using var request=JsonDocument.Parse(JsonSerializer.Serialize(new WorkDecisionRequestV1("1",true,null),options));Assert.Equal(4,request.RootElement.EnumerateObject().Count());
        Assert.DoesNotContain("bankAccount",JsonSerializer.Serialize(new WorkDecisionPreviewV1(Company,WorkDecisionHostV1.OpeningCost,Id,"SENTETIK","1",true,true,[],[]),options));
        var cap=Assert.Single(new FinanceShellCapabilityProvider().GetDeclarations(),c=>c.CapabilityId==FinanceShellCapabilityProvider.BankAccountWorkDecisionCapabilityId);
        Assert.False(cap.ImplementationAvailable);Assert.Null(cap.ParentCapabilityId);Assert.Equal([ErpPermissions.FinanceBankAccountApprove],cap.RequiredPermissions);
    }
    private sealed class Rig
    {
        public readonly Source Source=new();public readonly Delegations Delegations=new();public readonly Owner Owner;public readonly WorkDecisionHostV1 Host;
        public Rig(string actor=Actor){var company=new CompanyContext();company.Set(Company);var correlation=new CorrelationContext();correlation.Set("SENTETIK-work",null,"Web",null,null);var execution=new WorkDecisionExecutionFenceV1();
            var authority=new WorkDecisionAuthorityV1(new WorkInventoryTransferLossDecisionHostTests.LossAuthority(),execution);Owner=new();
            Host=new(company,new WorkInventoryTransferLossDecisionHostTests.LossUser(Company,actor),correlation,authority,[Source],Delegations,execution,null!,null!,bankAccounts:Owner);}
    }
    private sealed class Source:IWorkApprovalDecisionSourceV1
    {
        public bool Pending=true;public string Version="1";public string Module=>"finance";public string Kind{get;set;}=WorkDecisionHostV1.BankAccount;public string ApproverPermission=>ErpPermissions.FinanceBankAccountApprove;
        public Task<WorkApprovalDecisionSnapshotV1?> ReadDecisionAsync(WorkInboxSourceContextV1 c,Guid id,CancellationToken t)=>Task.FromResult<WorkApprovalDecisionSnapshotV1?>(new(Company,new(Module,Kind,Id,"SENTETIK",At,"/finans/banka-hesaplari",Maker,[],[new("bank-account","SENTETIK",Id,null),new("currency","SENTETIK",Currency,null)]),Version,Pending,BankAccountProof:new(Company,Id,Currency,"SENTETIK safe display",!Pending,false,Maker,At)));
        public Task<WorkInboxSourceResultV1> ListPendingAsync(WorkInboxSourceContextV1 c,int max,CancellationToken t)=>throw new NotSupportedException();
    }
    private sealed class Owner:IFinanceBankAccountMaster
    {
        public BankAccountApproveRequest? Captured;public string? Key;public BankAccountCommandResult Answer=new(CommandOutcome.Succeeded,Value(),false,"SENTETIK-work",[],Receipt());
        public Task<BankAccountCommandResult> ApproveAsync(FinanceContext c,CommandMetadata m,BankAccountApproveRequest r,CancellationToken t){Captured=r;Key=m.IdempotencyKey;return Task.FromResult(Answer);}
        public Task<BankAccountCommandResult> ProposeAsync(FinanceContext c,CommandMetadata m,BankAccountProposeRequest r,CancellationToken t)=>throw new NotSupportedException();
        public Task<BankAccountCommandResult> ProposeIbanChangeAsync(FinanceContext c,CommandMetadata m,BankAccountIbanChangeProposeRequest r,CancellationToken t)=>throw new NotSupportedException();
        public Task<BankAccountCommandResult> ApproveIbanChangeAsync(FinanceContext c,CommandMetadata m,BankAccountIbanChangeApproveRequest r,CancellationToken t)=>throw new NotSupportedException();
        public Task<ReadResult<BankAccountMasterPage>> ListMasterAsync(FinanceContext c,CancellationToken t)=>throw new NotSupportedException();
        public Task<ReadResult<BankAccountCurrencyCandidates>> CurrencyCandidatesAsync(FinanceContext c,CancellationToken t)=>throw new NotSupportedException();
    }
    private sealed class Delegations:IWorkDelegationReaderV1
    {public bool Live=true;public Task<WorkDelegationViewV1?> FindForActorAsync(Guid id,WorkInboxItemV1 item,CancellationToken t=default)=>Task.FromResult<WorkDelegationViewV1?>(Live?new(id,Company,item.Kind,ErpPermissions.FinanceBankAccountApprove,Guid.NewGuid(),Guid.NewGuid(),At,DateTimeOffset.UtcNow.AddDays(1),"1",Guid.NewGuid(),At,"SENTETIK",null,null,null):null);}
}
