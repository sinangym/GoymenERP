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

// Controlled native-owner answers: Work orchestration evidence only, not relational native command acceptance.
public sealed class WorkBankCreditLimitDecisionHostTests
{
    private static readonly Guid Company = Guid.Parse("ba420000-0000-4000-8000-000000000001"), Id = Guid.Parse("ba420000-0000-4000-8000-000000000002");
    private static readonly DateTimeOffset At = new(2026, 11, 27, 9, 0, 0, TimeSpan.Zero);
    private const string Actor = "SENTETIK-checker", Maker = "SENTETIK-maker", Key = "bank-limit-work-key", Correlation = "SENTETIK-work-correlation";
    private static FinanceBankCreditLimitDecisionDetailV1 Human()
    {
        var view = new CreditLimitViewV1(Id, Company, Guid.NewGuid(), Guid.NewGuid(), CreditAccountGroup.Cash, Guid.NewGuid(), "1", CreditAccountStatus.Proposed,
            new(Guid.NewGuid(), "2", null, null, "001250.50", new("SENTETIK immutable source", new(2026, 11, 26), "revision-1"), "SENTETIK recorded reason"),
            false, At, null, null, null);
        return new(view, Maker, FinanceCreditLimitServiceV1.SourceFactsDigest(view, Maker));
    }
    private static CreditLimitResultV1 Answer(FinanceBankCreditLimitDecisionDetailV1 detail, bool approve, string? reason, bool replay)
    {
        var view = detail.Limit with { Status = approve ? CreditAccountStatus.Approved : CreditAccountStatus.Rejected, Version = "2",
            DecidedAtUtc = At.AddMinutes(-1), RejectionReason = reason, SupersededById = null };
        var original = replay ? "SENTETIK-original-correlation" : Correlation;
        var result = new CreditLimitResultV1(CommandOutcome.Succeeded, view, replay, original, []);
        return result with { DecisionReceipt = new(Company, Id, FinanceCreditLimitServiceV1.DecideCommand, IdempotencyHash.ComputeKey(Key),
            FinanceCreditLimitServiceV1.DecisionPayloadHash(new(Id, "1", approve, reason)), Actor, "1", "2", original, view.DecidedAtUtc!.Value,
            detail.SourceFactsDigest, FinanceCreditLimitServiceV1.OriginalReceiptDigest(result)) };
    }

    [Theory] [InlineData(true)] [InlineData(false)]
    public async Task One_native_decide_keeps_exact_literal_reason_and_no_general_workspace_read(bool approve)
    {
        using var run = new Rig(approve); var preview = await run.Host.PreviewAsync(WorkDecisionHostV1.BankCreditLimit, Id, default);
        Assert.Equal(run.Owner.Header, preview.BankCreditLimit); Assert.Equal("1", preview.SourceVersion);
        Assert.True(preview.RejectionSupported); Assert.False(preview.ApprovalReasonRequired);
        var reads = run.Owner.Reads; var result = await run.Decide();
        Assert.Equal(approve ? "Approved" : "Rejected", result.State); Assert.False(result.Replayed); Assert.Equal(Correlation, result.OriginalCorrelationId);
        Assert.Equal(reads + 1, run.Owner.Reads); Assert.Equal(new CreditLimitDecisionV1(Id, "1", approve, run.Reason), run.Owner.Request);
        Assert.Equal(Key, run.Owner.Key); Assert.Equal(1, run.Owner.Decisions); Assert.Equal(0, run.Owner.WorkspaceReads); Assert.Equal(0, run.Owner.Proposals);
    }

    [Theory] [InlineData(true)] [InlineData(false)]
    public async Task Original_key_replay_keeps_original_decision_after_current_supersession_without_second_human_POST_read(bool approve)
    {
        using var run = new Rig(approve, replay: true, superseded: true); run.Owner.HumanChange = _ => throw new InvalidOperationException("No second human POST read");
        var result = await run.Decide(); Assert.True(result.Replayed); Assert.Equal(approve ? "Approved" : "Rejected", result.State);
        Assert.Equal("SENTETIK-original-correlation", result.OriginalCorrelationId); Assert.Equal(1, run.Owner.Reads);
        Assert.NotNull(run.Owner.Header.Limit.SupersededById); Assert.Null(run.Owner.Result.Resource!.SupersededById); Assert.Equal(1, run.Owner.Decisions);
    }

    [Theory]
    [InlineData("missing")] [InlineData("company")] [InlineData("limit")] [InlineData("command")] [InlineData("key")]
    [InlineData("hash")] [InlineData("actor")] [InlineData("expected")] [InlineData("version")] [InlineData("correlation")]
    [InlineData("time")] [InlineData("facts")] [InlineData("original-digest")] [InlineData("current-substitute")]
    [InlineData("fresh-terminal")] [InlineData("source-amount")] [InlineData("source-reference")] [InlineData("wrong-state")]
    public async Task Only_the_exact_original_native_command_receipt_can_release_Work_success(string fault)
    {
        using var run = new Rig(replay: true, superseded: true); var result = run.Owner.Result; var p = result.DecisionReceipt!;
        p = fault switch { "company" => p with { CompanyId = Guid.NewGuid() }, "limit" => p with { LimitId = Guid.NewGuid() },
            "command" => p with { CommandName = FinanceCreditLimitServiceV1.ProposeCommand }, "key" => p with { IdempotencyKeyHash = "wrong" },
            "hash" => p with { PayloadHash = "wrong" }, "actor" => p with { ActorId = Maker }, "expected" => p with { ExpectedVersion = "2" },
            "version" => p with { ResultVersion = "3" }, "correlation" => p with { OriginalCorrelationId = "wrong" },
            "time" => p with { DecidedAtUtc = At }, "facts" => p with { SourceFactsDigest = new string('b', 64) },
            "original-digest" => p with { OriginalReceiptDigest = new string('b', 64) }, _ => p };
        run.Owner.Result = result with { DecisionReceipt = fault == "missing" ? null : p,
            Resource = fault switch { "current-substitute" => run.Owner.Header.Limit,
                "source-amount" => result.Resource! with { Source = result.Resource.Source with { Amount = "999" } },
                "source-reference" => result.Resource! with { Source = result.Resource.Source with { SourceDocument = result.Resource.Source.SourceDocument with { Reference = "other" } } },
                "wrong-state" => result.Resource! with { Status = CreditAccountStatus.Rejected, RejectionReason = "other" }, _ => result.Resource },
            IsReplay = fault == "fresh-terminal" ? false : result.IsReplay };
        Assert.Equal(503, (await Assert.ThrowsAsync<WorkDecisionException>(() => run.Decide())).Status); Assert.Equal(0, run.Owner.WorkspaceReads);
    }

    [Theory]
    [InlineData("company")] [InlineData("limit")] [InlineData("version")] [InlineData("amount")] [InlineData("source")]
    [InlineData("maker")] [InlineData("status")] [InlineData("digest")] [InlineData("supersession")]
    public async Task Human_GET_binds_full_native_source_and_dynamic_preview_to_both_fresh_reads(string fault)
    {
        using var run = new Rig(); run.Owner.HumanChange = h => fault switch
        {
            "company" => h with { Limit = h.Limit with { CompanyId = Guid.NewGuid() } }, "limit" => h with { Limit = h.Limit with { Id = Guid.NewGuid() } },
            "version" => h with { Limit = h.Limit with { Version = "2" } }, "amount" => h with { Limit = h.Limit with { Source = h.Limit.Source with { Amount = "999" } } },
            "source" => h with { Limit = h.Limit with { Source = h.Limit.Source with { Reason = "other" } } }, "maker" => h with { ProposedBy = "other" },
            "status" => h with { Limit = h.Limit with { Status = CreditAccountStatus.Approved } }, "supersession" => h with { Limit = h.Limit with { SupersededById = Guid.NewGuid() } },
            _ => h with { SourceFactsDigest = new string('b', 64) },
        };
        Assert.Equal(503, (await Assert.ThrowsAsync<WorkDecisionException>(() => run.Host.PreviewAsync(WorkDecisionHostV1.BankCreditLimit, Id, default))).Status);
        Assert.Equal(0, run.Owner.Decisions);
    }

    [Theory] [InlineData(ReadOutcome.Denied, 403)] [InlineData(ReadOutcome.Unavailable, 503)] [InlineData(ReadOutcome.NotFound, 503)]
    public async Task Human_read_refusal_is_not_replaced_by_general_workspace_access(ReadOutcome outcome, int status)
    {
        using var run = new Rig(); run.Owner.HumanOutcome = outcome;
        Assert.Equal(status, (await Assert.ThrowsAsync<WorkDecisionException>(() => run.Host.PreviewAsync(WorkDecisionHostV1.BankCreditLimit, Id, default))).Status);
        Assert.Equal(0, run.Owner.Decisions); Assert.Equal(0, run.Owner.WorkspaceReads);
    }

    [Theory]
    [InlineData("missing")] [InlineData("company")] [InlineData("facts")] [InlineData("preview")] [InlineData("cross-kind")]
    [InlineData("reference")] [InlineData("threshold")] [InlineData("unit")] [InlineData("actual")]
    public async Task Scalar_proof_and_all_native_item_facts_are_kind_bound(string fault)
    {
        using var run = new Rig(); run.Source.Change = s =>
        {
            var p = s.BankCreditLimitProof!;
            return s with { BankCreditLimitProof = fault switch { "missing" => null, "company" => p with { CompanyId = Guid.NewGuid() },
                "facts" => p with { SourceFactsDigest = "wrong" }, "preview" => p with { PreviewDigest = "wrong" }, _ => p },
                ExpensePaymentProof = fault == "cross-kind" ? new(Company, Id, Maker, At, "Draft", new string('a', 64), new string('b', 64)) : null,
                Item = s.Item with { References = fault == "reference" ? s.Item.References.Select(x => x.Role == "currency" ? x with { RecordId = Guid.NewGuid() } : x).ToArray() : s.Item.References,
                    Rules = fault is "threshold" or "unit" or "actual" ? [s.Item.Rules[0] with { Threshold = fault == "threshold" ? "1" : null,
                        Unit = fault == "unit" ? "kg" : null, Actual = fault == "actual" ? "999" : s.Item.Rules[0].Actual }] : s.Item.Rules } };
        };
        Assert.Equal(503, (await Assert.ThrowsAsync<WorkDecisionException>(() => run.Decide())).Status); Assert.Equal(0, run.Owner.Proposals);
    }

    [Fact]
    public async Task Another_registered_kind_cannot_carry_the_bank_limit_private_proof()
    {
        using var run = new Rig(); var snapshot = await run.Source.ReadDecisionAsync(new(Company, Actor, Correlation, null), Id, default);
        Assert.NotNull(snapshot); const string kind = "finance.cash-account.v1";
        var source = new OtherKindSource(snapshot with { Item = snapshot.Item with { Kind = kind } }); run.Authority.Permission = ErpPermissions.FinanceCashAccountApprove;
        var user = new WorkInventoryTransferLossDecisionHostTests.LossUser(Company, Actor); var correlation = new CorrelationContext(); correlation.Set(Correlation, null, "Web", null, null);
        var fence = new WorkDecisionExecutionFenceV1();
        var host = new WorkDecisionHostV1(run.Company, user, correlation, new WorkDecisionAuthorityV1(run.Authority, fence), [source], run.Delegations, fence, null!, null!);
        Assert.Equal(503, (await Assert.ThrowsAsync<WorkDecisionException>(() => host.PreviewAsync(kind, Id, default))).Status); Assert.Equal(0, run.Owner.Decisions);
    }

    [Theory]
    [InlineData(true, "note", "1")] [InlineData(false, null, "1")] [InlineData(false, "", "1")]
    [InlineData(false, " padded ", "1")] [InlineData(true, null, "0")] [InlineData(true, null, "01")]
    [InlineData(true, null, "1\n")] [InlineData(true, null, "2")] [InlineData(true, null, "9223372036854775807")]
    public async Task Native_request_is_original_version_one_approve_null_or_canonical_literal_reject(bool approve, string? reason, string version)
    {
        using var run = new Rig(); Assert.Equal(400, (await Assert.ThrowsAsync<WorkDecisionException>(() => run.Host.DecideAsync(WorkDecisionHostV1.BankCreditLimit, Id, new(version, approve, reason), Key, default))).Status);
        Assert.Equal(0, run.Owner.Decisions);
    }
    [Fact]
    public async Task Native_reject_preserves_exact_1000_characters_and_refuses_1001_without_calling_owner()
    {
        using var run = new Rig(false, reason: new string('x', 1000)); Assert.Equal("Rejected", (await run.Decide()).State);
        using var over = new Rig(false, reason: new string('x', 1001)); Assert.Equal(400, (await Assert.ThrowsAsync<WorkDecisionException>(() => over.Decide())).Status);
        Assert.Equal(0, over.Owner.Decisions);
    }
    [Theory] [InlineData("loss")] [InlineData("overage")] [InlineData("manufacturing")] [InlineData("package")]
    public async Task Other_kind_typed_payload_never_enters_the_native_limit_decision(string selection)
    {
        using var run = new Rig(); var request = new WorkDecisionRequestV1("1", true, null);
        request = selection switch { "loss" => request with { InventoryTransferLoss = new(1, At) }, "overage" => request with { InventoryTransferOverage = new(1, At) },
            "package" => request with { ProductPackageStandard = new("2026-11-27") }, _ => request with { ManufacturingPlanReapproval = new([Guid.NewGuid()]) } };
        Assert.Equal(400, (await Assert.ThrowsAsync<WorkDecisionException>(() => run.Host.DecideAsync(WorkDecisionHostV1.BankCreditLimit, Id, request, Key, default))).Status); Assert.Equal(0, run.Owner.Decisions);
    }
    [Theory]
    [InlineData(CommandOutcome.Denied, 403)] [InlineData(CommandOutcome.Unavailable, 503)] [InlineData(CommandOutcome.VersionConflict, 409)]
    [InlineData(CommandOutcome.IdempotencyConflict, 409)] [InlineData(CommandOutcome.ValidationFailed, 422)]
    public async Task Native_refusal_preserves_its_immutable_original_status_code_and_correlation(CommandOutcome outcome, int status)
    {
        using var run = new Rig(); run.Owner.Result = new(outcome, null, true, "SENTETIK-native-original", [new("request", "SENTETIK-code")]);
        var error = await Assert.ThrowsAsync<WorkDecisionException>(() => run.Decide()); Assert.Equal(status, error.Status);
        Assert.True(error.Replayed); Assert.Equal("SENTETIK-native-original", error.OriginalCorrelationId); Assert.Equal("SENTETIK-code", error.Code);
    }
    [Fact]
    public async Task Maker_does_not_reach_the_native_decision()
    {
        using var run = new Rig(actor: Maker); Assert.Equal(403, (await Assert.ThrowsAsync<WorkDecisionException>(() => run.Decide())).Status); Assert.Equal(0, run.Owner.Decisions);
    }
    [Theory] [InlineData("permission")] [InlineData("company")] [InlineData("actor")]
    public async Task Late_authority_company_or_actor_loss_suppresses_the_response(string fault)
    {
        using var run = new Rig(); run.Owner.BeforeReturn = () => { if (fault == "permission") run.Authority.Allowed = false;
            else if (fault == "company") run.Company.Set(Guid.NewGuid()); else run.User.Actor = Maker; };
        Assert.Equal(403, (await Assert.ThrowsAsync<WorkDecisionException>(() => run.Decide())).Status); Assert.Equal(1, run.Owner.Decisions);
    }
    [Fact]
    public async Task Companywide_and_current_delegation_are_required_before_native_replay_and_inside_execution_fence()
    {
        using var run = new Rig(replay: true); run.Authority.CompanyWide = false;
        Assert.Equal(403, (await Assert.ThrowsAsync<WorkDecisionException>(() => run.Decide())).Status); run.Authority.CompanyWide = true;
        run.Delegations.Live = false; Assert.Equal(403, (await Assert.ThrowsAsync<WorkDecisionException>(() => run.Decide(Guid.NewGuid()))).Status); Assert.Equal(0, run.Owner.Decisions);
        run.Delegations.Live = true; run.Owner.BeforeReturn = () => run.Delegations.Live = false;
        Assert.Equal(403, (await Assert.ThrowsAsync<WorkDecisionException>(() => run.Decide(Guid.NewGuid()))).Status);
    }
    [Fact]
    public async Task Cancellation_from_the_last_human_owner_callback_never_releases_a_preview()
    {
        using var run = new Rig(); using var cancellation = new CancellationTokenSource(); run.Owner.AfterHumanFence = () => cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => run.Host.PreviewAsync(WorkDecisionHostV1.BankCreditLimit, Id, cancellation.Token)); Assert.Equal(0, run.Owner.Decisions);
    }
    [Fact]
    public async Task Full_native_source_is_human_GET_only_while_private_proof_and_command_wire_stay_unchanged()
    {
        using var run = new Rig(); var options = new JsonSerializerOptions(JsonSerializerDefaults.Web);
        var snapshot = await run.Source.ReadDecisionAsync(new(Company, Actor, Correlation, null), Id, default); var wire = JsonSerializer.Serialize(snapshot, options);
        Assert.DoesNotContain("bankCreditLimitProof", wire); Assert.DoesNotContain("SENTETIK immutable source", wire); Assert.DoesNotContain("SENTETIK recorded reason", wire);
        var preview = await run.Host.PreviewAsync(WorkDecisionHostV1.BankCreditLimit, Id, default); Assert.Contains("SENTETIK immutable source", JsonSerializer.Serialize(preview, options));
        Assert.DoesNotContain("bankCreditLimit", JsonSerializer.Serialize(new WorkDecisionPreviewV1(Company, WorkDecisionHostV1.BankAccount, Id, "SENTETIK", "1", true, false, [], []), options));
        using var native = JsonDocument.Parse(JsonSerializer.Serialize(run.Owner.Result, FinanceJson.CreateOptions())); Assert.Equal(5, native.RootElement.EnumerateObject().Count()); Assert.False(native.RootElement.TryGetProperty("decisionReceipt", out _));
        using var body = JsonDocument.Parse(JsonSerializer.Serialize(new WorkDecisionRequestV1("1", true, null), options)); Assert.Equal(4, body.RootElement.EnumerateObject().Count());
        using var result = JsonDocument.Parse(JsonSerializer.Serialize(await run.Decide(), options)); Assert.Equal(6, result.RootElement.EnumerateObject().Count());
    }

    private sealed class Rig : IDisposable
    {
        public Owner Owner { get; } public SourceProxy Source { get; } public Authority Authority { get; } = new(); public Delegations Delegations { get; } = new();
        public WorkDecisionHostV1 Host { get; } public MutableCompany Company { get; } = new(); public MutableUser User { get; } = new();
        public string? Reason { get; } private readonly FinanceDbContext db; private readonly bool approve;
        public Rig(bool approve = true, bool replay = false, bool superseded = false, string actor = Actor, string? reason = null)
        {
            this.approve = approve; Reason = approve ? null : reason ?? "SENTETIK literal reject";
            Owner = new() { Header = Human() }; Owner.Result = Answer(Owner.Header, approve, Reason, replay);
            if (replay) Owner.Header = Owner.Header with { Limit = Owner.Result.Resource! with { SupersededById = superseded ? Guid.NewGuid() : null } };
            Company.Set(WorkBankCreditLimitDecisionHostTests.Company); User.Actor = actor;
            db = new(new DbContextOptionsBuilder<FinanceDbContext>().UseInMemoryDatabase("bank-limit-work-" + Guid.NewGuid()).Options, Company);
            Source = new(new FinanceBankCreditLimitWorkInboxSourceV1(db, new(Company, User, Authority), Owner));
            var correlation = new CorrelationContext(); correlation.Set(Correlation, null, "Web", null, null); var fence = new WorkDecisionExecutionFenceV1();
            Host = new(Company, User, correlation, new WorkDecisionAuthorityV1(Authority, fence), [Source], Delegations, fence, null!, null!, bankCreditLimits: Owner);
            Owner.CommitFence = ct => fence.VerifyAsync(ct);
        }
        public Task<WorkDecisionResultV1> Decide(Guid? delegation = null) => Host.DecideAsync(WorkDecisionHostV1.BankCreditLimit, Id, new("1", approve, Reason, delegation), Key, default);
        public void Dispose() => db.Dispose();
    }
    private sealed class SourceProxy(IWorkApprovalDecisionSourceV1 inner) : IWorkApprovalDecisionSourceV1
    {
        public Func<WorkApprovalDecisionSnapshotV1, WorkApprovalDecisionSnapshotV1>? Change;
        public string Module => inner.Module; public string Kind => inner.Kind; public string ApproverPermission => inner.ApproverPermission;
        public Task<WorkInboxSourceResultV1> ListPendingAsync(WorkInboxSourceContextV1 c, int n, CancellationToken t) => inner.ListPendingAsync(c, n, t);
        public async Task<WorkApprovalDecisionSnapshotV1?> ReadDecisionAsync(WorkInboxSourceContextV1 c, Guid id, CancellationToken t)
        { var answer = await inner.ReadDecisionAsync(c, id, t); return answer is null || Change is null ? answer : Change(answer); }
    }
    private sealed class OtherKindSource(WorkApprovalDecisionSnapshotV1 snapshot) : IWorkApprovalDecisionSourceV1
    {
        public string Module => "finance"; public string Kind => "finance.cash-account.v1"; public string ApproverPermission => ErpPermissions.FinanceCashAccountApprove;
        public Task<WorkInboxSourceResultV1> ListPendingAsync(WorkInboxSourceContextV1 c, int n, CancellationToken t) => throw new NotSupportedException();
        public Task<WorkApprovalDecisionSnapshotV1?> ReadDecisionAsync(WorkInboxSourceContextV1 c, Guid id, CancellationToken t) => Task.FromResult<WorkApprovalDecisionSnapshotV1?>(snapshot);
    }
    private sealed class Owner : IFinanceCreditLimitServiceV1, IFinanceBankCreditLimitDecisionReadV1
    {
        public FinanceBankCreditLimitDecisionDetailV1 Header = null!; public CreditLimitResultV1 Result = null!;
        public int Reads, Decisions, WorkspaceReads, Proposals; public CreditLimitDecisionV1? Request; public string? Key;
        public ReadOutcome? HumanOutcome; public Action? BeforeReturn, AfterHumanFence; public Func<FinanceBankCreditLimitDecisionDetailV1, FinanceBankCreditLimitDecisionDetailV1>? HumanChange;
        public Func<CancellationToken, Task<bool>>? CommitFence;
        public async Task<ReadResult<FinanceBankCreditLimitDecisionDetailV1>> ReadDecisionAsync(FinanceContext c, Guid id, Func<CancellationToken, Task> fence, CancellationToken t)
        { await fence(t); Reads++; var result = Reads > 1 && HumanChange is not null ? HumanChange(Header) : Header; await fence(t); if (Reads > 1) { AfterHumanFence?.Invoke(); if (HumanOutcome is { } outcome) return new(outcome, null); } return new(ReadOutcome.Found, result); }
        public async Task<CreditLimitResultV1> DecideAsync(FinanceContext c, CommandMetadata m, CreditLimitDecisionV1 r, CancellationToken t)
        { Decisions++; Request = r; Key = m.IdempotencyKey; BeforeReturn?.Invoke(); if (CommitFence is not null && !await CommitFence(t)) return new(CommandOutcome.Denied, null, false, Correlation, []); return Result; }
        public Task<ReadResult<CreditLimitWorkspaceV1>> ReadAsync(FinanceContext c, Guid id, CancellationToken t) { WorkspaceReads++; throw new InvalidOperationException("No general read authority"); }
        public Task<CreditLimitResultV1> ProposeAsync(FinanceContext c, CommandMetadata m, CreditLimitProposeV1 r, CancellationToken t) { Proposals++; throw new NotSupportedException(); }
    }
    private sealed class MutableCompany : ICompanyContext
    { public bool IsResolved => CompanyId != Guid.Empty; public Guid CompanyId { get; private set; } public void Set(Guid id) => CompanyId = id; }
    private sealed class MutableUser : ICurrentUser
    {
        public string Actor = WorkBankCreditLimitDecisionHostTests.Actor; public bool IsAuthenticated => true; public string? UserId => Actor;
        public IReadOnlySet<Guid> AllowedCompanyIds { get; } = new HashSet<Guid> { Company }; public bool CanAccessCompany(Guid id) => AllowedCompanyIds.Contains(id);
    }
    private sealed class Authority : IErpAuthorityRevalidationService
    {
        public bool Allowed = true, CompanyWide = true; public string Permission = ErpPermissions.FinanceCreditsApprove;
        public Task<PermissionScope> RevalidatePermissionScopeAsync(string permission, Guid company, CancellationToken t = default)
        { Assert.Equal(Permission, permission); return Task.FromResult(new PermissionScope(Allowed, CompanyWide, new HashSet<string>(), new HashSet<Guid>(), new HashSet<Guid>(), new HashSet<Guid>())); }
    }
    private sealed class Delegations : IWorkDelegationReaderV1
    {
        public bool Live = true;
        public Task<WorkDelegationViewV1?> FindForActorAsync(Guid id, WorkInboxItemV1 item, CancellationToken t = default) => Task.FromResult<WorkDelegationViewV1?>(Live
            ? new(id, Company, item.Kind, ErpPermissions.FinanceCreditsApprove, Guid.NewGuid(), Guid.NewGuid(), At, DateTimeOffset.UtcNow.AddDays(1), "1", Guid.NewGuid(), At, "SENTETIK", null, null, null) : null);
    }
}
