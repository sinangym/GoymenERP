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
using GoymenERP.Modules.Work.Infrastructure.Delegations;
using Microsoft.EntityFrameworkCore;

namespace GoymenERP.Api.IntegrationTests;

// Controlled owner replies verify only the Work consumer boundary; native transactions use separate SQL facts.
public sealed class WorkManualBankPaymentReversalDecisionHostTests
{
    private static readonly Guid Company = Guid.Parse("ac000000-0000-4000-8000-000000000001"), Id = Guid.Parse("ac000000-0000-4000-8000-000000000002"), Payment = Guid.Parse("ac000000-0000-4000-8000-000000000003");
    private static readonly DateTimeOffset At = new(2026, 11, 27, 9, 0, 0, TimeSpan.Zero);
    private const string Actor = "SENTETIK-checker", Maker = "SENTETIK-maker", Key = "payment-reversal-work-key", Correlation = "SENTETIK-work-correlation", Reason = "SENTETIK ret";
    private static PaymentReversalProposalView Proposal() => new(Id, Payment, new(2026, 11, 27), "SENTETIK original correction", ReversalProposalStatus.Pending, Maker, At, null, null, null, null, "1");
    private static PaymentReversalDecisionPreviewV1 Human() => new(Company, Proposal(), "SENTETIK-payment-number", Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(),
        "1250.50", new(2026, 11, 26), null, Guid.NewGuid(), "SENTETIK-payment-recorder", At.AddDays(-1), new string('a', 64), "SENTETIK-bank-label");
    private static PaymentReversalProposalCommandResult Answer(PaymentReversalDecisionPreviewV1 human, bool approve, bool replay, string reason = Reason)
    {
        var proposal = human.Proposal with { Status = approve ? ReversalProposalStatus.Approved : ReversalProposalStatus.Rejected, Version = "2", DecidedBy = Actor,
            DecidedAtUtc = At.AddHours(1), DecisionReason = approve ? null : reason, ReversalEventId = approve ? Guid.NewGuid() : null };
        var original = replay ? "SENTETIK-original-correlation" : Correlation;
        return new(CommandOutcome.Succeeded, proposal, replay, original, [])
        { DecisionReceipt = new(Company, Id, Payment, approve ? PaymentReversalProposalService.ApproveCommand : PaymentReversalProposalService.RejectCommand,
            IdempotencyHash.ComputeKey(Key), approve ? PaymentReversalProposalService.ApprovePayloadHash(new(Id, "1")) : PaymentReversalProposalService.RejectPayloadHash(new(Id, "1", reason)),
            Actor, "1", "2", original, At.AddHours(1), human.RecordedFactsDigest) };
    }

    [Theory]
    [InlineData(true, false)] [InlineData(false, false)] [InlineData(true, true)] [InlineData(false, true)]
    public async Task Native_decision_and_terminal_original_key_replay_keep_exact_command_without_a_second_human_read(bool approve, bool replay)
    {
        using var run = new Rig(approve, replay);
        var preview = await run.Host.PreviewAsync(WorkDecisionHostV1.ManualBankPaymentReversal, Id, default);
        Assert.Equal(run.Owner.Header, preview.ManualBankPaymentReversal); Assert.False(preview.ApprovalReasonRequired); Assert.True(preview.RejectionSupported);
        var reads = run.Owner.Reads;
        var result = await run.Host.DecideAsync(WorkDecisionHostV1.ManualBankPaymentReversal, Id, new("1", approve, approve ? null : Reason), Key, default);
        Assert.Equal((approve ? "Approved" : "Rejected", replay, run.Owner.Result.OriginalCorrelationId), (result.State, result.Replayed, result.OriginalCorrelationId));
        Assert.Equal(reads + 1, run.Owner.Reads); // Source read only: POST never performs the extra human GET.
        Assert.Equal((Id, "1", Key, approve ? null : Reason, 1), (run.Owner.LastId, run.Owner.Version, run.Owner.Key, run.Owner.Reason, run.Owner.Commands));
    }

    [Theory]
    [InlineData(true)] [InlineData(false)]
    public async Task Native_timestamp_identity_does_not_add_a_new_decision_after_proposal_chronology_policy(bool approve)
    {
        using var run = new Rig(approve); var result = run.Owner.Result;
        run.Owner.Result = result with { Resource = result.Resource! with { DecidedAtUtc = At.AddDays(-1) },
            DecisionReceipt = result.DecisionReceipt! with { RecordedAtUtc = At.AddDays(-1) } };
        Assert.Equal(approve ? "Approved" : "Rejected", (await run.Decide(approve, approve ? null : Reason)).State);
    }

    [Theory]
    [InlineData("missing")] [InlineData("company")] [InlineData("proposal")] [InlineData("payment")] [InlineData("command")]
    [InlineData("key")] [InlineData("hash")] [InlineData("actor")] [InlineData("expected")] [InlineData("version")]
    [InlineData("correlation")] [InlineData("time")] [InlineData("facts")] [InlineData("counter-event")] [InlineData("fresh-terminal")]
    public async Task Missing_wrong_or_current_substitute_receipt_never_becomes_Work_success(string fault)
    {
        using var run = new Rig(replay: fault == "fresh-terminal"); var r = run.Owner.Result; var receipt = r.DecisionReceipt!;
        receipt = fault switch { "company" => receipt with { CompanyId = Guid.NewGuid() }, "proposal" => receipt with { ProposalId = Guid.NewGuid() },
            "payment" => receipt with { PaymentId = Guid.NewGuid() }, "command" => receipt with { CommandName = PaymentReversalProposalService.ProposeCommand },
            "key" => receipt with { IdempotencyKeyHash = "wrong" }, "hash" => receipt with { PayloadHash = "wrong" }, "actor" => receipt with { ActorId = Maker },
            "expected" => receipt with { ExpectedVersion = "2" }, "version" => receipt with { ResultVersion = "3" }, "correlation" => receipt with { OriginalCorrelationId = "other" },
            "time" => receipt with { RecordedAtUtc = At }, "facts" => receipt with { RecordedFactsDigest = new string('b', 64) }, _ => receipt };
        run.Owner.Result = r with { DecisionReceipt = fault == "missing" ? null : receipt, Resource = fault == "counter-event" ? r.Resource! with { ReversalEventId = null } : r.Resource };
        if (fault == "fresh-terminal") run.Owner.Result = run.Owner.Result with { IsReplay = false };
        Assert.Equal(503, (await Assert.ThrowsAsync<WorkDecisionException>(() => run.Decide())).Status);
    }

    [Theory]
    [InlineData("company")] [InlineData("amount")] [InlineData("account")] [InlineData("proposal")] [InlineData("status")] [InlineData("digest")] [InlineData("bank-label")]
    public async Task Human_GET_requires_the_same_native_full_preview_and_owner_formatted_item(string fault)
    {
        using var run = new Rig(); run.Owner.HumanChange = h => fault switch { "company" => h with { CompanyId = Guid.NewGuid() }, "amount" => h with { Amount = "999" },
            "account" => h with { TreasuryAccountId = Guid.NewGuid() }, "proposal" => h with { Proposal = h.Proposal with { Id = Guid.NewGuid() } },
            "status" => h with { Proposal = h.Proposal with { Status = ReversalProposalStatus.Approved } }, "bank-label" => h with { TreasuryAccountLabel = "SENTETIK-other-bank-label" }, _ => h with { RecordedFactsDigest = new string('b', 64) } };
        Assert.Equal(503, (await Assert.ThrowsAsync<WorkDecisionException>(() => run.Host.PreviewAsync(WorkDecisionHostV1.ManualBankPaymentReversal, Id, default))).Status); Assert.Equal(0, run.Owner.Commands);
    }

    [Fact]
    public async Task Changed_current_recorded_facts_cannot_replace_an_original_terminal_receipt()
    {
        using var run = new Rig(replay: true); run.Owner.Header = run.Owner.Header with { Amount = "999", RecordedFactsDigest = new string('b', 64) };
        Assert.Equal(503, (await Assert.ThrowsAsync<WorkDecisionException>(() => run.Decide())).Status);
    }

    [Theory]
    [InlineData("actual")] [InlineData("threshold")] [InlineData("deviation")] [InlineData("unit")] [InlineData("caption")] [InlineData("reference")]
    public async Task Human_GET_does_not_accept_inconsistent_native_rule_reference_or_caption(string fault)
    {
        using var run = new Rig(); run.Source.Change = s =>
        {
            var rule = s.Item.Rules.Single(); rule = fault switch { "actual" => rule with { Actual = "999" }, "threshold" => rule with { Threshold = "1" },
                "deviation" => rule with { Deviation = "1" }, "unit" => rule with { Unit = "kg" }, _ => rule };
            return s with { Item = s.Item with { Rules = [rule], DocumentLabel = fault == "caption" ? "wrong" : s.Item.DocumentLabel,
                References = fault == "reference" ? s.Item.References.Select(x => x.Role == "party" ? x with { RecordId = Guid.NewGuid() } : x).ToArray() : s.Item.References } };
        };
        Assert.Equal(503, (await Assert.ThrowsAsync<WorkDecisionException>(() => run.Host.PreviewAsync(WorkDecisionHostV1.ManualBankPaymentReversal, Id, default))).Status);
    }

    [Theory]
    [InlineData(true, "note", "1")] [InlineData(false, null, "1")] [InlineData(false, " padded ", "1")]
    [InlineData(false, "", "1")] [InlineData(true, null, "2")] [InlineData(true, null, "01")]
    public async Task Request_preserves_native_approve_null_and_reject_canonical_1000_reason(bool approve, string? reason, string version)
    {
        using var run = new Rig(); Assert.Equal(400, (await Assert.ThrowsAsync<WorkDecisionException>(() => run.Host.DecideAsync(WorkDecisionHostV1.ManualBankPaymentReversal, Id, new(version, approve, reason), Key, default))).Status); Assert.Equal(0, run.Owner.Commands);
    }

    [Theory]
    [InlineData("loss")] [InlineData("overage")] [InlineData("manufacturing")] [InlineData("package")]
    public async Task Other_native_kinds_selection_payload_never_reaches_reversal_command(string extra)
    {
        using var run = new Rig(); var request = new WorkDecisionRequestV1("1", true, null);
        request = extra switch { "loss" => request with { InventoryTransferLoss = new(1, At) },
            "overage" => request with { InventoryTransferOverage = new(1, At) }, "package" => request with { ProductPackageStandard = new("2026-11-27") },
            _ => request with { ManufacturingPlanReapproval = new([Guid.NewGuid()]) } };
        Assert.Equal(400, (await Assert.ThrowsAsync<WorkDecisionException>(() => run.Host.DecideAsync(WorkDecisionHostV1.ManualBankPaymentReversal, Id, request, Key, default))).Status);
        Assert.Equal(0, run.Owner.Commands);
    }

    [Fact]
    public async Task Native_rejection_1000_is_not_cut_to_generic_500()
    {
        using var run = new Rig(approve: false); var reason = new string('x', 1000); run.Owner.Result = Answer(run.Owner.Header, false, false, reason);
        Assert.Equal("Rejected", (await run.Decide(false, reason)).State); Assert.Equal(reason, run.Owner.Reason);
        Assert.Equal(400, (await Assert.ThrowsAsync<WorkDecisionException>(() => run.Decide(false, reason + "x"))).Status); Assert.Equal(1, run.Owner.Commands);
    }

    [Theory]
    [InlineData("missing")] [InlineData("foreign")] [InlineData("facts")] [InlineData("preview")] [InlineData("maker")] [InlineData("cross-kind")]
    public async Task Private_scalar_proof_is_required_and_other_kind_proof_is_rejected(string fault)
    {
        using var run = new Rig(); run.Source.Change = s => { var p = s.ManualBankPaymentReversalProof!;
            return s with { ManualBankPaymentReversalProof = fault switch { "missing" => null, "foreign" => p with { CompanyId = Guid.NewGuid() },
                "facts" => p with { RecordedFactsDigest = "wrong" }, "preview" => p with { PreviewDigest = "wrong" }, "maker" => p with { ProposedBy = Actor }, _ => p },
                ManualReceiptReversalProof = fault == "cross-kind" ? new(Company, Id, Payment, Maker, At, "Pending", new string('a', 64), new string('b', 64)) : null }; };
        Assert.Equal(503, (await Assert.ThrowsAsync<WorkDecisionException>(() => run.Decide())).Status); Assert.Equal(0, run.Owner.Commands);
    }

    [Theory]
    [InlineData(CommandOutcome.Denied, 403)] [InlineData(CommandOutcome.Unavailable, 503)] [InlineData(CommandOutcome.VersionConflict, 409)] [InlineData(CommandOutcome.IdempotencyConflict, 409)] [InlineData(CommandOutcome.ValidationFailed, 422)]
    public async Task Native_refusal_retains_its_original_correlation(CommandOutcome outcome, int status)
    {
        using var run = new Rig(); run.Owner.Result = new(outcome, null, true, "SENTETIK-refusal-correlation", [new("request", "SENTETIK-code")]);
        var error = await Assert.ThrowsAsync<WorkDecisionException>(() => run.Decide()); Assert.Equal(status, error.Status); Assert.True(error.Replayed); Assert.Equal("SENTETIK-refusal-correlation", error.OriginalCorrelationId); Assert.Equal("SENTETIK-code", error.Code);
    }

    [Fact]
    public async Task Maker_foreign_scope_lost_permission_and_fresh_native_delegation_fence_remain_closed()
    {
        using var maker = new Rig(actor: Maker); Assert.Equal(403, (await Assert.ThrowsAsync<WorkDecisionException>(() => maker.Decide())).Status); Assert.Equal(0, maker.Owner.Commands);
        using var run = new Rig(); run.Authority.CompanyWide = false; Assert.Equal(403, (await Assert.ThrowsAsync<WorkDecisionException>(() => run.Decide())).Status);
        run.Authority.CompanyWide = true; run.Delegations.Live = false; Assert.Equal(403, (await Assert.ThrowsAsync<WorkDecisionException>(() => run.Decide(delegation: Guid.NewGuid()))).Status); Assert.Equal(0, run.Owner.Commands);
        run.Delegations.Live = true; run.Owner.BeforeReturn = () => run.Delegations.Live = false;
        Assert.Equal(403, (await Assert.ThrowsAsync<WorkDecisionException>(() => run.Decide(delegation: Guid.NewGuid()))).Status);
    }

    [Theory]
    [InlineData("permission")] [InlineData("company")]
    public async Task Current_permission_or_company_loss_after_native_await_suppresses_the_result(string boundary)
    {
        using var run = new Rig(); run.Owner.BeforeReturn = () => { if (boundary == "company") run.CompanyContext.Set(Guid.NewGuid()); else run.Authority.Allowed = false; };
        Assert.Equal(403, (await Assert.ThrowsAsync<WorkDecisionException>(() => run.Decide())).Status);
        Assert.Equal(1, run.Owner.Commands);
    }

    [Fact]
    public async Task Company_drift_during_the_second_human_owner_await_cannot_release_old_facts()
    {
        using var run = new Rig(); run.Owner.HumanChange = h => { run.CompanyContext.Set(Guid.NewGuid()); return h; };
        Assert.Equal(403, (await Assert.ThrowsAsync<WorkDecisionException>(() => run.Host.PreviewAsync(WorkDecisionHostV1.ManualBankPaymentReversal, Id, default))).Status);
        Assert.Equal(0, run.Owner.Commands);
    }

    [Fact]
    public async Task Human_money_and_receipt_stay_outside_snapshot_command_other_kind_and_result_wire()
    {
        using var run = new Rig(); var options = new JsonSerializerOptions(JsonSerializerDefaults.Web);
        var snapshot = await run.Source.ReadDecisionAsync(new(Company, Actor, Correlation, null), Id, default);
        var snapshotJson = JsonSerializer.Serialize(snapshot, options); Assert.DoesNotContain("manualBankPaymentReversalProof", snapshotJson); Assert.DoesNotContain("companyAccountId", snapshotJson); Assert.DoesNotContain("1250.50", snapshotJson);
        Assert.DoesNotContain("decisionReceipt", JsonSerializer.Serialize(run.Owner.Result, FinanceJson.CreateOptions()));
        using var native = JsonDocument.Parse(JsonSerializer.Serialize(run.Owner.Result, FinanceJson.CreateOptions())); Assert.Equal(5, native.RootElement.EnumerateObject().Count());
        var preview = await run.Host.PreviewAsync(WorkDecisionHostV1.ManualBankPaymentReversal, Id, default); Assert.Contains("paymentRecordedBy", JsonSerializer.Serialize(preview, options));
        Assert.DoesNotContain("manualBankPaymentReversal", JsonSerializer.Serialize(new WorkDecisionPreviewV1(Company, WorkDecisionHostV1.BankAccount, Id, "SENTETIK", "1", true, false, [], []), options));
        using var body = JsonDocument.Parse(JsonSerializer.Serialize(new WorkDecisionRequestV1("1", true, null), options)); Assert.Equal(4, body.RootElement.EnumerateObject().Count());
        using var result = JsonDocument.Parse(JsonSerializer.Serialize(await run.Decide(), options)); Assert.Equal(6, result.RootElement.EnumerateObject().Count());
    }

    private sealed class Rig : IDisposable
    {
        public Owner Owner { get; } public SourceProxy Source { get; } public Authority Authority { get; } = new(); public Delegations Delegations { get; } = new();
        public WorkDecisionHostV1 Host { get; } public MutableCompany CompanyContext { get; } = new(); private readonly FinanceDbContext db;
        public Rig(bool approve = true, bool replay = false, string actor = Actor)
        {
            Owner = new(); Owner.Result = Answer(Owner.Header, approve, replay); if (replay) Owner.Header = Owner.Header with { Proposal = Owner.Result.Resource! };
            var company = CompanyContext; company.Set(Company); var user = new WorkInventoryTransferLossDecisionHostTests.LossUser(Company, actor);
            db = new(new DbContextOptionsBuilder<FinanceDbContext>().UseInMemoryDatabase("payment-reversal-work-" + Guid.NewGuid()).Options, company);
            Source = new(new FinanceManualBankPaymentReversalWorkInboxSourceV1(db, new(company, user, Authority), Owner));
            var correlation = new CorrelationContext(); correlation.Set(Correlation, null, "Web", null, null); var fence = new WorkDecisionExecutionFenceV1();
            Host = new(company, user, correlation, new WorkDecisionAuthorityV1(Authority, fence), [Source], Delegations, fence, null!, null!, manualBankPaymentReversals: Owner);
            Owner.CommitFence = ct => fence.VerifyAsync(ct);
        }
        public Task<WorkDecisionResultV1> Decide(bool approve = true, string? reason = null, Guid? delegation = null) => Host.DecideAsync(WorkDecisionHostV1.ManualBankPaymentReversal, Id, new("1", approve, reason, delegation), Key, default);
        public void Dispose() => db.Dispose();
    }
    private sealed class SourceProxy(IWorkApprovalDecisionSourceV1 inner) : IWorkApprovalDecisionSourceV1
    {
        public Func<WorkApprovalDecisionSnapshotV1, WorkApprovalDecisionSnapshotV1>? Change;
        public string Module => inner.Module; public string Kind => inner.Kind; public string ApproverPermission => inner.ApproverPermission;
        public Task<WorkInboxSourceResultV1> ListPendingAsync(WorkInboxSourceContextV1 c, int count, CancellationToken t) => inner.ListPendingAsync(c, count, t);
        public async Task<WorkApprovalDecisionSnapshotV1?> ReadDecisionAsync(WorkInboxSourceContextV1 c, Guid id, CancellationToken t)
        { var s = await inner.ReadDecisionAsync(c, id, t); return s is null || Change is null ? s : Change(s); }
    }
    private sealed class Owner : IPaymentReversalProposalService, IPaymentReversalDecisionPreviewReadV1
    {
        public PaymentReversalDecisionPreviewV1 Header = Human(); public PaymentReversalProposalCommandResult Result = null!;
        public Func<PaymentReversalDecisionPreviewV1, PaymentReversalDecisionPreviewV1>? HumanChange; public Action? BeforeReturn; public Func<CancellationToken, Task<bool>>? CommitFence;
        public int Reads, Commands; public Guid LastId; public string? Version, Key, Reason;
        public async Task<ReadResult<PaymentReversalDecisionPreviewV1>> ReadDecisionPreviewAsync(FinanceContext c, Guid id, Func<CancellationToken, Task> fence, CancellationToken t)
        { await fence(t); Reads++; var view = Reads > 1 && HumanChange is not null ? HumanChange(Header) : Header; await fence(t); return new(ReadOutcome.Found, view); }
        private async Task<PaymentReversalProposalCommandResult> Decide(Guid id, string version, string? reason, CommandMetadata metadata, CancellationToken token)
        { Commands++; LastId = id; Version = version; Reason = reason; Key = metadata.IdempotencyKey; BeforeReturn?.Invoke(); if (CommitFence is not null && !await CommitFence(token)) return new(CommandOutcome.Denied, null, false, Correlation, []); return Result; }
        public Task<PaymentReversalProposalCommandResult> ApproveAsync(FinanceContext c, CommandMetadata m, ApprovePaymentReversalRequest r, CancellationToken t) => Decide(r.ProposalId, r.ExpectedVersion, null, m, t);
        public Task<PaymentReversalProposalCommandResult> RejectAsync(FinanceContext c, CommandMetadata m, RejectPaymentReversalRequest r, CancellationToken t) => Decide(r.ProposalId, r.ExpectedVersion, r.Reason, m, t);
        public Task<PaymentReversalProposalCommandResult> ProposeAsync(FinanceContext c, CommandMetadata m, ProposePaymentReversalRequest r, CancellationToken t) => throw new NotSupportedException();
        public Task<ReadResult<PaymentReversalProposalView>> DetailAuthorizedAsync(FinanceContext c, Guid id, CancellationToken t) => throw new NotSupportedException();
        public Task<ReadResult<PaymentReversalProposalPage>> PendingAuthorizedAsync(FinanceContext c, CancellationToken t) => throw new NotSupportedException();
    }
    private sealed class MutableCompany : ICompanyContext
    { public bool IsResolved => CompanyId != Guid.Empty; public Guid CompanyId { get; private set; } public void Set(Guid id) => CompanyId = id; }
    private sealed class Authority : IErpAuthorityRevalidationService
    {
        public bool Allowed = true, CompanyWide = true;
        public Task<PermissionScope> RevalidatePermissionScopeAsync(string permission, Guid company, CancellationToken t = default)
        { Assert.Equal(FinancePermissionNames.PaymentReverseApprove, permission); return Task.FromResult(new PermissionScope(Allowed, CompanyWide, new HashSet<string>(), new HashSet<Guid>(), new HashSet<Guid>(), new HashSet<Guid>())); }
    }
    private sealed class Delegations : IWorkDelegationReaderV1
    {
        public bool Live = true;
        public Task<WorkDelegationViewV1?> FindForActorAsync(Guid id, WorkInboxItemV1 item, CancellationToken t = default) => Task.FromResult<WorkDelegationViewV1?>(Live
            ? new(id, Company, item.Kind, FinancePermissionNames.PaymentReverseApprove, Guid.NewGuid(), Guid.NewGuid(), At, DateTimeOffset.UtcNow.AddDays(1), "1", Guid.NewGuid(), At, "SENTETIK", null, null, null) : null);
    }
}
