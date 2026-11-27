using System.Text.Json;
using GoymenERP.Api.WorkRuntime;
using GoymenERP.BuildingBlocks.Correlation;
using GoymenERP.BuildingBlocks.Multitenancy;
using GoymenERP.BuildingBlocks.Security;
using GoymenERP.BuildingBlocks.Work;
using GoymenERP.Modules.Finance.Contracts;
using GoymenERP.Modules.Finance.Infrastructure;
using GoymenERP.Modules.Finance.Infrastructure.Persistence;
using GoymenERP.Modules.Finance.Infrastructure.WorkInbox;
using GoymenERP.Modules.Idempotency.Domain.Executions;
using Microsoft.EntityFrameworkCore;

namespace GoymenERP.Api.IntegrationTests;

// Synthetic owner replies exercise the consumer boundary; native receipt/rollback uses the separate real SQL fixture.
public sealed class WorkOfficialSalesInvoiceDecisionHostTests
{
    private static readonly Guid Company = Guid.Parse("ab000000-0000-4000-8000-000000000001"), Id = Guid.Parse("ab000000-0000-4000-8000-000000000002");
    private const string Actor = "SENTETIK-checker", Maker = "SENTETIK-maker", Key = "official-work-key", Correlation = "official-work-correlation", Reason = "SENTETIK düzeltme gerekli";
    private static readonly DateTimeOffset At = new(2026, 11, 27, 9, 0, 0, TimeSpan.Zero);
    private static FinanceOfficialSalesInvoiceViewV1 View() => new(Id, Company, "SNT2026000000001", "ab000000-0000-4000-8000-000000000003",
        Guid.Parse("ab000000-0000-4000-8000-000000000004"), Guid.Parse("ab000000-0000-4000-8000-000000000005"),
        "1250.50", "1250.50", "1042.08", "208.42", "0", Guid.Parse("ab000000-0000-4000-8000-000000000006"), 1, new string('a', 64),
        FinanceOfficialSalesInvoiceStatusV1.PendingApproval, "1", Maker, At, null, null, null, null, null, null, null, new(2026, 11, 27));
    private static FinanceOfficialSalesInvoiceResultV1 Answer(bool approve, bool replay, string? reason = Reason)
    {
        var v = approve ? View() with { Status = FinanceOfficialSalesInvoiceStatusV1.Approved, Version = "2", ApprovedBy = Actor, ApprovedAtUtc = At.AddHours(1) }
            : View() with { Status = FinanceOfficialSalesInvoiceStatusV1.Rejected, Version = "2", RejectedBy = Actor, RejectedAtUtc = At.AddHours(1), RejectionReason = reason };
        var original = replay ? "SENTETIK-original-correlation" : Correlation;
        var command = approve ? FinanceOfficialSalesInvoiceServiceV1.ApproveCommand : FinanceOfficialSalesInvoiceServiceV1.RejectCommand;
        var hash = approve ? FinanceOfficialSalesInvoiceServiceV1.ApprovePayloadHash(new(Id, "1")) : FinanceOfficialSalesInvoiceServiceV1.RejectPayloadHash(new(Id, "1", reason!));
        return new(CommandOutcome.Succeeded, v, replay, original, [], null)
        { DecisionReceipt = new(Company, Id, command, IdempotencyHash.ComputeKey(Key), hash, Actor, "1", "2", original, At.AddHours(1)) };
    }

    [Theory]
    [InlineData(true, false)] [InlineData(false, false)] [InlineData(true, true)] [InlineData(false, true)]
    public async Task Exact_native_approve_reject_and_original_key_terminal_replay_preserve_frozen_body(bool approve, bool replay)
    {
        using var run = new Rig(approve, replay);
        var preview = await run.Host.PreviewAsync(WorkDecisionHostV1.OfficialSalesInvoice, Id, default);
        Assert.False(preview.ApprovalReasonRequired); Assert.True(preview.RejectionSupported);
        Assert.Equal(run.Owner.Header, preview.OfficialSalesInvoice);
        var result = await run.Host.DecideAsync(WorkDecisionHostV1.OfficialSalesInvoice, Id, new("1", approve, approve ? null : Reason), Key, default);
        Assert.Equal((approve ? "Approved" : "Rejected", replay, run.Owner.Result.OriginalCorrelationId), (result.State, result.Replayed, result.OriginalCorrelationId));
        Assert.Equal((Id, "1", Key, approve, 1), (run.Owner.LastId, run.Owner.LastVersion, run.Owner.Key, run.Owner.Approve, run.Owner.Commands));
    }

    [Theory]
    [InlineData("missing")] [InlineData("company")] [InlineData("invoice")] [InlineData("command")] [InlineData("key")]
    [InlineData("hash")] [InlineData("actor")] [InlineData("expected")] [InlineData("result-version")] [InlineData("correlation")]
    [InlineData("time")] [InlineData("status")] [InlineData("recorded-amount")] [InlineData("recorded-pdf")]
    [InlineData("fresh-on-terminal")]
    public async Task Wrong_or_missing_native_receipt_and_current_state_substitutions_never_become_Work_success(string fault)
    {
        using var run = new Rig(replay: fault == "fresh-on-terminal"); var result = run.Owner.Result; var p = result.DecisionReceipt!;
        p = fault switch { "company" => p with { CompanyId = Guid.NewGuid() }, "invoice" => p with { InvoiceId = Guid.NewGuid() },
            "command" => p with { Command = FinanceOfficialSalesInvoiceServiceV1.RecordCommand }, "key" => p with { KeyHash = "wrong" },
            "hash" => p with { PayloadHash = "wrong" }, "actor" => p with { ActorId = Maker }, "expected" => p with { ExpectedVersion = "2" },
            "result-version" => p with { ResultVersion = "3" }, "correlation" => p with { OriginalCorrelationId = "other" },
            "time" => p with { RecordedAtUtc = At }, _ => p };
        var view = fault switch { "status" => result.Resource! with { Status = FinanceOfficialSalesInvoiceStatusV1.PendingApproval },
            "recorded-amount" => result.Resource! with { PayableAmount = "1" }, "recorded-pdf" => result.Resource! with { PdfDocumentVersionNo = 2 }, _ => result.Resource! };
        run.Owner.Result = result with { Resource = view, DecisionReceipt = fault == "missing" ? null : p };
        if (fault == "fresh-on-terminal") run.Owner.Result = run.Owner.Result with { IsReplay = false, OriginalCorrelationId = Correlation };
        Assert.Equal(503, (await Assert.ThrowsAsync<WorkDecisionException>(() => run.Host.DecideAsync(WorkDecisionHostV1.OfficialSalesInvoice, Id, new("1", true, null), Key, default))).Status);
    }

    [Theory]
    [InlineData(true, "note", "1")] [InlineData(false, null, "1")] [InlineData(false, " padded ", "1")]
    [InlineData(true, null, "2")] [InlineData(true, null, "01")] [InlineData(false, "reason", "1\n")]
    public async Task Only_native_canonical_version_and_decision_reason_are_accepted(bool approve, string? reason, string version)
    {
        using var run = new Rig();
        Assert.Equal(400, (await Assert.ThrowsAsync<WorkDecisionException>(() => run.Host.DecideAsync(WorkDecisionHostV1.OfficialSalesInvoice, Id, new(version, approve, reason), Key, default))).Status);
        Assert.Equal(0, run.Owner.Commands);
    }

    [Fact]
    public async Task Native_1000_character_rejection_is_preserved_without_a_new_500_character_policy()
    {
        using var run = new Rig(approve: false); var reason = new string('x', 1000); run.Owner.Result = Answer(false, false, reason);
        Assert.Equal("Rejected", (await run.Host.DecideAsync(WorkDecisionHostV1.OfficialSalesInvoice, Id, new("1", false, reason), Key, default)).State);
        Assert.Equal(reason, run.Owner.Reason);
        Assert.Equal(400, (await Assert.ThrowsAsync<WorkDecisionException>(() => run.Host.DecideAsync(WorkDecisionHostV1.OfficialSalesInvoice, Id, new("1", false, reason + "x"), "other", default))).Status);
        Assert.Equal(1, run.Owner.Commands);
    }

    [Theory]
    [InlineData("company")] [InlineData("amount")] [InlineData("pdf")] [InlineData("version")] [InlineData("source-difference")]
    public async Task Human_preview_requires_the_same_full_native_facts_after_the_second_fresh_owner_read(string fault)
    {
        using var run = new Rig(); run.Owner.HumanChange = v => fault switch
        {
            "company" => v with { CompanyId = Guid.NewGuid() }, "amount" => v with { PayableAmount = "999" },
            "pdf" => v with { PdfDocumentVersionNo = 2 }, "version" => v with { Version = "2" },
            _ => v with { SourceDifferences = [new(Guid.NewGuid(), Guid.NewGuid(), "SENTETIK", Guid.NewGuid().ToString("D"), ["amount"], null, "99", "approved-import")] },
        };
        Assert.Equal(503, (await Assert.ThrowsAsync<WorkDecisionException>(() => run.Host.PreviewAsync(WorkDecisionHostV1.OfficialSalesInvoice, Id, default))).Status);
    }

    [Theory]
    [InlineData("actual")] [InlineData("threshold")] [InlineData("unit")] [InlineData("deviation")]
    public async Task Human_preview_rejects_misleading_rule_facts_even_when_the_full_native_view_is_correct(string fault)
    {
        using var run = new Rig(); run.Source.Change = snapshot =>
        {
            var rule = snapshot.Item.Rules.Single();
            rule = fault switch { "actual" => rule with { Actual = "999" }, "threshold" => rule with { Threshold = "1" },
                "unit" => rule with { Unit = "kg" }, _ => rule with { Deviation = "1" } };
            return snapshot with { Item = snapshot.Item with { Rules = [rule] } };
        };
        Assert.Equal(503, (await Assert.ThrowsAsync<WorkDecisionException>(() => run.Host.PreviewAsync(WorkDecisionHostV1.OfficialSalesInvoice, Id, default))).Status);
        Assert.Equal(0, run.Owner.Commands);
    }

    [Fact]
    public async Task Dynamic_import_differences_do_not_replace_or_invalidate_the_original_recorded_facts_receipt()
    {
        using var run = new Rig(replay: true);
        run.Owner.Header = run.Owner.Header with { SourceDifferences = [new(Guid.NewGuid(), Guid.NewGuid(), "SENTETIK", Guid.NewGuid().ToString("D"), ["amount"], null, "99", "manual")] };
        var preview = await run.Host.PreviewAsync(WorkDecisionHostV1.OfficialSalesInvoice, Id, default);
        Assert.Single(preview.OfficialSalesInvoice!.SourceDifferences!);
        Assert.Null(run.Owner.Result.Resource!.SourceDifferences);
        Assert.True((await run.Host.DecideAsync(WorkDecisionHostV1.OfficialSalesInvoice, Id, new("1", true, null), Key, default)).Replayed);
    }

    [Fact]
    public async Task Raw_human_view_and_native_receipt_are_omitted_from_snapshot_and_other_kind_wire()
    {
        using var run = new Rig(); var snapshot = await run.Source.ReadDecisionAsync(new(Company, Actor, Correlation, null), Id, default);
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web); var wire = JsonSerializer.Serialize(snapshot, options);
        Assert.DoesNotContain("officialSalesInvoiceProof", wire); Assert.DoesNotContain("payableFunctionalAmount", wire); Assert.DoesNotContain("pdfSha256", wire);
        var preview = await run.Host.PreviewAsync(WorkDecisionHostV1.OfficialSalesInvoice, Id, default);
        Assert.Contains("payableFunctionalAmount", JsonSerializer.Serialize(preview, options));
        Assert.DoesNotContain("officialSalesInvoice", JsonSerializer.Serialize(new WorkDecisionPreviewV1(Company, WorkDecisionHostV1.BankAccount, Id, "SENTETIK", "1", true, false, [], []), options));
        Assert.DoesNotContain("decisionReceipt", JsonSerializer.Serialize(run.Owner.Result, FinanceJson.CreateOptions()));
        using var body = JsonDocument.Parse(JsonSerializer.Serialize(new WorkDecisionRequestV1("1", true, null), options));
        Assert.Equal(4, body.RootElement.EnumerateObject().Count());
    }

    [Fact]
    public async Task Maker_and_scoped_or_revoked_authority_and_delegation_remain_denied()
    {
        using var maker = new Rig(actor: Maker);
        Assert.Equal(403, (await Assert.ThrowsAsync<WorkDecisionException>(() => maker.Host.PreviewAsync(WorkDecisionHostV1.OfficialSalesInvoice, Id, default))).Status);
        using var run = new Rig(); run.Authority.CompanyWide = false;
        Assert.Equal(403, (await Assert.ThrowsAsync<WorkDecisionException>(() => run.Host.PreviewAsync(WorkDecisionHostV1.OfficialSalesInvoice, Id, default))).Status);
        run.Authority.CompanyWide = true; run.Delegations.Live = false;
        Assert.Equal(403, (await Assert.ThrowsAsync<WorkDecisionException>(() => run.Host.DecideAsync(WorkDecisionHostV1.OfficialSalesInvoice, Id, new("1", true, null, Guid.NewGuid()), Key, default))).Status);
        Assert.Equal(0, run.Owner.Commands); run.Delegations.Live = true; run.Owner.BeforeReturn = () => run.Authority.Allowed = false;
        Assert.Equal(403, (await Assert.ThrowsAsync<WorkDecisionException>(() => run.Host.DecideAsync(WorkDecisionHostV1.OfficialSalesInvoice, Id, new("1", true, null), Key, default))).Status);
    }

    [Theory]
    [InlineData("missing")] [InlineData("company")] [InlineData("recorded-digest")] [InlineData("preview-digest")]
    [InlineData("maker")] [InlineData("status")]
    public async Task Scalar_source_proof_is_required_and_bound_before_native_execution(string fault)
    {
        using var run = new Rig(); run.Source.Change = s =>
        {
            var p = s.OfficialSalesInvoiceProof!;
            p = fault switch { "company" => p with { CompanyId = Guid.NewGuid() }, "recorded-digest" => p with { RecordedFactsDigest = "wrong" },
                "preview-digest" => p with { PreviewDigest = "wrong" }, "maker" => p with { RecordedBy = "other" }, "status" => p with { Status = "Approved" }, _ => p };
            return s with { OfficialSalesInvoiceProof = fault == "missing" ? null : p };
        };
        Assert.Equal(503, (await Assert.ThrowsAsync<WorkDecisionException>(() => run.Host.DecideAsync(WorkDecisionHostV1.OfficialSalesInvoice, Id, new("1", true, null), Key, default))).Status);
        Assert.Equal(0, run.Owner.Commands);
    }

    [Theory]
    [InlineData("loss")] [InlineData("overage")] [InlineData("manufacturing")]
    public async Task Other_kinds_typed_payload_is_not_accepted_by_the_native_invoice_decision(string extra)
    {
        using var run = new Rig(); var request = new WorkDecisionRequestV1("1", true, null);
        request = extra switch { "loss" => request with { InventoryTransferLoss = new(1, At) },
            "overage" => request with { InventoryTransferOverage = new(1, At) },
            _ => request with { ManufacturingPlanReapproval = new([Guid.NewGuid()]) } };
        Assert.Equal(400, (await Assert.ThrowsAsync<WorkDecisionException>(() => run.Host.DecideAsync(WorkDecisionHostV1.OfficialSalesInvoice, Id, request, Key, default))).Status);
        Assert.Equal(0, run.Owner.Commands);
    }

    [Theory]
    [InlineData(CommandOutcome.Denied, 403)] [InlineData(CommandOutcome.Unavailable, 503)] [InlineData(CommandOutcome.VersionConflict, 409)] [InlineData(CommandOutcome.ValidationFailed, 422)]
    public async Task Native_refusal_is_preserved_with_original_correlation_and_no_fake_success(CommandOutcome outcome, int status)
    {
        using var run = new Rig(); run.Owner.Result = new(outcome, null, true, "SENTETIK-original-refusal", [new("request", "SENTETIK-code")], null);
        var refused = await Assert.ThrowsAsync<WorkDecisionException>(() => run.Host.DecideAsync(WorkDecisionHostV1.OfficialSalesInvoice, Id, new("1", true, null), Key, default));
        Assert.Equal(status, refused.Status); Assert.Equal("SENTETIK-code", refused.Code); Assert.True(refused.Replayed); Assert.Equal("SENTETIK-original-refusal", refused.OriginalCorrelationId);
    }

    private sealed class Rig : IDisposable
    {
        public readonly Owner Owner; public readonly SourceProxy Source; public readonly Authority Authority = new(); public readonly Delegations Delegations = new();
        public WorkDecisionHostV1 Host { get; } private readonly FinanceDbContext db;
        public Rig(bool approve = true, bool replay = false, string actor = Actor)
        {
            Owner = new() { Result = Answer(approve, replay) }; Owner.Header = replay ? Owner.Result.Resource! : View();
            var company = new CompanyContext(); company.Set(Company); var user = new WorkInventoryTransferLossDecisionHostTests.LossUser(Company, actor);
            db = new(new DbContextOptionsBuilder<FinanceDbContext>().UseInMemoryDatabase("official-work-" + Guid.NewGuid()).Options, company);
            Source = new(new FinanceOfficialSalesInvoiceWorkInboxSourceV1(db, new(company, user, Authority), Owner));
            var correlation = new CorrelationContext(); correlation.Set(Correlation, null, "Web", null, null); var fence = new WorkDecisionExecutionFenceV1();
            Host = new(company, user, correlation, new WorkDecisionAuthorityV1(Authority, fence), [Source], Delegations, fence, null!, null!, officialSalesInvoices: Owner);
        }
        public void Dispose() => db.Dispose();
    }
    private sealed class SourceProxy(IWorkApprovalDecisionSourceV1 inner) : IWorkApprovalDecisionSourceV1
    {
        public Func<WorkApprovalDecisionSnapshotV1, WorkApprovalDecisionSnapshotV1>? Change;
        public string Module => inner.Module; public string Kind => inner.Kind; public string ApproverPermission => inner.ApproverPermission;
        public Task<WorkInboxSourceResultV1> ListPendingAsync(WorkInboxSourceContextV1 context, int count, CancellationToken token) => inner.ListPendingAsync(context, count, token);
        public async Task<WorkApprovalDecisionSnapshotV1?> ReadDecisionAsync(WorkInboxSourceContextV1 context, Guid id, CancellationToken token)
        { var s = await inner.ReadDecisionAsync(context, id, token); return s is null || Change is null ? s : Change(s); }
    }
    private sealed class Owner : IFinanceOfficialSalesInvoiceServiceV1, IFinanceOfficialSalesInvoiceDecisionReadV1
    {
        public FinanceOfficialSalesInvoiceViewV1 Header = View(); public FinanceOfficialSalesInvoiceResultV1 Result = null!;
        public Func<FinanceOfficialSalesInvoiceViewV1, FinanceOfficialSalesInvoiceViewV1>? HumanChange; public Action? BeforeReturn;
        public Guid LastId; public string? LastVersion, Key, Reason; public bool Approve; public int Reads, Commands;
        public async Task<ReadResult<FinanceOfficialSalesInvoiceViewV1>> ReadDecisionAsync(FinanceContext c, Guid id, Func<CancellationToken, Task> fence, CancellationToken t)
        { await fence(t); Reads++; var v = Reads > 1 && HumanChange is not null ? HumanChange(Header) : Header; await fence(t); return new(ReadOutcome.Found, v); }
        private Task<FinanceOfficialSalesInvoiceResultV1> Decide(Guid id, string version, bool approve, string? reason, CommandMetadata m)
        { LastId = id; LastVersion = version; Key = m.IdempotencyKey; Approve = approve; Reason = reason; Commands++; BeforeReturn?.Invoke(); return Task.FromResult(Result); }
        public Task<FinanceOfficialSalesInvoiceResultV1> ApproveAsync(FinanceContext c, CommandMetadata m, FinanceOfficialSalesInvoiceApproveRequestV1 r, CancellationToken t) => Decide(r.InvoiceId, r.ExpectedVersion, true, null, m);
        public Task<FinanceOfficialSalesInvoiceResultV1> RejectAsync(FinanceContext c, CommandMetadata m, FinanceOfficialSalesInvoiceRejectRequestV1 r, CancellationToken t) => Decide(r.InvoiceId, r.ExpectedVersion, false, r.Reason, m);
        public Task<FinanceOfficialSalesInvoiceReservationResultV1> ReserveAsync(FinanceContext c, CommandMetadata m, FinanceOfficialSalesInvoiceReservationRequestV1 r, CancellationToken t) => throw new NotSupportedException();
        public Task<FinanceOfficialSalesInvoiceResultV1> RecordAsync(FinanceContext c, CommandMetadata m, FinanceOfficialSalesInvoiceRecordRequestV1 r, CancellationToken t) => throw new NotSupportedException();
        public Task<FinanceOfficialSalesInvoiceResultV1> WithdrawAsync(FinanceContext c, CommandMetadata m, FinanceOfficialSalesInvoiceWithdrawRequestV1 r, CancellationToken t) => throw new NotSupportedException();
        public Task<ReadResult<FinanceOfficialSalesInvoiceViewV1>> GetAsync(FinanceContext c, Guid id, CancellationToken t) => throw new NotSupportedException();
        public Task<ReadResult<FinanceOfficialSalesInvoicePageV1>> ListAsync(FinanceContext c, bool pending, string? cursor, CancellationToken t) => throw new NotSupportedException();
    }
    private sealed class Delegations : IWorkDelegationReaderV1
    {
        public bool Live = true;
        public Task<WorkDelegationViewV1?> FindForActorAsync(Guid id, WorkInboxItemV1 item, CancellationToken t = default) => Task.FromResult<WorkDelegationViewV1?>(Live
            ? new(id, Company, item.Kind, FinanceOfficialSalesInvoicePermissionNamesV1.Approve, Guid.NewGuid(), Guid.NewGuid(), At, DateTimeOffset.UtcNow.AddDays(1), "1", Guid.NewGuid(), At, "SENTETIK", null, null, null) : null);
    }
    private sealed class Authority : IErpAuthorityRevalidationService
    {
        public bool Allowed = true, CompanyWide = true;
        public Task<PermissionScope> RevalidatePermissionScopeAsync(string permission, Guid company, CancellationToken t = default)
        { Assert.Equal(FinanceOfficialSalesInvoicePermissionNamesV1.Approve, permission); return Task.FromResult(new PermissionScope(Allowed, CompanyWide, new HashSet<string>(), new HashSet<Guid>(), new HashSet<Guid>(), new HashSet<Guid>())); }
    }
}
