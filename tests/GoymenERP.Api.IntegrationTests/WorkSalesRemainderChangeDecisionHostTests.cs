using System.Globalization;
using System.Reflection;
using GoymenERP.Api.WorkRuntime;
using GoymenERP.BuildingBlocks.Multitenancy;
using GoymenERP.BuildingBlocks.Observability;
using GoymenERP.BuildingBlocks.Security;
using GoymenERP.BuildingBlocks.Work;
using GoymenERP.Modules.Sales.Application;
using GoymenERP.Modules.Sales.Application.Contracts;
using GoymenERP.Modules.Sales.Infrastructure.Persistence;
using GoymenERP.Modules.Sales.Infrastructure.WorkInbox;
using GoymenERP.Modules.Work.Infrastructure.Delegations;
using static GoymenERP.Api.IntegrationTests.WorkSalesRemainderChangeDecisionPreviewV1Tests;

namespace GoymenERP.Api.IntegrationTests;

// Actual Work source/host/WP3; controlled native protocol, not SQL/Audit/owned-transaction acceptance.
public sealed class WorkSalesRemainderChangeDecisionHostTests
{
    private const string Kind = WorkDecisionHostV1.SalesRemainderChange, Key = "SENTETIK-remainder-work", Corr = "SENTETIK-remainder-original";
    private static WorkDecisionRequestV1 Request(bool approve = true, string? reason = null) => new("6", approve, reason);

    [Theory] [InlineData(true, null)] [InlineData(false, null)] [InlineData(false, "")] [InlineData(false, "   ")] [InlineData(false, "  SENTETIK ret  ")]
    public async Task Two_current_human_reads_and_one_selected_POST_keep_native_changeNo_version_and_optional_literal_rejection(bool approve, string? reason)
    {
        var r = new Rig(); var preview = await r.Preview(); var human = Assert.IsType<WorkSalesRemainderChangeDecisionPreviewV1>(preview.SalesRemainderChange);
        Assert.Equal(("6", true, true, true), (preview.SourceVersion, preview.Pending, preview.ApprovalSupported, preview.RejectionSupported));
        Assert.Equal(r.Native.Current.Change, human.Change); Assert.Equal(2, r.Native.Reads);
        Assert.Equal(r.Native.LastReadPermissionCount, r.Authority.Permissions.Count); // No authority await after owner final persona capture.
        var before = r.Native.Reads;
        var q = Request(approve, reason); var result = await r.Decide(q);
        Assert.Equal((approve ? "Approved" : "Rejected", false, Corr), (result.State, result.Replayed, result.OriginalCorrelationId));
        Assert.Equal(before + 1, r.Native.Reads); Assert.Equal(1, r.Native.HistoryReads); Assert.Equal("6", r.Source.Selected);
        Assert.Equal(3, r.Native.LastChangeNo); Assert.Equal(new SalesDraftRemainderDecisionRequest("6", reason), r.Native.LastBody);
        Assert.Equal((Key, Corr, "SENTETIK-cause"), (r.Native.LastMetadata!.IdempotencyKey, r.Native.LastMetadata.CorrelationId, r.Native.LastMetadata.CausationId));
        Assert.Equal(1, r.Native.Calls); Assert.Equal(1, r.Native.Effects); Assert.Single(r.Native.Commands);
        Assert.All(r.Authority.Permissions, p => Assert.Equal(SalesPermissionManifestV1.OrdersApprove, p)); // No general read or price co-grant.
    }

    [Theory] [InlineData(true)] [InlineData(false)]
    public async Task SAT_D2_only_SalesSupport_is_blocked_by_own_request_or_own_draft(bool ownRequest)
    {
        var r = new Rig(); r.Native.Current = Seal(r.Native.Current with { Change = r.Native.Current.Change with { RequestedBy = ownRequest ? Actor.ToString("D") : Maker.ToString("D") },
            DraftCreatedBy = ownRequest ? Maker.ToString("D") : Actor.ToString("D") });
        r.Native.Support = true;
        Assert.Equal(403, (await Assert.ThrowsAsync<WorkDecisionException>(() => r.Decide(Request()))).Status); Assert.Equal(0, r.Native.Calls); Assert.Equal(0, r.Native.Effects);
        r.Native.Support = false;
        Assert.False((await r.Decide(Request())).Replayed); Assert.Equal(1, r.Native.Effects); // No new global maker prohibition.
    }

    [Fact]
    public async Task Original_N_to_N_plus_one_replay_survives_new_self_requested_change_and_current_native_N_plus_k_reply()
    {
        var r = new Rig(); var q = Request(false, "  SENTETIK ret  "); await r.Decide(q); var original = r.Native.Commands[Key];
        r.Native.Current = Seal(r.Native.Current with { Version = "12", Change = Facts().Change with { ChangeNo = 4, RequestedBy = Actor.ToString("D") },
            DraftCreatedBy = Actor.ToString("D"), ApprovalSupported = true, RejectionSupported = true }); r.Native.Support = true;
        Assert.Equal(403, (await Assert.ThrowsAsync<WorkDecisionException>(() => r.Preview())).Status); var reads = r.Native.Reads;
        r.SetCorrelation("SENTETIK-retry"); var answer = await r.Decide(q);
        Assert.Equal(("Rejected", true, Corr), (answer.State, answer.Replayed, answer.OriginalCorrelationId)); Assert.Equal(reads, r.Native.Reads);
        Assert.Equal((6L, 7L, 3), (original.ExpectedVersion, original.ResultVersion, r.Native.LastChangeNo));
        Assert.Equal("12", r.Native.LastAnswer!.Detail.Draft.Version); Assert.Equal(4, r.Native.Current.Change.ChangeNo);
        Assert.Equal(1, r.Native.Effects); Assert.Single(r.Native.Commands); Assert.Equal(original.OriginalDecisionDigest, r.Native.Commands[Key].OriginalDecisionDigest);
    }

    [Theory] [InlineData("missing")] [InlineData("actor")] [InlineData("company")] [InlineData("key")] [InlineData("payload")]
    [InlineData("version")] [InlineData("line")] [InlineData("digest")] [InlineData("response")] [InlineData("correlation")]
    public async Task Forged_or_missing_postnative_proof_is_unknown_with_retained_effect_then_original_key_reconciles_once(string fault)
    {
        var r = new Rig(); r.Native.ChangeReceipt = p => fault switch
        { "missing" => null, "actor" => p with { ActorId = Maker.ToString("D") }, "company" => p with { CompanyId = Guid.NewGuid() },
            "key" => p with { IdempotencyKeyHash = new('A', 64) }, "payload" => p with { PayloadHash = new('a', 64) }, "version" => p with { ResultVersion = 8 },
            "line" => p with { LineId = Guid.NewGuid() }, "digest" => p with { OriginalDecisionDigest = new('b', 64) },
            "response" => p with { CurrentResponseDigest = new('c', 64) }, _ => p with { OriginalCorrelationId = "SENTETIK-other" } };
        Assert.Equal(503, (await Assert.ThrowsAsync<WorkDecisionException>(() => r.Decide(Request()))).Status);
        Assert.Equal(1, r.Native.Effects); Assert.Single(r.Native.Commands); // A release failure cannot undo this retained controlled effect.
        r.Native.ChangeReceipt = null; r.SetCorrelation("SENTETIK-reconcile"); var replay = await r.Decide(Request());
        Assert.True(replay.Replayed); Assert.Equal(Corr, replay.OriginalCorrelationId); Assert.Equal(1, r.Native.Effects); Assert.Single(r.Native.Commands);
    }

    [Theory] [InlineData("approve-reason")] [InlineData("reject-long")] [InlineData("zero")] [InlineData("padded")] [InlineData("max")]
    [InlineData("floor")] [InlineData("quote")] [InlineData("cancel")]
    public async Task Invalid_or_foreign_body_is_rejected_before_source_IO(string fault)
    {
        var r = new Rig(); var q = fault switch { "approve-reason" => Request(true, ""), "reject-long" => Request(false, new('x', 4001)),
            "zero" => Request() with { ExpectedVersion = "0" }, "padded" => Request() with { ExpectedVersion = "06" }, "max" => Request() with { ExpectedVersion = long.MaxValue.ToString(CultureInfo.InvariantCulture) },
            "floor" => Request() with { SalesPriceFloor = new([]) }, "quote" => Request() with { SalesQuoteConversion = new(Id, 1, 0, "6") }, _ => Request() with { InventoryCostCancellation = new("1") } };
        Assert.Equal(400, (await Assert.ThrowsAsync<WorkDecisionException>(() => r.Decide(q))).Status);
        Assert.Equal(0, r.Source.Reads); Assert.Equal(0, r.Native.Reads); Assert.Equal(0, r.Native.HistoryReads); Assert.Equal(0, r.Native.Calls);
    }

    [Theory] [InlineData("missing")] [InlineData("caption")] [InlineData("reference")] [InlineData("line")] [InlineData("version")]
    [InlineData("digest")] [InlineData("company")] [InlineData("foreign-carrier")]
    public async Task Mismatched_source_cannot_select_a_native_change_or_mutation(string fault)
    {
        var r = new Rig(); r.Source.Change = s => fault switch { "missing" => s with { SalesRemainderChangeProof = null },
            "caption" => s with { Item = s.Item with { DocumentLabel = "SENTETIK-other" } },
            "reference" => s with { Item = s.Item with { References = s.Item.References.Select(x => x with { RecordId = Guid.NewGuid() }).ToArray() } },
            "line" => s with { SalesRemainderChangeProof = s.SalesRemainderChangeProof! with { LineId = Guid.NewGuid() } },
            "version" => s with { SalesRemainderChangeProof = s.SalesRemainderChangeProof! with { SelectedExpectedVersion = 7 } },
            "digest" => s with { SalesRemainderChangeProof = s.SalesRemainderChangeProof! with { SourceFactsDigest = "bad" } }, "company" => s with { CompanyId = Guid.NewGuid() },
            _ => s with { InventoryCostCancellationProof = new(Company, Id, Id, Id, 1, 1, Id, Maker, At, At, "Prepared", "", "") } };
        Assert.Equal(503, (await Assert.ThrowsAsync<WorkDecisionException>(() => r.Decide(Request()))).Status); Assert.Equal(0, r.Native.Calls); Assert.Equal(0, r.Native.Effects);
    }

    [Theory] [InlineData(false, "permission")] [InlineData(true, "permission")] [InlineData(false, "delegation")] [InlineData(true, "delegation")]
    [InlineData(false, "actor")] [InlineData(true, "actor")] [InlineData(false, "company")] [InlineData(true, "company")]
    [InlineData(false, "membership")] [InlineData(true, "membership")] [InlineData(false, "cancel")] [InlineData(true, "cancel")]
    public async Task Native_and_Work_current_authority_fences_refuse_loss_before_effect_or_after_retained_commit(bool retained, string fault)
    {
        var r = new Rig(); using var cts = new CancellationTokenSource();
        void Revoke() { if (fault == "permission") r.Authority.Allowed = false; if (fault == "delegation") r.Delegations.Live = false;
            if (fault == "actor") r.User.ActorId = Maker; if (fault == "company") r.Company.Set(Guid.NewGuid()); if (fault == "membership") r.User.Member = false; if (fault == "cancel") cts.Cancel(); }
        if (retained) r.Native.AfterEffect = Revoke; else r.Native.BeforeEffect = Revoke;
        var q = Request() with { DelegationId = Guid.NewGuid() };
        if (fault == "cancel") await Assert.ThrowsAnyAsync<OperationCanceledException>(() => r.Decide(q, token: cts.Token));
        else Assert.Equal(403, (await Assert.ThrowsAsync<WorkDecisionException>(() => r.Decide(q, token: cts.Token))).Status);
        Assert.Equal(retained ? 1 : 0, r.Native.Effects); Assert.Equal(retained ? 1 : 0, r.Native.Commands.Count);
    }

    [Theory] [InlineData("facts")] [InlineData("permission")] [InlineData("actor")] [InlineData("company")] [InlineData("cancel")]
    public async Task Final_human_read_never_releases_changed_facts_or_context(string fault)
    {
        var r = new Rig(); using var cts = new CancellationTokenSource(); r.Native.OnRead = n => { if (n != 2) return;
            if (fault == "facts") r.Native.Current = Seal(r.Native.Current with { DocumentNo = "SENTETIK-other" }); if (fault == "permission") r.Authority.Allowed = false;
            if (fault == "actor") r.User.ActorId = Maker; if (fault == "company") r.Company.Set(Guid.NewGuid()); if (fault == "cancel") cts.Cancel(); };
        if (fault == "cancel") await Assert.ThrowsAnyAsync<OperationCanceledException>(() => r.Host.PreviewAsync(Kind, Id, cts.Token));
        else Assert.Equal(fault == "facts" ? 503 : 403, (await Assert.ThrowsAsync<WorkDecisionException>(() => r.Preview())).Status);
        Assert.Equal(2, r.Native.Reads); Assert.Equal(0, r.Native.Calls);
    }

    [Theory] [InlineData(404, "not_found")] [InlineData(409, "version_conflict")] [InlineData(422, "validation_failed")]
    public async Task Native_refusal_wire_identity_is_preserved(int status, string code)
    {
        var r = new Rig(); r.Native.Refusal = new(status, code, isReplay: true, originalCorrelationId: "SENTETIK-refusal");
        var e = await Assert.ThrowsAsync<WorkDecisionException>(() => r.Decide(Request())); Assert.Equal((status, code, true, "SENTETIK-refusal"), (e.Status, e.Code, e.Replayed, e.OriginalCorrelationId));
        Assert.Equal(0, r.Native.Effects);
    }

    [Fact]
    public async Task Unknown_retry_keeps_the_original_raw_rejection_body_and_does_not_consume_an_altered_payload()
    {
        var r = new Rig(); var q = Request(false, "  SENTETIK ret  "); r.Native.ChangeReceipt = _ => null;
        Assert.Equal(503, (await Assert.ThrowsAsync<WorkDecisionException>(() => r.Decide(q))).Status); var hash = r.Native.Commands[Key].PayloadHash;
        r.Native.ChangeReceipt = null;
        Assert.Equal(409, (await Assert.ThrowsAsync<WorkDecisionException>(() => r.Decide(q with { Reason = q.Reason!.Trim() }))).Status);
        Assert.True((await r.Decide(q)).Replayed); Assert.Equal(hash, r.Native.Commands[Key].PayloadHash); Assert.Equal(q.Reason, r.Native.LastBody!.Reason);
        Assert.Equal(1, r.Native.Effects); Assert.Single(r.Native.Commands);
    }

    [Theory] [InlineData("permission")] [InlineData("scope")]
    public async Task Strict_current_native_authority_is_required_before_any_owner_source_IO(string fault)
    {
        var r = new Rig(); if (fault == "permission") r.Authority.Allowed = false; else r.Authority.CompanyWide = false;
        Assert.Equal(403, (await Assert.ThrowsAsync<WorkDecisionException>(() => r.Decide(Request()))).Status);
        Assert.Equal(0, r.Source.Reads); Assert.Equal(0, r.Native.Reads); Assert.Equal(0, r.Native.HistoryReads); Assert.Equal(0, r.Native.Calls);
    }

    [Theory] [InlineData("missing-owner")] [InlineData("no-read-port")] [InlineData("legacy")]
    public async Task Missing_same_instance_owner_read_or_original_provenance_fails_closed(string fault)
    {
        var r = new Rig(); if (fault == "legacy") { await r.Decide(Request()); r.Native.Legacy = true; }
        else r.Host = r.BuildHost(fault == "missing-owner" ? null : DispatchProxy.Create<ISalesDraftService, OwnerProxy>());
        Assert.Equal(503, (await Assert.ThrowsAsync<WorkDecisionException>(() => r.Decide(Request()))).Status); Assert.Equal(fault == "legacy" ? 1 : 0, r.Native.Effects);
    }

    public interface IRemainderOwner : ISalesDraftService, ISalesRemainderDecisionReadV1 { }
    public class OwnerProxy : DispatchProxy
    {
        internal Func<MethodInfo, object?[], object?> Handler = (_, _) => throw new NotSupportedException("No unrelated native command/general read");
        protected override object? Invoke(MethodInfo? method, object?[]? args) => Handler(method!, args!);
    }
    private sealed class Rig
    {
        internal readonly MutableCompany Company = new(); internal readonly MutableUser User = new(); internal readonly Authority Authority = new();
        internal readonly Delegations Delegations = new(); internal readonly Protocol Native; internal readonly SourceProxy Source; internal readonly IRemainderOwner Owner;
        internal WorkDecisionHostV1 Host; private CorrelationContext correlation = new(); private readonly WorkDecisionExecutionFenceV1 execution = new(); private readonly WorkDecisionAuthorityV1 decorated;
        internal Rig()
        {
            Company.Set(WorkSalesRemainderChangeDecisionPreviewV1Tests.Company); correlation.Set(Corr, "SENTETIK-cause", "Web", null, null); decorated = new(Authority, execution);
            Native = new(this, decorated); Owner = DispatchProxy.Create<IRemainderOwner, OwnerProxy>();
            ((OwnerProxy)(object)Owner).Handler = (m, a) => m.Name switch {
                nameof(ISalesRemainderDecisionReadV1.ReadRemainderDecisionAsync) => Native.Read((Guid)a[0]!, (Func<CancellationToken, Task>)a[1]!, (CancellationToken)a[2]!),
                nameof(ISalesRemainderDecisionReadV1.ReadRemainderOriginalVersionAsync) => Native.History((Guid)a[0]!, (string)a[1]!, (Func<CancellationToken, Task>)a[2]!, (CancellationToken)a[3]!),
                nameof(ISalesDraftService.ApproveRemainderChangeAsync) => Native.Decide((Guid)a[0]!, (int)a[1]!, (SalesDraftRemainderDecisionRequest)a[2]!, (SalesCommandMetadata)a[3]!, true, (CancellationToken)a[4]!),
                nameof(ISalesDraftService.RejectRemainderChangeAsync) => Native.Decide((Guid)a[0]!, (int)a[1]!, (SalesDraftRemainderDecisionRequest)a[2]!, (SalesCommandMetadata)a[3]!, false, (CancellationToken)a[4]!), _ => throw new NotSupportedException(m.Name) };
            Source = new(new SalesRemainderChangeWorkInboxSourceV1(Company, User, decorated, Owner)); Host = BuildHost(Owner);
        }
        internal WorkDecisionHostV1 BuildHost(ISalesDraftService? owner) => new(Company, User, correlation, decorated, [Source], Delegations, execution, null!, null!, salesOrders: owner);
        internal void SetCorrelation(string value) { correlation = new(); correlation.Set(value, "SENTETIK-cause", "Web", null, null); Host = BuildHost(Owner); }
        internal Task<WorkDecisionPreviewV1> Preview() => Host.PreviewAsync(Kind, Id, default);
        internal Task<WorkDecisionResultV1> Decide(WorkDecisionRequestV1 q, string key = Key, CancellationToken token = default) => Host.DecideAsync(Kind, Id, q, key, token);
    }
    private sealed class Protocol(Rig rig, IErpAuthorityRevalidationService authority)
    {
        internal SalesRemainderDecisionDetailV1 Current = Facts(); internal int Reads, HistoryReads, Calls, Effects, LastChangeNo, LastReadPermissionCount; internal bool Support, Legacy;
        internal Action<int>? OnRead; internal Action? BeforeEffect, AfterEffect; internal SalesDraftServiceException? Refusal;
        internal SalesDraftRemainderDecisionRequest? LastBody; internal SalesCommandMetadata? LastMetadata; internal SalesOrderDraftResponse? LastAnswer;
        internal Func<SalesRemainderOriginalReceiptV1, SalesRemainderOriginalReceiptV1?>? ChangeReceipt; internal readonly Dictionary<string, SalesRemainderOriginalReceiptV1> Commands = [];
        internal async Task<SalesRemainderDecisionReadResultV1> Read(Guid id, Func<CancellationToken, Task> fence, CancellationToken ct)
        {
            await fence(ct); Reads++; OnRead?.Invoke(Reads); await fence(ct);
            if (id != Id) return new(SalesRemainderDecisionReadStateV1.NotFound, null);
            if (Support && Current.Change.State == "Pending" && (Current.Change.RequestedBy == rig.User.UserId || Current.DraftCreatedBy == rig.User.UserId)) return new(SalesRemainderDecisionReadStateV1.Denied, null);
            LastReadPermissionCount = rig.Authority.Permissions.Count;
            return new(SalesRemainderDecisionReadStateV1.Found, Current with { IsSalesSupport = Support });
        }
        internal async Task<SalesRemainderOriginalVersionReadResultV1> History(Guid id, string version, Func<CancellationToken, Task> fence, CancellationToken ct)
        {
            await fence(ct); HistoryReads++; await fence(ct); if (Legacy) return new(SalesRemainderDecisionReadStateV1.Unavailable, null);
            var rows = Commands.Values.Where(x => x.DraftId == id && x.ActorId == rig.User.UserId && x.ExpectedVersion.ToString(CultureInfo.InvariantCulture) == version).ToArray();
            return rows.Length == 1 ? new(SalesRemainderDecisionReadStateV1.Found, rows[0]) { CurrentVersion = long.Parse(Current.Version, CultureInfo.InvariantCulture) }
                : new(SalesRemainderDecisionReadStateV1.NotFound, null) { CurrentVersion = long.Parse(Current.Version, CultureInfo.InvariantCulture) };
        }
        internal async Task<SalesOrderDraftResponse> Decide(Guid id, int changeNo, SalesDraftRemainderDecisionRequest body, SalesCommandMetadata m, bool approve, CancellationToken ct)
        {
            Calls++; LastChangeNo = changeNo; LastBody = body; LastMetadata = m; var actor = rig.User.UserId; BeforeEffect?.Invoke(); await Fence(actor, ct); if (Refusal is not null) throw Refusal;
            var command = approve ? SalesDraftCommands.ApproveRemainderChangeV1 : SalesDraftCommands.RejectRemainderChangeV1;
            var hash = SalesDraftRequestHash.Compute(command, id, changeNo, body); var replay = Commands.TryGetValue(m.IdempotencyKey, out var saved);
            if (replay && (saved!.ActorId != actor || saved.PayloadHash != hash || saved.CommandName != command)) throw new SalesDraftServiceException(409, SalesDraftProblemCodesV1.SameKeyPayloadMismatch);
            if (!replay)
            {
                if (body.ExpectedVersion != Current.Version || Current.Change.ChangeNo != changeNo) throw new SalesDraftServiceException(409, SalesDraftProblemCodesV1.VersionConflict);
                if (Support && (Current.Change.RequestedBy == actor || Current.DraftCreatedBy == actor)) throw new SalesDraftServiceException(403, "forbidden");
                var original = Current; var n = long.Parse(body.ExpectedVersion, CultureInfo.InvariantCulture); var decided = Current.Change with { State = approve ? "Approved" : "Rejected", DecidedBy = actor,
                    DecidedAtUtc = At.AddSeconds(1), DecisionReason = string.IsNullOrWhiteSpace(body.Reason) ? null : body.Reason.Trim(), ResultQuantity = approve ? Current.Change.RequestedQuantity : null, LoadedAtDecision = approve ? "1" : null };
                Current = Seal(Current with { Version = (n + 1).ToString(CultureInfo.InvariantCulture), Change = decided, ApprovalSupported = false, RejectionSupported = false });
                var p = new SalesRemainderOriginalReceiptV1(WorkSalesRemainderChangeDecisionPreviewV1Tests.Company, id, original.DocumentNo, changeNo, original.Change.LineId, command, actor,
                    SalesStore.IdempotencyKeyHash(m.IdempotencyKey), hash, n, n + 1, m.CorrelationId, At.AddSeconds(1), decided, Guid.NewGuid(), new('A', 64), original.SourceFactsDigest, "")
                    { DraftCreatedBy = original.DraftCreatedBy, CreatorProofEstablished = true };
                saved = p with { OriginalDecisionDigest = SalesRemainderDecisionDigestsV1.OriginalDecisionDigest(p) }; Commands.Add(m.IdempotencyKey, saved); Effects++; AfterEffect?.Invoke();
            }
            await Fence(actor, ct); var answer = new SalesOrderDraftResponse(NativeDetail(Current), replay, saved!.OriginalCorrelationId);
            var receipt = saved with { CurrentResponseDigest = SalesRemainderDecisionDigestsV1.CurrentResponseDigest(answer) };
            LastAnswer = answer with { OriginalRemainderDecisionReceipt = ChangeReceipt is null ? receipt : ChangeReceipt(receipt) }; return LastAnswer;
        }
        private async Task Fence(string actor, CancellationToken ct)
        {
            ct.ThrowIfCancellationRequested(); var p = await authority.RevalidatePermissionScopeAsync(SalesPermissionManifestV1.OrdersApprove, WorkSalesRemainderChangeDecisionPreviewV1Tests.Company, ct);
            ct.ThrowIfCancellationRequested(); if (!p.IsGranted || !p.IsCompanyWide || rig.User.UserId != actor || !rig.User.Member || rig.Company.CompanyId != WorkSalesRemainderChangeDecisionPreviewV1Tests.Company)
                throw new SalesDraftServiceException(403, "forbidden");
        }
    }
    private static SalesOrderDraftDetailView NativeDetail(SalesRemainderDecisionDetailV1 d) => new(new(d.DraftId, d.CompanyId, d.DocumentNo, "Order", d.State, d.Version, "Rev.0",
        Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), new(2026, 11, 28), At, 1, d.ApprovalState), null, null,
        [new(d.Line!.Id, d.Line.LineNo, d.Line.ProductId, "1", d.Line.ProductCode, d.Line.ProductName, d.Line.OrderUnitId, d.Line.CurrentQuantity,
            null, null, null, null, null, null, SalesPriceDisclosure.Withheld)], null, null, null, null, null, null,
        new(null, null, null, null, null), new(false, null, null, null, false), false, false);
    private sealed class SourceProxy(IWorkApprovalDecisionVersionSourceV1 inner) : IWorkApprovalDecisionVersionSourceV1
    {
        internal int Reads; internal string? Selected; internal Func<WorkApprovalDecisionSnapshotV1, WorkApprovalDecisionSnapshotV1>? Change;
        public string Module => inner.Module; public string Kind => inner.Kind; public string ApproverPermission => inner.ApproverPermission;
        public Task<WorkInboxSourceResultV1> ListPendingAsync(WorkInboxSourceContextV1 c, int n, CancellationToken ct) => throw new NotSupportedException();
        public async Task<WorkApprovalDecisionSnapshotV1?> ReadDecisionAsync(WorkInboxSourceContextV1 c, Guid id, CancellationToken ct)
        { Reads++; var s = await inner.ReadDecisionAsync(c, id, ct); return s is null ? null : Change?.Invoke(s) ?? s; }
        public async Task<WorkApprovalDecisionSnapshotV1?> ReadDecisionVersionAsync(WorkInboxSourceContextV1 c, Guid id, string v, CancellationToken ct)
        { Reads++; Selected = v; var s = await inner.ReadDecisionVersionAsync(c, id, v, ct); return s is null ? null : Change?.Invoke(s) ?? s; }
    }
    private sealed class MutableCompany : ICompanyContext { public Guid CompanyId { get; private set; } public bool IsResolved => CompanyId != Guid.Empty; public void Set(Guid id) => CompanyId = id; }
    private sealed class MutableUser : ICurrentUser
    {
        internal Guid ActorId = Actor; internal bool Member = true; public bool IsAuthenticated => true; public string UserId => ActorId.ToString("D");
        public IReadOnlySet<Guid> AllowedCompanyIds { get; } = new HashSet<Guid>([WorkSalesRemainderChangeDecisionPreviewV1Tests.Company]);
        public bool CanAccessCompany(Guid id) => Member && id == WorkSalesRemainderChangeDecisionPreviewV1Tests.Company;
    }
    private sealed class Authority : IErpAuthorityRevalidationService
    {
        internal bool Allowed = true, CompanyWide = true; internal readonly List<string> Permissions = [];
        public Task<PermissionScope> RevalidatePermissionScopeAsync(string p, Guid c, CancellationToken ct = default)
        { Permissions.Add(p); return Task.FromResult(new PermissionScope(Allowed && p == SalesPermissionManifestV1.OrdersApprove, CompanyWide, new HashSet<string>(), new HashSet<Guid>(), new HashSet<Guid>(), new HashSet<Guid>())); }
    }
    private sealed class Delegations : IWorkDelegationReaderV1
    {
        internal bool Live = true;
        public Task<WorkDelegationViewV1?> FindForActorAsync(Guid id, WorkInboxItemV1 item, CancellationToken ct = default) => Task.FromResult<WorkDelegationViewV1?>(Live
            ? new(id, Company, Kind, SalesPermissionManifestV1.OrdersApprove, Maker, Actor, At.AddYears(-1), At.AddYears(1), "1", Maker, At, "SENTETIK", null, null, null) : null);
    }
}
