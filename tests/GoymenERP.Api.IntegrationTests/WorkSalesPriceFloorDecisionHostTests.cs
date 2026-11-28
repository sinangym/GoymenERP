using System.Globalization;
using System.Reflection;
using GoymenERP.Api.WorkRuntime;
using GoymenERP.BuildingBlocks.Multitenancy;
using GoymenERP.BuildingBlocks.Observability;
using GoymenERP.BuildingBlocks.Security;
using GoymenERP.BuildingBlocks.Work;
using GoymenERP.Modules.Sales.Application;
using GoymenERP.Modules.Sales.Application.Contracts;
using GoymenERP.Modules.Sales.Infrastructure.WorkInbox;
using GoymenERP.Modules.Work.Infrastructure.Delegations;
using static GoymenERP.Api.IntegrationTests.WorkSalesPriceFloorDecisionPreviewV1Tests;

namespace GoymenERP.Api.IntegrationTests;

// Real Work source/host/WP3 decorator; controlled native protocol only, not SQL/Audit/transaction acceptance.
public sealed class WorkSalesPriceFloorDecisionHostTests
{
    private const string Kind = WorkDecisionHostV1.SalesPriceFloor, Key = "SENTETIK-floor-work-key", Corr = "SENTETIK-floor-original";

    [Theory] [InlineData(true)] [InlineData(false)]
    public async Task Human_GET_has_two_exact_reads_POST_has_one_selected_source_and_no_second_human_GET(bool prices)
    {
        var r = new Rig(); r.Authority.Prices = prices;
        var preview = await r.Preview(); var human = Assert.IsType<WorkSalesPriceFloorDecisionPreviewV1>(preview.SalesPriceFloor);
        Assert.Equal(("9", true, true, false), (preview.SourceVersion, preview.Pending, preview.ApprovalSupported, preview.RejectionSupported));
        Assert.Equal(2, r.Native.Reads); Assert.Equal(prices, human.PriceDisclosure == SalesPriceDisclosure.Visible);
        var q = Request(); var reads = r.Native.Reads; var result = await r.Decide(q);
        Assert.Equal(("Preparing", false, Corr), (result.State, result.Replayed, result.OriginalCorrelationId));
        Assert.Equal(reads + 1, r.Native.Reads); Assert.Equal(1, r.Native.HistoryReads); Assert.Equal("9", r.Source.Selected);
        Assert.Equal(1, r.Native.Calls); Assert.Equal(1, r.Native.Effects); Assert.Single(r.Native.Commands);
        Assert.Equal(q.ExpectedVersion, r.Native.LastBody!.ExpectedVersion); Assert.Equal(q.SalesPriceFloor!.Lines, r.Native.LastBody.Lines);
        Assert.Equal(Key, r.Native.LastMetadata!.IdempotencyKey); Assert.Equal((Corr, "SENTETIK-cause"), (r.Native.LastMetadata.CorrelationId, r.Native.LastMetadata.CausationId));
        Assert.All(r.Authority.Permissions, p => Assert.True(p is SalesPermissionManifestV1.OrdersPriceFloorExceptionApprove or SalesPermissionManifestV1.OrdersPriceRead));
        Assert.DoesNotContain(SalesPermissionManifestV1.DraftsRead, r.Authority.Permissions);
    }

    [Fact]
    public async Task Historical_original_retry_survives_later_self_prepared_commercial_version_without_human_history_or_reexecution()
    {
        var r = new Rig(); var q = Request(); await r.Decide(q);
        r.Native.Current = r.Native.Current with { Version = 11, Lines = r.Native.Current.Lines.Select(x => x with
        { Commercial = x.Commercial with { FloorException = x.Commercial.FloorException! with { RequestedBy = Actor.ToString("D"), ApprovedBy = null, ApprovedAtUtc = null, Reason = null } } }).ToArray() };
        Assert.Equal(403, (await Assert.ThrowsAsync<WorkDecisionException>(() => r.Preview())).Status);
        var reads = r.Native.Reads; r.SetCorrelation("SENTETIK-later-retry"); var replay = await r.Decide(q);
        Assert.Equal((true, "Preparing", Corr), (replay.Replayed, replay.State, replay.OriginalCorrelationId));
        Assert.Equal(reads, r.Native.Reads); Assert.Equal(1, r.Native.Effects); Assert.Single(r.Native.Commands); Assert.Equal(11, r.Native.Current.Version);
        Assert.Equal("9", r.Source.Selected);
    }

    [Theory] [InlineData("missing")] [InlineData("actor")] [InlineData("company")] [InlineData("key")] [InlineData("payload")]
    [InlineData("version")] [InlineData("digest")] [InlineData("response")] [InlineData("correlation")]
    public async Task Missing_or_forged_postnative_receipt_is_unknown_then_same_frozen_key_reconciles_once(string fault)
    {
        var r = new Rig(); r.Native.ChangeReceipt = p => fault switch
        {
            "missing" => null, "actor" => p with { ActorId = Maker.ToString("D") }, "company" => p with { CompanyId = Guid.NewGuid() },
            "key" => p with { IdempotencyKeyHash = new('A', 64) }, "payload" => p with { PayloadHash = new('b', 64) },
            "version" => p with { ExpectedVersion = 10 }, "digest" => p with { OriginalDecisionDigest = new('c', 64) },
            "response" => p with { CurrentResponseDigest = new('d', 64) }, _ => p with { OriginalCorrelationId = "another" }
        };
        Assert.Equal(503, (await Assert.ThrowsAsync<WorkDecisionException>(() => r.Decide(Request()))).Status);
        Assert.Equal(1, r.Native.Effects); Assert.Single(r.Native.Commands); // Already retained native effect, not rollback evidence.
        r.Native.ChangeReceipt = null; r.SetCorrelation("SENTETIK-reconcile");
        var answer = await r.Decide(Request()); Assert.True(answer.Replayed); Assert.Equal(Corr, answer.OriginalCorrelationId);
        Assert.Equal(1, r.Native.Effects); Assert.Single(r.Native.Commands);
    }

    [Theory] [InlineData("null-selection")] [InlineData("empty")] [InlineData("null-lines")] [InlineData("null-line")] [InlineData("duplicate")]
    [InlineData("zero-id")] [InlineData("blank-reason")] [InlineData("long-reason")] [InlineData("501")]
    [InlineData("reject")] [InlineData("top-reason")] [InlineData("version-zero")] [InlineData("version-padded")] [InlineData("version-max")]
    [InlineData("quote")] [InlineData("cancellation")]
    public async Task Invalid_or_foreign_selection_is_refused_before_any_source_IO(string fault)
    {
        var r = new Rig(); var q = Request(); var lines = q.SalesPriceFloor!.Lines;
        q = fault switch
        {
            "null-selection" => q with { SalesPriceFloor = null }, "empty" => q with { SalesPriceFloor = new([]) },
            "null-lines" => q with { SalesPriceFloor = new(null!) }, "null-line" => q with { SalesPriceFloor = new([null!]) },
            "duplicate" => q with { SalesPriceFloor = new([lines[0], lines[0]]) },
            "zero-id" => q with { SalesPriceFloor = new([lines[0] with { LineId = Guid.Empty }]) },
            "blank-reason" => q with { SalesPriceFloor = new([lines[0] with { Reason = " \t " }]) },
            "long-reason" => q with { SalesPriceFloor = new([lines[0] with { Reason = new('x', 4001) }]) },
            "501" => q with { SalesPriceFloor = new(Enumerable.Range(0, 501).Select(_ => new SalesDraftPriceFloorExceptionApprovalLineV1(Guid.NewGuid(), "SENTETIK")).ToArray()) },
            "reject" => q with { Approve = false }, "top-reason" => q with { Reason = "SENTETIK" },
            "version-zero" => q with { ExpectedVersion = "0" }, "version-padded" => q with { ExpectedVersion = "09" },
            "version-max" => q with { ExpectedVersion = long.MaxValue.ToString(CultureInfo.InvariantCulture) },
            "quote" => q with { SalesQuoteConversion = new(Id, 1, 0, "9") },
            _ => q with { InventoryCostCancellation = new("1") }
        };
        Assert.Equal(400, (await Assert.ThrowsAsync<WorkDecisionException>(() => r.Decide(q))).Status);
        Assert.Equal(0, r.Source.Reads); Assert.Equal(0, r.Native.Reads); Assert.Equal(0, r.Native.HistoryReads); Assert.Equal(0, r.Native.Calls);
    }

    [Theory] [InlineData("missing")] [InlineData("extra")] [InlineData("other")]
    public async Task Ordered_selection_must_name_every_source_floor_line_exactly_once(string fault)
    {
        var r = new Rig(); var q = Request(); var lines = q.SalesPriceFloor!.Lines;
        q = q with { SalesPriceFloor = new(fault switch
        {
            "missing" => [lines[0]], "extra" => [.. lines, new(Guid.NewGuid(), "SENTETIK")],
            _ => [lines[0], lines[1] with { LineId = Guid.NewGuid() }]
        }) };
        Assert.Equal(400, (await Assert.ThrowsAsync<WorkDecisionException>(() => r.Decide(q))).Status);
        Assert.Equal(1, r.Source.Reads); Assert.Equal(0, r.Native.Calls); Assert.Equal(0, r.Native.Effects);
    }

    [Fact]
    public async Task Caller_mutation_during_source_await_cannot_replace_the_frozen_native_line_order_or_reasons()
    {
        var r = new Rig(); var lines = Request().SalesPriceFloor!.Lines.ToList(); var expected = lines.ToArray();
        r.Native.OnRead = _ => { lines.Reverse(); lines[0] = lines[0] with { Reason = "another" }; };
        var result = await r.Decide(new("9", true, null) { SalesPriceFloor = new(lines) });
        Assert.False(result.Replayed); Assert.Equal(expected, r.Native.LastBody!.Lines); Assert.Equal(1, r.Native.Effects);
    }

    [Theory] [InlineData("missing")] [InlineData("caption")] [InlineData("reference")] [InlineData("requester")] [InlineData("digest")]
    [InlineData("company")] [InlineData("permission")] [InlineData("foreign-carrier")]
    public async Task Source_mismatch_or_wrong_authority_cannot_reach_the_native_command(string fault)
    {
        var r = new Rig(); r.Source.Change = s => fault switch
        {
            "missing" => s with { SalesPriceFloorProof = null }, "caption" => s with { Item = s.Item with { DocumentLabel = "another" } },
            "reference" => s with { Item = s.Item with { References = s.Item.References.Select(x => x with { RecordId = Guid.NewGuid() }).ToArray() } },
            "requester" => s with { SalesPriceFloorProof = s.SalesPriceFloorProof! with { Lines = s.SalesPriceFloorProof!.Lines.Select(x => x with { RequestedBy = "another" }).ToArray() } },
            "digest" => s with { SalesPriceFloorProof = s.SalesPriceFloorProof! with { SourceFactsDigest = "bad" } },
            "company" => s with { CompanyId = Guid.NewGuid() },
            "foreign-carrier" => s with { InventoryCostCancellationProof = new(Company, Id, Id, Id, 1, 1, Id, Maker, At, At, "Prepared", "", "") },
            _ => s
        };
        if (fault == "permission") r.Source.Permission = SalesPermissionManifestV1.OrdersApprove;
        Assert.Equal(503, (await Assert.ThrowsAsync<WorkDecisionException>(() => r.Decide(Request()))).Status);
        Assert.Equal(0, r.Native.Calls); Assert.Equal(0, r.Native.Effects);
    }

    [Theory] [InlineData(false, "permission")] [InlineData(true, "permission")] [InlineData(false, "delegation")] [InlineData(true, "delegation")]
    [InlineData(false, "actor")] [InlineData(true, "actor")] [InlineData(false, "company")] [InlineData(true, "company")]
    [InlineData(false, "membership")] [InlineData(true, "membership")] [InlineData(false, "cancel")] [InlineData(true, "cancel")]
    public async Task Native_and_Work_fences_deny_current_authority_delegation_scope_or_completed_cancellation(bool retained, string fault)
    {
        var r = new Rig(); using var cancellation = new CancellationTokenSource();
        void Revoke()
        {
            if (fault == "permission") r.Authority.Allowed = false; if (fault == "delegation") r.Delegations.Live = false;
            if (fault == "actor") r.User.ActorId = Maker; if (fault == "company") r.Company.Set(Guid.NewGuid());
            if (fault == "membership") r.User.Member = false; if (fault == "cancel") cancellation.Cancel();
        }
        if (retained) r.Native.AfterEffect = Revoke; else r.Native.BeforeEffect = Revoke;
        var q = Request() with { DelegationId = Guid.NewGuid() };
        if (fault == "cancel") await Assert.ThrowsAnyAsync<OperationCanceledException>(() => r.Decide(q, token: cancellation.Token));
        else Assert.Equal(403, (await Assert.ThrowsAsync<WorkDecisionException>(() => r.Decide(q, token: cancellation.Token))).Status);
        Assert.Equal(retained ? 1 : 0, r.Native.Effects); Assert.Equal(retained ? 1 : 0, r.Native.Commands.Count);
    }

    [Theory] [InlineData("facts")] [InlineData("permission")] [InlineData("cancel")] [InlineData("price")] [InlineData("actor")] [InlineData("price-error")]
    public async Task Second_human_read_and_last_optional_price_read_release_no_stale_facts_or_prices(string fault)
    {
        var r = new Rig(); using var cancellation = new CancellationTokenSource();
        r.Native.OnRead = n =>
        {
            if (n != 2) return;
            if (fault == "facts") r.Native.Current = r.Native.Current with { DocumentNo = "another" };
            if (fault == "permission") r.Authority.Allowed = false; if (fault == "cancel") cancellation.Cancel();
        };
        if (fault is "price" or "actor" or "price-error") r.Authority.OnRead = p =>
        {
            if (r.Native.Reads != 2 || p != SalesPermissionManifestV1.OrdersPriceRead) return;
            if (fault == "price-error") throw new InvalidOperationException("SENTETIK unreadable price permission");
            if (fault == "price") r.Authority.Prices = false; else r.User.ActorId = Maker;
        };
        if (fault == "cancel") await Assert.ThrowsAnyAsync<OperationCanceledException>(() => r.Host.PreviewAsync(Kind, Id, cancellation.Token));
        else Assert.Equal(fault is "permission" or "actor" ? 403 : 503, (await Assert.ThrowsAsync<WorkDecisionException>(() => r.Preview())).Status);
        Assert.Equal(2, r.Native.Reads); Assert.Equal(0, r.Native.Calls); Assert.Equal(0, r.Native.Effects);
        if (fault is "price" or "actor" or "price-error") Assert.Equal(SalesPermissionManifestV1.OrdersPriceRead, r.Authority.Permissions[^1]);
    }

    [Fact]
    public async Task Native_request_order_literal_reason_and_expected_version_remain_frozen_across_unknown_retry()
    {
        var r = new Rig(); var q = Request(); r.Native.ChangeReceipt = _ => null;
        Assert.Equal(503, (await Assert.ThrowsAsync<WorkDecisionException>(() => r.Decide(q))).Status);
        var originalHash = r.Native.Commands[Key].PayloadHash;
        r.Native.ChangeReceipt = null;
        var changed = q with { SalesPriceFloor = new(q.SalesPriceFloor!.Lines.Reverse().ToArray()) };
        Assert.Equal(409, (await Assert.ThrowsAsync<WorkDecisionException>(() => r.Decide(changed))).Status);
        changed = q with { SalesPriceFloor = new(q.SalesPriceFloor!.Lines.Select(x => x with { Reason = x.Reason.Trim() }).ToArray()) };
        Assert.Equal(409, (await Assert.ThrowsAsync<WorkDecisionException>(() => r.Decide(changed))).Status);
        Assert.True((await r.Decide(q)).Replayed); Assert.Equal(originalHash, r.Native.Commands[Key].PayloadHash); Assert.Equal(1, r.Native.Effects);
    }

    [Theory] [InlineData("maker")] [InlineData("legacy")] [InlineData("owner-missing")] [InlineData("no-version-port")]
    public async Task Current_maker_legacy_history_or_missing_owner_port_fail_closed(string fault)
    {
        var r = new Rig(); if (fault == "maker") r.User.ActorId = Maker;
        if (fault == "legacy") { await r.Decide(Request()); r.Native.Legacy = true; }
        if (fault == "owner-missing") r.Host = r.BuildHost(null);
        if (fault == "no-version-port") r.Host = r.BuildHost(r.Owner, new CurrentOnlySource(r.Source));
        Assert.Equal(fault == "maker" ? 403 : 503, (await Assert.ThrowsAsync<WorkDecisionException>(() => r.Decide(Request()))).Status);
        Assert.Equal(fault == "legacy" ? 1 : 0, r.Native.Effects);
    }

    [Fact]
    public async Task Every_foreign_kind_rejects_floor_selection_before_IO_and_foreign_floor_carriers_remain_denied()
    {
        var r = new Rig(); foreach (var kind in WorkDecisionHostV1.Kinds.Where(x => x != Kind))
            Assert.Equal(400, (await Assert.ThrowsAsync<WorkDecisionException>(() => r.Host.DecideAsync(kind, Id, Request(), Key, default))).Status);
        Assert.Equal(0, r.Source.Reads); Assert.Equal(0, r.Native.Calls);
    }

    [Theory] [InlineData(409, "version_conflict")] [InlineData(422, "validation_failed")]
    public async Task Native_refusal_code_replay_and_original_correlation_are_preserved(int status, string code)
    {
        var r = new Rig(); r.Native.Refusal = new(status, code, isReplay: true, originalCorrelationId: "SENTETIK-original-refusal");
        var failure = await Assert.ThrowsAsync<WorkDecisionException>(() => r.Decide(Request()));
        Assert.Equal((status, code, true, "SENTETIK-original-refusal"), (failure.Status, failure.Code, failure.Replayed, failure.OriginalCorrelationId)); Assert.Equal(0, r.Native.Effects);
    }

    private static WorkDecisionRequestV1 Request() => new("9", true, null)
    { SalesPriceFloor = new(Source().Lines.Reverse().Select(x => new SalesDraftPriceFloorExceptionApprovalLineV1(x.LineId, "  SENTETIK satır " + x.LineNo + "  ")).ToArray()) };
    public interface IFloorOwner : ISalesDraftService, ISalesPriceFloorDecisionReadV1 { }
    public class OwnerProxy : DispatchProxy
    {
        internal Func<MethodInfo, object?[], object?> Handler = (_, _) => throw new NotSupportedException("No other native command or general read");
        protected override object? Invoke(MethodInfo? method, object?[]? args) => Handler(method!, args!);
    }

    private sealed class Rig
    {
        internal readonly MutableCompany Company = new(); internal readonly MutableUser User = new(); internal readonly Authority Authority = new();
        internal readonly Delegations Delegations = new(); internal readonly Protocol Native; internal readonly SourceProxy Source; internal readonly IFloorOwner Owner;
        internal WorkDecisionHostV1 Host; private CorrelationContext correlation = new(); private readonly WorkDecisionExecutionFenceV1 execution = new();
        private readonly WorkDecisionAuthorityV1 decorated;
        internal Rig()
        {
            Company.Set(WorkSalesPriceFloorDecisionPreviewV1Tests.Company); correlation.Set(Corr, "SENTETIK-cause", "Web", null, null);
            decorated = new(Authority, execution); Native = new(this, decorated); Owner = DispatchProxy.Create<IFloorOwner, OwnerProxy>();
            ((OwnerProxy)(object)Owner).Handler = (m, a) => m.Name switch
            {
                nameof(ISalesPriceFloorDecisionReadV1.ReadPriceFloorDecisionAsync) => Native.Read((Guid)a[0]!, (Func<CancellationToken, Task>)a[1]!, (CancellationToken)a[2]!),
                nameof(ISalesPriceFloorDecisionReadV1.ReadPriceFloorOriginalVersionAsync) => Native.History((Guid)a[0]!, (string)a[1]!, (Func<CancellationToken, Task>)a[2]!, (CancellationToken)a[3]!),
                nameof(ISalesDraftService.ApprovePriceFloorExceptionsAsync) => Native.Approve((Guid)a[0]!, (SalesDraftPriceFloorExceptionApprovalRequest)a[1]!, (SalesCommandMetadata)a[2]!, (CancellationToken)a[3]!),
                _ => throw new NotSupportedException(m.Name)
            };
            Source = new(new SalesPriceFloorExceptionWorkInboxSourceV1(null!, Company, User, decorated, Owner)); Host = BuildHost(Owner);
        }
        internal WorkDecisionHostV1 BuildHost(ISalesDraftService? owner, IWorkApprovalDecisionSourceV1? source = null) =>
            new(Company, User, correlation, decorated, [source ?? Source], Delegations, execution, null!, null!, salesOrders: owner);
        internal void SetCorrelation(string value) { correlation = new(); correlation.Set(value, "SENTETIK-cause", "Web", null, null); Host = BuildHost(Owner); }
        internal Task<WorkDecisionPreviewV1> Preview() => Host.PreviewAsync(Kind, Id, default);
        internal Task<WorkDecisionResultV1> Decide(WorkDecisionRequestV1 q, string key = Key, CancellationToken token = default) => Host.DecideAsync(Kind, Id, q, key, token);
    }

    private sealed class Protocol(Rig rig, IErpAuthorityRevalidationService authority)
    {
        internal SalesPriceFloorDecisionSourceV1 Current = Source(); internal int Reads, HistoryReads, Calls, Effects;
        internal bool Legacy; internal Action<int>? OnRead; internal Action? BeforeEffect, AfterEffect;
        internal SalesDraftPriceFloorExceptionApprovalRequest? LastBody; internal SalesCommandMetadata? LastMetadata; internal SalesDraftServiceException? Refusal;
        internal Func<SalesPriceFloorOriginalReceiptV1, SalesPriceFloorOriginalReceiptV1?>? ChangeReceipt;
        internal readonly Dictionary<string, SalesPriceFloorOriginalReceiptV1> Commands = [];
        internal async Task<SalesPriceFloorDecisionReadResultV1> Read(Guid id, Func<CancellationToken, Task> fence, CancellationToken ct)
        {
            await fence(ct); Reads++; OnRead?.Invoke(Reads); await fence(ct);
            if (id != Id) return new(SalesPriceFloorDecisionReadStateV1.NotFound, null);
            if (Current.Lines.Any(x => x.Commercial.FloorException is { IsApproved: false } f && f.RequestedBy == rig.User.UserId)) return new(SalesPriceFloorDecisionReadStateV1.Denied, null);
            return new(SalesPriceFloorDecisionReadStateV1.Found, SalesDraftService.ProjectPriceFloorDecision(Current, rig.Authority.Prices ? SalesPriceDisclosure.Visible : SalesPriceDisclosure.Withheld));
        }
        internal async Task<SalesPriceFloorOriginalVersionReadResultV1> History(Guid id, string version, Func<CancellationToken, Task> fence, CancellationToken ct)
        {
            await fence(ct); HistoryReads++; await fence(ct);
            if (Legacy) return new(SalesPriceFloorDecisionReadStateV1.Unavailable, null);
            var matches = Commands.Values.Where(x => x.DraftId == id && x.ActorId == rig.User.UserId && x.ExpectedVersion.ToString(CultureInfo.InvariantCulture) == version).ToArray();
            return matches.Length == 1 ? new(SalesPriceFloorDecisionReadStateV1.Found, matches[0])
                : new(SalesPriceFloorDecisionReadStateV1.NotFound, null) { CurrentVersion = Current.Version };
        }
        internal async Task<SalesOrderDraftResponse> Approve(Guid id, SalesDraftPriceFloorExceptionApprovalRequest body, SalesCommandMetadata m, CancellationToken ct)
        {
            Calls++; LastBody = body; LastMetadata = m; var actor = rig.User.UserId; BeforeEffect?.Invoke(); await Fence(actor, ct);
            if (Refusal is not null) throw Refusal;
            var hash = SalesPriceFloorDecisionBindingV1.DecisionPayloadHash(id, body); var replay = Commands.TryGetValue(m.IdempotencyKey, out var saved);
            if (replay && (saved!.ActorId != actor || saved.PayloadHash != hash)) throw new SalesDraftServiceException(409, SalesDraftProblemCodesV1.SameKeyPayloadMismatch);
            if (!replay)
            {
                if (body.ExpectedVersion != Current.Version.ToString(CultureInfo.InvariantCulture)) throw new SalesDraftServiceException(409, SalesDraftProblemCodesV1.VersionConflict);
                if (Current.Lines.Any(x => x.Commercial.FloorException!.RequestedBy == actor)) throw new SalesDraftServiceException(422, "price_floor_exception_same_actor");
                var before = Current; var after = before.Lines.Select(x => x with { Commercial = x.Commercial with { FloorException = x.Commercial.FloorException! with
                { ApprovedBy = actor, ApprovedAtUtc = At.AddSeconds(1), Reason = body.Lines.Single(l => l.LineId == x.LineId).Reason.Trim() } } }).ToArray();
                Current = before with { Version = before.Version + 1, Lines = Array.AsReadOnly(after) };
                var stored = new SalesDraftReplayResult(Detail(Current, true), Detail(Current, false));
                var lines = before.Lines.Select((x, n) => new SalesPriceFloorOriginalLineV1(x.LineId, x.LineNo, x.Commercial, after[n].Commercial)).ToArray();
                var p = new SalesPriceFloorOriginalReceiptV1(WorkSalesPriceFloorDecisionPreviewV1Tests.Company, id, SalesDraftCommands.ApprovePriceFloorExceptionsV1,
                    actor, SalesPriceFloorDecisionBindingV1.DecisionKeyHash(m.IdempotencyKey), hash, before.Version, Current.Version, m.CorrelationId,
                    At.AddSeconds(1), Guid.NewGuid(), new('A', 64), Array.AsReadOnly(lines), stored, SalesPriceFloorDecisionDigestsV1.OriginalResultDigest(stored), "");
                saved = p with { OriginalDecisionDigest = SalesPriceFloorDecisionDigestsV1.OriginalDecisionDigest(p) }; Commands.Add(m.IdempotencyKey, saved); Effects++; AfterEffect?.Invoke();
            }
            await Fence(actor, ct);
            var answer = new SalesOrderDraftResponse(rig.Authority.Prices ? saved!.OriginalResult.Visible : saved!.OriginalResult.Withheld, replay, saved.OriginalCorrelationId);
            var receipt = saved with { CurrentResponseDigest = SalesPriceFloorDecisionDigestsV1.CurrentResponseDigest(answer) };
            return answer with { OriginalFloorApprovalReceipt = ChangeReceipt is null ? receipt : ChangeReceipt(receipt) };
        }
        private async Task Fence(string actor, CancellationToken ct)
        {
            ct.ThrowIfCancellationRequested(); var p = await authority.RevalidatePermissionScopeAsync(SalesPermissionManifestV1.OrdersPriceFloorExceptionApprove, WorkSalesPriceFloorDecisionPreviewV1Tests.Company, ct);
            ct.ThrowIfCancellationRequested(); if (!p.IsGranted || !p.IsCompanyWide || rig.User.UserId != actor || !rig.User.Member || rig.Company.CompanyId != WorkSalesPriceFloorDecisionPreviewV1Tests.Company)
                throw new SalesDraftServiceException(403, "forbidden");
        }
    }
    private static SalesOrderDraftDetailView Detail(SalesPriceFloorDecisionSourceV1 source, bool prices) => new(
        new(source.DraftId, source.CompanyId, source.DocumentNo, "Order", source.State, source.Version.ToString(CultureInfo.InvariantCulture), "Rev.0", source.PartyId,
            source.CompanyAccountId, source.CurrencyId, source.BusinessDate, At, source.Lines.Count, source.ApprovalState), null, null,
        source.Lines.Select(x => new SalesOrderDraftLineDetail(x.LineId, x.LineNo, x.ProductId, x.ProductVersion, x.ProductCode, x.ProductName, x.OrderUnitId,
            x.Quantity, prices ? x.Commercial.UnitPrice : null, prices ? x.Commercial.PriceUnitId : null, null, null, null, null,
            prices ? SalesPriceDisclosure.Visible : SalesPriceDisclosure.Withheld)).ToArray(), null, null, null, null, null, null,
        new(null, null, null, null, null), new(false, null, null, null, false), false, false);

    private sealed class SourceProxy(IWorkApprovalDecisionVersionSourceV1 inner) : IWorkApprovalDecisionVersionSourceV1
    {
        internal int Reads; internal string? Selected; internal string? Permission; internal Func<WorkApprovalDecisionSnapshotV1, WorkApprovalDecisionSnapshotV1>? Change;
        public string Module => "sales"; public string Kind => inner.Kind; public string ApproverPermission => Permission ?? inner.ApproverPermission;
        public Task<WorkInboxSourceResultV1> ListPendingAsync(WorkInboxSourceContextV1 c, int n, CancellationToken ct) => throw new InvalidOperationException("No list fallback");
        public async Task<WorkApprovalDecisionSnapshotV1?> ReadDecisionAsync(WorkInboxSourceContextV1 c, Guid id, CancellationToken ct)
        { Reads++; var s = await inner.ReadDecisionAsync(c, id, ct); return s is null ? null : Change?.Invoke(s) ?? s; }
        public async Task<WorkApprovalDecisionSnapshotV1?> ReadDecisionVersionAsync(WorkInboxSourceContextV1 c, Guid id, string version, CancellationToken ct)
        { Reads++; Selected = version; var s = await inner.ReadDecisionVersionAsync(c, id, version, ct); return s is null ? null : Change?.Invoke(s) ?? s; }
    }
    private sealed class CurrentOnlySource(IWorkApprovalDecisionSourceV1 inner) : IWorkApprovalDecisionSourceV1
    {
        public string Module => inner.Module; public string Kind => inner.Kind; public string ApproverPermission => inner.ApproverPermission;
        public Task<WorkApprovalDecisionSnapshotV1?> ReadDecisionAsync(WorkInboxSourceContextV1 c, Guid id, CancellationToken ct) => inner.ReadDecisionAsync(c, id, ct);
        public Task<WorkInboxSourceResultV1> ListPendingAsync(WorkInboxSourceContextV1 c, int n, CancellationToken ct) => throw new NotSupportedException();
    }
    private sealed class MutableCompany : ICompanyContext { public Guid CompanyId { get; private set; } public bool IsResolved => CompanyId != Guid.Empty; public void Set(Guid id) => CompanyId = id; }
    private sealed class MutableUser : ICurrentUser
    {
        internal Guid ActorId = Actor; internal bool Member = true; public bool IsAuthenticated => true; public string UserId => ActorId.ToString("D");
        public IReadOnlySet<Guid> AllowedCompanyIds { get; } = new HashSet<Guid>([WorkSalesPriceFloorDecisionPreviewV1Tests.Company]);
        public bool CanAccessCompany(Guid id) => Member && id == WorkSalesPriceFloorDecisionPreviewV1Tests.Company;
    }
    private sealed class Authority : IErpAuthorityRevalidationService
    {
        internal bool Allowed = true, Prices = true; internal Action<string>? OnRead; internal readonly List<string> Permissions = [];
        public Task<PermissionScope> RevalidatePermissionScopeAsync(string p, Guid c, CancellationToken ct = default)
        { Permissions.Add(p); OnRead?.Invoke(p); return Task.FromResult(new PermissionScope(Allowed && (p == SalesPermissionManifestV1.OrdersPriceFloorExceptionApprove || p == SalesPermissionManifestV1.OrdersPriceRead && Prices || p == SalesPermissionManifestV1.OrdersApprove), true, new HashSet<string>(), new HashSet<Guid>(), new HashSet<Guid>(), new HashSet<Guid>())); }
    }
    private sealed class Delegations : IWorkDelegationReaderV1
    {
        internal bool Live = true;
        public Task<WorkDelegationViewV1?> FindForActorAsync(Guid id, WorkInboxItemV1 item, CancellationToken ct = default) => Task.FromResult<WorkDelegationViewV1?>(Live
            ? new(id, Company, Kind, SalesPermissionManifestV1.OrdersPriceFloorExceptionApprove, Maker, Actor, At.AddYears(-1), At.AddYears(1), "1", Maker, At, "SENTETIK", null, null, null) : null);
    }
}
