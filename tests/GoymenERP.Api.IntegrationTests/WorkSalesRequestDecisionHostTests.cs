using System.Globalization;
using GoymenERP.Api.WorkRuntime;
using GoymenERP.BuildingBlocks.Multitenancy;
using GoymenERP.BuildingBlocks.Observability;
using GoymenERP.BuildingBlocks.Security;
using GoymenERP.BuildingBlocks.Work;
using GoymenERP.Modules.Sales.Application.Contracts;
using GoymenERP.Modules.Sales.Infrastructure.Persistence;
using GoymenERP.Modules.Sales.Infrastructure.WorkInbox;
using GoymenERP.Modules.Work.Infrastructure.Delegations;
using static GoymenERP.Api.IntegrationTests.WorkSalesRequestDecisionPreviewV1Tests;

namespace GoymenERP.Api.IntegrationTests;

// Real Work host/source/decorated authority over an explicit SYNTHETIC native command protocol.
// Effects/Commands below are counters, not SQL, Audit, transaction or rollback acceptance.
public sealed class WorkSalesRequestDecisionHostTests
{
    private const string Kind = "sales.request.v1", Key = "SENTETIK-request-key", Correlation = "SENTETIK-request-work-correlation";
    private const string Reason = "  SENTETIK ret  ";

    [Theory] [InlineData(true)] [InlineData(false)]
    public async Task Human_uses_two_selected_reads_and_POST_only_the_source_read_then_one_frozen_native_command(bool approve)
    {
        var r = new Rig(); var preview = await r.Preview(); var human = Assert.IsType<WorkSalesRequestDecisionPreviewV1>(preview.SalesRequest);
        Assert.True(preview.Pending); Assert.True(preview.ApprovalSupported); Assert.True(preview.RejectionSupported); Assert.False(preview.ApprovalReasonRequired);
        Assert.Collection(human.Detail.Lines, line => Assert.Equal(1, line.LineNo), line => Assert.Equal(2, line.LineNo)); Assert.Equal(2, r.Native.Reads);
        var q = Request(approve); var reads = r.Native.Reads; var result = await r.Decide(q);
        Assert.Equal(approve ? "Approved" : "Rejected", result.State); Assert.False(result.Replayed); Assert.Equal(Correlation, result.OriginalCorrelationId);
        Assert.Equal(reads + 1, r.Native.Reads); Assert.Equal(1, r.Native.Calls); Assert.Equal(1, r.Native.Effects); Assert.Single(r.Native.Commands);
        Assert.Equal(new SalesDraftStateCommandRequest(q.ExpectedVersion, q.Reason), r.Native.LastBody); Assert.Equal(Key, r.Native.LastKey);
        Assert.Equal((Correlation, "SENTETIK-cause"), (r.Native.LastMetadata!.CorrelationId, r.Native.LastMetadata.CausationId));
        Assert.All(r.Authority.Permissions, permission => Assert.Equal(SalesPermissionManifestV1.RequestsApprove, permission));
        Assert.Equal(4, System.Text.Json.JsonSerializer.SerializeToElement(q, Json).EnumerateObject().Count());
    }

    [Fact]
    public async Task Approved_is_not_pending_but_can_be_rejected_and_original_approve_replay_keeps_its_own_decision()
    {
        var r = new Rig(); var original = await r.Decide(Request(true)); var approved = await r.Preview();
        Assert.False(approved.Pending); Assert.False(approved.ApprovalSupported); Assert.True(approved.RejectionSupported);
        Assert.Equal("Rejected", (await r.Decide(new("2", false, Reason), "SENTETIK-reject-key")).State);
        var terminal = await r.Preview(); Assert.False(terminal.Pending); Assert.False(terminal.ApprovalSupported); Assert.False(terminal.RejectionSupported);
        r.SetCorrelation("SENTETIK-later-same-key"); var replay = await r.Decide(Request(true));
        Assert.Equal(("Approved", true, original.OriginalCorrelationId), (replay.State, replay.Replayed, replay.OriginalCorrelationId));
        Assert.Equal(("Rejected", "3"), (r.Native.Current.Request.State, r.Native.Current.Request.Version));
        Assert.Equal(2, r.Native.Effects);
        Assert.Collection(r.Native.Commands.Values, saved => Assert.Equal("Approved", saved.Receipt.State), saved => Assert.Equal("Rejected", saved.Receipt.State));
    }

    [Fact]
    public async Task SAT_D22_authorized_preparer_keeps_the_native_self_approval_exception_without_a_price_permission()
    {
        var r = new Rig(); r.User.Actor = Maker;
        Assert.True((await r.Preview()).ApprovalSupported); Assert.Equal("Approved", (await r.Decide(Request(true))).State);
        Assert.Equal(Maker.ToString("D"), r.Native.Current.Decision!.DecidedBy); Assert.Equal(1, r.Native.Effects);
        Assert.All(r.Authority.Permissions, p => Assert.Equal(SalesPermissionManifestV1.RequestsApprove, p));
    }

    [Theory]
    [InlineData(409, SalesDraftProblemCodesV1.VersionConflict, false)] [InlineData(409, SalesRequestProblemCodesV1.HasOrder, true)]
    [InlineData(403, "forbidden", false)] [InlineData(422, "sales_request_rejection_reason_required", false)]
    public async Task Native_policy_refusals_and_original_refusal_correlation_are_not_remapped_to_Work_success(int status, string code, bool replay)
    {
        var r = new Rig(); r.Native.Refusal = new(status, code, isReplay: replay, originalCorrelationId: replay ? "SENTETIK-old-refusal" : null);
        var error = await Assert.ThrowsAsync<WorkDecisionException>(() => r.Decide(Request(false)));
        Assert.Equal((status, code, replay, replay ? "SENTETIK-old-refusal" : null), (error.Status, error.Code, error.Replayed, error.OriginalCorrelationId));
        Assert.Equal(0, r.Native.Effects); Assert.Equal(1, r.Native.Calls); Assert.Empty(r.Native.Commands);
    }

    [Theory]
    [InlineData("")] [InlineData("0")] [InlineData("01")] [InlineData("+1")] [InlineData("1\n")]
    [InlineData("9223372036854775807")] [InlineData("9223372036854775808")]
    public async Task Noncanonical_or_nonincrementable_versions_fail_before_source_or_native_IO(string version)
    {
        var r = new Rig();
        foreach (var approve in new[] { true, false }) Assert.Equal(400, (await Assert.ThrowsAsync<WorkDecisionException>(() => r.Decide(Request(approve) with { ExpectedVersion = version }))).Status);
        Assert.Equal(0, r.Source.Reads); Assert.Equal(0, r.Native.Calls);
    }

    [Theory] [InlineData("approve-note")] [InlineData("blank-reject")] [InlineData("long-reject")]
    [InlineData("foreign-catalog")] [InlineData("foreign-period")] [InlineData("foreign-cancellation")]
    public async Task Invalid_reason_or_foreign_selection_does_not_reach_a_native_command(string fault)
    {
        var r = new Rig(); var q = fault switch
        {
            "approve-note" => Request(true) with { Reason = "note" }, "blank-reject" => Request(false) with { Reason = " \t " },
            "long-reject" => Request(false) with { Reason = new('x', 4001) },
            "foreign-catalog" => Request(true) with { ApprovedSourceCatalogSelection = new("0") },
            "foreign-period" => Request(true) with { AccountingPeriodReopening = new("1") },
            _ => Request(true) with { InventoryCostCancellation = new("1") },
        };
        Assert.Equal(400, (await Assert.ThrowsAsync<WorkDecisionException>(() => r.Decide(q))).Status);
        Assert.Equal(0, r.Source.Reads); Assert.Equal(0, r.Native.Calls);
    }

    [Fact]
    public async Task Native_raw_4000_character_reason_is_not_trimmed_or_restricted_by_an_invented_500_character_or_control_policy()
    {
        var r = new Rig(); var reason = " \n" + new string('x', 3996) + "  "; Assert.Equal(4000, reason.Length);
        Assert.Equal("Rejected", (await r.Decide(new("1", false, reason))).State); Assert.Equal(reason, r.Native.LastBody!.Reason);
        Assert.Equal(reason, r.Native.Current.Decision!.RejectionReason); Assert.Equal(1, r.Native.Effects);
    }

    [Theory] [InlineData("reference")] [InlineData("caption")] [InlineData("rule")]
    [InlineData("company")] [InlineData("version")] [InlineData("pending")]
    public async Task Validatable_source_identity_rule_reference_and_action_state_faults_fail_before_dispatch(string fault)
    {
        var r = new Rig(); r.Source.Change = s => fault switch
        {
            "reference" => s with { Item = s.Item with { References = [s.Item.References[0] with { RecordId = Guid.NewGuid() }] } },
            "caption" => s with { Item = s.Item with { DocumentLabel = "other" } },
            "rule" => s with { Item = s.Item with { Rules = [s.Item.Rules[0] with { Actual = "1" }] } },
            "company" => s with { SalesRequestProof = s.SalesRequestProof! with { CompanyId = Guid.NewGuid() } },
            "version" => s with { SourceVersion = "2" }, _ => s with { Pending = false },
        };
        Assert.Equal(503, (await Assert.ThrowsAsync<WorkDecisionException>(() => r.Decide(Request(true)))).Status);
        Assert.Equal(0, r.Native.Calls); Assert.Equal(0, r.Native.Effects);
    }

    [Theory] [InlineData("missing")] [InlineData("actor")] [InlineData("key")] [InlineData("payload")]
    [InlineData("original-digest")] [InlineData("observed-digest")] [InlineData("correlation")]
    public async Task Postnative_receipt_mismatch_is_unknown_not_rollback_and_same_original_key_reconciles_once(string fault)
    {
        var r = new Rig(); r.Native.ChangeAnswer = a =>
        {
            var p = a.OriginalDecisionReceipt!;
            p = fault switch { "actor" => p with { ActorId = Maker.ToString("D") }, "key" => p with { IdempotencyKeyHash = new('B', 64) },
                "payload" => p with { PayloadHash = new('b', 64) }, "original-digest" => p with { OriginalDecisionDigest = new('c', 64) },
                "observed-digest" => p with { CurrentResponseDigest = new('d', 64) }, _ => p };
            return a with { OriginalDecisionReceipt = fault == "missing" ? null : p, OriginalCorrelationId = fault == "correlation" ? "other" : a.OriginalCorrelationId };
        };
        Assert.Equal(503, (await Assert.ThrowsAsync<WorkDecisionException>(() => r.Decide(Request(true)))).Status);
        Assert.Equal(1, r.Native.Effects); Assert.Single(r.Native.Commands);
        r.Native.ChangeAnswer = null; r.SetCorrelation("SENTETIK-reconcile-correlation"); var replay = await r.Decide(Request(true));
        Assert.Equal((true, "Approved", Correlation), (replay.Replayed, replay.State, replay.OriginalCorrelationId));
        Assert.Equal(1, r.Native.Effects); Assert.Single(r.Native.Commands);
    }

    [Theory] [InlineData(false, "permission")] [InlineData(true, "permission")]
    [InlineData(false, "delegation")] [InlineData(true, "delegation")]
    [InlineData(false, "cancel")] [InlineData(true, "cancel")]
    [InlineData(false, "actor")] [InlineData(true, "actor")]
    [InlineData(false, "company")] [InlineData(true, "company")]
    [InlineData(false, "membership")] [InlineData(true, "membership")]
    public async Task Current_permission_delegation_and_normal_completion_cancellation_are_fenced_before_mutation_and_release(bool afterEffect, string fault)
    {
        var r = new Rig(); using var cancelled = new CancellationTokenSource();
        Action revoke = () =>
        {
            if (fault == "permission") r.Authority.Allowed = false; if (fault == "delegation") r.Delegations.Live = false;
            if (fault == "cancel") cancelled.Cancel(); if (fault == "actor") r.User.Actor = Maker;
            if (fault == "company") r.Company.Set(Guid.NewGuid()); if (fault == "membership") r.User.Member = false;
        };
        if (afterEffect) r.Native.AfterEffect = revoke; else r.Native.BeforeAuthority = revoke;
        var q = Request(true) with { DelegationId = Guid.NewGuid() };
        if (fault == "cancel") await Assert.ThrowsAnyAsync<OperationCanceledException>(() => r.Decide(q, token: cancelled.Token));
        else Assert.Equal(403, (await Assert.ThrowsAsync<WorkDecisionException>(() => r.Decide(q))).Status);
        Assert.Equal(afterEffect ? 1 : 0, r.Native.Effects);
        if (afterEffect) Assert.Single(r.Native.Commands); else Assert.Empty(r.Native.Commands);
        if (afterEffect)
        {
            r.Native.AfterEffect = null; r.Authority.Allowed = true; r.Delegations.Live = true;
            r.User.Actor = Actor; r.User.Member = true; r.Company.Set(Company);
            Assert.True((await r.Decide(q)).Replayed); Assert.Equal(1, r.Native.Effects);
        }
    }

    [Theory] [InlineData("line")] [InlineData("version")] [InlineData("permission")]
    public async Task Changed_or_revoked_second_human_read_does_not_release_mixed_facts(string fault)
    {
        var r = new Rig(); r.Native.BeforeRead = n =>
        {
            if (n != 2) return;
            if (fault == "permission") r.Authority.Allowed = false;
            if (fault == "version") r.Native.Current = r.Native.Current with { Request = r.Native.Current.Request with { Version = "2" } };
            if (fault == "line") r.Native.Current = r.Native.Current with { Lines = [r.Native.Current.Lines[0] with { Quantity = "999" }, r.Native.Current.Lines[1]] };
        };
        Assert.Equal(fault == "permission" ? 403 : 503, (await Assert.ThrowsAsync<WorkDecisionException>(() => r.Preview())).Status);
        Assert.Equal(0, r.Native.Calls); Assert.Equal(0, r.Native.Effects);
    }

    [Fact]
    public async Task Sales_request_private_proof_is_refused_on_another_kind_and_missing_native_owner_cannot_fall_back_to_general_read()
    {
        var r = new Rig(); Assert.Equal(503, (await Assert.ThrowsAsync<WorkDecisionException>(() => r.BuildHost(null).PreviewAsync(Kind, Id, default))).Status);
        r.Authority.AllowOtherPermissions = true; r.Source.OtherKind = WorkDecisionHostV1.SalesOrder;
        r.Source.Change = s => s with { Item = s.Item with { Kind = WorkDecisionHostV1.SalesOrder, Rules = [], References = [] } };
        Assert.Equal(503, (await Assert.ThrowsAsync<WorkDecisionException>(() => r.Host.PreviewAsync(WorkDecisionHostV1.SalesOrder, Id, default))).Status);
        Assert.Equal(0, r.Native.Calls); Assert.Equal(0, r.Native.Effects);
    }

    private static WorkDecisionRequestV1 Request(bool approve) => new("1", approve, approve ? null : Reason);
    private sealed class Rig
    {
        internal readonly MutableCompany Company = new(); internal readonly MutableUser User = new(); internal readonly Authority Authority = new(); internal readonly Delegations Delegations = new();
        internal readonly Native Native; internal readonly SourceProxy Source; internal readonly WorkDecisionHostV1 Host;
        private readonly CorrelationContext correlation = new(); private readonly WorkDecisionExecutionFenceV1 execution = new(); private readonly WorkDecisionAuthorityV1 scopedAuthority;
        internal Rig()
        {
            Company.Set(WorkSalesRequestDecisionPreviewV1Tests.Company); correlation.Set(Correlation, "SENTETIK-cause", "Web", null, null); scopedAuthority = new(Authority, execution);
            Native = new(this, scopedAuthority);
            // Selected source never uses the legacy pending-list store. A fallback would fail this protocol fixture.
            Source = new(new SalesRequestWorkInboxSourceV1(null!, Company, User, scopedAuthority, Native)); Host = BuildHost(Native);
        }
        internal WorkDecisionHostV1 BuildHost(ISalesRequestService? owner) => new(Company, User, correlation, scopedAuthority, [Source], Delegations, execution, null!, null!, salesRequests: owner);
        internal void SetCorrelation(string value) => correlation.Set(value, "SENTETIK-cause", "Web", null, null);
        internal Task<WorkDecisionPreviewV1> Preview() => Host.PreviewAsync(Kind, Id, default);
        internal Task<WorkDecisionResultV1> Decide(WorkDecisionRequestV1 request, string key = Key, CancellationToken token = default) => Host.DecideAsync(Kind, Id, request, key, token);
    }
    private sealed record Saved(string Hash, string Actor, SalesRequestOriginalDecisionReceiptV1 Receipt);
    private sealed class Native(Rig rig, IErpAuthorityRevalidationService authority) : ISalesRequestService, ISalesRequestDecisionReadV1
    {
        internal SalesRequestDetailView Current = Facts().Detail; internal int Reads, Calls, Effects;
        internal Action<int>? BeforeRead; internal Action? BeforeAuthority, AfterEffect;
        internal Func<SalesRequestResponse, SalesRequestResponse>? ChangeAnswer; internal SalesDraftServiceException? Refusal;
        internal SalesDraftStateCommandRequest? LastBody; internal string? LastKey; internal SalesCommandMetadata? LastMetadata;
        internal readonly Dictionary<(string Command, string Key), Saved> Commands = [];
        public async Task<SalesRequestDecisionReadResultV1> ReadRequestDecisionAsync(Guid id, Func<CancellationToken, Task> fence, CancellationToken token = default)
        {
            await fence(token); Reads++; BeforeRead?.Invoke(Reads); await fence(token);
            if (id != Id) return new(SalesRequestDecisionReadStateV1.NotFound, null);
            return new(SalesRequestDecisionReadStateV1.Found, Seal(new(Current, Current.Request.State == "Submitted", Current.Request.State is "Submitted" or "Approved", "", "")));
        }
        public Task<SalesRequestResponse> ApproveRequestAsync(Guid id, SalesDraftStateCommandRequest request, SalesCommandMetadata metadata, CancellationToken token = default) => Decide(id, request, metadata, true, token);
        public Task<SalesRequestResponse> RejectRequestAsync(Guid id, SalesDraftStateCommandRequest request, SalesCommandMetadata metadata, CancellationToken token = default) => Decide(id, request, metadata, false, token);
        private async Task<SalesRequestResponse> Decide(Guid id, SalesDraftStateCommandRequest body, SalesCommandMetadata metadata, bool approve, CancellationToken token)
        {
            Calls++; LastBody = body; LastKey = metadata.IdempotencyKey; LastMetadata = metadata; var actor = rig.User.UserId; BeforeAuthority?.Invoke();
            await Fence(actor, token); if (Refusal is not null) throw Refusal;
            var command = approve ? SalesRequestCommands.ApproveV1 : SalesRequestCommands.RejectV1; var hash = SalesRequestHash.Compute(command, id, body);
            var replay = Commands.TryGetValue((command, metadata.IdempotencyKey), out var saved);
            if (replay && (saved!.Hash != hash || saved.Actor != actor)) throw new SalesDraftServiceException(409, SalesDraftProblemCodesV1.SameKeyPayloadMismatch);
            if (!replay)
            {
                if (Current.Request.Version != body.ExpectedVersion) throw new SalesDraftServiceException(409, SalesDraftProblemCodesV1.VersionConflict);
                if (approve && Current.Request.State != "Submitted" || !approve && Current.Request.State == "Rejected") throw new SalesDraftServiceException(409, SalesDraftProblemCodesV1.StateConflict);
                var version = (long.Parse(body.ExpectedVersion, CultureInfo.InvariantCulture) + 1).ToString(CultureInfo.InvariantCulture);
                Current = Current with { Request = Current.Request with { State = approve ? "Approved" : "Rejected", Version = version }, Decision = new(actor, At.AddHours(1), body.Reason) };
                Effects++; var h = Current.Request;
                // A synthetic receipt protocol, deliberately not an assertion that a real Audit row/TX was written.
                var proof = new SalesRequestOriginalDecisionReceiptV1(h.CompanyId, id, h.DocumentNo, h.PartyId, h.CompanyAccountId, h.BusinessDate,
                    command, actor, SalesStore.IdempotencyKeyHash(metadata.IdempotencyKey), hash, body.ExpectedVersion, version, h.State, Current.Decision!, metadata.CorrelationId,
                    Guid.NewGuid(), new('A', 64), "", "");
                proof = proof with { OriginalDecisionDigest = SalesRequestDecisionDigestsV1.OriginalDecisionDigest(proof) };
                saved = new(hash, actor, proof); Commands.Add((command, metadata.IdempotencyKey), saved); AfterEffect?.Invoke();
            }
            await Fence(actor, token);
            var original = saved!.Receipt;
            var answer = new SalesRequestResponse(Current, replay, original.OriginalCorrelationId);
            answer = answer with { OriginalDecisionReceipt = original with { CurrentResponseDigest = SalesRequestDecisionDigestsV1.CurrentResponseDigest(answer) } };
            return ChangeAnswer?.Invoke(answer) ?? answer;
        }
        private async Task Fence(string actor, CancellationToken token)
        {
            token.ThrowIfCancellationRequested(); var s = await authority.RevalidatePermissionScopeAsync(SalesPermissionManifestV1.RequestsApprove, WorkSalesRequestDecisionPreviewV1Tests.Company, token); token.ThrowIfCancellationRequested();
            if (!s.IsGranted || !s.IsCompanyWide || rig.Company.CompanyId != WorkSalesRequestDecisionPreviewV1Tests.Company || rig.User.UserId != actor || !rig.User.Member)
                throw new SalesDraftServiceException(403, "forbidden");
        }
        public Task<SalesRequestResponse> CreateRequestAsync(CreateSalesRequestRequest request, SalesCommandMetadata metadata, CancellationToken token = default) => throw new InvalidOperationException("No create");
        public Task<SalesRequestListResponse> GetRequestListAsync(SalesRequestListQuery query, CancellationToken token = default) => throw new InvalidOperationException("No general read");
        public Task<SalesRequestDetailView> GetRequestDetailAsync(Guid id, CancellationToken token = default) => throw new InvalidOperationException("No general detail or price read");
    }
    private sealed class SourceProxy(IWorkApprovalDecisionSourceV1 inner) : IWorkApprovalDecisionSourceV1
    {
        internal int Reads; internal string? OtherKind; internal Func<WorkApprovalDecisionSnapshotV1, WorkApprovalDecisionSnapshotV1>? Change;
        public string Module => "sales"; public string Kind => OtherKind ?? inner.Kind;
        public string ApproverPermission => OtherKind is null ? inner.ApproverPermission : SalesPermissionManifestV1.OrdersApprove;
        public Task<WorkInboxSourceResultV1> ListPendingAsync(WorkInboxSourceContextV1 c, int max, CancellationToken t) => throw new InvalidOperationException("No pending-list fallback");
        public async Task<WorkApprovalDecisionSnapshotV1?> ReadDecisionAsync(WorkInboxSourceContextV1 c, Guid id, CancellationToken t)
        { Reads++; var s = await inner.ReadDecisionAsync(c, id, t); return s is null ? null : Change?.Invoke(s) ?? s; }
    }
    private sealed class MutableCompany : ICompanyContext { public Guid CompanyId { get; private set; } public bool IsResolved => CompanyId != Guid.Empty; public void Set(Guid id) => CompanyId = id; }
    private sealed class MutableUser : ICurrentUser
    {
        internal Guid Actor = WorkSalesRequestDecisionPreviewV1Tests.Actor; internal bool Member = true;
        public bool IsAuthenticated => true; public string UserId => Actor.ToString("D"); public IReadOnlySet<Guid> AllowedCompanyIds { get; } = new HashSet<Guid>([WorkSalesRequestDecisionPreviewV1Tests.Company]);
        public bool CanAccessCompany(Guid id) => Member && id == WorkSalesRequestDecisionPreviewV1Tests.Company;
    }
    private sealed class Authority : IErpAuthorityRevalidationService
    {
        internal bool Allowed = true, AllowOtherPermissions; internal readonly List<string> Permissions = [];
        public Task<PermissionScope> RevalidatePermissionScopeAsync(string p, Guid company, CancellationToken token = default)
        { Permissions.Add(p); return Task.FromResult(new PermissionScope(Allowed && (AllowOtherPermissions || p == SalesPermissionManifestV1.RequestsApprove), true, new HashSet<string>(), new HashSet<Guid>(), new HashSet<Guid>(), new HashSet<Guid>())); }
    }
    private sealed class Delegations : IWorkDelegationReaderV1
    {
        internal bool Live = true;
        public Task<WorkDelegationViewV1?> FindForActorAsync(Guid id, WorkInboxItemV1 item, CancellationToken token = default) => Task.FromResult<WorkDelegationViewV1?>(Live
            ? new(id, Company, Kind, SalesPermissionManifestV1.RequestsApprove, Maker, Actor, At.AddYears(-1), At.AddYears(1), "1", Maker, At, "SENTETIK", null, null, null) : null);
    }
}
