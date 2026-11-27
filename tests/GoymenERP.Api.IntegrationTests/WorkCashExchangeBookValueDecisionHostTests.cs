using System.Text.Json;
using GoymenERP.Api.WorkRuntime;
using GoymenERP.BuildingBlocks.Multitenancy;
using GoymenERP.BuildingBlocks.Observability;
using GoymenERP.BuildingBlocks.Security;
using GoymenERP.BuildingBlocks.Work;
using GoymenERP.Modules.Finance.Contracts;
using GoymenERP.Modules.Finance.Infrastructure;
using GoymenERP.Modules.Finance.Infrastructure.Persistence;
using GoymenERP.Modules.Finance.Infrastructure.WorkInbox;
using GoymenERP.Modules.Idempotency.Domain.Executions;
using GoymenERP.Modules.Settings.Application.Contracts.V1;
using GoymenERP.Modules.Work.Infrastructure.Delegations;
using Microsoft.EntityFrameworkCore;

namespace GoymenERP.Api.IntegrationTests;

// Controlled owner ports prove Work orchestration only; native SQL/HTTP acceptance is separate.
public sealed class WorkCashExchangeBookValueDecisionHostTests
{
    private static readonly Guid Company = Guid.Parse("ce460000-0000-4000-8000-000000000001"), Id = Guid.Parse("ce460000-0000-4000-8000-000000000002");
    private static readonly DateTimeOffset At = new(2026, 11, 27, 9, 0, 0, TimeSpan.Zero);
    private const string Actor = "SENTETIK-checker", Maker = "SENTETIK-maker", Key = "cash-book-work-key", Correlation = "SENTETIK-work-correlation";
    private static CashExchangeBookValueDecisionDetailV1 Human(bool legacy = false)
    {
        // Existing native calculator/typed transfer contract; no duplicated exchange or book-value arithmetic.
        var usd = SettingsCurrencyIdentitiesV1.UnitedStatesDollar; var tl = SettingsCurrencyIdentitiesV1.TurkishLira;
        var source = new MoneyText("100", usd); var target = new MoneyText("3300", tl); var profile = new ProfileReference(Guid.NewGuid().ToString("D"), "1");
        var date = new DateOnly(2026, 11, 27);
        var exchange = CashTransferExchangeCalculatorV1.Calculate(Company, date, source, target, profile, profile,
            new(usd, "34", date, "Tcmb", Guid.NewGuid(), "1", date, "SENTETIK-reference"),
            new(tl, "1", date, "FunctionalCurrency", null, null, null, null));
        var transfer = new CashTransferView(Guid.NewGuid(), Company, new(CashTransferKind.CurrencyExchange, CashTransferAccountKind.Bank,
            Guid.NewGuid(), source, profile, CashTransferAccountKind.Cash, Guid.NewGuid(), target, profile, exchange.TransactionRate,
            date, null, "SENTETIK transfer reference", "SENTETIK transfer description"), "SENTETIK-treasurer", At.AddDays(-1), true, "1", exchange, new("SENTETIK-CEX-1", "1"));
        var proposal = new CashExchangeBookValueViewV1(Id, Company, transfer.Id, "1", CashExchangeBookValueServiceV1.Hash(JsonSerializer.Serialize(transfer, FinanceJson.CreateOptions())),
            "3500.00", new(Guid.NewGuid(), 1, new string('b', 64)), "SENTETIK recorded proposal reason", Maker, At,
            CashExchangeBookValueStatusV1.Pending, "1", null, null, null, legacy ? null : "3300.00");
        return Proof(new(transfer, proposal));
    }
    private static CashExchangeBookValueDecisionDetailV1 Proof(CashExchangeBookValueDecisionDetailV1 detail)
    {
        var p = detail.Proposal;
        return detail with { DecisionProof = new(Company, Id, p.TransferId, p.PreparedBy, p.PreparedAtUtc, p.Status.ToString(), p.Version,
            p.TransferSourceHash, CashExchangeBookValueServiceV1.ProposalFactsDigest(p), CashExchangeBookValueServiceV1.DecisionPreviewDigest(detail)) };
    }
    private static CashExchangeBookValueCommandResultV1 Answer(CashExchangeBookValueDecisionDetailV1 detail, bool approve, string? reason, bool replay)
    {
        var p = detail.Proposal with { Version = "2", Status = approve ? CashExchangeBookValueStatusV1.Approved : CashExchangeBookValueStatusV1.Rejected,
            DecidedBy = Actor, DecidedAtUtc = At.AddMinutes(1), DecisionReason = reason };
        var original = replay ? "SENTETIK-original-correlation" : Correlation;
        var result = new CashExchangeBookValueCommandResultV1(CommandOutcome.Succeeded, p, replay, original, null);
        return result with { DecisionReceipt = new(Company, Id, p.TransferId, CashExchangeBookValueServiceV1.DecideCommand,
            IdempotencyHash.ComputeKey(Key), CashExchangeBookValueServiceV1.DecidePayloadHash(new(Id, "1", approve, reason)), Actor, "1", "2", original,
            p.DecidedAtUtc.Value, CashExchangeBookValueServiceV1.ProposalFactsDigest(p), CashExchangeBookValueServiceV1.OriginalReceiptDigest(result))
            { TransferSource = detail.Transfer } };
    }

    [Theory] [InlineData(true)] [InlineData(false)]
    public async Task Native_decision_uses_original_four_fields_and_one_command_without_a_second_human_POST_read(bool approve)
    {
        using var run = new Rig(approve); var preview = await run.Host.PreviewAsync(WorkDecisionHostV1.CashExchangeBookValue, Id, default);
        Assert.Equal(run.Owner.Header, preview.CashExchangeBookValue); Assert.True(preview.RejectionSupported); Assert.True(preview.ApprovalSupported);
        Assert.False(preview.ApprovalReasonRequired); var reads = run.Owner.Reads; run.Owner.RejectHumanRead = true;
        var result = await run.Decide(); Assert.Equal(approve ? "Approved" : "Rejected", result.State); Assert.Equal(reads + 1, run.Owner.Reads);
        Assert.Equal(new DecideCashExchangeBookValueV1(Id, "1", approve, run.Reason), run.Owner.Request); Assert.Equal(Key, run.Owner.Key);
        Assert.Equal(1, run.Owner.Decisions); Assert.Equal(0, run.Owner.GeneralReads); Assert.Equal(0, run.Owner.Proposals);
    }
    [Theory] [InlineData(true)] [InlineData(false)]
    public async Task Terminal_version_two_still_replays_the_original_key_version_one_and_correlation(bool approve)
    {
        using var run = new Rig(approve, replay: true); run.Owner.RejectHumanRead = true;
        var result = await run.Decide(); Assert.True(result.Replayed); Assert.Equal(approve ? "Approved" : "Rejected", result.State);
        Assert.Equal("SENTETIK-original-correlation", result.OriginalCorrelationId); Assert.Equal("1", run.Owner.Request!.ExpectedVersion); Assert.Equal(1, run.Owner.Reads);
    }
    [Fact]
    public async Task Legacy_target_null_is_visible_and_rejectable_but_approval_keeps_the_native_refusal()
    {
        using var reject = new Rig(false, legacy: true); var preview = await reject.Host.PreviewAsync(WorkDecisionHostV1.CashExchangeBookValue, Id, default);
        Assert.False(preview.ApprovalSupported); Assert.True(preview.RejectionSupported); Assert.Null(preview.CashExchangeBookValue!.Proposal.TargetFunctionalValueTl);
        Assert.Equal("Rejected", (await reject.Decide()).State);
        using var approve = new Rig(legacy: true); approve.Owner.Result = new(CommandOutcome.ValidationFailed, null, false, Correlation, "cash_exchange_target_functional_value_required");
        var failure = await Assert.ThrowsAsync<WorkDecisionException>(() => approve.Decide()); Assert.Equal(422, failure.Status); Assert.Equal("cash_exchange_target_functional_value_required", failure.Code);
    }
    [Theory]
    [InlineData("missing")] [InlineData("company")] [InlineData("proposal")] [InlineData("transfer")] [InlineData("command")] [InlineData("key")] [InlineData("hash")]
    [InlineData("actor")] [InlineData("expected")] [InlineData("version")] [InlineData("correlation")] [InlineData("time")] [InlineData("facts")] [InlineData("original-digest")]
    [InlineData("carrier-null")] [InlineData("carrier-tampered")] [InlineData("current-substitute")] [InlineData("fresh-terminal")] [InlineData("source-amount")] [InlineData("evidence")]
    public async Task Missing_forged_or_current_reconstructed_receipt_never_becomes_Work_success(string fault)
    {
        using var run = new Rig(replay: true); var result = run.Owner.Result; var p = result.DecisionReceipt!;
        p = fault switch { "company" => p with { CompanyId = Guid.NewGuid() }, "proposal" => p with { ProposalId = Guid.NewGuid() }, "transfer" => p with { TransferId = Guid.NewGuid() },
            "command" => p with { CommandName = CashExchangeBookValueServiceV1.ProposeCommand }, "key" => p with { IdempotencyKeyHash = "wrong" }, "hash" => p with { PayloadHash = "wrong" },
            "actor" => p with { ActorId = Maker }, "expected" => p with { ExpectedVersion = "2" }, "version" => p with { ResultVersion = "3" },
            "correlation" => p with { OriginalCorrelationId = "wrong" }, "time" => p with { RecordedAtUtc = At }, "facts" => p with { ProposalFactsDigest = new string('c', 64) },
            "original-digest" => p with { OriginalReceiptDigest = new string('c', 64) }, "carrier-null" => p with { TransferSource = null },
            "carrier-tampered" => p with { TransferSource = p.TransferSource! with { DocumentNumber = new("SENTETIK other document", "1") } }, _ => p };
        run.Owner.Result = result with { DecisionReceipt = fault == "missing" ? null : p, IsReplay = fault != "fresh-terminal",
            Resource = fault switch { "current-substitute" => result.Resource! with { Status = CashExchangeBookValueStatusV1.Pending, Version = "1", DecidedBy = null, DecidedAtUtc = null },
                "source-amount" => result.Resource! with { SourceBookValueTl = "999" }, "evidence" => result.Resource! with { Evidence = result.Resource.Evidence with { DocumentId = Guid.NewGuid() } }, _ => result.Resource } };
        Assert.Equal(503, (await Assert.ThrowsAsync<WorkDecisionException>(() => run.Decide())).Status); Assert.Equal(0, run.Owner.GeneralReads);
    }
    [Theory] [InlineData("company")] [InlineData("proposal")] [InlineData("transfer")] [InlineData("version")] [InlineData("amount")] [InlineData("reason")] [InlineData("proof")]
    public async Task Human_GET_requires_identical_full_native_facts_and_private_proof_across_both_reads(string fault)
    {
        using var run = new Rig(); run.Owner.HumanChange = h => fault switch { "company" => h with { Proposal = h.Proposal with { CompanyId = Guid.NewGuid() } },
            "proposal" => h with { Proposal = h.Proposal with { Id = Guid.NewGuid() } }, "transfer" => h with { Transfer = h.Transfer with { RecordedBy = "other" } },
            "version" => h with { Proposal = h.Proposal with { Version = "2" } }, "amount" => h with { Proposal = h.Proposal with { TargetFunctionalValueTl = "999" } },
            "reason" => h with { Proposal = h.Proposal with { Reason = "other" } }, _ => h with { DecisionProof = null } };
        Assert.Equal(503, (await Assert.ThrowsAsync<WorkDecisionException>(() => run.Host.PreviewAsync(WorkDecisionHostV1.CashExchangeBookValue, Id, default))).Status); Assert.Equal(0, run.Owner.Decisions);
    }
    [Theory] [InlineData(ReadOutcome.Denied,403)] [InlineData(ReadOutcome.Unavailable,503)] [InlineData(ReadOutcome.NotFound,503)]
    public async Task Human_owner_read_refusal_does_not_fall_back_to_the_native_workspace(ReadOutcome outcome,int status)
    {
        using var run=new Rig();run.Owner.HumanOutcome=outcome;
        Assert.Equal(status,(await Assert.ThrowsAsync<WorkDecisionException>(()=>run.Host.PreviewAsync(WorkDecisionHostV1.CashExchangeBookValue,Id,default))).Status);
        Assert.Equal(0,run.Owner.GeneralReads);Assert.Equal(0,run.Owner.Decisions);
    }
    [Theory] [InlineData("missing")] [InlineData("company")] [InlineData("facts")] [InlineData("preview")] [InlineData("cross-kind")]
    [InlineData("reference")] [InlineData("caption")] [InlineData("threshold")] [InlineData("unit")] [InlineData("actual")]
    public async Task Scalar_rules_caption_and_references_are_bound_to_the_native_formatter_on_POST(string fault)
    {
        using var run = new Rig(); run.Source.Change = s => { var p = s.CashExchangeBookValueProof!; return s with {
            CashExchangeBookValueProof = fault switch { "missing" => null, "company" => p with { CompanyId = Guid.NewGuid() }, "facts" => p with { ProposalFactsDigest = "wrong" }, "preview" => p with { PreviewDigest = "wrong" }, _ => p },
            ExpensePaymentProof = fault == "cross-kind" ? new(Company, Id, Maker, At, "Draft", new string('a',64), new string('b',64)) : null,
            Item = s.Item with { DocumentLabel = fault == "caption" ? "SENTETIK forged caption" : s.Item.DocumentLabel,
                References = fault == "reference" ? s.Item.References.Select(x => x.Role == "evidence-document" ? x with { RecordId = Guid.NewGuid() } : x).ToArray() : s.Item.References,
                Rules = fault is "threshold" or "unit" or "actual" ? [s.Item.Rules[0] with { Threshold = fault == "threshold" ? "1" : null, Unit = fault == "unit" ? "TL" : null, Actual = fault == "actual" ? "999" : s.Item.Rules[0].Actual }, s.Item.Rules[1]] : s.Item.Rules } }; };
        Assert.Equal(503, (await Assert.ThrowsAsync<WorkDecisionException>(() => run.Decide())).Status); Assert.Equal(0, run.Owner.Proposals);
    }
    [Theory] [InlineData(true,"note","1")] [InlineData(false,null,"1")] [InlineData(false," ret ","1")] [InlineData(false,"ret\nline","1")] [InlineData(false,"ret\u0085line","1")]
    [InlineData(true,null,"0")] [InlineData(true,null,"01")] [InlineData(true,null,"1\n")] [InlineData(true,null,"2")]
    public async Task Native_request_rejects_wrong_version_note_padding_and_embedded_controls(bool approve, string? reason, string version)
    {
        using var run = new Rig(); var request = new WorkDecisionRequestV1(version, approve, reason);
        Assert.Equal(400, (await Assert.ThrowsAsync<WorkDecisionException>(() => run.Host.DecideAsync(WorkDecisionHostV1.CashExchangeBookValue, Id, request, Key, default))).Status); Assert.Equal(0, run.Owner.Decisions);
    }
    [Fact]
    public async Task Reject_preserves_exact_1000_characters_but_not_1001_or_another_kind_payload()
    {
        using var valid = new Rig(false, reason: new string('x',1000)); Assert.Equal("Rejected", (await valid.Decide()).State);
        using var invalid = new Rig(false, reason: new string('x',1001)); Assert.Equal(400, (await Assert.ThrowsAsync<WorkDecisionException>(() => invalid.Decide())).Status);
        using var typed = new Rig(); Assert.Equal(400, (await Assert.ThrowsAsync<WorkDecisionException>(() => typed.Host.DecideAsync(WorkDecisionHostV1.CashExchangeBookValue, Id,
            new("1",true,null, ProductPackageStandard:new("2026-11-27")), Key, default))).Status); Assert.Equal(0, typed.Owner.Decisions);
    }
    [Theory] [InlineData(CommandOutcome.Denied,403)] [InlineData(CommandOutcome.Unavailable,503)] [InlineData(CommandOutcome.VersionConflict,409)] [InlineData(CommandOutcome.IdempotencyConflict,409)] [InlineData(CommandOutcome.ValidationFailed,422)]
    public async Task Native_refusals_keep_status_code_and_original_correlation(CommandOutcome outcome, int status)
    {
        using var run = new Rig(); run.Owner.Result = new(outcome,null,true,"SENTETIK-original","SENTETIK-code");
        var error = await Assert.ThrowsAsync<WorkDecisionException>(()=>run.Decide()); Assert.Equal(status,error.Status); Assert.True(error.Replayed); Assert.Equal("SENTETIK-original",error.OriginalCorrelationId); Assert.Equal("SENTETIK-code",error.Code);
    }
    [Theory] [InlineData("permission")] [InlineData("company")] [InlineData("actor")]
    public async Task Fresh_authority_after_native_return_suppresses_release(string fault)
    {
        using var run = new Rig(); run.Owner.BeforeReturn = ()=> { if(fault=="permission")run.Authority.Allowed=false; else if(fault=="company")run.Company.Set(Guid.NewGuid()); else run.User.Actor=Maker; };
        Assert.Equal(403,(await Assert.ThrowsAsync<WorkDecisionException>(()=>run.Decide())).Status); Assert.Equal(1,run.Owner.Decisions);
    }
    [Fact]
    public async Task Maker_companywide_and_current_delegation_fences_are_preserved()
    {
        using var maker = new Rig(actor:Maker); Assert.Equal(403,(await Assert.ThrowsAsync<WorkDecisionException>(()=>maker.Decide())).Status); Assert.Equal(0,maker.Owner.Decisions);
        using var run = new Rig(replay:true); run.Authority.CompanyWide=false; Assert.Equal(403,(await Assert.ThrowsAsync<WorkDecisionException>(()=>run.Decide())).Status);
        run.Authority.CompanyWide=true; run.Delegations.Live=false; Assert.Equal(403,(await Assert.ThrowsAsync<WorkDecisionException>(()=>run.Decide(Guid.NewGuid()))).Status); Assert.Equal(0,run.Owner.Decisions);
        run.Delegations.Live=true; run.Owner.BeforeReturn=()=>run.Delegations.Live=false; Assert.Equal(403,(await Assert.ThrowsAsync<WorkDecisionException>(()=>run.Decide(Guid.NewGuid()))).Status);
    }
    [Fact]
    public async Task Another_owner_kind_cannot_carry_the_cash_book_private_proof()
    {
        using var run = new Rig(); var snapshot = await run.Source.ReadDecisionAsync(new(Company,Actor,Correlation,null),Id,default); Assert.NotNull(snapshot);
        var source = new OtherKindSource(snapshot with { Item = snapshot.Item with { Kind = "finance.cash-account.v1" } });
        var authority = new WorkInventoryTransferLossDecisionHostTests.LossAuthority();
        var user = new WorkInventoryTransferLossDecisionHostTests.LossUser(Company,Actor); var correlation = new CorrelationContext(); correlation.Set(Correlation,null,"Web",null,null);
        var fence = new WorkDecisionExecutionFenceV1();
        var host = new WorkDecisionHostV1(run.Company,user,correlation,new WorkDecisionAuthorityV1(authority,fence),[source],run.Delegations,fence,null!,null!);
        Assert.Equal(503,(await Assert.ThrowsAsync<WorkDecisionException>(()=>host.PreviewAsync(source.Kind,Id,default))).Status); Assert.Equal(0,run.Owner.Decisions);
    }
    [Fact]
    public async Task Last_human_callback_cancellation_cannot_release_a_preview_and_missing_owner_fails_closed()
    {
        using var run = new Rig(); using var cancellation = new CancellationTokenSource(); run.Owner.AfterHumanFence=()=>cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(()=>run.Host.PreviewAsync(WorkDecisionHostV1.CashExchangeBookValue,Id,cancellation.Token));
        using var absent = new Rig(inject:false); Assert.Equal(503,(await Assert.ThrowsAsync<WorkDecisionException>(()=>absent.Decide())).Status); Assert.Equal(0,absent.Owner.Decisions);
    }
    [Fact]
    public async Task Full_facts_are_authorized_GET_only_private_proofs_and_original_public_wires_do_not_expand()
    {
        using var run = new Rig(); var json = new JsonSerializerOptions(JsonSerializerDefaults.Web);
        var snapshot=await run.Source.ReadDecisionAsync(new(Company,Actor,Correlation,null),Id,default); var wire=JsonSerializer.Serialize(snapshot,json);
        Assert.DoesNotContain("cashExchangeBookValueProof",wire); Assert.DoesNotContain("SENTETIK recorded proposal reason",wire); Assert.DoesNotContain("SENTETIK transfer description",wire);
        var human=JsonSerializer.Serialize(await run.Host.PreviewAsync(WorkDecisionHostV1.CashExchangeBookValue,Id,default),json);
        Assert.Contains("SENTETIK recorded proposal reason",human); Assert.Contains("SENTETIK transfer description",human); Assert.DoesNotContain("decisionProof",human);
        using var native=JsonDocument.Parse(JsonSerializer.Serialize(run.Owner.Result,FinanceJson.CreateOptions())); Assert.Equal(5,native.RootElement.EnumerateObject().Count());
        using var command=JsonDocument.Parse(JsonSerializer.Serialize(new WorkDecisionRequestV1("1",true,null),json)); Assert.Equal(4,command.RootElement.EnumerateObject().Count());
        using var result=JsonDocument.Parse(JsonSerializer.Serialize(await run.Decide(),json)); Assert.Equal(6,result.RootElement.EnumerateObject().Count());
    }

    private sealed class Rig : IDisposable
    {
        public Owner Owner {get;} public SourceProxy Source {get;} public Authority Authority {get;}=new(); public Delegations Delegations {get;}=new();
        public WorkDecisionHostV1 Host {get;} public MutableCompany Company {get;}=new(); public MutableUser User {get;}=new(); public string? Reason {get;}
        private readonly FinanceDbContext db; private readonly bool approve;
        public Rig(bool approve=true,bool replay=false,bool legacy=false,string actor=Actor,string? reason=null,bool inject=true)
        {
            this.approve=approve; Reason=approve?null:reason??"SENTETIK literal ret"; Owner=new(){Header=Human(legacy)}; Owner.Result=Answer(Owner.Header,approve,Reason,replay);
            if(replay)Owner.Header=Proof(Owner.Header with {Proposal=Owner.Result.Resource!});
            Company.Set(WorkCashExchangeBookValueDecisionHostTests.Company); User.Actor=actor;
            db=new(new DbContextOptionsBuilder<FinanceDbContext>().UseInMemoryDatabase("book-work-"+Guid.NewGuid()).Options,Company);
            Source=new(new FinanceCashExchangeBookValueWorkInboxSourceV1(db,new(Company,User,Authority),Owner));
            var correlation=new CorrelationContext(); correlation.Set(Correlation,null,"Web",null,null); var fence=new WorkDecisionExecutionFenceV1();
            Host=new(Company,User,correlation,new WorkDecisionAuthorityV1(Authority,fence),[Source],Delegations,fence,null!,null!,cashExchangeBookValues:inject?Owner:null);
            Owner.CommitFence=ct=>fence.VerifyAsync(ct);
        }
        public Task<WorkDecisionResultV1> Decide(Guid? delegation=null)=>Host.DecideAsync(WorkDecisionHostV1.CashExchangeBookValue,Id,new("1",approve,Reason,delegation),Key,default);
        public void Dispose()=>db.Dispose();
    }
    private sealed class SourceProxy(IWorkApprovalDecisionSourceV1 inner):IWorkApprovalDecisionSourceV1
    {
        public Func<WorkApprovalDecisionSnapshotV1,WorkApprovalDecisionSnapshotV1>? Change;
        public string Module=>inner.Module;public string Kind=>inner.Kind;public string ApproverPermission=>inner.ApproverPermission;
        public Task<WorkInboxSourceResultV1> ListPendingAsync(WorkInboxSourceContextV1 c,int n,CancellationToken t)=>inner.ListPendingAsync(c,n,t);
        public async Task<WorkApprovalDecisionSnapshotV1?> ReadDecisionAsync(WorkInboxSourceContextV1 c,Guid id,CancellationToken t)
        {var answer=await inner.ReadDecisionAsync(c,id,t);return answer is null||Change is null?answer:Change(answer);}
    }
    private sealed class OtherKindSource(WorkApprovalDecisionSnapshotV1 snapshot):IWorkApprovalDecisionSourceV1
    {
        public string Module=>"finance"; public string Kind=>"finance.cash-account.v1"; public string ApproverPermission=>ErpPermissions.FinanceCashAccountApprove;
        public Task<WorkInboxSourceResultV1> ListPendingAsync(WorkInboxSourceContextV1 c,int n,CancellationToken t)=>throw new NotSupportedException();
        public Task<WorkApprovalDecisionSnapshotV1?> ReadDecisionAsync(WorkInboxSourceContextV1 c,Guid id,CancellationToken t)=>Task.FromResult<WorkApprovalDecisionSnapshotV1?>(snapshot);
    }
    private sealed class Owner:ICashExchangeBookValueServiceV1,ICashExchangeBookValueDecisionReadV1
    {
        public CashExchangeBookValueDecisionDetailV1 Header=null!;public CashExchangeBookValueCommandResultV1 Result=null!;
        public int Reads,Decisions,GeneralReads,Proposals;public bool RejectHumanRead;public DecideCashExchangeBookValueV1? Request;public string? Key;
        public Func<CashExchangeBookValueDecisionDetailV1,CashExchangeBookValueDecisionDetailV1>? HumanChange;public Action? BeforeReturn,AfterHumanFence;public ReadOutcome? HumanOutcome;
        public Func<CancellationToken,Task<bool>>? CommitFence;
        public async Task<ReadResult<CashExchangeBookValueDecisionDetailV1>> ReadDecisionAsync(FinanceContext c,Guid id,Func<CancellationToken,Task> fence,CancellationToken t)
        {await fence(t);Reads++;if(Reads%2==0&&RejectHumanRead)throw new InvalidOperationException("No second human POST read");var h=Reads>1&&HumanChange is not null?HumanChange(Header):Header;await fence(t);if(Reads>1){AfterHumanFence?.Invoke();if(HumanOutcome is {} outcome)return new(outcome,null);}return new(ReadOutcome.Found,h);}
        public async Task<CashExchangeBookValueCommandResultV1> DecideAsync(FinanceContext c,CommandMetadata m,DecideCashExchangeBookValueV1 r,CancellationToken t)
        {Decisions++;Request=r;Key=m.IdempotencyKey;BeforeReturn?.Invoke();if(CommitFence is not null&&!await CommitFence(t))return new(CommandOutcome.Denied,null,false,Correlation,null);return Result;}
        public Task<ReadResult<CashExchangeBookValueCandidatePageV1>> ListAsync(FinanceContext c,Guid? id,CancellationToken t){GeneralReads++;throw new NotSupportedException();}
        public Task<ReadResult<CashExchangeBookValueWorkspaceV1>> GetAsync(FinanceContext c,Guid id,CancellationToken t){GeneralReads++;throw new NotSupportedException();}
        public Task<CashExchangeBookValueCommandResultV1> ProposeAsync(FinanceContext c,CommandMetadata m,ProposeCashExchangeBookValueV1 r,CancellationToken t){Proposals++;throw new NotSupportedException();}
    }
    private sealed class MutableCompany:ICompanyContext
    {public bool IsResolved=>CompanyId!=Guid.Empty;public Guid CompanyId{get;private set;}public void Set(Guid id)=>CompanyId=id;}
    private sealed class MutableUser:ICurrentUser
    {public string Actor=WorkCashExchangeBookValueDecisionHostTests.Actor;public bool IsAuthenticated=>true;public string UserId=>Actor;public IReadOnlySet<Guid> AllowedCompanyIds{get;}=new HashSet<Guid>{Company};public bool CanAccessCompany(Guid id)=>AllowedCompanyIds.Contains(id);}
    private sealed class Authority:IErpAuthorityRevalidationService
    {public bool Allowed=true,CompanyWide=true;public Task<PermissionScope> RevalidatePermissionScopeAsync(string permission,Guid company,CancellationToken t=default)
        {Assert.Equal(CashExchangeBookValuePermissionsV1.Approve,permission);return Task.FromResult(new PermissionScope(Allowed,CompanyWide,new HashSet<string>(),new HashSet<Guid>(),new HashSet<Guid>(),new HashSet<Guid>()));}}
    private sealed class Delegations:IWorkDelegationReaderV1
    {public bool Live=true;public Task<WorkDelegationViewV1?> FindForActorAsync(Guid id,WorkInboxItemV1 item,CancellationToken t=default)=>Task.FromResult<WorkDelegationViewV1?>(Live
        ?new(id,Company,item.Kind,CashExchangeBookValuePermissionsV1.Approve,Guid.NewGuid(),Guid.NewGuid(),At,DateTimeOffset.UtcNow.AddDays(1),"1",Guid.NewGuid(),At,"SENTETIK",null,null,null):null);}
}
