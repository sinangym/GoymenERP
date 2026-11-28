using System.Globalization;
using System.Reflection;
using GoymenERP.Api.WorkRuntime;
using GoymenERP.BuildingBlocks.Multitenancy;
using GoymenERP.BuildingBlocks.Observability;
using GoymenERP.BuildingBlocks.Security;
using GoymenERP.BuildingBlocks.Work;
using GoymenERP.Modules.Sales.Application.Contracts;
using GoymenERP.Modules.Sales.Domain.ApprovalRequests;
using GoymenERP.Modules.Sales.Infrastructure.Persistence;
using GoymenERP.Modules.Sales.Infrastructure.WorkInbox;
using GoymenERP.Modules.Work.Infrastructure.Delegations;
using static GoymenERP.Api.IntegrationTests.WorkSalesOverDeliveryRequestDecisionPreviewV1Tests;

namespace GoymenERP.Api.IntegrationTests;

// Real source, host and WP3 decorator with a controlled native protocol. Not SQL, audit-integrity or transaction acceptance.
public sealed class WorkSalesOverDeliveryRequestDecisionHostTests
{
    private const string Kind = WorkDecisionHostV1.SalesOverDeliveryRequest, Key = "SENTETIK-over-work-key", Corr = "SENTETIK-over-work-original";
    private static WorkDecisionRequestV1 Request(bool approve = true) => new("1", approve, approve ? null : "  SENTETIK ret  ");

    [Theory] [InlineData(true)] [InlineData(false)]
    public async Task Current_GET_has_two_owner_reads_POST_selects_one_request_version_and_calls_exact_native_mode_without_human_reread(bool approve)
    {
        var r = new Rig(); var preview = await r.Preview();
        var human = Assert.IsType<WorkSalesOverDeliveryRequestDecisionPreviewV1>(preview.SalesOverDeliveryRequest);
        Assert.Equal(("1", true, true, true), (preview.SourceVersion, preview.Pending, preview.ApprovalSupported, preview.RejectionSupported));
        Assert.Equal("9", human.Order.Version); Assert.Equal(2, r.Native.Reads);
        var q = Request(approve); var reads = r.Native.Reads; var answer = await r.Decide(q);
        Assert.Equal((approve ? "Approved" : "Rejected", false, Corr), (answer.State, answer.Replayed, answer.OriginalCorrelationId));
        Assert.Equal(reads + 1, r.Native.Reads); Assert.Equal(1, r.Native.HistoryReads); Assert.Equal("1", r.Source.Selected);
        Assert.Equal(approve, r.Native.LastApprove); Assert.Equal(new SalesDraftStateCommandRequest(q.ExpectedVersion, q.Reason), r.Native.LastBody);
        Assert.Equal(new SalesOverDeliveryRequestTargetV1(Id, Order, 3, 0, Line, "1"), r.Native.LastTarget);
        Assert.Equal((Key, Corr, "SENTETIK-cause"), (r.Native.LastMetadata!.IdempotencyKey, r.Native.LastMetadata.CorrelationId, r.Native.LastMetadata.CausationId));
        Assert.Single(r.Native.Commands); Assert.Equal(1, r.Native.Effects);
        Assert.All(r.Authority.Permissions, p => Assert.Equal(SalesPermissionManifestV1.OrdersOverDeliveryApprove, p));
        var receipt = Assert.Single(r.Native.Commands).Value;
        Assert.Equal((1L, 2L), (receipt.ExpectedRequestVersion, receipt.ResultRequestVersion));
        Assert.Equal(approve ? (long?)9 : null, receipt.OriginalOrderVersionBefore); Assert.Equal(approve ? (long?)10 : null, receipt.OriginalOrderVersionAfter);
    }

    [Theory] [InlineData(true)] [InlineData(false)]
    public async Task Original_key_retry_keeps_old_request_actor_quantities_and_independent_order_versions_without_current_human_selection(bool approve)
    {
        var r = new Rig(); var q = Request(approve); await r.Decide(q); var old = Assert.Single(r.Native.Commands).Value;
        r.Native.Current = Seal(Facts() with { Request = Facts().Request with { RequestedBy = Actor.ToString("D"), RequestNo = 4 },
            Order = Facts().Order with { Version = 27, Revision = 8 } });
        Assert.Equal(403, (await Assert.ThrowsAsync<WorkDecisionException>(() => r.Preview())).Status);
        var reads = r.Native.Reads; r.SetCorrelation("SENTETIK-retry"); var answer = await r.Decide(q);
        Assert.Equal((true, approve ? "Approved" : "Rejected", Corr), (answer.Replayed, answer.State, answer.OriginalCorrelationId));
        Assert.Equal(reads, r.Native.Reads); Assert.Equal(27, r.Native.Current.Order.Version); Assert.Equal(1, r.Native.Effects);
        Assert.Equal(old, Assert.Single(r.Native.Commands).Value); Assert.Equal(Maker.ToString("D"), r.Source.Last!.Item.PreparedBy);
        Assert.Equal("1", r.Source.Last.SourceVersion); Assert.Equal(2, r.Source.Last.SalesOverDeliveryRequestProof!.RequestVersion);
    }

    [Theory] [InlineData("missing")] [InlineData("actor")] [InlineData("company")] [InlineData("key")] [InlineData("payload")]
    [InlineData("version")] [InlineData("order-version")] [InlineData("source")] [InlineData("response")] [InlineData("correlation")]
    public async Task Postnative_invalid_receipt_is_unknown_then_same_frozen_key_reconciles_without_another_effect(string fault)
    {
        var r = new Rig(); r.Native.ChangeReceipt = p => fault switch
        {
            "missing" => null, "actor" => p with { ActorId = Maker.ToString("D") }, "company" => p with { CompanyId = Guid.NewGuid() },
            "key" => p with { IdempotencyKeyHash = new('A', 64) }, "payload" => p with { PayloadHash = new('b', 64) },
            "version" => p with { ExpectedRequestVersion = 2 }, "order-version" => p with { OriginalOrderVersionAfter = 11 },
            "source" => p with { SourceFactsDigest = new('c', 64) }, "response" => p with { CurrentResponseDigest = new('d', 64) },
            _ => p with { OriginalCorrelationId = "another" }
        };
        Assert.Equal(503, (await Assert.ThrowsAsync<WorkDecisionException>(() => r.Decide(Request()))).Status);
        Assert.Single(r.Native.Commands); Assert.Equal(1, r.Native.Effects); // Native effect retained; no rollback claim.
        r.Native.ChangeReceipt = null; r.SetCorrelation("SENTETIK-reconcile"); var replay = await r.Decide(Request());
        Assert.True(replay.Replayed); Assert.Equal(Corr, replay.OriginalCorrelationId); Assert.Equal(1, r.Native.Effects);
    }

    [Theory] [InlineData("zero")] [InlineData("padded")] [InlineData("max")] [InlineData("approve-reason")]
    [InlineData("reject-null")] [InlineData("reject-blank")] [InlineData("reject-long")] [InlineData("quote")]
    [InlineData("floor")] [InlineData("loss")] [InlineData("period")] [InlineData("cancellation")]
    public async Task Malformed_or_foreign_body_is_refused_before_any_source_or_owner_IO(string fault)
    {
        var r = new Rig(); var q = Request(); q = fault switch
        {
            "zero" => q with { ExpectedVersion = "0" }, "padded" => q with { ExpectedVersion = "01" },
            "max" => q with { ExpectedVersion = long.MaxValue.ToString(CultureInfo.InvariantCulture) }, "approve-reason" => q with { Reason = "SENTETIK" },
            "reject-null" => q with { Approve = false }, "reject-blank" => q with { Approve = false, Reason = " \t " },
            "reject-long" => q with { Approve = false, Reason = new('x', SalesApprovalRequest.MaximumTextLength + 1) },
            "quote" => q with { SalesQuoteConversion = new(Order, 3, 0, "1") }, "floor" => q with { SalesPriceFloor = new([]) },
            "loss" => q with { InventoryTransferLoss = new(1, At) }, "period" => q with { NormalJournalDraft = new(Order) },
            _ => q with { InventoryCostCancellation = new("1") }
        };
        Assert.Equal(400, (await Assert.ThrowsAsync<WorkDecisionException>(() => r.Decide(q))).Status);
        Assert.Equal((0, 0, 0, 0), (r.Source.Reads, r.Native.Reads, r.Native.HistoryReads, r.Native.Calls));
    }

    [Theory] [InlineData("missing")] [InlineData("caption")] [InlineData("reference")] [InlineData("rule")]
    [InlineData("maker")] [InlineData("quantity")] [InlineData("version")] [InlineData("company")] [InlineData("permission")]
    [InlineData("foreign-carrier")]
    public async Task Altered_source_or_wrong_native_authority_cannot_reach_the_command(string fault)
    {
        var r = new Rig(); r.Source.Change = s => fault switch
        {
            "missing" => s with { SalesOverDeliveryRequestProof = null }, "caption" => s with { Item = s.Item with { DocumentLabel = "another" } },
            "reference" => s with { Item = s.Item with { References = s.Item.References.Select(x => x with { RecordId = Guid.NewGuid() }).ToArray() } },
            "rule" => s with { Item = s.Item with { Rules = s.Item.Rules.Select(x => x with { Actual = "1.29" }).ToArray() } },
            "maker" => s with { SalesOverDeliveryRequestProof = s.SalesOverDeliveryRequestProof! with { RequestedBy = "another" } },
            "quantity" => s with { SalesOverDeliveryRequestProof = s.SalesOverDeliveryRequestProof! with { RequestedTotal = "1.29" } },
            "version" => s with { SourceVersion = "2" }, "company" => s with { CompanyId = Guid.NewGuid() },
            "foreign-carrier" => s with { InventoryCostCancellationProof = new(Company, Id, Id, Id, 1, 1, Id, Maker, At, At, "Prepared", "", "") }, _ => s
        };
        if (fault == "permission") r.Source.Permission = SalesPermissionManifestV1.OrdersApprove;
        Assert.Equal(503, (await Assert.ThrowsAsync<WorkDecisionException>(() => r.Decide(Request()))).Status);
        Assert.Equal(0, r.Native.Calls); Assert.Empty(r.Native.Commands);
    }

    [Theory] [InlineData("facts")] [InlineData("permission")] [InlineData("actor")] [InlineData("company")] [InlineData("cancel")]
    public async Task Second_human_read_revalidates_exact_facts_current_context_and_completed_cancellation(string fault)
    {
        var r = new Rig(); using var cancellation = new CancellationTokenSource(); r.Native.OnRead = n =>
        {
            if (n != 2) return;
            if (fault == "facts") r.Native.Current = Seal(r.Native.Current with { Order = r.Native.Current.Order with { DocumentNo = "SENTETIK changed" } });
            if (fault == "permission") r.Authority.Allowed = false; if (fault == "actor") r.User.ActorId = Maker;
            if (fault == "company") r.Company.Set(Guid.NewGuid()); if (fault == "cancel") cancellation.Cancel();
        };
        if (fault == "cancel") await Assert.ThrowsAnyAsync<OperationCanceledException>(() => r.Host.PreviewAsync(Kind, Id, cancellation.Token));
        else Assert.Equal(fault == "facts" ? 503 : 403, (await Assert.ThrowsAsync<WorkDecisionException>(() => r.Preview())).Status);
        Assert.Equal(2, r.Native.Reads); Assert.Equal(0, r.Native.Calls);
    }

    [Theory] [InlineData(false, "permission")] [InlineData(true, "permission")] [InlineData(false, "delegation")] [InlineData(true, "delegation")]
    [InlineData(false, "actor")] [InlineData(true, "actor")] [InlineData(false, "company")] [InlineData(true, "company")]
    [InlineData(false, "membership")] [InlineData(true, "membership")] [InlineData(false, "cancel")] [InlineData(true, "cancel")]
    public async Task Native_and_release_fences_preserve_live_authority_delegation_and_postcommit_unknown_semantics(bool retained, string fault)
    {
        var r = new Rig(); using var cancellation = new CancellationTokenSource(); void Revoke()
        {
            if (fault == "permission") r.Authority.Allowed = false; if (fault == "delegation") r.Delegations.Live = false;
            if (fault == "actor") r.User.ActorId = Maker; if (fault == "company") r.Company.Set(Guid.NewGuid());
            if (fault == "membership") r.User.Member = false; if (fault == "cancel") cancellation.Cancel();
        }
        if (retained) r.Native.AfterEffect = Revoke; else r.Native.BeforeEffect = Revoke;
        var q = Request() with { DelegationId = Guid.NewGuid() };
        if (fault == "cancel") await Assert.ThrowsAnyAsync<OperationCanceledException>(() => r.Decide(q, token: cancellation.Token));
        else Assert.Equal(403, (await Assert.ThrowsAsync<WorkDecisionException>(() => r.Decide(q))).Status);
        Assert.Equal(retained ? 1 : 0, r.Native.Effects); Assert.Equal(retained ? 1 : 0, r.Native.Commands.Count);
    }

    [Theory] [InlineData(404, "not_found")] [InlineData(409, "sales_approval_request_stale")] [InlineData(422, "sales_approval_request_blocked")]
    public async Task Native_refusal_status_code_replay_and_original_correlation_survive_unchanged(int status, string code)
    {
        var r = new Rig(); r.Native.Refusal = new(status, code, isReplay: true, originalCorrelationId: "SENTETIK-original-refusal");
        var e = await Assert.ThrowsAsync<WorkDecisionException>(() => r.Decide(Request()));
        Assert.Equal((status, code, true, "SENTETIK-original-refusal"), (e.Status, e.Code, e.Replayed, e.OriginalCorrelationId)); Assert.Empty(r.Native.Commands);
    }

    [Fact]
    public async Task Rejection_preserves_the_native_literal_bound_above_the_generic_500_character_limit()
    {
        var r = new Rig(); var reason = "  " + new string('x', SalesApprovalRequest.MaximumTextLength - 4) + "  ";
        var answer = await r.Decide(Request(false) with { Reason = reason });
        Assert.Equal("Rejected", answer.State); Assert.Equal(reason, r.Native.LastBody!.Reason);
        Assert.Equal(reason, Assert.Single(r.Native.Commands).Value.OriginalRequest.DecisionReason);
    }

    [Fact]
    public async Task Terminal_current_human_card_has_no_new_decision_and_a_new_key_cannot_reexecute_an_old_request()
    {
        var r = new Rig(); await r.Decide(Request()); var preview = await r.Preview();
        Assert.False(preview.Pending); Assert.False(preview.ApprovalSupported); Assert.False(preview.RejectionSupported);
        Assert.Equal("2", preview.SourceVersion); Assert.Equal("10", preview.SalesOverDeliveryRequest!.Order.Version);
        Assert.Equal(409, (await Assert.ThrowsAsync<WorkDecisionException>(() => r.Decide(new("2", true, null), "SENTETIK-new-key"))).Status);
        Assert.Equal(1, r.Native.Effects); Assert.Single(r.Native.Commands);
    }

    [Fact]
    public async Task Missing_historical_proof_never_falls_back_to_a_current_later_request_or_native_legacy_path()
    {
        var r = new Rig(); await r.Decide(Request()); r.Native.Legacy = true; var calls = r.Native.Calls; var reads = r.Native.Reads;
        Assert.Equal(503, (await Assert.ThrowsAsync<WorkDecisionException>(() => r.Decide(Request()))).Status);
        Assert.Equal(calls, r.Native.Calls); Assert.Equal(reads, r.Native.Reads); Assert.Equal(1, r.Native.Effects);
        var missing = r.BuildHost(null);
        Assert.Equal(503, (await Assert.ThrowsAsync<WorkDecisionException>(() => missing.PreviewAsync(Kind, Id, default))).Status);
    }

    [Fact]
    public void Every_foreign_kind_rejects_the_over_delivery_carrier_and_current_terminal_kind_keeps_it()
    {
        var d = Facts(); var s = new WorkApprovalDecisionSnapshotV1(Company, SalesOverDeliveryRequestDecisionBindingV1.Item(d)!, "1", true)
        { SalesOverDeliveryRequestProof = SalesOverDeliveryRequestDecisionBindingV1.Proof(d) };
        var guard = typeof(WorkDecisionHostV1).GetMethod("ValidSalesOverDeliveryRequestProof", BindingFlags.NonPublic | BindingFlags.Static)!;
        Assert.True((bool)guard.Invoke(null, [Kind, s])!);
        Assert.All(WorkDecisionHostV1.Kinds.Where(x => x != Kind), kind => Assert.False((bool)guard.Invoke(null, [kind, s])!));
    }

    private sealed class Rig
    {
        internal readonly MutableCompany Company = new(); internal readonly MutableUser User = new(); internal readonly Authority Authority = new();
        internal readonly Delegations Delegations = new(); internal readonly Native Native; internal readonly SourceProxy Source;
        internal WorkDecisionHostV1 Host; private CorrelationContext correlation = new(); private readonly WorkDecisionExecutionFenceV1 execution = new();
        private readonly WorkDecisionAuthorityV1 decorated;
        internal Rig()
        {
            Company.Set(WorkSalesOverDeliveryRequestDecisionPreviewV1Tests.Company); correlation.Set(Corr, "SENTETIK-cause", "Web", null, null);
            decorated = new(Authority, execution); Native = new(this, decorated);
            Source = new(new SalesOverDeliveryRequestWorkInboxSourceV1(null!, Company, User, decorated, Native)); Host = BuildHost(Native);
        }
        internal WorkDecisionHostV1 BuildHost(ISalesOverDeliveryRequestDecisionReadV1? owner) => new(Company, User, correlation, decorated,
            [Source], Delegations, execution, null!, null!, salesOverDeliveryRequests: owner);
        internal void SetCorrelation(string value) { correlation = new(); correlation.Set(value, "SENTETIK-cause", "Web", null, null); Host = BuildHost(Native); }
        internal Task<WorkDecisionPreviewV1> Preview() => Host.PreviewAsync(Kind, Id, default);
        internal Task<WorkDecisionResultV1> Decide(WorkDecisionRequestV1 q, string key = Key, CancellationToken token = default) => Host.DecideAsync(Kind, Id, q, key, token);
    }

    private sealed class Native(Rig rig, IErpAuthorityRevalidationService authority) : ISalesOverDeliveryRequestDecisionReadV1
    {
        internal SalesOverDeliveryRequestDecisionDetailV1 Current = Facts(); internal int Reads, HistoryReads, Calls, Effects;
        internal Action<int>? OnRead; internal Action? BeforeEffect, AfterEffect; internal bool Legacy; internal bool? LastApprove;
        internal SalesOverDeliveryRequestTargetV1? LastTarget; internal SalesDraftStateCommandRequest? LastBody; internal SalesCommandMetadata? LastMetadata;
        internal SalesDraftServiceException? Refusal; internal Func<SalesOverDeliveryRequestOriginalReceiptV1, SalesOverDeliveryRequestOriginalReceiptV1?>? ChangeReceipt;
        internal readonly Dictionary<(string Command, string Key), SalesOverDeliveryRequestOriginalReceiptV1> Commands = [];
        public async Task<SalesOverDeliveryRequestDecisionReadResultV1> ReadOverDeliveryRequestDecisionAsync(Guid id, Func<CancellationToken, Task> fence, CancellationToken ct = default)
        {
            await fence(ct); Reads++; OnRead?.Invoke(Reads); await fence(ct);
            if (id != Id) return new(SalesOverDeliveryRequestDecisionReadStateV1.NotFound, null);
            if (Current.Request.Status == "Pending" && Current.Request.RequestedBy == rig.User.UserId) return new(SalesOverDeliveryRequestDecisionReadStateV1.Denied, null);
            return new(SalesOverDeliveryRequestDecisionReadStateV1.Found, Current);
        }
        public async Task<SalesOverDeliveryRequestOriginalVersionReadResultV1> ReadOverDeliveryRequestOriginalVersionAsync(Guid id, string version, Func<CancellationToken, Task> fence, CancellationToken ct = default)
        {
            await fence(ct); HistoryReads++; await fence(ct);
            if (Legacy) return new(SalesOverDeliveryRequestDecisionReadStateV1.Unavailable, null);
            var matches = Commands.Values.Where(p => p.Target.RequestId == id && p.ActorId == rig.User.UserId && p.ExpectedRequestVersion.ToString(CultureInfo.InvariantCulture) == version).ToArray();
            return matches.Length == 1 ? new(SalesOverDeliveryRequestDecisionReadStateV1.Found, matches[0])
                : new(SalesOverDeliveryRequestDecisionReadStateV1.NotFound, null) { CurrentVersion = long.Parse(Current.Request.Version, CultureInfo.InvariantCulture) };
        }
        public Task<SalesApprovalRequestCommandResult> ApproveSelectedOverDeliveryRequestAsync(SalesOverDeliveryRequestTargetV1 target, SalesDraftStateCommandRequest body, SalesCommandMetadata m, CancellationToken ct = default) => Execute(target, body, m, true, ct);
        public Task<SalesApprovalRequestCommandResult> RejectSelectedOverDeliveryRequestAsync(SalesOverDeliveryRequestTargetV1 target, SalesDraftStateCommandRequest body, SalesCommandMetadata m, CancellationToken ct = default) => Execute(target, body, m, false, ct);
        private async Task<SalesApprovalRequestCommandResult> Execute(SalesOverDeliveryRequestTargetV1 target, SalesDraftStateCommandRequest body, SalesCommandMetadata m, bool approve, CancellationToken ct)
        {
            Calls++; LastApprove = approve; LastTarget = target; LastBody = body; LastMetadata = m; var actor = rig.User.UserId;
            BeforeEffect?.Invoke(); await Fence(actor, ct); if (Refusal is not null) throw Refusal;
            var command = approve ? SalesApprovalRequestCommands.ApproveOverDeliveryV1 : SalesApprovalRequestCommands.RejectOverDeliveryV1;
            var hash = SalesApprovalRequestHash.Compute(command, target.OrderId, target.RequestNo, body);
            var replay = Commands.TryGetValue((command, m.IdempotencyKey), out var p);
            if (replay && (p!.PayloadHash != hash || p.ActorId != actor)) throw new SalesDraftServiceException(409, SalesDraftProblemCodesV1.SameKeyPayloadMismatch);
            if (!replay)
            {
                if (target != SalesOverDeliveryRequestDecisionBindingV1.Target(SalesOverDeliveryRequestDecisionBindingV1.Proof(Current), body.ExpectedVersion)) throw new SalesDraftServiceException(503, "service_unavailable");
                if (Current.Request.Version != body.ExpectedVersion) throw new SalesDraftServiceException(409, SalesDraftProblemCodesV1.VersionConflict);
                if (Current.Request.Status != "Pending") throw new SalesDraftServiceException(409, SalesDraftProblemCodesV1.StateConflict);
                if (Current.Request.RequestedBy == actor) throw new SalesDraftServiceException(409, SalesApprovalRequestProblemCodesV1.SelfDecision);
                var before = Current; var original = Current.Request with { Version = "2", Status = approve ? "Approved" : "Rejected", DecidedBy = actor, DecidedAtUtc = At.AddSeconds(1), DecisionReason = body.Reason };
                var answer = new SalesApprovalRequestCommandResult(original, false, m.CorrelationId);
                p = new(WorkSalesOverDeliveryRequestDecisionPreviewV1Tests.Company, target, Current.Order.DocumentNo,
                    command, actor, SalesStore.IdempotencyKeyHash(m.IdempotencyKey), hash, 1, 2, approve ? before.Order.Version : null,
                    approve ? before.Order.Version + 1 : null, m.CorrelationId, At.AddSeconds(1), original, Guid.NewGuid(), new('A', 64),
                    approve ? Guid.NewGuid() : null, approve ? new('B', 64) : null, Current.SourceFactsDigest,
                    SalesOverDeliveryRequestDecisionDigestsV1.CurrentResponseDigest(answer), "");
                p = p with { OriginalDecisionDigest = SalesOverDeliveryRequestDecisionDigestsV1.OriginalDecisionDigest(p) };
                Commands.Add((command, m.IdempotencyKey), p); Effects++;
                Current = Seal(before with { Request = original, Order = before.Order with { Version = approve ? before.Order.Version + 1 : before.Order.Version }, ApprovalSupported = false, RejectionSupported = false });
                AfterEffect?.Invoke();
            }
            await Fence(actor, ct);
            var response = new SalesApprovalRequestCommandResult(p!.OriginalRequest, replay, p.OriginalCorrelationId);
            var proof = p with { CurrentResponseDigest = SalesOverDeliveryRequestDecisionDigestsV1.CurrentResponseDigest(response) };
            return response with { OriginalOverDeliveryReceipt = ChangeReceipt is null ? proof : ChangeReceipt(proof) };
        }
        private async Task Fence(string actor, CancellationToken ct)
        {
            ct.ThrowIfCancellationRequested(); var p = await authority.RevalidatePermissionScopeAsync(SalesPermissionManifestV1.OrdersOverDeliveryApprove, WorkSalesOverDeliveryRequestDecisionPreviewV1Tests.Company, ct);
            ct.ThrowIfCancellationRequested(); if (!p.IsGranted || !p.IsCompanyWide || rig.User.UserId != actor || !rig.User.Member || rig.Company.CompanyId != WorkSalesOverDeliveryRequestDecisionPreviewV1Tests.Company)
                throw new SalesDraftServiceException(403, "forbidden");
        }
    }

    private sealed class SourceProxy(IWorkApprovalDecisionVersionSourceV1 inner) : IWorkApprovalDecisionVersionSourceV1
    {
        internal int Reads; internal string? Selected, Permission; internal WorkApprovalDecisionSnapshotV1? Last;
        internal Func<WorkApprovalDecisionSnapshotV1, WorkApprovalDecisionSnapshotV1>? Change;
        public string Module => "sales"; public string Kind => inner.Kind; public string ApproverPermission => Permission ?? inner.ApproverPermission;
        public Task<WorkInboxSourceResultV1> ListPendingAsync(WorkInboxSourceContextV1 c, int n, CancellationToken ct) => throw new NotSupportedException("No list fallback");
        public async Task<WorkApprovalDecisionSnapshotV1?> ReadDecisionAsync(WorkInboxSourceContextV1 c, Guid id, CancellationToken ct)
        { Reads++; var s = await inner.ReadDecisionAsync(c, id, ct); return Last = s is null ? null : Change?.Invoke(s) ?? s; }
        public async Task<WorkApprovalDecisionSnapshotV1?> ReadDecisionVersionAsync(WorkInboxSourceContextV1 c, Guid id, string n, CancellationToken ct)
        { Reads++; Selected = n; var s = await inner.ReadDecisionVersionAsync(c, id, n, ct); return Last = s is null ? null : Change?.Invoke(s) ?? s; }
    }
    private sealed class MutableCompany : ICompanyContext { public Guid CompanyId { get; private set; } public bool IsResolved => CompanyId != Guid.Empty; public void Set(Guid id) => CompanyId = id; }
    private sealed class MutableUser : ICurrentUser
    {
        internal Guid ActorId = Actor; internal bool Member = true; public bool IsAuthenticated => true; public string UserId => ActorId.ToString("D");
        public IReadOnlySet<Guid> AllowedCompanyIds { get; } = new HashSet<Guid>([WorkSalesOverDeliveryRequestDecisionPreviewV1Tests.Company]);
        public bool CanAccessCompany(Guid id) => Member && id == WorkSalesOverDeliveryRequestDecisionPreviewV1Tests.Company;
    }
    private sealed class Authority : IErpAuthorityRevalidationService
    {
        internal bool Allowed = true; internal readonly List<string> Permissions = [];
        public Task<PermissionScope> RevalidatePermissionScopeAsync(string p, Guid c, CancellationToken ct = default)
        { Permissions.Add(p); return Task.FromResult(new PermissionScope(Allowed && p == SalesPermissionManifestV1.OrdersOverDeliveryApprove, true, new HashSet<string>(), new HashSet<Guid>(), new HashSet<Guid>(), new HashSet<Guid>())); }
    }
    private sealed class Delegations : IWorkDelegationReaderV1
    {
        internal bool Live = true;
        public Task<WorkDelegationViewV1?> FindForActorAsync(Guid id, WorkInboxItemV1 item, CancellationToken ct = default) => Task.FromResult<WorkDelegationViewV1?>(Live
            ? new(id, Company, Kind, SalesPermissionManifestV1.OrdersOverDeliveryApprove, Maker, Actor, At.AddYears(-1), At.AddYears(1), "1", Maker, At, "SENTETIK", null, null, null) : null);
    }
}
