using GoymenERP.Modules.Purchasing.Application.Contracts;
using GoymenERP.Modules.Purchasing.Domain;

namespace GoymenERP.Purchasing.UnitTests;

/// <summary>
/// A1-1 (plan API-01; ALS-D2, ALS-D16, No.161 §1): the request approval and rejection through the service — the
/// <c>purchasing.requests.approve</c> name, maker ≠ checker, the terminal refusals recorded under the key and replayed,
/// the reason's 422, the bound approved order, and the order approval's fail-closed source-request rule.
/// SENTETIK / NOT_ENV.
/// </summary>
public sealed partial class PurchasingServiceTests
{
    [Fact]
    public async Task Another_actor_approves_a_submitted_request_under_the_request_approve_name_and_the_same_key_replays()
    {
        var world = new World();
        var id = await SubmittedRequestAsync(world);

        var approved = await world.AsApprover(() =>
            world.Service().ApproveRequestAsync(id, new ApprovePurchaseRequestRequest("3"), Metadata("k-approve", correlation: "approve-1")));

        Assert.Equal(("Approved", "4", false), (approved.Detail.Request.Status, approved.Detail.Request.Version, approved.IsReplay));
        Assert.Equal(PurchasingPermissionManifestV1.RequestsApprove, world.Authority.LastPermission);
        Assert.Equal(Approver, approved.Detail.Approval!.DecidedBy);
        Assert.Null(approved.Detail.Approval.Reason);
        Assert.Null(approved.Detail.Rejection);
        Assert.Equal("purchasing.purchase-request.approved.v1", world.Store.Audits.Last().EventType);
        var envelope = world.Store.Envelopes.Last();
        Assert.Equal((Approver, PurchasingPermissionManifestV1.RequestsApprove), (envelope.ActorId, envelope.PermissionCode));

        var replay = await world.AsApprover(() =>
            world.Service().ApproveRequestAsync(id, new ApprovePurchaseRequestRequest("3"), Metadata("k-approve", correlation: "approve-2")));
        Assert.Equal((true, "approve-1", "4"), (replay.IsReplay, replay.OriginalCorrelationId, replay.Detail.Request.Version));
        Assert.Equal(1, world.Store.Transitions.Count(item => item.Command == PurchasingCommands.RequestApproveV1));
    }

    [Fact]
    public async Task The_preparer_and_submitter_cannot_decide_their_own_request_and_the_refusal_is_terminal()
    {
        var world = new World();
        var id = await SubmittedRequestAsync(world);

        var approval = await Assert.ThrowsAsync<PurchasingServiceException>(() =>
            world.Service().ApproveRequestAsync(id, new ApprovePurchaseRequestRequest("3"), Metadata("k-self-approve")));
        Assert.Equal((409, PurchasingProblemCodesV1.RequestSameActor, "actor", PurchasingRuleCodes.RequestSameActor, false),
            (approval.Status, approval.Code, approval.Errors[0].Field, approval.Errors[0].Code, approval.IsReplay));
        var again = await Assert.ThrowsAsync<PurchasingServiceException>(() =>
            world.Service().ApproveRequestAsync(id, new ApprovePurchaseRequestRequest("3"), Metadata("k-self-approve", correlation: "retry")));
        Assert.Equal((409, PurchasingProblemCodesV1.RequestSameActor, true), (again.Status, again.Code, again.IsReplay));

        var rejection = await Assert.ThrowsAsync<PurchasingServiceException>(() =>
            world.Service().RejectRequestAsync(id, new RejectPurchaseRequestRequest("3", "SENTETIK"), Metadata("k-self-reject")));
        Assert.Equal((409, PurchasingProblemCodesV1.RequestSameActor), (rejection.Status, rejection.Code));
        Assert.Equal(2, world.Store.Refusals.Count);
        Assert.Equal(("Submitted", "3"), ((await world.Service().GetRequestDetailAsync(id)).Request.Status, (await world.Service().GetRequestDetailAsync(id)).Request.Version));
    }

    [Fact]
    public async Task A_submitter_other_than_the_preparer_is_also_a_maker()
    {
        var world = new World();
        var id = (await world.Service().CreateRequestAsync(RequestOf("5"), Metadata("k-req"))).Detail.Request.Id;
        // The approver submits; the preparer is another actor — neither may decide.
        await world.AsApprover(() => world.Service().SubmitRequestAsync(id, new SubmitPurchaseRequestRequest("2"), Metadata("k-submit")));

        var refusal = await world.AsApprover(() => Assert.ThrowsAsync<PurchasingServiceException>(() =>
            world.Service().ApproveRequestAsync(id, new ApprovePurchaseRequestRequest("3"), Metadata("k-approve"))));
        Assert.Equal((409, PurchasingProblemCodesV1.RequestSameActor), (refusal.Status, refusal.Code));
        var preparer = await Assert.ThrowsAsync<PurchasingServiceException>(() =>
            world.Service().ApproveRequestAsync(id, new ApprovePurchaseRequestRequest("3"), Metadata("k-approve-preparer")));
        Assert.Equal((409, PurchasingProblemCodesV1.RequestSameActor), (preparer.Status, preparer.Code));
    }

    [Fact]
    public async Task A_stale_version_and_a_wrong_state_are_terminal_and_replay()
    {
        var world = new World();
        var id = await SubmittedRequestAsync(world);

        var stale = await world.AsApprover(() => Assert.ThrowsAsync<PurchasingServiceException>(() =>
            world.Service().ApproveRequestAsync(id, new ApprovePurchaseRequestRequest("2"), Metadata("k-stale"))));
        Assert.Equal((409, PurchasingProblemCodesV1.VersionConflict, "expectedVersion"), (stale.Status, stale.Code, stale.Errors[0].Field));

        await world.ApproveRequestAsApprover(id, "3");
        var twice = await world.AsApprover(() => Assert.ThrowsAsync<PurchasingServiceException>(() =>
            world.Service().ApproveRequestAsync(id, new ApprovePurchaseRequestRequest("4"), Metadata("k-twice"))));
        Assert.Equal((409, PurchasingProblemCodesV1.RequestDecisionState, "status", PurchasingRuleCodes.RequestNotSubmitted),
            (twice.Status, twice.Code, twice.Errors[0].Field, twice.Errors[0].Code));
        var replayed = await world.AsApprover(() => Assert.ThrowsAsync<PurchasingServiceException>(() =>
            world.Service().ApproveRequestAsync(id, new ApprovePurchaseRequestRequest("4"), Metadata("k-twice", correlation: "retry"))));
        Assert.True(replayed.IsReplay);

        // A Draft request is neither approved nor rejected.
        var draft = (await world.Service().CreateRequestAsync(RequestOf("1"), Metadata("k-draft"))).Detail.Request.Id;
        var reject = await world.AsApprover(() => Assert.ThrowsAsync<PurchasingServiceException>(() =>
            world.Service().RejectRequestAsync(draft, new RejectPurchaseRequestRequest("2", "SENTETIK"), Metadata("k-draft-reject"))));
        Assert.Equal((409, PurchasingProblemCodesV1.RequestDecisionState, PurchasingRuleCodes.RequestNotRejectable),
            (reject.Status, reject.Code, reject.Errors[0].Code));
        Assert.Equal(3, world.Store.Refusals.Count);
    }

    [Theory]
    [InlineData("", PurchasingProblemCodesV1.RequestRejectionReasonRequired)]
    [InlineData("   ", PurchasingProblemCodesV1.RequestRejectionReasonRequired)]
    [InlineData("long", PurchasingProblemCodesV1.RequestRejectionReasonTooLong)]
    public async Task A_rejection_needs_a_reason_that_is_never_cut_and_the_422_is_not_recorded(string reason, string code)
    {
        var world = new World();
        var id = await SubmittedRequestAsync(world);
        var text = reason == "long" ? new string('x', 4001) : reason;

        var refusal = await world.AsApprover(() => Assert.ThrowsAsync<PurchasingServiceException>(() =>
            world.Service().RejectRequestAsync(id, new RejectPurchaseRequestRequest("3", text), Metadata("k-reject"))));

        Assert.Equal((422, code, "reason"), (refusal.Status, refusal.Code, refusal.Errors[0].Field));
        Assert.Empty(world.Store.Refusals);
        Assert.Equal("Submitted", (await world.Service().GetRequestDetailAsync(id)).Request.Status);
    }

    [Fact]
    public async Task An_approved_request_is_rejected_with_its_reason_and_keeps_its_approval()
    {
        var world = new World();
        var id = await SubmittedRequestAsync(world);
        await world.ApproveRequestAsApprover(id, "3");

        var rejected = await world.AsApprover(() =>
            world.Service().RejectRequestAsync(id, new RejectPurchaseRequestRequest("4", "SENTETIK ihtiyaç kalktı"), Metadata("k-reject")));

        Assert.Equal(("Rejected", "5"), (rejected.Detail.Request.Status, rejected.Detail.Request.Version));
        Assert.Equal((Approver, "SENTETIK ihtiyaç kalktı"), (rejected.Detail.Rejection!.DecidedBy, rejected.Detail.Rejection.Reason));
        Assert.NotNull(rejected.Detail.Approval);
        Assert.Equal("purchasing.purchase-request.rejected.v1", world.Store.Audits.Last().EventType);

        var again = await world.AsApprover(() => Assert.ThrowsAsync<PurchasingServiceException>(() =>
            world.Service().RejectRequestAsync(id, new RejectPurchaseRequestRequest("5", "SENTETIK"), Metadata("k-reject-2"))));
        Assert.Equal((409, PurchasingProblemCodesV1.RequestDecisionState), (again.Status, again.Code));
    }

    [Fact]
    public async Task A_bound_approved_order_blocks_the_rejection_and_a_rejected_requests_draft_order_is_never_approved()
    {
        var world = new World();
        var created = await world.Service().CreateRequestAsync(RequestOf("100"), Metadata("k-req"));
        var id = created.Detail.Request.Id;
        var lineId = created.Detail.Lines[0].LineId;
        await world.Service().SubmitRequestAsync(id, new SubmitPurchaseRequestRequest("2"), Metadata("k-submit"));
        await world.ApproveRequestAsApprover(id, "3");
        var approvedOrder = (await world.Service().CreateOrderAsync(OrderOf(id, lineId, "40"), Metadata("k-order-1"))).Detail.Order.Id;
        var draftOrder = (await world.Service().CreateOrderAsync(OrderOf(id, lineId, "10"), Metadata("k-order-2"))).Detail.Order.Id;
        await world.Service().ApproveOrderAsync(approvedOrder, new ApprovePurchaseOrderRequest("2"), Metadata("k-order-approve-1"));

        var blocked = await world.AsApprover(() => Assert.ThrowsAsync<PurchasingServiceException>(() =>
            world.Service().RejectRequestAsync(id, new RejectPurchaseRequestRequest("4", "SENTETIK"), Metadata("k-reject"))));
        Assert.Equal((409, PurchasingProblemCodesV1.RequestHasApprovedOrder, "status", PurchasingRuleCodes.RequestHasApprovedOrder),
            (blocked.Status, blocked.Code, blocked.Errors[0].Field, blocked.Errors[0].Code));
        var replayed = await world.AsApprover(() => Assert.ThrowsAsync<PurchasingServiceException>(() =>
            world.Service().RejectRequestAsync(id, new RejectPurchaseRequestRequest("4", "SENTETIK"), Metadata("k-reject", correlation: "retry"))));
        Assert.True(replayed.IsReplay);
        Assert.Equal("Approved", (await world.Service().GetRequestDetailAsync(id)).Request.Status);

        // A second request: rejected while its order is only a draft; that draft can never be approved afterwards.
        var second = await world.Service().CreateRequestAsync(RequestOf("50"), Metadata("k-req-2"));
        var secondId = second.Detail.Request.Id;
        await world.Service().SubmitRequestAsync(secondId, new SubmitPurchaseRequestRequest("2"), Metadata("k-submit-2"));
        await world.ApproveRequestAsApprover(secondId, "3");
        var orphan = (await world.Service().CreateOrderAsync(OrderOf(secondId, second.Detail.Lines[0].LineId, "50"), Metadata("k-order-3"))).Detail.Order.Id;
        await world.AsApprover(() =>
            world.Service().RejectRequestAsync(secondId, new RejectPurchaseRequestRequest("4", "SENTETIK"), Metadata("k-reject-2")));

        var refused = await Assert.ThrowsAsync<PurchasingServiceException>(() =>
            world.Service().ApproveOrderAsync(orphan, new ApprovePurchaseOrderRequest("2"), Metadata("k-order-approve-orphan")));
        Assert.Equal((409, PurchasingProblemCodesV1.RequestState, "sourceRequestId", "not_approved"),
            (refused.Status, refused.Code, refused.Errors[0].Field, refused.Errors[0].Code));
        Assert.Contains(world.Store.Refusals, item => item.Command == PurchasingCommands.OrderApproveV1);
        Assert.Equal("Draft", (await world.Service().GetOrderDetailAsync(orphan)).Order.Status);
        Assert.Equal("Draft", (await world.Service().GetOrderDetailAsync(draftOrder)).Order.Status);
    }

    [Fact]
    public async Task The_decision_name_is_measured_before_anything_and_an_unbound_name_is_a_setup_problem()
    {
        var world = new World();
        var id = await SubmittedRequestAsync(world);
        world.Authority.Granted = false;
        var denied = await world.AsApprover(() => Assert.ThrowsAsync<PurchasingServiceException>(() =>
            world.Service().ApproveRequestAsync(id, new ApprovePurchaseRequestRequest("3"), Metadata("k-denied"))));
        Assert.Equal((403, "forbidden"), (denied.Status, denied.Code));
        Assert.Equal(PurchasingPermissionManifestV1.RequestsApprove, world.Authority.LastPermission);

        world.Authority.Granted = true;
        world.Bindings = world.Bindings with { RequestApprovePermission = null };
        var unbound = await world.AsApprover(() => Assert.ThrowsAsync<PurchasingServiceException>(() =>
            world.Service().RejectRequestAsync(id, new RejectPurchaseRequestRequest("3", "SENTETIK"), Metadata("k-unbound"))));
        Assert.Equal((503, PurchasingProblemCodesV1.PermissionUnregistered), (unbound.Status, unbound.Code));

        var missing = await world.AsApprover(() => Assert.ThrowsAsync<PurchasingServiceException>(() =>
            new World().Service().ApproveRequestAsync(Guid.NewGuid(), new ApprovePurchaseRequestRequest("3"), Metadata("k-missing"))));
        Assert.Equal(404, missing.Status);
        Assert.Empty(world.Store.Refusals);
    }

    [Fact]
    public void The_decision_wire_shapes_are_strict()
    {
        Assert.Equal("3", PurchasingJson.ReadApproveRequest("{\"expectedVersion\":\"3\"}").ExpectedVersion);
        Assert.Throws<System.Text.Json.JsonException>(() => PurchasingJson.ReadApproveRequest("{\"expectedVersion\":\"3\",\"reason\":\"x\"}"));
        Assert.Throws<System.Text.Json.JsonException>(() => PurchasingJson.ReadApproveRequest("{\"expectedVersion\":\"0\"}"));
        Assert.Equal("neden", PurchasingJson.ReadRejectRequest("{\"expectedVersion\":\"3\",\"reason\":\"neden\"}").Reason);
        Assert.Throws<System.Text.Json.JsonException>(() => PurchasingJson.ReadRejectRequest("{\"expectedVersion\":\"3\"}"));
        Assert.Throws<System.Text.Json.JsonException>(() => PurchasingJson.ReadRejectRequest("{\"expectedVersion\":\"3\",\"reason\":null}"));
        Assert.Throws<System.Text.Json.JsonException>(() => PurchasingJson.ReadRejectRequest("{\"expectedVersion\":3,\"reason\":\"x\"}"));
        Assert.NotEqual(PurchasingRequestHash.Compute(PurchasingCommands.RequestRejectV1, Guid.Empty, new RejectPurchaseRequestRequest("3", "a")),
            PurchasingRequestHash.Compute(PurchasingCommands.RequestRejectV1, Guid.Empty, new RejectPurchaseRequestRequest("3", "b")));
    }

    private static async Task<Guid> SubmittedRequestAsync(World world)
    {
        var id = (await world.Service().CreateRequestAsync(RequestOf("5"), Metadata("k-req"))).Detail.Request.Id;
        await world.Service().SubmitRequestAsync(id, new SubmitPurchaseRequestRequest("2"), Metadata("k-submit"));
        return id;
    }
}
