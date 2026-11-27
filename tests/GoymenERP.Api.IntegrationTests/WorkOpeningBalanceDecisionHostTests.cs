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

// Controlled owner receipts test the Work boundary. They do not establish SQL/native journey acceptance.
public sealed class WorkOpeningBalanceDecisionHostTests
{
    private static readonly Guid Company = Guid.Parse("ae000000-0000-4000-8000-000000000001"), OpeningId = Guid.Parse("ae000000-0000-4000-8000-000000000002"), ReversalId = Guid.Parse("ae000000-0000-4000-8000-000000000003");
    private static readonly DateTimeOffset At = new(2026, 11, 27, 9, 0, 0, TimeSpan.Zero);
    private const string Actor = "SENTETIK-checker", Maker = "SENTETIK-maker", OpeningMaker = "SENTETIK-opening-maker", Key = "opening-work-key", Correlation = "SENTETIK-opening-work";
    private static OpeningBalanceDecisionDetailV1 Detail(bool reversal, string actor)
    {
        var opening = new OpeningBalanceView(OpeningId, Company, OpeningBalanceTargetKind.PartyAccount, Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), OpeningBalanceDirection.Debit,
            "001250.50", new(2026, 11, 26), reversal ? OpeningBalanceStatus.Approved : OpeningBalanceStatus.Proposed, "SENTETIK-period", actor == (reversal ? OpeningMaker : Maker), At.AddDays(-1), reversal ? At : null, null, reversal ? "2" : "1",
            reversal ? new(ReversalId, OpeningBalanceReversalStatus.Proposed, "SENTETIK private reversal reason", "SENTETIK-period", actor == Maker, At, null, null, "1") : null);
        return Prove(new(opening), reversal);
    }
    private static OpeningBalanceDecisionDetailV1 Prove(OpeningBalanceDecisionDetailV1 detail, bool reversal)
    {
        var v = detail.OpeningBalance; var t = v.Reversal;
        return detail with { DecisionProof = new(Company, OpeningId, reversal ? ReversalId : null, reversal ? OpeningMaker : Maker, Maker,
            reversal ? t!.ProposedAtUtc : v.ProposedAtUtc, reversal ? t!.Status.ToString() : v.Status.ToString(), reversal ? t!.Version : v.Version,
            FinanceOpeningBalanceService.OpeningFactsDigest(v, reversal ? OpeningMaker : Maker), reversal ? FinanceOpeningBalanceService.ReversalFactsDigest(OpeningId, t!, Maker) : null,
            FinanceOpeningBalanceService.DecisionPreviewDigest(detail)) };
    }
    private static OpeningBalanceCommandResult Answer(OpeningBalanceDecisionDetailV1 detail, bool reversal, bool approve, bool replay, string actor)
    {
        var v = detail.OpeningBalance; var time = At.AddDays(-2); // Native policy imposes no new clock chronology rule.
        var target = v.Reversal is null ? null : v.Reversal with { Status = approve ? OpeningBalanceReversalStatus.Approved : OpeningBalanceReversalStatus.Rejected,
            Version = "2", DecidedAtUtc = time, RejectionReason = approve ? null : "SENTETIK ret" };
        var opening = v with { Status = reversal ? approve ? OpeningBalanceStatus.Reversed : OpeningBalanceStatus.Approved : approve ? OpeningBalanceStatus.Approved : OpeningBalanceStatus.Rejected,
            Version = reversal && approve ? "3" : "2", DecidedAtUtc = reversal ? v.DecidedAtUtc : time, RejectionReason = !reversal && !approve ? "SENTETIK ret" : null, Reversal = target };
        var command = reversal ? approve ? FinanceOpeningBalanceService.ApproveReversalCommand : FinanceOpeningBalanceService.RejectReversalCommand
            : approve ? FinanceOpeningBalanceService.ApproveCommand : FinanceOpeningBalanceService.RejectCommand;
        var hash = reversal ? approve ? FinanceOpeningBalanceService.ApproveReversalPayloadHash(new(OpeningId, "1")) : FinanceOpeningBalanceService.RejectReversalPayloadHash(new(OpeningId, "1", "SENTETIK ret"))
            : approve ? FinanceOpeningBalanceService.ApprovePayloadHash(new(OpeningId, "1")) : FinanceOpeningBalanceService.RejectPayloadHash(new(OpeningId, "1", "SENTETIK ret"));
        var original = replay ? "SENTETIK-original" : Correlation;
        var result = new OpeningBalanceCommandResult(CommandOutcome.Succeeded, opening, replay, original, []);
        return result with { DecisionReceipt = new(Company, OpeningId, reversal ? ReversalId : null, command, IdempotencyHash.ComputeKey(Key), hash, actor, "1", "2", original, time,
            detail.DecisionProof!.OpeningFactsDigest, detail.DecisionProof.ReversalFactsDigest, FinanceOpeningBalanceService.OriginalReceiptDigest(result)) };
    }

    [Theory] [InlineData(false, false)] [InlineData(false, true)] [InlineData(true, false)] [InlineData(true, true)]
    public async Task Native_opening_and_bound_reversal_commands_preserve_versions_reason_and_original_receipt_without_second_human_POST_read(bool reversal, bool approve)
    {
        using var run = new Rig(reversal, approve); var preview = await run.Host.PreviewAsync(run.Kind, run.Id, default);
        Assert.Equal(run.Owner.Header, preview.OpeningBalance); Assert.Equal("1", preview.SourceVersion); Assert.False(preview.ApprovalReasonRequired); Assert.True(preview.RejectionSupported);
        var reads = run.Owner.Reads; var result = await run.Decide();
        Assert.Equal(approve ? "Approved" : "Rejected", result.State); Assert.False(result.Replayed); Assert.Equal(Correlation, result.OriginalCorrelationId);
        Assert.Equal(reads + 1, run.Owner.Reads); Assert.Equal((OpeningId, reversal ? (Guid?)ReversalId : null, "1", Key, approve ? null : "SENTETIK ret"),
            (run.Owner.LastOpeningId, run.Owner.LastReversalId, run.Owner.Version, run.Owner.Key, run.Owner.Reason)); Assert.Equal(0, run.Owner.PlainReversals);
    }

    [Theory] [InlineData(false, false)] [InlineData(false, true)] [InlineData(true, false)] [InlineData(true, true)]
    public async Task Old_key_uses_immutable_original_receipt_when_current_opening_is_Reversed_or_old_A_is_rejected_with_B_pending(bool reversal, bool approve)
    {
        using var run = new Rig(reversal, approve, replay: true, laterParent: true);
        run.Owner.HumanChange = _ => throw new InvalidOperationException("POST must not fetch another human preview");
        var result = await run.Decide(); Assert.True(result.Replayed); Assert.Equal(approve ? "Approved" : "Rejected", result.State);
        Assert.Equal("SENTETIK-original", result.OriginalCorrelationId); Assert.Equal(1, run.Owner.Reads); Assert.Equal(1, run.Owner.Decisions);
        Assert.Equal("1", run.Owner.Version); Assert.Equal(0, run.Owner.PlainReversals);
        if (!reversal && approve) { Assert.Equal("3", run.Owner.Header.OpeningBalance.Version); Assert.Equal("2", run.Owner.Result.Resource!.Version); }
        if (reversal) Assert.Equal(ReversalId, run.Owner.LastReversalId);
    }

    [Theory]
    [InlineData("missing")] [InlineData("company")] [InlineData("opening")] [InlineData("reversal")] [InlineData("actor")] [InlineData("command")]
    [InlineData("key")] [InlineData("hash")] [InlineData("expected")] [InlineData("version")] [InlineData("correlation")] [InlineData("time")]
    [InlineData("facts")] [InlineData("reversal-facts")] [InlineData("original-digest")] [InlineData("current-result")]
    public async Task Missing_or_inconsistent_native_receipt_never_becomes_a_Work_success(string fault)
    {
        using var run = new Rig(true, true, replay: true); var answer = run.Owner.Result; var p = answer.DecisionReceipt!;
        p = fault switch { "company" => p with { CompanyId = Guid.NewGuid() }, "opening" => p with { OpeningBalanceId = Guid.NewGuid() }, "reversal" => p with { ReversalId = Guid.NewGuid() },
            "actor" => p with { ActorId = Maker }, "command" => p with { CommandName = FinanceOpeningBalanceService.RejectReversalCommand }, "key" => p with { IdempotencyKeyHash = "wrong" },
            "hash" => p with { PayloadHash = "wrong" }, "expected" => p with { ExpectedVersion = "2" }, "version" => p with { ResultVersion = "3" }, "correlation" => p with { OriginalCorrelationId = "wrong" },
            "time" => p with { RecordedAtUtc = At }, "facts" => p with { OpeningFactsDigest = new string('b', 64) }, "reversal-facts" => p with { ReversalFactsDigest = new string('b', 64) },
            "original-digest" => p with { OriginalReceiptDigest = new string('b', 64) }, _ => p };
        run.Owner.Result = answer with { DecisionReceipt = fault == "missing" ? null : p, Resource = fault == "current-result" ? answer.Resource! with { Version = "4" } : answer.Resource };
        Assert.Equal(503, (await Assert.ThrowsAsync<WorkDecisionException>(() => run.Decide())).Status); Assert.Equal(0, run.Owner.PlainReversals);
    }

    [Theory]
    [InlineData("company")] [InlineData("item")] [InlineData("maker")] [InlineData("rule")] [InlineData("reference")] [InlineData("digest")] [InlineData("cross-kind")]
    public async Task Source_scalar_proof_is_company_kind_reference_rule_and_maker_bound(string fault)
    {
        using var run = new Rig(); run.Source.Change = s => s with { OpeningBalanceProof = fault switch
        { "company" => s.OpeningBalanceProof! with { CompanyId = Guid.NewGuid() }, "item" => s.OpeningBalanceProof! with { OpeningBalanceId = Guid.NewGuid() },
            "maker" => s.OpeningBalanceProof! with { ProposedBy = "other" }, "digest" => s.OpeningBalanceProof! with { PreviewDigest = "wrong" }, _ => s.OpeningBalanceProof },
            ExpensePaymentProof = fault == "cross-kind" ? new(Company, OpeningId, Maker, At, "Draft", new string('a', 64), new string('b', 64)) : null,
            Item = s.Item with { Rules = fault == "rule" ? [s.Item.Rules[0] with { Threshold = "1" }] : s.Item.Rules,
                References = fault == "reference" ? s.Item.References.Where(x => x.Role != "currency").ToArray() : s.Item.References } };
        Assert.Equal(503, (await Assert.ThrowsAsync<WorkDecisionException>(() => run.Decide())).Status); Assert.Equal(0, run.Owner.Decisions);
    }

    [Theory] [InlineData("amount")] [InlineData("caption")] [InlineData("link")] [InlineData("reference")] [InlineData("proof")]
    public async Task Human_GET_rejects_owner_snapshot_and_formatter_mismatches(string fault)
    {
        using var run = new Rig();
        if (fault is "amount" or "proof") run.Owner.HumanChange = h => fault == "amount" ? h with { OpeningBalance = h.OpeningBalance with { Amount = "999" } }
            : h with { DecisionProof = h.DecisionProof! with { PreviewDigest = new string('b', 64) } };
        else run.Source.Change = s => s with { Item = s.Item with { DocumentLabel = fault == "caption" ? "other" : s.Item.DocumentLabel,
            Link = fault == "link" ? "/other" : s.Item.Link, References = fault == "reference" ? s.Item.References.Select(x => x.Role == "currency" ? x with { RecordId = Guid.NewGuid() } : x).ToArray() : s.Item.References } };
        Assert.Equal(503, (await Assert.ThrowsAsync<WorkDecisionException>(() => run.Host.PreviewAsync(run.Kind, run.Id, default))).Status); Assert.Equal(0, run.Owner.Decisions);
    }

    [Theory] [InlineData(true, "note", "1")] [InlineData(false, null, "1")] [InlineData(false, " ret ", "1")] [InlineData(false, "ret", "2")] [InlineData(true, null, "01")]
    public async Task Native_reason_and_original_version_shape_are_not_silently_normalized(bool approve, string? reason, string version)
    {
        using var run = new Rig(); Assert.Equal(400, (await Assert.ThrowsAsync<WorkDecisionException>(() => run.Host.DecideAsync(run.Kind, run.Id, new(version, approve, reason), Key, default))).Status);
        Assert.Equal(0, run.Owner.Decisions);
    }

    [Theory] [InlineData(false)] [InlineData(true)]
    public async Task Maker_is_denied_even_when_a_controlled_owner_returns_Found_but_the_opening_maker_may_decide_reversal(bool reversal)
    {
        using (var run = new Rig(reversal, actor: Maker)) { Assert.Equal(403, (await Assert.ThrowsAsync<WorkDecisionException>(() => run.Decide())).Status); Assert.Equal(0, run.Owner.Decisions); }
        if (reversal) { using var run = new Rig(true, actor: OpeningMaker); Assert.Equal("Approved", (await run.Decide()).State); }
    }

    [Theory] [InlineData("permission")] [InlineData("company")] [InlineData("actor")] [InlineData("membership")] [InlineData("delegation")]
    public async Task Late_authority_context_and_delegation_loss_suppress_release(string fault)
    {
        using var run = new Rig(); run.Owner.BeforeReturn = () => { if (fault == "permission") run.Authority.Allowed = false; else if (fault == "company") run.Company.Set(Guid.NewGuid());
            else if (fault == "actor") run.User.Actor = Maker; else if (fault == "membership") run.User.Member = false; else run.Delegations.Live = false; };
        Assert.Equal(403, (await Assert.ThrowsAsync<WorkDecisionException>(() => run.Decide(fault == "delegation" ? Guid.NewGuid() : null))).Status);
    }

    [Theory] [InlineData(CommandOutcome.Denied, 403)] [InlineData(CommandOutcome.Unavailable, 503)] [InlineData(CommandOutcome.VersionConflict, 409)] [InlineData(CommandOutcome.IdempotencyConflict, 409)] [InlineData(CommandOutcome.ValidationFailed, 422)]
    public async Task Native_refusal_preserves_original_key_outcome_and_correlation(CommandOutcome outcome, int status)
    {
        using var run = new Rig(); run.Owner.Result = new(outcome, null, true, "SENTETIK-native-original", [new("request", "SENTETIK-refusal")]);
        var error = await Assert.ThrowsAsync<WorkDecisionException>(() => run.Decide()); Assert.Equal(status, error.Status); Assert.True(error.Replayed); Assert.Equal("SENTETIK-native-original", error.OriginalCorrelationId);
    }

    [Fact]
    public async Task Optional_owner_absence_scoped_grant_and_cancellation_fail_closed_without_a_native_command()
    {
        using (var run = new Rig(injectOwner: false)) Assert.Equal(503, (await Assert.ThrowsAsync<WorkDecisionException>(() => run.Decide())).Status);
        using (var run = new Rig()) { run.Authority.CompanyWide = false; Assert.Equal(403, (await Assert.ThrowsAsync<WorkDecisionException>(() => run.Decide())).Status); Assert.Equal(0, run.Owner.Decisions); }
        using (var run = new Rig()) { using var cts = new CancellationTokenSource(); run.Owner.BeforeReadReturn = () => { if (run.Owner.Reads == 2) cts.Cancel(); };
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => run.Host.PreviewAsync(run.Kind, run.Id, cts.Token)); Assert.Equal(0, run.Owner.Decisions); }
    }

    [Fact]
    public async Task Private_owner_proof_and_human_facts_are_GET_only_and_native_request_result_wires_are_unchanged()
    {
        using var run = new Rig(true); var options = new JsonSerializerOptions(JsonSerializerDefaults.Web);
        var snapshot = await run.Source.ReadDecisionAsync(new(Company, Actor, Correlation, null), run.Id, default);
        var json = JsonSerializer.Serialize(snapshot, options); Assert.DoesNotContain("openingBalanceProof", json); Assert.DoesNotContain("SENTETIK private reversal reason", json);
        var preview = await run.Host.PreviewAsync(run.Kind, run.Id, default); Assert.Contains("SENTETIK private reversal reason", JsonSerializer.Serialize(preview, options));
        Assert.DoesNotContain("openingBalance", JsonSerializer.Serialize(new WorkDecisionPreviewV1(Company, WorkDecisionHostV1.BankAccount, OpeningId, "SENTETIK", "1", true, false, [], []), options));
        using var native = JsonDocument.Parse(JsonSerializer.Serialize(run.Owner.Result, FinanceJson.CreateOptions())); Assert.Equal(5, native.RootElement.EnumerateObject().Count()); Assert.False(native.RootElement.TryGetProperty("decisionReceipt", out _));
        using var body = JsonDocument.Parse(JsonSerializer.Serialize(new WorkDecisionRequestV1("1", true, null), options)); Assert.Equal(4, body.RootElement.EnumerateObject().Count());
        using var result = JsonDocument.Parse(JsonSerializer.Serialize(await run.Decide(), options)); Assert.Equal(6, result.RootElement.EnumerateObject().Count());
    }

    private sealed class Rig : IDisposable
    {
        public Owner Owner { get; } public Proxy Source { get; } public Authority Authority { get; } = new(); public Delegations Delegations { get; } = new();
        public WorkDecisionHostV1 Host { get; } public MutableCompany Company { get; } = new(); public User User { get; } public string Kind { get; } public Guid Id { get; }
        private readonly FinanceDbContext db; private readonly bool approve;
        public Rig(bool reversal = false, bool approve = true, bool replay = false, bool laterParent = false, string actor = Actor, bool injectOwner = true)
        {
            this.approve = approve; Kind = reversal ? WorkDecisionHostV1.OpeningBalanceReversal : WorkDecisionHostV1.OpeningBalance; Id = reversal ? ReversalId : OpeningId;
            Owner = new() { Header = Detail(reversal, actor) }; Owner.Result = Answer(Owner.Header, reversal, approve, replay, actor);
            if (replay)
            {
                var header = Owner.Result.Resource!;
                if (laterParent && approve && !reversal || laterParent && !approve && reversal) header = header with { Status = OpeningBalanceStatus.Reversed, Version = "3" };
                Owner.Header = Prove(new(header), reversal);
            }
            Company.Set(WorkOpeningBalanceDecisionHostTests.Company); User = new(actor);
            db = new(new DbContextOptionsBuilder<FinanceDbContext>().UseInMemoryDatabase("opening-work-" + Guid.NewGuid()).Options, Company);
            IWorkApprovalDecisionSourceV1 source = reversal ? new FinanceOpeningBalanceReversalWorkInboxSourceV1(db, new(Company, User, Authority), Owner)
                : new FinanceOpeningBalanceWorkInboxSourceV1(db, new(Company, User, Authority), Owner);
            Source = new(source); var correlation = new CorrelationContext(); correlation.Set(Correlation, null, "Web", null, null); var fence = new WorkDecisionExecutionFenceV1();
            Host = new(Company, User, correlation, new WorkDecisionAuthorityV1(Authority, fence), [Source], Delegations, fence, null!, null!, openingBalances: injectOwner ? Owner : null);
            Owner.CommitFence = ct => fence.VerifyAsync(ct);
        }
        public Task<WorkDecisionResultV1> Decide(Guid? delegation = null) => Host.DecideAsync(Kind, Id, new("1", approve, approve ? null : "SENTETIK ret", delegation), Key, default);
        public void Dispose() => db.Dispose();
    }
    private sealed class Proxy(IWorkApprovalDecisionSourceV1 inner) : IWorkApprovalDecisionSourceV1
    {
        public Func<WorkApprovalDecisionSnapshotV1, WorkApprovalDecisionSnapshotV1>? Change;
        public string Module => inner.Module; public string Kind => inner.Kind; public string ApproverPermission => inner.ApproverPermission;
        public Task<WorkInboxSourceResultV1> ListPendingAsync(WorkInboxSourceContextV1 c, int n, CancellationToken t) => inner.ListPendingAsync(c, n, t);
        public async Task<WorkApprovalDecisionSnapshotV1?> ReadDecisionAsync(WorkInboxSourceContextV1 c, Guid id, CancellationToken t)
        { var answer = await inner.ReadDecisionAsync(c, id, t); return answer is null || Change is null ? answer : Change(answer); }
    }
    private sealed class Owner : IFinanceOpeningBalanceService, IFinanceOpeningBalanceDecisionReadV1, IFinanceOpeningBalanceReversalDecisionV1
    {
        public OpeningBalanceDecisionDetailV1 Header = null!; public OpeningBalanceCommandResult Result = null!; public int Reads, Decisions, PlainReversals;
        public Guid LastOpeningId; public Guid? LastReversalId; public string? Version, Key, Reason; public Action? BeforeReturn, BeforeReadReturn;
        public Func<OpeningBalanceDecisionDetailV1, OpeningBalanceDecisionDetailV1>? HumanChange; public Func<CancellationToken, Task<bool>>? CommitFence;
        private async Task<ReadResult<OpeningBalanceDecisionDetailV1>> Read(FinanceContext c, Guid id, Func<CancellationToken, Task> fence, CancellationToken t)
        { await fence(t); Reads++; var value = Reads > 1 && HumanChange is not null ? HumanChange(Header) : Header; BeforeReadReturn?.Invoke(); await fence(t); return new(ReadOutcome.Found, value); }
        public Task<ReadResult<OpeningBalanceDecisionDetailV1>> ReadOpeningDecisionAsync(FinanceContext c, Guid id, Func<CancellationToken, Task> f, CancellationToken t) => Read(c, id, f, t);
        public Task<ReadResult<OpeningBalanceDecisionDetailV1>> ReadReversalDecisionAsync(FinanceContext c, Guid id, Func<CancellationToken, Task> f, CancellationToken t) => Read(c, id, f, t);
        private async Task<OpeningBalanceCommandResult> Execute(Guid id, Guid? reversal, string version, string? reason, CommandMetadata metadata, CancellationToken t)
        { Decisions++; LastOpeningId = id; LastReversalId = reversal; Version = version; Reason = reason; Key = metadata.IdempotencyKey; BeforeReturn?.Invoke();
            if (CommitFence is not null && !await CommitFence(t)) return new(CommandOutcome.Denied, null, false, Correlation, []); return Result; }
        public Task<OpeningBalanceCommandResult> ApproveAsync(FinanceContext c, CommandMetadata m, OpeningBalanceApproveRequest r, CancellationToken t) => Execute(r.OpeningBalanceId, null, r.ExpectedVersion, null, m, t);
        public Task<OpeningBalanceCommandResult> RejectAsync(FinanceContext c, CommandMetadata m, OpeningBalanceRejectRequest r, CancellationToken t) => Execute(r.OpeningBalanceId, null, r.ExpectedVersion, r.Reason, m, t);
        public Task<OpeningBalanceCommandResult> ApproveReversalDecisionAsync(FinanceContext c, CommandMetadata m, Guid id, OpeningBalanceReversalApproveRequest r, CancellationToken t) => Execute(r.OpeningBalanceId, id, r.ExpectedVersion, null, m, t);
        public Task<OpeningBalanceCommandResult> RejectReversalDecisionAsync(FinanceContext c, CommandMetadata m, Guid id, OpeningBalanceReversalRejectRequest r, CancellationToken t) => Execute(r.OpeningBalanceId, id, r.ExpectedVersion, r.Reason, m, t);
        public Task<OpeningBalanceCommandResult> ApproveReversalAsync(FinanceContext c, CommandMetadata m, OpeningBalanceReversalApproveRequest r, CancellationToken t) { PlainReversals++; throw new NotSupportedException(); }
        public Task<OpeningBalanceCommandResult> RejectReversalAsync(FinanceContext c, CommandMetadata m, OpeningBalanceReversalRejectRequest r, CancellationToken t) { PlainReversals++; throw new NotSupportedException(); }
        public Task<OpeningBalanceCommandResult> ProposeAsync(FinanceContext c, CommandMetadata m, OpeningBalanceProposeRequest r, CancellationToken t) => throw new NotSupportedException();
        public Task<OpeningBalanceCommandResult> ProposeReversalAsync(FinanceContext c, CommandMetadata m, OpeningBalanceReversalProposeRequest r, CancellationToken t) => throw new NotSupportedException();
        public Task<ReadResult<OpeningBalanceListPage>> ListAsync(FinanceContext c, CancellationToken t) => throw new NotSupportedException();
        public Task<ReadResult<OpeningPeriodView>> GetPeriodAsync(FinanceContext c, CancellationToken t) => throw new NotSupportedException();
        public Task<ReadResult<OpeningBalanceCurrencyCandidates>> CurrencyCandidatesAsync(FinanceContext c, CancellationToken t) => throw new NotSupportedException();
    }
    private sealed class MutableCompany : ICompanyContext
    { public bool IsResolved => CompanyId != Guid.Empty; public Guid CompanyId { get; private set; } public void Set(Guid id) => CompanyId = id; }
    private sealed class User(string actor) : ICurrentUser
    { public string Actor = actor; public bool Member = true; public bool IsAuthenticated => true; public string UserId => Actor; public IReadOnlySet<Guid> AllowedCompanyIds { get; } = new HashSet<Guid> { Company }; public bool CanAccessCompany(Guid id) => Member && AllowedCompanyIds.Contains(id); }
    private sealed class Authority : IErpAuthorityRevalidationService
    {
        public bool Allowed = true, CompanyWide = true;
        public Task<PermissionScope> RevalidatePermissionScopeAsync(string permission, Guid company, CancellationToken t = default)
        { Assert.Equal(ErpPermissions.FinanceOpeningBalancesApprove, permission); return Task.FromResult(new PermissionScope(Allowed, CompanyWide, new HashSet<string>(), new HashSet<Guid>(), new HashSet<Guid>(), new HashSet<Guid>())); }
    }
    private sealed class Delegations : IWorkDelegationReaderV1
    {
        public bool Live = true;
        public Task<WorkDelegationViewV1?> FindForActorAsync(Guid id, WorkInboxItemV1 item, CancellationToken t = default) => Task.FromResult<WorkDelegationViewV1?>(Live
            ? new(id, Company, item.Kind, ErpPermissions.FinanceOpeningBalancesApprove, Guid.NewGuid(), Guid.NewGuid(), At, DateTimeOffset.UtcNow.AddDays(1), "1", Guid.NewGuid(), At, "SENTETIK", null, null, null) : null);
    }
}
