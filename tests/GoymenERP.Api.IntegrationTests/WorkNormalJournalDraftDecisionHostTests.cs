using System.Globalization;
using System.Text.Json;
using GoymenERP.Api.WorkRuntime;
using GoymenERP.BuildingBlocks.Multitenancy;
using GoymenERP.BuildingBlocks.Observability;
using GoymenERP.BuildingBlocks.Security;
using GoymenERP.BuildingBlocks.Work;
using GoymenERP.Modules.Accounting.Application;
using GoymenERP.Modules.Accounting.Domain.Journals;
using GoymenERP.Modules.Accounting.Domain.Posting;
using GoymenERP.Modules.Accounting.Infrastructure.Persistence;
using GoymenERP.Modules.Accounting.Infrastructure.WorkInbox;
using GoymenERP.Modules.Work.Infrastructure.Delegations;
using Microsoft.EntityFrameworkCore;

namespace GoymenERP.Api.IntegrationTests;

// Actual native lifecycle over an explicit synthetic protocol port. No relational commit/rollback acceptance claim.
public sealed class WorkNormalJournalDraftDecisionHostTests
{
    private static readonly Guid Company = Guid.Parse("ac490000-0000-4000-8000-000000000001"), Id = Guid.Parse("ac490000-0000-4000-8000-000000000002"),
        Actor = Guid.Parse("ac490000-0000-4000-8000-000000000003"), Maker = Guid.Parse("ac490000-0000-4000-8000-000000000004"), Period = Guid.Parse("ac490000-0000-4000-8000-000000000005");
    private static readonly DateTimeOffset At = new(2026, 11, 27, 9, 0, 0, TimeSpan.Zero);
    private const string Key = "normal-work-original", Correlation = "SENTETIK-normal-work";

    [Theory] [InlineData(1L, false)] [InlineData(7L, false)] [InlineData(1L, true)]
    public async Task Human_full_native_view_and_explicit_period_native_approval_keep_version_protocol_and_original_key(long version, bool credit)
    {
        using var rig = new Rig(version, credit); var preview = await rig.Preview();
        Assert.False(preview.RejectionSupported); Assert.False(preview.ApprovalReasonRequired);
        var detail = Assert.IsType<NormalJournalDraftDecisionDetailV1>(preview.NormalJournalDraft);
        Assert.Equal(version, detail.Draft.Version); Assert.Null(detail.Draft.PeriodId); Assert.Equal(2, detail.Draft.Lines.Count);
        Assert.Equal("10", detail.Draft.Lines[0].Amount); Assert.Equal(20, JsonSerializer.SerializeToElement(detail.Draft).EnumerateObject().Count());
        rig.Port.Reads = 0;
        var first = await rig.Decide(); Assert.Equal("Approved", first.State); Assert.False(first.Replayed);
        Assert.Equal(Period, rig.Port.SelectedPeriod); Assert.Equal(version, rig.Port.Expected); Assert.Equal(Key, rig.Port.Key);
        Assert.Equal(2, rig.Port.Reads); // One source read and the genuine receipt's retained-source read; no second human POST GET.
        Assert.Equal(1, rig.Port.Approvals); Assert.Single(rig.Port.Commands);
        var replay = await rig.Decide(); Assert.True(replay.Replayed); Assert.Equal(Correlation, replay.OriginalCorrelationId);
        Assert.Equal(version + 1, rig.Port.Record.Version); Assert.Equal(1, rig.Port.Approvals); Assert.Single(rig.Port.Commands);
    }

    [Theory] [InlineData(false, null, "1")][InlineData(true, "", "1")][InlineData(true, "note", "1")]
    [InlineData(true, null, "0")][InlineData(true, null, "01")][InlineData(true, null, "1\n")]
    [InlineData(true, null, "+1")][InlineData(true, null, "9223372036854775808")]
    public async Task Only_approve_null_reason_and_exact_positive_native_long_can_enter_owner(bool approve, string? reason, string version)
    {
        using var rig = new Rig(); var request = rig.Request with { Approve = approve, Reason = reason, ExpectedVersion = version };
        Assert.Equal(400, (await Assert.ThrowsAsync<WorkDecisionException>(() => rig.Decide(request))).Status); Assert.Equal(0, rig.Port.Approvals);
    }

    [Theory] [InlineData("missing")][InlineData("empty")][InlineData("loss")][InlineData("overage")][InlineData("manufacturing")][InlineData("package")][InlineData("customer-return")][InlineData("plan-excess")][InlineData("supplier-return")]
    public async Task Period_is_required_and_other_kind_selections_cannot_be_combined(string fault)
    {
        using var rig = new Rig(); var request = rig.Request;
        request = fault switch { "missing" => request with { NormalJournalDraft = null }, "empty" => request with { NormalJournalDraft = new(Guid.Empty) },
            "loss" => request with { InventoryTransferLoss = new(1, At) }, "overage" => request with { InventoryTransferOverage = new(1, At) },
            "manufacturing" => request with { ManufacturingPlanReapproval = new([Guid.NewGuid()]) }, "customer-return" => request with { LogisticsCustomerReturn = new(Guid.NewGuid()) },
            "plan-excess" => request with { LogisticsPlanExcess = new("1") }, "supplier-return" => request with { SupplierReturnCostException = new() }, _ => request with { ProductPackageStandard = new("2026-11-27") } };
        Assert.Equal(400, (await Assert.ThrowsAsync<WorkDecisionException>(() => rig.Decide(request))).Status); Assert.Equal(0, rig.Port.Approvals);
    }

    [Theory] [InlineData("missing")][InlineData("company")][InlineData("id")][InlineData("maker")][InlineData("version")]
    [InlineData("source")][InlineData("preview")][InlineData("threshold")][InlineData("unit")][InlineData("reference")]
    public async Task Scalar_proof_and_exact_source_rule_reference_binding_are_required(string fault)
    {
        using var rig = new Rig(); rig.Source.Change = snapshot =>
        {
            var p = snapshot.NormalJournalDraftProof!;
            return snapshot with { NormalJournalDraftProof = fault switch { "missing" => null, "company" => p with { CompanyId = Guid.NewGuid() },
                "id" => p with { DraftId = Guid.NewGuid() }, "maker" => p with { PreparedBy = Actor }, "version" => p with { Version = 7 },
                "source" => p with { SourceFactsDigest = new string('B', 64) }, "preview" => p with { PreviewDigest = "wrong" }, _ => p },
                Item = snapshot.Item with { Rules = fault is "threshold" or "unit" ? [snapshot.Item.Rules[0] with
                    { Threshold = fault == "threshold" ? "1" : null, Unit = fault == "unit" ? "kg" : null }] : snapshot.Item.Rules,
                    References = fault == "reference" ? snapshot.Item.References.Select((x, i) => i == 0 ? x with { RecordId = Guid.NewGuid() } : x).ToArray() : snapshot.Item.References } };
        };
        Assert.Equal(503, (await Assert.ThrowsAsync<WorkDecisionException>(() => rig.Decide())).Status);
    }

    [Theory] [InlineData("company")][InlineData("amount")][InlineData("source")][InlineData("version")][InlineData("membership")]
    public async Task Human_GET_second_read_must_match_full_native_source_and_owner_membership(string fault)
    {
        using var rig = new Rig(); rig.Port.ReadChange = (record, number) => number != 2 ? record : fault switch
        {
            "company" => record with { CompanyId = Guid.NewGuid() }, "version" => record with { Version = 7 },
            "source" => record with { AccountPlanSourceVersion = "different" },
            "amount" => record with { Request = record.Request with { Lines = record.Request.Lines.Select(x => x with { Amount = 11m }).ToArray() } }, _ => record,
        };
        if (fault == "membership") rig.Port.Membership = number => number == 2;
        Assert.Equal(503, (await Assert.ThrowsAsync<WorkDecisionException>(() => rig.Preview())).Status); Assert.Equal(0, rig.Port.Approvals);
    }

    [Theory] [InlineData("missing")][InlineData("key")][InlineData("payload")][InlineData("actor")][InlineData("period")]
    [InlineData("journal")][InlineData("version")][InlineData("correlation")][InlineData("digest")][InlineData("membership")]
    public async Task Work_success_requires_exact_original_native_receipt_without_current_or_snapshot_substitution(string fault)
    {
        using var rig = new Rig(); rig.Port.AfterAnswer = answer =>
        {
            var view = answer.Resource!; var p = view.ApprovalReceipt!;
            p = fault switch { "key" => p with { IdempotencyKeyHash = "wrong" }, "payload" => p with { PayloadHash = "wrong" },
                "actor" => p with { ActorId = Maker }, "period" => p with { PeriodId = Guid.NewGuid() }, "journal" => p with { PostedJournalId = Guid.NewGuid() },
                "version" => p with { ResultVersion = 7 }, "correlation" => p with { OriginalCorrelationId = "other" },
                "digest" => p with { OriginalResultDigest = new string('B', 64) }, "membership" => p with { IsCreditSource = true }, _ => p };
            return answer with { Resource = view with { ApprovalReceipt = fault == "missing" ? null : p } };
        };
        Assert.Equal(503, (await Assert.ThrowsAsync<WorkDecisionException>(() => rig.Decide())).Status);
    }

    [Theory] [InlineData("unknown-root")][InlineData("private-proof")]
    public async Task Bare_legacy_native_replay_stays_native_but_malformed_provenance_never_becomes_Work_success(string fault)
    {
        using var rig = new Rig(); await rig.Decide(); var command = Assert.Single(rig.Port.Commands);
        var json = System.Text.Json.Nodes.JsonNode.Parse(command.ResultJson)!;
        if (fault == "unknown-root") json["unexpected"] = true; else json["resource"]!["approvalReceipt"] = true;
        rig.Port.Commands[0] = command with { ResultJson = json.ToJsonString() };
        Assert.Equal(503, (await Assert.ThrowsAsync<WorkDecisionException>(() => rig.Decide())).Status);
        Assert.Equal(1, rig.Port.Approvals); Assert.Single(rig.Port.Commands);
    }

    [Theory] [InlineData(AccountingApiStatus.Denied, 403)][InlineData(AccountingApiStatus.Unavailable, 503)]
    [InlineData(AccountingApiStatus.Conflict, 409)][InlineData(AccountingApiStatus.NotFound, 404)][InlineData(AccountingApiStatus.Unprocessable, 422)]
    public async Task Native_terminal_refusal_code_and_original_correlation_are_not_replaced(AccountingApiStatus status, int expected)
    {
        using var rig = new Rig(); rig.Port.Refusal = new(status, null, true, "native-code", OriginalCorrelationId: "native-original");
        var failure = await Assert.ThrowsAsync<WorkDecisionException>(() => rig.Decide());
        Assert.Equal(expected, failure.Status); Assert.Equal("native-code", failure.Code); Assert.True(failure.Replayed); Assert.Equal("native-original", failure.OriginalCorrelationId);
        Assert.Equal(0, rig.Port.Approvals);
    }

    [Theory] [InlineData("maker")][InlineData("permission")][InlineData("company")][InlineData("delegation")]
    public async Task Maker_current_authority_company_and_revoked_delegation_do_not_reach_native_effects(string fault)
    {
        using var rig = new Rig(); Guid? delegation = null;
        if (fault == "maker") rig.User.ActorId = Maker;
        if (fault == "permission") rig.Authority.Allowed = false;
        if (fault == "company") rig.Company.Set(Guid.NewGuid());
        if (fault == "delegation") { delegation = Guid.NewGuid(); rig.Delegations.Live = false; }
        Assert.Equal(403, (await Assert.ThrowsAsync<WorkDecisionException>(() => rig.Decide(rig.Request with { DelegationId = delegation }))).Status);
        Assert.Equal(0, rig.Port.Approvals);
    }

    [Fact]
    public async Task Live_delegation_is_revalidated_by_native_fences_and_revocation_during_command_prevents_protocol_effect()
    {
        using var live = new Rig(); Assert.Equal("Approved", (await live.Decide(live.Request with { DelegationId = Guid.NewGuid() })).State);
        using var revoked = new Rig(); revoked.Port.BeforeApprove = () => revoked.Delegations.Live = false;
        Assert.Equal(403, (await Assert.ThrowsAsync<WorkDecisionException>(() => revoked.Decide(revoked.Request with { DelegationId = Guid.NewGuid() }))).Status);
        Assert.Equal(0, revoked.Port.Approvals);
    }

    [Theory] [InlineData("permission")][InlineData("company")][InlineData("actor")]
    public async Task Native_fresh_fence_rechecks_current_authority_after_owner_reads_before_protocol_mutation(string fault)
    {
        using var rig = new Rig(); rig.Port.BeforeApprove = () =>
        {
            if (fault == "permission") rig.Authority.Allowed = false;
            if (fault == "company") rig.Company.Set(Guid.NewGuid());
            if (fault == "actor") rig.User.ActorId = Guid.NewGuid();
        };
        Assert.Equal(403, (await Assert.ThrowsAsync<WorkDecisionException>(() => rig.Decide())).Status);
        Assert.Equal(0, rig.Port.Approvals); Assert.Empty(rig.Port.Commands);
    }

    [Fact]
    public async Task Cancellation_during_owner_read_never_releases_a_human_preview()
    {
        using var rig = new Rig(); using var cancellation = new CancellationTokenSource();
        rig.Port.ReadChange = (record, _) => { cancellation.Cancel(); return record; };
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => rig.Host.PreviewAsync(WorkDecisionHostV1.NormalJournalDraft, Id, cancellation.Token));
        Assert.Equal(0, rig.Port.Approvals); Assert.Empty(rig.Port.Commands);
    }

    [Fact]
    public async Task Typed_period_body_and_human_only_view_do_not_serialize_private_provenance_or_change_legacy_request_shape()
    {
        using var rig = new Rig(); var json = new JsonSerializerOptions(JsonSerializerDefaults.Web);
        var preview = await rig.Preview(); var snapshot = await rig.Source.ReadDecisionAsync(new(Company, Actor.ToString("D"), Correlation, null), Id, default);
        Assert.Contains("normalJournalDraft", JsonSerializer.Serialize(preview, json));
        Assert.DoesNotContain("isCreditSource", JsonSerializer.Serialize(preview, json), StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("normalJournalDraftProof", JsonSerializer.Serialize(snapshot, json), StringComparison.OrdinalIgnoreCase);
        using var body = JsonDocument.Parse(JsonSerializer.Serialize(rig.Request, json));
        Assert.Equal(5, body.RootElement.EnumerateObject().Count()); Assert.Equal(Period, body.RootElement.GetProperty("normalJournalDraft").GetProperty("periodId").GetGuid());
        Assert.Single(body.RootElement.GetProperty("normalJournalDraft").EnumerateObject());
        Assert.DoesNotContain("lines", body.RootElement.GetRawText(), StringComparison.OrdinalIgnoreCase);
        using var legacy = JsonDocument.Parse(JsonSerializer.Serialize(new WorkDecisionRequestV1("1", true, null), json));
        Assert.Equal(4, legacy.RootElement.EnumerateObject().Count()); Assert.False(legacy.RootElement.TryGetProperty("normalJournalDraft", out _));
    }

    private sealed class Rig : IDisposable
    {
        public MutableCompany Company { get; } = new(); public MutableUser User { get; } = new(); public Authority Authority { get; } = new();
        public Delegations Delegations { get; } = new(); public ProtocolPort Port { get; } public SourceProxy Source { get; } public WorkDecisionHostV1 Host { get; }
        private readonly AccountingDbContext db; private readonly long version;
        public Rig(long version = 1, bool credit = false)
        {
            this.version = version; Company.Set(WorkNormalJournalDraftDecisionHostTests.Company); var correlation = new CorrelationContext(); correlation.Set(Correlation, null, "Web", null, null);
            var execution = new WorkDecisionExecutionFenceV1(); var authority = new WorkDecisionAuthorityV1(Authority, execution);
            Port = new(version, credit); var binding = new NormalJournalPermissionBindingV1();
            var owner = new NormalJournalDraftServiceV1(Port, Port, new(binding, authority, Company, User), binding, authority, Company, User, correlation);
            db = new(new DbContextOptionsBuilder<AccountingDbContext>().UseInMemoryDatabase("normal-work-" + Guid.NewGuid()).Options, Company);
            Source = new(new AccountingNormalJournalDraftWorkInboxSourceV1(db, new(Company, User, authority), owner));
            Host = new(Company, User, correlation, authority, [Source], Delegations, execution, null!, null!, normalJournalDrafts: owner);
        }
        public WorkDecisionRequestV1 Request => new(version.ToString(CultureInfo.InvariantCulture), true, null, NormalJournalDraft: new(Period));
        public Task<WorkDecisionPreviewV1> Preview() => Host.PreviewAsync(WorkDecisionHostV1.NormalJournalDraft, Id, default);
        public Task<WorkDecisionResultV1> Decide(WorkDecisionRequestV1? request = null) => Host.DecideAsync(WorkDecisionHostV1.NormalJournalDraft, Id, request ?? Request, Key, default);
        public void Dispose() => db.Dispose();
    }

    private sealed class ProtocolPort : IAccountingNormalJournalDraftStore, IAccountingNormalJournalDraftDecisionStoreV1, IAccountingCommandLog
    {
        public NormalJournalDraftRecordV1 Record; public List<AccountingCommandRecord> Commands { get; } = []; public int Approvals, Reads;
        public Guid SelectedPeriod; public long Expected; public string? Key; public Action? BeforeApprove;
        public Func<NormalJournalDraftRecordV1, int, NormalJournalDraftRecordV1>? ReadChange; public Func<int, bool>? Membership;
        public Func<AccountingApiResult<NormalJournalDraftViewV1>, AccountingApiResult<NormalJournalDraftViewV1>>? AfterAnswer;
        public AccountingApiResult<NormalJournalDraftViewV1>? Refusal;
        public ProtocolPort(long version, bool credit)
        {
            var request = new PostingRequest("SENTETIK", "SENTETIK-message", Id, new(Company, Guid.NewGuid(), "SENTETIK-purpose"), "rule-1", Guid.NewGuid(),
                new(new(2026, 11, 27), new(2026, 11, 27), null, At), [new("debit", new AccountRef(Company, Guid.NewGuid()), PostingSide.Debit, 10m), new("credit", new AccountRef(Company, Guid.NewGuid()), PostingSide.Credit, 10m)]);
            Record = new(Id, Company, version, NormalJournalDraftStatusV1.Draft, Maker, At, null, null, null, null,
                "plan-1", new string('A', 64), Maker, At, Maker, At, request); Membership = _ => credit;
        }
        public async Task<AccountingApiResult<NormalJournalDraftViewV1>> RunNormalApprovalAsync(Func<bool, Task<AccountingApiResult<NormalJournalDraftViewV1>>> attempt, Func<CancellationToken, Task> fence, CancellationToken token = default)
        { if (Refusal is not null) return Refusal; var answer = await attempt(false); return AfterAnswer?.Invoke(answer) ?? answer; }
        public async Task<NormalJournalDraftDecisionSourceV1?> ReadNormalDraftDecisionAsync(Guid id, Func<CancellationToken, Task> fence, CancellationToken token = default)
        { await fence(token); Reads++; var record = ReadChange?.Invoke(Record, Reads) ?? Record; await fence(token); return id == Record.Id ? new(record, Membership?.Invoke(Reads) ?? false) : null; }
        public Task<NormalJournalDraftRecordV1?> FindNormalDraftAsync(Guid id, CancellationToken token = default) => Task.FromResult<NormalJournalDraftRecordV1?>(id == Record.Id ? Record : null);
        public async Task<NormalJournalDraftRecordV1> ApproveAndPostNormalDraftAsync(Guid id, long expected, Guid period, Guid actor, Func<CancellationToken, Task> fence, AccountingCommandMetadata metadata, CancellationToken token = default)
        {
            BeforeApprove?.Invoke(); await fence(token); Expected = expected; SelectedPeriod = period; Key = metadata.IdempotencyKey; Approvals++;
            Record = Record with { Version = expected + 1, Status = NormalJournalDraftStatusV1.Approved, ApprovedByUserId = actor, ApprovedAtUtc = At.AddMinutes(1), PeriodId = period, PostedJournalId = id };
            Commands.Add(metadata.Completion!(Record)); return Record;
        }
        public Task<AccountingCommandRecord?> FindCommandAsync(string command, string key, CancellationToken token = default) => Task.FromResult(Commands.SingleOrDefault(x => x.CommandName == command && x.IdempotencyKeyHash == key));
        public Task RecordCommandAsync(AccountingCommandRecord command, CancellationToken token = default) { Commands.Add(command); return Task.CompletedTask; }
        public Task<(NormalJournalDraftRecordV1 Record, AccountingWriteOutcome Outcome)> CreateNormalDraftAsync(NormalJournalDraftV1 draft, Func<CancellationToken, Task> fence, AccountingCommandMetadata metadata, CancellationToken token = default) => throw new InvalidOperationException("No create or period discovery in a Work decision");
    }
    private sealed class SourceProxy(IWorkApprovalDecisionSourceV1 inner) : IWorkApprovalDecisionSourceV1
    {
        public Func<WorkApprovalDecisionSnapshotV1, WorkApprovalDecisionSnapshotV1>? Change;
        public string Module => inner.Module; public string Kind => inner.Kind; public string ApproverPermission => inner.ApproverPermission;
        public Task<WorkInboxSourceResultV1> ListPendingAsync(WorkInboxSourceContextV1 context, int count, CancellationToken token) => inner.ListPendingAsync(context, count, token);
        public async Task<WorkApprovalDecisionSnapshotV1?> ReadDecisionAsync(WorkInboxSourceContextV1 context, Guid id, CancellationToken token)
        { var snapshot = await inner.ReadDecisionAsync(context, id, token); return snapshot is null ? null : Change?.Invoke(snapshot) ?? snapshot; }
    }
    private sealed class MutableCompany : ICompanyContext
    { public bool IsResolved => CompanyId != Guid.Empty; public Guid CompanyId { get; private set; } public void Set(Guid id) => CompanyId = id; }
    private sealed class MutableUser : ICurrentUser
    { public Guid ActorId = Actor; public bool IsAuthenticated => true; public string UserId => ActorId.ToString("D"); public IReadOnlySet<Guid> AllowedCompanyIds { get; } = new HashSet<Guid> { Company }; public bool CanAccessCompany(Guid id) => AllowedCompanyIds.Contains(id); }
    private sealed class Authority : IErpAuthorityRevalidationService
    { public bool Allowed = true; public Task<PermissionScope> RevalidatePermissionScopeAsync(string permission, Guid company, CancellationToken token = default)
      { Assert.Equal(NormalJournalPermissionNamesV1.DraftApprove, permission); return Task.FromResult(new PermissionScope(Allowed, true, new HashSet<string>(), new HashSet<Guid>(), new HashSet<Guid>(), new HashSet<Guid>())); } }
    private sealed class Delegations : IWorkDelegationReaderV1
    {
        public bool Live = true;
        public Task<WorkDelegationViewV1?> FindForActorAsync(Guid id, WorkInboxItemV1 item, CancellationToken token = default) => Task.FromResult<WorkDelegationViewV1?>(Live
            ? new(id, Company, item.Kind, NormalJournalPermissionNamesV1.DraftApprove, Guid.NewGuid(), Guid.NewGuid(), At, DateTimeOffset.UtcNow.AddDays(1), "1", Guid.NewGuid(), At, "SENTETIK", null, null, null) : null);
    }
}
