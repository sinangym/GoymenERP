using System.Globalization;
using System.Text.Json;
using GoymenERP.Api.WorkRuntime;
using GoymenERP.BuildingBlocks.Multitenancy;
using GoymenERP.BuildingBlocks.Observability;
using GoymenERP.BuildingBlocks.Security;
using GoymenERP.BuildingBlocks.Work;
using GoymenERP.Modules.Sales.Application.Contracts;
using GoymenERP.Modules.Sales.Domain;
using GoymenERP.Modules.Sales.Domain.RiskExceptions;
using GoymenERP.Modules.Sales.Infrastructure.Persistence;
using GoymenERP.Modules.Sales.Infrastructure.WorkInbox;
using GoymenERP.Modules.Work.Infrastructure.Delegations;
using static GoymenERP.Api.IntegrationTests.WorkSalesRiskExceptionDecisionPreviewV1Tests;

namespace GoymenERP.Api.IntegrationTests;

// Real Work source/host/WP3 decorator with a controlled native protocol. Not SQL, immutable Audit integrity or TX acceptance.
public sealed class WorkSalesRiskExceptionDecisionHostTests
{
    private const string Kind = WorkDecisionHostV1.SalesRiskException, Key = "SENTETIK-risk-work-key", Corr = "SENTETIK-risk-work-original";
    private static WorkDecisionRequestV1 Request(bool approve = true) => new("1", approve, approve ? null : "  SENTETIK ret  ");

    [Theory] [InlineData(true)] [InlineData(false)]
    public async Task GET_keeps_all_price_free_facts_POST_uses_exception_version_and_order_number_target_without_second_human_read(bool approve)
    {
        var r = new Rig(); var preview = await r.Preview(); var human = Assert.IsType<WorkSalesRiskExceptionDecisionPreviewV1>(preview.SalesRiskException);
        Assert.Equal(("1", true, true, true), (preview.SourceVersion, preview.Pending, preview.ApprovalSupported, preview.RejectionSupported));
        Assert.Equal("9", human.OrderVersion); Assert.Equal(Facts().Exception, human.Exception); Assert.Equal(2, r.Native.Reads);
        var q = Request(approve); var reads = r.Native.Reads; var answer = await r.Decide(q);
        Assert.Equal((approve ? "Approved" : "Rejected", false, Corr), (answer.State, answer.Replayed, answer.OriginalCorrelationId));
        Assert.Equal(reads + 1, r.Native.Reads); Assert.Equal(0, r.Native.HistoryReads); Assert.Equal("1", r.Source.Selected);
        Assert.Equal((Order, 3, approve), (r.Native.LastOrder, r.Native.LastNo, r.Native.LastApprove));
        Assert.Equal(new SalesDraftStateCommandRequest(q.ExpectedVersion, q.Reason), r.Native.LastBody);
        Assert.Equal((Key, Corr, "SENTETIK-cause"), (r.Native.LastMetadata!.IdempotencyKey, r.Native.LastMetadata.CorrelationId, r.Native.LastMetadata.CausationId));
        Assert.Equal(1, r.Native.Effects); Assert.Single(r.Native.Commands); Assert.Equal("9", r.Native.Current.OrderVersion);
        Assert.All(r.Authority.Permissions, p => Assert.Equal(SalesPermissionManifestV1.OrdersRiskExceptionApprove, p));
        var receipt = Assert.Single(r.Native.Commands).Value;
        using var native = JsonDocument.Parse(JsonSerializer.Serialize(new SalesRiskExceptionCommandResult(r.Native.Current.Exception, false, Corr)
            { OriginalDecisionReceipt = receipt }, Json));
        Assert.Equal(3, native.RootElement.EnumerateObject().Count()); Assert.False(native.RootElement.TryGetProperty("originalDecisionReceipt", out _));
        using var work = JsonDocument.Parse(JsonSerializer.Serialize(answer, Json)); Assert.Equal(6, work.RootElement.EnumerateObject().Count());
        using var humanWire = JsonDocument.Parse(JsonSerializer.Serialize(preview, Json));
        Assert.True(humanWire.RootElement.TryGetProperty("salesRiskException", out var typedHuman)); Assert.Equal(10, typedHuman.EnumerateObject().Count());
        Assert.DoesNotContain("sourceFactsDigest", humanWire.RootElement.GetRawText(), StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("salesRiskExceptionProof", humanWire.RootElement.GetRawText(), StringComparison.OrdinalIgnoreCase);
    }
    [Theory] [InlineData(true)] [InlineData(false)]
    public async Task Original_key_retry_separates_original_risk_decision_from_current_order_version_and_today(bool approve)
    {
        var r = new Rig(); var q = Request(approve); await r.Decide(q); var original = Assert.Single(r.Native.Commands).Value;
        r.Native.Current = Seal(r.Native.Current.Source! with { OrderVersion = 9007199254740993L, OrderState = "Cancelled" }, Today.AddDays(1));
        var preview = await r.Preview(); Assert.False(preview.Pending); Assert.False(preview.ApprovalSupported); Assert.False(preview.RejectionSupported);
        Assert.Equal("2", preview.SourceVersion); Assert.Equal("9007199254740993", preview.SalesRiskException!.OrderVersion); Assert.False(preview.SalesRiskException.Exception.IsValidToday);
        r.SetCorrelation("SENTETIK-retry"); var reads = r.Native.Reads; var replay = await r.Decide(q);
        Assert.Equal((true, approve ? "Approved" : "Rejected", Corr), (replay.Replayed, replay.State, replay.OriginalCorrelationId));
        Assert.Equal(reads + 1, r.Native.Reads); Assert.Equal(1, r.Native.HistoryReads); Assert.Equal(1, r.Native.Effects); Assert.Equal(original, Assert.Single(r.Native.Commands).Value);
        Assert.Equal(("1", "2", false, true), (r.Source.Last!.SourceVersion, r.Source.Last.SalesRiskExceptionProof!.CurrentVersion, r.Source.Last.Pending, r.Source.Last.SalesRiskExceptionProof.Historical));
        Assert.Equal("9007199254740993", r.Native.Current.OrderVersion);
    }
    [Theory] [InlineData("missing")] [InlineData("actor")] [InlineData("company")] [InlineData("key")] [InlineData("payload")]
    [InlineData("expected")] [InlineData("result")] [InlineData("before")] [InlineData("after")] [InlineData("digest")] [InlineData("response")] [InlineData("correlation")]
    public async Task Postnative_bad_original_receipt_suppresses_answer_then_same_key_reconciles_one_effect(string fault)
    {
        var r = new Rig(); r.Native.ChangeReceipt = p => fault switch
        {
            "missing" => null, "actor" => p with { ActorId = Maker.ToString("D") }, "company" => p with { CompanyId = Guid.NewGuid() },
            "key" => p with { IdempotencyKeyHash = new('A', 64) }, "payload" => p with { PayloadHash = new('b', 64) },
            "expected" => p with { ExpectedVersion = "2" }, "result" => p with { ResultVersion = "3" },
            "before" => p with { Before = p.Before with { LoadingInstructionId = Guid.NewGuid() } },
            "after" => p with { After = p.After with { Reason = "different" } }, "digest" => p with { OriginalDecisionDigest = new('c', 64) },
            "response" => p with { CurrentResponseDigest = new('d', 64) }, _ => p with { OriginalCorrelationId = "different" }
        };
        Assert.Equal(503, (await Assert.ThrowsAsync<WorkDecisionException>(() => r.Decide(Request()))).Status);
        Assert.Equal(1, r.Native.Effects); Assert.Single(r.Native.Commands); // Retained native outcome, not a rollback claim.
        r.Native.ChangeReceipt = null; r.SetCorrelation("SENTETIK-reconcile"); var replay = await r.Decide(Request());
        Assert.True(replay.Replayed); Assert.Equal(Corr, replay.OriginalCorrelationId); Assert.Equal(1, r.Native.Effects);
    }
    [Fact]
    public async Task Valid_shape_source_digest_mismatch_is_postnative_unknown_not_a_zero_effect_precheck()
    {
        var r = new Rig(); r.Source.Change = s => s with { SalesRiskExceptionProof = s.SalesRiskExceptionProof! with { SourceFactsDigest = new('a', 64) } };
        Assert.Equal(503, (await Assert.ThrowsAsync<WorkDecisionException>(() => r.Decide(Request()))).Status);
        Assert.Equal(1, r.Native.Effects); Assert.Single(r.Native.Commands);
        r.Source.Change = null; r.SetCorrelation("SENTETIK-reconcile"); var answer = await r.Decide(Request());
        Assert.True(answer.Replayed); Assert.Equal(Corr, answer.OriginalCorrelationId); Assert.Equal(1, r.Native.Effects);
    }
    [Theory] [InlineData("zero")] [InlineData("padded")] [InlineData("max")] [InlineData("approve-reason")]
    [InlineData("reject-null")] [InlineData("reject-blank")] [InlineData("reject-long")] [InlineData("quote")]
    [InlineData("floor")] [InlineData("discount")] [InlineData("loss")] [InlineData("period")] [InlineData("cancellation")]
    public async Task Malformed_or_cross_kind_body_stops_before_source_or_owner_IO(string fault)
    {
        var r = new Rig(); var q = Request(); q = fault switch
        {
            "zero" => q with { ExpectedVersion = "0" }, "padded" => q with { ExpectedVersion = "01" }, "max" => q with { ExpectedVersion = long.MaxValue.ToString(CultureInfo.InvariantCulture) },
            "approve-reason" => q with { Reason = "SENTETIK" }, "reject-null" => q with { Approve = false }, "reject-blank" => q with { Approve = false, Reason = " \t " },
            "reject-long" => q with { Approve = false, Reason = new('x', SalesShipmentRiskException.MaximumReasonLength + 1) },
            "quote" => q with { SalesQuoteConversion = new(Order, 3, 0, "1") }, "floor" => q with { SalesPriceFloor = new([]) }, "discount" => q with { SalesDiscount = new([]) },
            "loss" => q with { InventoryTransferLoss = new(1, At) }, "period" => q with { NormalJournalDraft = new(Order) }, _ => q with { InventoryCostCancellation = new("1") }
        };
        Assert.Equal(400, (await Assert.ThrowsAsync<WorkDecisionException>(() => r.Decide(q))).Status);
        Assert.Equal((0, 0, 0, 0), (r.Source.Reads, r.Native.Reads, r.Native.HistoryReads, r.Native.Calls));
    }
    [Theory] [InlineData("missing")] [InlineData("caption")] [InlineData("reference")] [InlineData("rule")] [InlineData("maker")]
    [InlineData("version")] [InlineData("company")] [InlineData("permission")] [InlineData("digest-shape")] [InlineData("foreign-carrier")]
    public async Task Invalid_identity_formatter_or_native_authority_pin_has_zero_command_effects(string fault)
    {
        var r = new Rig(); r.Source.Change = s => fault switch
        {
            "missing" => s with { SalesRiskExceptionProof = null }, "caption" => s with { Item = s.Item with { DocumentLabel = "different" } },
            "reference" => s with { Item = s.Item with { References = s.Item.References.Select(x => x with { RecordId = Guid.NewGuid() }).ToArray() } },
            "rule" => s with { Item = s.Item with { Rules = s.Item.Rules.Select(x => x with { Actual = "1" }).ToArray() } },
            "maker" => s with { SalesRiskExceptionProof = s.SalesRiskExceptionProof! with { RequestedBy = "different" } },
            "version" => s with { SourceVersion = "2" }, "company" => s with { CompanyId = Guid.NewGuid() },
            "digest-shape" => s with { SalesRiskExceptionProof = s.SalesRiskExceptionProof! with { PreviewDigest = "wrong" } },
            "foreign-carrier" => s with { SalesDiscountProof = SalesDiscountDecisionBindingV1.Proof(WorkSalesDiscountDecisionPreviewV1Tests.Facts()) }, _ => s
        };
        if (fault == "permission") { r.Source.Permission = SalesPermissionManifestV1.OrdersApprove; r.Authority.Additional.Add(SalesPermissionManifestV1.OrdersApprove); }
        Assert.Equal(503, (await Assert.ThrowsAsync<WorkDecisionException>(() => r.Decide(Request()))).Status);
        Assert.Equal(0, r.Native.Calls); Assert.Equal(0, r.Native.Effects); Assert.Empty(r.Native.Commands);
    }
    [Theory] [InlineData("facts")] [InlineData("permission")] [InlineData("actor")] [InlineData("company")] [InlineData("membership")] [InlineData("cancel")]
    public async Task Second_human_read_binds_current_native_facts_and_rechecks_late_context(string fault)
    {
        var r = new Rig(); using var cancel = new CancellationTokenSource(); r.Native.OnRead = n =>
        {
            if (n != 2) return;
            if (fault == "facts") r.Native.Current = Seal(r.Native.Current.Source! with { OrderVersion = 10 });
            if (fault == "permission") r.Authority.Allowed = false; if (fault == "actor") r.User.ActorId = Maker;
            if (fault == "company") r.Company.Set(Guid.NewGuid()); if (fault == "membership") r.User.Member = false; if (fault == "cancel") cancel.Cancel();
        };
        if (fault == "cancel") await Assert.ThrowsAnyAsync<OperationCanceledException>(() => r.Host.PreviewAsync(Kind, Id, cancel.Token));
        else Assert.Equal(fault == "facts" ? 503 : 403, (await Assert.ThrowsAsync<WorkDecisionException>(() => r.Preview())).Status);
        Assert.Equal(2, r.Native.Reads); Assert.Equal(0, r.Native.Calls);
    }
    [Theory] [InlineData(false, "permission")] [InlineData(true, "permission")] [InlineData(false, "delegation")] [InlineData(true, "delegation")]
    [InlineData(false, "actor")] [InlineData(true, "actor")] [InlineData(false, "company")] [InlineData(true, "company")]
    [InlineData(false, "membership")] [InlineData(true, "membership")] [InlineData(false, "cancel")] [InlineData(true, "cancel")]
    public async Task Native_and_release_fences_keep_live_authority_delegation_and_distinguish_retained_outcomes(bool retained, string fault)
    {
        var r = new Rig(); using var cancel = new CancellationTokenSource(); void Revoke()
        {
            if (fault == "permission") r.Authority.Allowed = false; if (fault == "delegation") r.Delegations.Live = false;
            if (fault == "actor") r.User.ActorId = Maker; if (fault == "company") r.Company.Set(Guid.NewGuid());
            if (fault == "membership") r.User.Member = false; if (fault == "cancel") cancel.Cancel();
        }
        if (retained) r.Native.AfterEffect = Revoke; else r.Native.BeforeEffect = Revoke;
        var q = Request() with { DelegationId = Guid.NewGuid() };
        if (fault == "cancel") await Assert.ThrowsAnyAsync<OperationCanceledException>(() => r.Decide(q, token: cancel.Token));
        else Assert.Equal(403, (await Assert.ThrowsAsync<WorkDecisionException>(() => r.Decide(q))).Status);
        Assert.Equal(retained ? 1 : 0, r.Native.Effects); Assert.Equal(retained ? 1 : 0, r.Native.Commands.Count);
    }
    [Theory] [InlineData(true)] [InlineData(false)]
    public async Task Requester_is_forbidden_for_both_decisions_without_a_self_approval_exception(bool approve)
    {
        var r = new Rig(); r.User.ActorId = Maker;
        Assert.Equal(403, (await Assert.ThrowsAsync<WorkDecisionException>(() => r.Decide(Request(approve)))).Status);
        Assert.Equal(0, r.Native.Calls); Assert.Empty(r.Native.Commands);
    }
    [Fact]
    public async Task Closed_order_disables_approve_but_native_reject_remains_independent_and_raw_reason_4000_is_preserved()
    {
        var r = new Rig(); r.Native.Current = Seal(Facts().Source! with { OrderState = "Closed" }); var preview = await r.Preview();
        Assert.True(preview.Pending); Assert.False(preview.ApprovalSupported); Assert.True(preview.RejectionSupported);
        var blocked = await Assert.ThrowsAsync<WorkDecisionException>(() => r.Decide(Request(), "SENTETIK-ineligible-approve"));
        Assert.Equal((409, SalesRiskExceptionProblemCodesV1.OrderNotEligible), (blocked.Status, blocked.Code)); Assert.Empty(r.Native.Commands);
        var reason = "  " + new string('x', SalesShipmentRiskException.MaximumReasonLength - 4) + "  ";
        var answer = await r.Decide(Request(false) with { Reason = reason }); Assert.Equal("Rejected", answer.State);
        Assert.Equal(reason, r.Native.LastBody!.Reason); Assert.Equal(reason, Assert.Single(r.Native.Commands).Value.After.RejectionReason); Assert.Equal("9", r.Native.Current.OrderVersion);
    }
    [Theory] [InlineData(404, "not_found")] [InlineData(409, "sales_risk_exception_order_not_eligible")] [InlineData(422, "validation_failed")]
    public async Task Native_refusal_code_replay_and_original_correlation_are_forwarded_unchanged(int status, string code)
    {
        var r = new Rig(); r.Native.Refusal = new(status, code, isReplay: true, originalCorrelationId: "SENTETIK-original-refusal");
        var error = await Assert.ThrowsAsync<WorkDecisionException>(() => r.Decide(Request()));
        Assert.Equal((status, code, true, "SENTETIK-original-refusal"), (error.Status, error.Code, error.Replayed, error.OriginalCorrelationId)); Assert.Empty(r.Native.Commands);
    }
    [Fact]
    public async Task Current_terminal_card_is_read_only_and_a_new_key_cannot_repeat_the_old_decision()
    {
        var r = new Rig(); await r.Decide(Request()); var preview = await r.Preview();
        Assert.False(preview.Pending); Assert.False(preview.ApprovalSupported); Assert.False(preview.RejectionSupported);
        Assert.Equal("2", preview.SourceVersion);
        var refusal = await Assert.ThrowsAsync<WorkDecisionException>(() => r.Decide(new("2", true, null), "SENTETIK-new-key"));
        Assert.Equal((409, SalesDraftProblemCodesV1.StateConflict), (refusal.Status, refusal.Code));
        Assert.Equal(1, r.Native.Effects); Assert.Single(r.Native.Commands);
    }
    [Fact]
    public async Task A_wrong_source_permission_cannot_borrow_the_native_risk_grant_before_reading()
    {
        var r = new Rig(); r.Source.Permission = SalesPermissionManifestV1.OrdersApprove;
        Assert.Equal(403, (await Assert.ThrowsAsync<WorkDecisionException>(() => r.Decide(Request()))).Status);
        Assert.Equal((0, 0, 0), (r.Source.Reads, r.Native.Reads, r.Native.Calls));
        Assert.NotEmpty(r.Authority.Permissions);
        Assert.All(r.Authority.Permissions, p => Assert.Equal(SalesPermissionManifestV1.OrdersApprove, p));
    }
    [Fact]
    public async Task Missing_original_provenance_or_optional_owner_never_uses_current_facts_as_a_legacy_decision()
    {
        var r = new Rig(); await r.Decide(Request()); r.Native.Legacy = true; var calls = r.Native.Calls;
        Assert.Equal(503, (await Assert.ThrowsAsync<WorkDecisionException>(() => r.Decide(Request()))).Status); Assert.Equal(calls, r.Native.Calls); Assert.Equal(1, r.Native.Effects);
        Assert.Equal(503, (await Assert.ThrowsAsync<WorkDecisionException>(() => r.BuildHost(null).PreviewAsync(Kind, Id, default))).Status);
    }
    [Theory] [InlineData("unauthenticated")] [InlineData("company")] [InlineData("membership")] [InlineData("permission")] [InlineData("scoped")]
    public async Task Initial_context_and_company_wide_permission_denial_do_not_read_the_source(string fault)
    {
        var r = new Rig(); if (fault == "unauthenticated") r.User.Authenticated = false; if (fault == "company") r.Company.Set(Guid.NewGuid());
        if (fault == "membership") r.User.Member = false; if (fault == "permission") r.Authority.Allowed = false; if (fault == "scoped") r.Authority.CompanyWide = false;
        Assert.Equal(fault == "unauthenticated" ? 401 : 403, (await Assert.ThrowsAsync<WorkDecisionException>(() => r.Decide(Request()))).Status);
        Assert.Equal(0, r.Source.Reads); Assert.Equal(0, r.Native.Calls);
    }
    [Theory] [InlineData("finance.cash-account.v1")] [InlineData("finance.cash-account-passivation.v1")]
    [InlineData("finance.cash-account-reactivation.v1")] [InlineData(WorkDecisionHostV1.OpeningCost)]
    public async Task Existing_generic_kind_preview_stays_valid_but_rejects_foreign_private_risk_proof(string foreignKind)
    {
        var r = new Rig(); var source = new GenericSource(foreignKind); r.Authority.Additional.Add(source.ApproverPermission);
        var host = r.BuildHost(r.Native, [source]); var preview = await host.PreviewAsync(foreignKind, Id, default); Assert.Equal(foreignKind, preview.Kind);
        source.Foreign = SalesRiskExceptionDecisionBindingV1.Proof(Facts());
        Assert.Equal(503, (await Assert.ThrowsAsync<WorkDecisionException>(() => host.PreviewAsync(foreignKind, Id, default))).Status);
        Assert.Equal(0, r.Native.Calls);
    }

    private sealed class Rig
    {
        internal readonly MutableCompany Company = new(); internal readonly MutableUser User = new(); internal readonly Authority Authority = new();
        internal readonly Delegations Delegations = new(); internal readonly Native Native; internal readonly SourceProxy Source;
        internal WorkDecisionHostV1 Host; private CorrelationContext correlation = new(); private readonly WorkDecisionExecutionFenceV1 execution = new();
        private readonly WorkDecisionAuthorityV1 decorated;
        internal Rig()
        {
            Company.Set(WorkSalesRiskExceptionDecisionPreviewV1Tests.Company); correlation.Set(Corr, "SENTETIK-cause", "Web", null, null);
            decorated = new(Authority, execution); Native = new(this, decorated);
            Source = new(new SalesRiskExceptionWorkInboxSourceV1(null!, Company, User, decorated, Native)); Host = BuildHost(Native);
        }
        internal WorkDecisionHostV1 BuildHost(ISalesRiskExceptionService? owner, IEnumerable<IWorkInboxSourceV1>? sources = null) =>
            new(Company, User, correlation, decorated, sources ?? [Source], Delegations, execution, null!, null!, salesRiskExceptions: owner);
        internal void SetCorrelation(string value) { correlation = new(); correlation.Set(value, "SENTETIK-cause", "Web", null, null); Host = BuildHost(Native); }
        internal Task<WorkDecisionPreviewV1> Preview() => Host.PreviewAsync(Kind, Id, default);
        internal Task<WorkDecisionResultV1> Decide(WorkDecisionRequestV1 body, string key = Key, CancellationToken token = default) => Host.DecideAsync(Kind, Id, body, key, token);
    }
    private sealed class Native(Rig rig, IErpAuthorityRevalidationService authority) : ISalesRiskExceptionService, ISalesRiskExceptionDecisionReadV1
    {
        internal SalesRiskExceptionDecisionDetailV1 Current = Facts(); internal int Reads, HistoryReads, Calls, Effects;
        internal Action<int>? OnRead; internal Action? BeforeEffect, AfterEffect; internal bool Legacy; internal Guid LastOrder; internal int LastNo; internal bool LastApprove;
        internal SalesDraftStateCommandRequest? LastBody; internal SalesCommandMetadata? LastMetadata; internal SalesDraftServiceException? Refusal;
        internal Func<SalesRiskExceptionOriginalDecisionReceiptV1, SalesRiskExceptionOriginalDecisionReceiptV1?>? ChangeReceipt;
        internal readonly Dictionary<(string Command, string Key), SalesRiskExceptionOriginalDecisionReceiptV1> Commands = [];
        public async Task<SalesRiskExceptionDecisionReadResultV1> ReadRiskExceptionDecisionAsync(Guid id, Func<CancellationToken, Task> fence, CancellationToken ct = default)
        {
            await fence(ct); Reads++; OnRead?.Invoke(Reads); await fence(ct);
            if (id != Id) return new(SalesRiskExceptionDecisionReadStateV1.NotFound, null);
            if (Current.Exception.RequestedBy == rig.User.UserId) return new(SalesRiskExceptionDecisionReadStateV1.Denied, null);
            return new(SalesRiskExceptionDecisionReadStateV1.Found, Current);
        }
        public async Task<SalesRiskExceptionOriginalVersionReadResultV1> ReadRiskExceptionOriginalVersionAsync(Guid id, string version, Func<CancellationToken, Task> fence, CancellationToken ct = default)
        {
            await fence(ct); HistoryReads++; await fence(ct); if (Legacy) return new(SalesRiskExceptionDecisionReadStateV1.Unavailable, null);
            var matches = Commands.Values.Where(x => x.ExceptionId == id && x.ExpectedVersion == version && x.ActorId == rig.User.UserId).ToArray();
            return matches.Length == 1 ? new(SalesRiskExceptionDecisionReadStateV1.Found, Observe(matches[0], replay: true).OriginalDecisionReceipt)
                : new(SalesRiskExceptionDecisionReadStateV1.NotFound, null) { CurrentVersion = long.Parse(Current.Exception.Version, CultureInfo.InvariantCulture) };
        }
        public Task<SalesRiskExceptionCommandResult> ApproveRiskExceptionAsync(Guid order, int no, SalesDraftStateCommandRequest body, SalesCommandMetadata m, CancellationToken ct = default) => Execute(order, no, body, m, true, ct);
        public Task<SalesRiskExceptionCommandResult> RejectRiskExceptionAsync(Guid order, int no, SalesDraftStateCommandRequest body, SalesCommandMetadata m, CancellationToken ct = default) => Execute(order, no, body, m, false, ct);
        private async Task<SalesRiskExceptionCommandResult> Execute(Guid order, int no, SalesDraftStateCommandRequest body, SalesCommandMetadata m, bool approve, CancellationToken ct)
        {
            Calls++; LastOrder = order; LastNo = no; LastApprove = approve; LastBody = body; LastMetadata = m; var actor = rig.User.UserId;
            BeforeEffect?.Invoke(); await Fence(actor, ct); if (Refusal is not null) throw Refusal;
            var command = approve ? SalesRiskExceptionCommands.ApproveV1 : SalesRiskExceptionCommands.RejectV1;
            var hash = SalesRiskExceptionHash.Compute(command, order, no, body); var replay = Commands.TryGetValue((command, m.IdempotencyKey), out var p);
            if (replay && (p!.PayloadHash != hash || p.ActorId != actor)) throw new SalesDraftServiceException(409, SalesDraftProblemCodesV1.SameKeyPayloadMismatch);
            if (!replay)
            {
                if (order != Order || no != 3) throw new SalesDraftServiceException(404, "not_found");
                if (Current.Exception.Version != body.ExpectedVersion) throw new SalesDraftServiceException(409, SalesDraftProblemCodesV1.VersionConflict);
                if (Current.Exception.State != "Requested") throw new SalesDraftServiceException(409, SalesDraftProblemCodesV1.StateConflict);
                if (approve && !Current.ApprovalSupported) throw new SalesDraftServiceException(409, SalesRiskExceptionProblemCodesV1.OrderNotEligible);
                var source = Current.Source!; var before = source.Recorded; var at = At.AddSeconds(1); var native = SalesRiskExceptionDecisionDigestsV1.Restore(WorkSalesRiskExceptionDecisionPreviewV1Tests.Company, before);
                if (approve) native.Approve(before.Version, actor, at, SalesBusinessDay.DateOf(at)); else native.Reject(before.Version, actor, body.Reason, at);
                var decision = native.Decision ?? throw new InvalidOperationException("Controlled native decision is missing.");
                var after = new SalesRiskExceptionRecordedStateV1(native.Id, native.OrderId, native.ExceptionNo, native.LoadingInstructionId, native.State.ToString(), native.Version,
                    native.Reason, native.RequestedBy, native.RequestedAtUtc, decision.DecidedBy, decision.DecidedAtUtc, decision.RejectionReason, native.ValidOn);
                Current = Seal(source with { Recorded = after });
                var response = new SalesRiskExceptionCommandResult(Current.Exception, false, m.CorrelationId);
                p = new(WorkSalesRiskExceptionDecisionPreviewV1Tests.Company, Id, command, actor, SalesStore.IdempotencyKeyHash(m.IdempotencyKey), hash,
                    body.ExpectedVersion, Current.Exception.Version, m.CorrelationId, at, Guid.NewGuid(), new('A', 64), before, after, "")
                    { CurrentSource = Current.Source, CurrentResponseDigest = SalesRiskExceptionDecisionDigestsV1.CurrentResponseDigest(response) };
                p = p with { OriginalDecisionDigest = SalesRiskExceptionDecisionDigestsV1.OriginalDecisionDigest(p) };
                Commands.Add((command, m.IdempotencyKey), p); Effects++; AfterEffect?.Invoke();
            }
            await Fence(actor, ct); var answer = Observe(p!, replay);
            return answer with { OriginalDecisionReceipt = Legacy ? null : ChangeReceipt is null ? answer.OriginalDecisionReceipt : ChangeReceipt(answer.OriginalDecisionReceipt!) };
        }
        private SalesRiskExceptionCommandResult Observe(SalesRiskExceptionOriginalDecisionReceiptV1 receipt, bool replay)
        {
            var response = new SalesRiskExceptionCommandResult(Current.Exception, replay, receipt.OriginalCorrelationId);
            return response with { OriginalDecisionReceipt = receipt with { CurrentSource = Current.Source, CurrentResponseDigest = SalesRiskExceptionDecisionDigestsV1.CurrentResponseDigest(response) } };
        }
        private async Task Fence(string actor, CancellationToken ct)
        {
            ct.ThrowIfCancellationRequested(); var p = await authority.RevalidatePermissionScopeAsync(SalesPermissionManifestV1.OrdersRiskExceptionApprove, WorkSalesRiskExceptionDecisionPreviewV1Tests.Company, ct);
            ct.ThrowIfCancellationRequested(); if (!p.IsGranted || !p.IsCompanyWide || rig.User.UserId != actor || !rig.User.Member || rig.Company.CompanyId != WorkSalesRiskExceptionDecisionPreviewV1Tests.Company)
                throw new SalesDraftServiceException(403, "forbidden");
        }
        public Task<SalesRiskExceptionCommandResult> RequestRiskExceptionAsync(Guid id, RequestSalesRiskExceptionRequest body, SalesCommandMetadata m, CancellationToken ct = default) => throw new NotSupportedException("No Work Request");
        public Task<SalesRiskExceptionCommandResult> WithdrawRiskExceptionAsync(Guid id, int no, SalesDraftStateCommandRequest body, SalesCommandMetadata m, CancellationToken ct = default) => throw new NotSupportedException("No Work Withdraw");
        public Task<SalesRiskExceptionListResponse> GetRiskExceptionsAsync(Guid id, CancellationToken ct = default) => throw new NotSupportedException("No general Risk read");
    }
    private sealed class SourceProxy(IWorkApprovalDecisionVersionSourceV1 inner) : IWorkApprovalDecisionVersionSourceV1
    {
        internal int Reads; internal string? Selected, Permission; internal WorkApprovalDecisionSnapshotV1? Last; internal Func<WorkApprovalDecisionSnapshotV1, WorkApprovalDecisionSnapshotV1>? Change;
        public string Module => "sales"; public string Kind => inner.Kind; public string ApproverPermission => Permission ?? inner.ApproverPermission;
        public Task<WorkInboxSourceResultV1> ListPendingAsync(WorkInboxSourceContextV1 c, int n, CancellationToken ct) => throw new NotSupportedException("No list fallback");
        public async Task<WorkApprovalDecisionSnapshotV1?> ReadDecisionAsync(WorkInboxSourceContextV1 c, Guid id, CancellationToken ct)
        { Reads++; var s = await inner.ReadDecisionAsync(c, id, ct); return Last = s is null ? null : Change?.Invoke(s) ?? s; }
        public async Task<WorkApprovalDecisionSnapshotV1?> ReadDecisionVersionAsync(WorkInboxSourceContextV1 c, Guid id, string version, CancellationToken ct)
        { Reads++; Selected = version; var s = await inner.ReadDecisionVersionAsync(c, id, version, ct); return Last = s is null ? null : Change?.Invoke(s) ?? s; }
    }
    private sealed class GenericSource(string kind) : IWorkApprovalDecisionSourceV1
    {
        internal WorkSalesRiskExceptionProofV1? Foreign;
        public string Module => kind.StartsWith("finance", StringComparison.Ordinal) ? "finance" : "inventory"; public string Kind => kind;
        public string ApproverPermission => kind switch { WorkDecisionHostV1.OpeningCost => ErpPermissions.InventoryOpeningCostApprove,
            "finance.cash-account-passivation.v1" => ErpPermissions.FinanceCashAccountPassivationApprove,
            "finance.cash-account-reactivation.v1" => ErpPermissions.FinanceCashAccountReactivationApprove, _ => ErpPermissions.FinanceCashAccountApprove };
        public Task<WorkInboxSourceResultV1> ListPendingAsync(WorkInboxSourceContextV1 c, int n, CancellationToken ct) => throw new NotSupportedException();
        public Task<WorkApprovalDecisionSnapshotV1?> ReadDecisionAsync(WorkInboxSourceContextV1 c, Guid id, CancellationToken ct) =>
            Task.FromResult<WorkApprovalDecisionSnapshotV1?>(new(Company, new(Module, Kind, id, "SENTETIK generic source", At, "/onaylarim", Maker.ToString("D"), [], []), "7", true) { SalesRiskExceptionProof = Foreign });
    }
    private sealed class MutableCompany : ICompanyContext { public Guid CompanyId { get; private set; } public bool IsResolved => CompanyId != Guid.Empty; public void Set(Guid id) => CompanyId = id; }
    private sealed class MutableUser : ICurrentUser
    {
        internal Guid ActorId = Actor; internal bool Member = true, Authenticated = true; public bool IsAuthenticated => Authenticated; public string UserId => ActorId.ToString("D");
        public IReadOnlySet<Guid> AllowedCompanyIds { get; } = new HashSet<Guid>([WorkSalesRiskExceptionDecisionPreviewV1Tests.Company]);
        public bool CanAccessCompany(Guid id) => Member && id == WorkSalesRiskExceptionDecisionPreviewV1Tests.Company;
    }
    private sealed class Authority : IErpAuthorityRevalidationService
    {
        internal bool Allowed = true, CompanyWide = true; internal readonly List<string> Permissions = []; internal readonly HashSet<string> Additional = new(StringComparer.Ordinal);
        public Task<PermissionScope> RevalidatePermissionScopeAsync(string p, Guid company, CancellationToken ct = default)
        { Permissions.Add(p); return Task.FromResult(new PermissionScope(Allowed && (p == SalesPermissionManifestV1.OrdersRiskExceptionApprove || Additional.Contains(p)), CompanyWide, new HashSet<string>(), new HashSet<Guid>(), new HashSet<Guid>(), new HashSet<Guid>())); }
    }
    private sealed class Delegations : IWorkDelegationReaderV1
    {
        internal bool Live = true;
        public Task<WorkDelegationViewV1?> FindForActorAsync(Guid id, WorkInboxItemV1 item, CancellationToken ct = default) => Task.FromResult<WorkDelegationViewV1?>(Live
            ? new(id, Company, Kind, SalesPermissionManifestV1.OrdersRiskExceptionApprove, Maker, Actor, At.AddYears(-1), At.AddYears(1), "1", Maker, At, "SENTETIK", null, null, null) : null);
    }
}
