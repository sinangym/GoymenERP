using GoymenERP.Modules.Purchasing.Application.Contracts;
using GoymenERP.Modules.Purchasing.Domain;

namespace GoymenERP.Purchasing.UnitTests;

/// <summary>
/// A1-1b (plan API-01b; ALS-D46, ALS-D47): the withdrawal through the service — the <c>purchasing.requests.withdraw</c>
/// name, "only the preparer or the submitter", the terminal refusals recorded under the key and replayed, the reason's 422, the audit
/// event and the detail's withdrawal and submitter. SENTETIK / NOT_ENV.
/// </summary>
public sealed partial class PurchasingServiceTests
{
    [Fact]
    public async Task The_submitter_withdraws_a_submitted_request_under_the_withdraw_name_and_the_same_key_replays()
    {
        var world = new World();
        var id = await SubmittedRequestAsync(world);

        var withdrawn = await world.Service().WithdrawRequestAsync(id, new WithdrawPurchaseRequestRequest("3", "SENTETIK yanlış ürün"),
            Metadata("k-withdraw", correlation: "withdraw-1"));

        Assert.Equal(("Withdrawn", "4", false), (withdrawn.Detail.Request.Status, withdrawn.Detail.Request.Version, withdrawn.IsReplay));
        Assert.Equal(PurchasingPermissionManifestV1.RequestsWithdraw, world.Authority.LastPermission);
        Assert.Equal((Actor, "SENTETIK yanlış ürün"), (withdrawn.Detail.Withdrawal!.DecidedBy, withdrawn.Detail.Withdrawal.Reason));
        Assert.Equal((Actor, Actor), (withdrawn.Detail.PreparedBy, withdrawn.Detail.SubmittedBy));
        Assert.Null(withdrawn.Detail.Rejection);
        Assert.Null(withdrawn.Detail.Approval);
        Assert.Equal(("purchasing.purchase-request.withdrawn.v1", "Withdraw"), (world.Store.Audits.Last().EventType, world.Store.Audits.Last().Operation));
        var envelope = world.Store.Envelopes.Last();
        Assert.Equal((Actor, PurchasingPermissionManifestV1.RequestsWithdraw), (envelope.ActorId, envelope.PermissionCode));

        var replay = await world.Service().WithdrawRequestAsync(id, new WithdrawPurchaseRequestRequest("3", "SENTETIK yanlış ürün"),
            Metadata("k-withdraw", correlation: "withdraw-2"));
        Assert.Equal((true, "withdraw-1", "4"), (replay.IsReplay, replay.OriginalCorrelationId, replay.Detail.Request.Version));
        Assert.Equal(1, world.Store.Transitions.Count(item => item.Command == PurchasingCommands.RequestWithdrawV1));
        var detail = await world.Service().GetRequestDetailAsync(id);
        Assert.Equal(("Withdrawn", Actor), (detail.Request.Status, detail.SubmittedBy));
    }

    [Fact]
    public async Task A_third_party_cannot_withdraw_and_the_refusal_is_terminal()
    {
        var world = new World();
        var id = await SubmittedRequestAsync(world);

        var other = await world.AsApprover(() => Assert.ThrowsAsync<PurchasingServiceException>(() =>
            world.Service().WithdrawRequestAsync(id, new WithdrawPurchaseRequestRequest("3", "SENTETIK"), Metadata("k-other"))));
        Assert.Equal((409, PurchasingProblemCodesV1.RequestNotPreparer, "actor", PurchasingRuleCodes.RequestNotPreparer, false),
            (other.Status, other.Code, other.Errors[0].Field, other.Errors[0].Code, other.IsReplay));
        var again = await world.AsApprover(() => Assert.ThrowsAsync<PurchasingServiceException>(() =>
            world.Service().WithdrawRequestAsync(id, new WithdrawPurchaseRequestRequest("3", "SENTETIK"), Metadata("k-other", correlation: "retry"))));
        Assert.Equal((409, PurchasingProblemCodesV1.RequestNotPreparer, true), (again.Status, again.Code, again.IsReplay));
        Assert.Single(world.Store.Refusals);
        Assert.Equal(("Submitted", "3"), ((await world.Service().GetRequestDetailAsync(id)).Request.Status, (await world.Service().GetRequestDetailAsync(id)).Request.Version));
    }

    [Fact]
    public async Task When_the_creator_and_the_submitter_differ_either_withdraws_and_a_third_party_cannot()
    {
        // ALS-D47 (Sinan 2026-11-02): "oluşturan veya gönderen". The approver persona submits what the actor prepared.
        async Task<Guid> PreparedByActorSubmittedByApprover(World world, string suffix)
        {
            var id = (await world.Service().CreateRequestAsync(RequestOf("5"), Metadata($"k-req-{suffix}"))).Detail.Request.Id;
            await world.AsApprover(() => world.Service().SubmitRequestAsync(id, new SubmitPurchaseRequestRequest("2"), Metadata($"k-submit-{suffix}")));
            return id;
        }

        var world = new World();
        var byCreator = await PreparedByActorSubmittedByApprover(world, "a");
        var creator = await world.Service().WithdrawRequestAsync(byCreator, new WithdrawPurchaseRequestRequest("3", "SENTETIK"), Metadata("k-creator"));
        Assert.Equal(("Withdrawn", Actor, Actor, Approver), (creator.Detail.Request.Status, creator.Detail.Withdrawal!.DecidedBy,
            creator.Detail.PreparedBy, creator.Detail.SubmittedBy));

        var bySubmitter = await PreparedByActorSubmittedByApprover(world, "b");
        var submitter = await world.AsApprover(() =>
            world.Service().WithdrawRequestAsync(bySubmitter, new WithdrawPurchaseRequestRequest("3", "SENTETIK"), Metadata("k-submitter")));
        Assert.Equal(("Withdrawn", Approver), (submitter.Detail.Request.Status, submitter.Detail.Withdrawal!.DecidedBy));

        var third = await PreparedByActorSubmittedByApprover(world, "c");
        world.User.CurrentActor = "synthetic-third-actor";
        var refused = await Assert.ThrowsAsync<PurchasingServiceException>(() =>
            world.Service().WithdrawRequestAsync(third, new WithdrawPurchaseRequestRequest("3", "SENTETIK"), Metadata("k-third")));
        Assert.Equal((409, PurchasingProblemCodesV1.RequestNotPreparer, "actor"), (refused.Status, refused.Code, refused.Errors[0].Field));
        world.User.CurrentActor = Actor;
        Assert.Equal("Submitted", (await world.Service().GetRequestDetailAsync(third)).Request.Status);
    }

    [Fact]
    public async Task Draft_approved_and_stale_requests_are_terminal_refusals_that_replay()
    {
        var world = new World();
        var draft = (await world.Service().CreateRequestAsync(RequestOf("1"), Metadata("k-draft"))).Detail.Request.Id;
        var fromDraft = await Assert.ThrowsAsync<PurchasingServiceException>(() =>
            world.Service().WithdrawRequestAsync(draft, new WithdrawPurchaseRequestRequest("2", "SENTETIK"), Metadata("k-draft-withdraw")));
        Assert.Equal((409, PurchasingProblemCodesV1.RequestDecisionState, "status", PurchasingRuleCodes.RequestNotWithdrawable),
            (fromDraft.Status, fromDraft.Code, fromDraft.Errors[0].Field, fromDraft.Errors[0].Code));

        var id = await SubmittedRequestAsync(world);
        var stale = await Assert.ThrowsAsync<PurchasingServiceException>(() =>
            world.Service().WithdrawRequestAsync(id, new WithdrawPurchaseRequestRequest("2", "SENTETIK"), Metadata("k-stale")));
        Assert.Equal((409, PurchasingProblemCodesV1.VersionConflict, "expectedVersion"), (stale.Status, stale.Code, stale.Errors[0].Field));

        // An approved request is never withdrawn; its rejection stays the manager's decision (ALS-D46, No.161 §1).
        await world.ApproveRequestAsApprover(id, "3");
        var approved = await Assert.ThrowsAsync<PurchasingServiceException>(() =>
            world.Service().WithdrawRequestAsync(id, new WithdrawPurchaseRequestRequest("4", "SENTETIK"), Metadata("k-approved")));
        Assert.Equal((409, PurchasingProblemCodesV1.RequestDecisionState, PurchasingRuleCodes.RequestNotWithdrawable),
            (approved.Status, approved.Code, approved.Errors[0].Code));
        var replayed = await Assert.ThrowsAsync<PurchasingServiceException>(() =>
            world.Service().WithdrawRequestAsync(id, new WithdrawPurchaseRequestRequest("4", "SENTETIK"), Metadata("k-approved", correlation: "retry")));
        Assert.True(replayed.IsReplay);
        Assert.Equal(3, world.Store.Refusals.Count);
        Assert.Equal("Approved", (await world.Service().GetRequestDetailAsync(id)).Request.Status);
    }

    [Theory]
    [InlineData("", PurchasingProblemCodesV1.RequestWithdrawalReasonRequired)]
    [InlineData("   ", PurchasingProblemCodesV1.RequestWithdrawalReasonRequired)]
    [InlineData("long", PurchasingProblemCodesV1.RequestWithdrawalReasonTooLong)]
    public async Task A_withdrawal_needs_a_reason_that_is_never_cut_and_the_422_is_not_recorded(string reason, string code)
    {
        var world = new World();
        var id = await SubmittedRequestAsync(world);
        var text = reason == "long" ? new string('x', 4001) : reason;

        var refusal = await Assert.ThrowsAsync<PurchasingServiceException>(() =>
            world.Service().WithdrawRequestAsync(id, new WithdrawPurchaseRequestRequest("3", text), Metadata("k-withdraw")));

        Assert.Equal((422, code, "reason"), (refusal.Status, refusal.Code, refusal.Errors[0].Field));
        Assert.Empty(world.Store.Refusals);
        Assert.Equal("Submitted", (await world.Service().GetRequestDetailAsync(id)).Request.Status);
    }

    [Fact]
    public async Task The_withdraw_name_is_measured_before_anything_and_neither_decision_name_confers_it()
    {
        var world = new World();
        var id = await SubmittedRequestAsync(world);
        world.Authority.Granted = false;
        var denied = await Assert.ThrowsAsync<PurchasingServiceException>(() =>
            world.Service().WithdrawRequestAsync(id, new WithdrawPurchaseRequestRequest("3", "SENTETIK"), Metadata("k-denied")));
        Assert.Equal((403, "forbidden"), (denied.Status, denied.Code));
        Assert.Equal(PurchasingPermissionManifestV1.RequestsWithdraw, world.Authority.LastPermission);

        world.Authority.Granted = true;
        world.Bindings = world.Bindings with { RequestWithdrawPermission = null };
        var unbound = await Assert.ThrowsAsync<PurchasingServiceException>(() =>
            world.Service().WithdrawRequestAsync(id, new WithdrawPurchaseRequestRequest("3", "SENTETIK"), Metadata("k-unbound")));
        Assert.Equal((503, PurchasingProblemCodesV1.PermissionUnregistered), (unbound.Status, unbound.Code));

        var missing = await Assert.ThrowsAsync<PurchasingServiceException>(() =>
            new World().Service().WithdrawRequestAsync(Guid.NewGuid(), new WithdrawPurchaseRequestRequest("3", "SENTETIK"), Metadata("k-missing")));
        Assert.Equal(404, missing.Status);
        Assert.Empty(world.Store.Refusals);
        Assert.Equal("Submitted", (await world.Service().GetRequestDetailAsync(id)).Request.Status);
    }

    [Fact]
    public void The_withdrawal_wire_shape_is_strict()
    {
        Assert.Equal("neden", PurchasingJson.ReadWithdrawRequest("{\"expectedVersion\":\"3\",\"reason\":\"neden\"}").Reason);
        Assert.Throws<System.Text.Json.JsonException>(() => PurchasingJson.ReadWithdrawRequest("{\"expectedVersion\":\"3\"}"));
        Assert.Throws<System.Text.Json.JsonException>(() => PurchasingJson.ReadWithdrawRequest("{\"expectedVersion\":\"3\",\"reason\":null}"));
        Assert.Throws<System.Text.Json.JsonException>(() => PurchasingJson.ReadWithdrawRequest("{\"expectedVersion\":3,\"reason\":\"x\"}"));
        Assert.Throws<System.Text.Json.JsonException>(() => PurchasingJson.ReadWithdrawRequest("{\"expectedVersion\":\"0\",\"reason\":\"x\"}"));
        Assert.Throws<System.Text.Json.JsonException>(() =>
            PurchasingJson.ReadWithdrawRequest("{\"expectedVersion\":\"3\",\"reason\":\"x\",\"actor\":\"SENTETIK-other\"}"));
        Assert.NotEqual(PurchasingRequestHash.Compute(PurchasingCommands.RequestWithdrawV1, Guid.Empty, new WithdrawPurchaseRequestRequest("3", "a")),
            PurchasingRequestHash.Compute(PurchasingCommands.RequestWithdrawV1, Guid.Empty, new WithdrawPurchaseRequestRequest("3", "b")));
        Assert.NotEqual(PurchasingRequestHash.Compute(PurchasingCommands.RequestWithdrawV1, Guid.Empty, new WithdrawPurchaseRequestRequest("3", "a")),
            PurchasingRequestHash.Compute(PurchasingCommands.RequestRejectV1, Guid.Empty, new RejectPurchaseRequestRequest("3", "a")));
    }
}
