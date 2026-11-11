using GoymenERP.Modules.Purchasing.Application.Contracts;
using GoymenERP.Modules.Purchasing.Domain;
using GoymenERP.Modules.Purchasing.Domain.Orders;
using GoymenERP.Modules.Purchasing.Domain.Requests;
using GoymenERP.Modules.Purchasing.Infrastructure.Persistence.Migrations;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore.Migrations.Operations;

namespace GoymenERP.Api.IntegrationTests;

/// <summary>
/// A1-1 (plan API-01, TEK-09; ALS-D2, ALS-D16, No.161 §1) on real SQL Server: a request's approval and rejection are
/// stored in ONE save each (header facts, attempt, audit) and the same key replays; the preparer cannot decide (the
/// domain refuses and the CK_PurchaseRequests_MakerChecker check refuses a direct write too); the decision facts match
/// the status (CK_PurchaseRequests_Decision); a bound APPROVED order blocks the rejection; a rejection and a binding
/// order approval racing on one request leave exactly one of them; a rejected request's draft order is never approved;
/// company B neither sees nor decides company A's request; and the migration's submitter backfill and Down guard run
/// as written. SENTETIK owners.
/// </summary>
public sealed partial class PurchasingSqlServerRelationalAcceptanceTests
{
    private const string Manager = "SENTETIK-manager";
    private static readonly PurchasingMutationAudit RequestApproved =
        new PurchasingMutationAudit("purchasing.purchase-request.approved.v1", "Approve", "SENTETIK talep onaylandı");
    private static readonly PurchasingMutationAudit RequestRejected =
        new PurchasingMutationAudit("purchasing.purchase-request.rejected.v1", "Reject", "SENTETIK talep reddedildi");

    [SqlServerFact]
    public async Task A_request_decision_is_one_save_the_maker_cannot_decide_and_the_checks_hold_the_facts()
    {
        await using Run run = await Run.StartAsync();
        Owners owners = Owners.For(CompanyA);
        Guid requestId = await SubmittedRequestAsync(run, owners, 100m);

        await using (StoreScope a = run.Store(CompanyA))
        {
            // Hazırlayan ≠ onaylayan (ALS-D2): the run's actor prepared and submitted the request.
            var self = await Assert.ThrowsAsync<PurchasingRuleException>(() => a.Store.MutateRequestAsync(run.Envelope("k-self"), Hash("self"),
                PurchasingCommands.RequestApproveV1, requestId, (r, _, now) => r.Approve(3, run.Actor, now), RequestApproved));
            Assert.Equal(PurchasingRuleCodes.RequestSameActor, self.Code);
        }

        Assert.Equal((1, 1, 2, 2, 3L), await run.RequestCountsAsync(requestId));

        await using (StoreScope a = run.Store(CompanyA))
        {
            var approved = await ApproveRequestAsync(run, a, requestId, 3);
            Assert.Equal((PurchaseRequestStatus.Approved, 4L, Manager), (approved.Request.Status, approved.Request.Version, approved.Request.Approval!.DecidedBy));
            var replay = await a.Store.MutateRequestAsync(run.Envelope("k-approve-request", correlation: "SENTETIK-second", actor: Manager), Hash("k-approve-request"),
                PurchasingCommands.RequestApproveV1, requestId, (_, _, _) => throw new InvalidOperationException("a replay never mutates"), RequestApproved);
            Assert.Equal((PurchasingWriteOutcome.Replayed, run.Correlation, 4L), (replay!.Outcome, replay.OriginalCorrelationId, replay.Request.Request.Version));
        }

        Assert.Equal((1, 1, 3, 3, 4L), await run.RequestCountsAsync(requestId));
        Assert.Equal((run.Actor, Manager), (await run.ScalarAsync<string>("SELECT SubmittedBy FROM purchasing.PurchaseRequests WHERE Id = @id", ("@id", requestId)),
            await run.ScalarAsync<string>("SELECT ApprovedBy FROM purchasing.PurchaseRequests WHERE Id = @id", ("@id", requestId))));
        Assert.Equal(1, await run.ScalarAsync<int>("SELECT COUNT(*) FROM audit.AuditEvents WHERE EventType = N'purchasing.purchase-request.approved.v1' AND ActorId = @actor",
            ("@actor", Manager)));

        await using (StoreScope a = run.Store(CompanyA))
        {
            // Rebuilt through the domain (lines, submission, approval): the same version and decision.
            var rebuilt = await a.Store.FindRequestAsync(requestId);
            Assert.Equal((PurchaseRequestStatus.Approved, 4L, run.Actor, Manager), (rebuilt!.Request.Status, rebuilt.Request.Version,
                rebuilt.Request.SubmittedBy, rebuilt.Request.Approval!.DecidedBy));
            Assert.Equal(requestId, Assert.Single((await a.Store.ListRequestsAsync(10, null, PurchaseRequestStatus.Approved)).Items).Request.Id);
        }

        // The checks refuse what the domain refuses, on a direct write too (547: CHECK constraint).
        var maker = await Assert.ThrowsAsync<SqlException>(() => run.ExecuteAsync(
            "UPDATE purchasing.PurchaseRequests SET ApprovedBy = CreatedBy WHERE Id = @id", ("@id", requestId)));
        Assert.Equal(547, maker.Number);
        Assert.Contains("CK_PurchaseRequests_MakerChecker", maker.Message, StringComparison.Ordinal);
        var facts = await Assert.ThrowsAsync<SqlException>(() => run.ExecuteAsync(
            "UPDATE purchasing.PurchaseRequests SET Status = 4 WHERE Id = @id", ("@id", requestId)));
        Assert.Equal(547, facts.Number);
        Assert.Contains("CK_PurchaseRequests_Decision", facts.Message, StringComparison.Ordinal);
        Assert.Equal(4L, await run.ScalarAsync<long>("SELECT Version FROM purchasing.PurchaseRequests WHERE Id = @id", ("@id", requestId)));
    }

    [SqlServerFact]
    public async Task A_bound_approved_order_blocks_the_rejection_and_a_rejected_requests_draft_order_is_never_approved()
    {
        await using Run run = await Run.StartAsync();
        Owners owners = Owners.For(CompanyA);
        Guid blockedId = await SubmittedRequestAsync(run, owners, 100m);
        Guid rejectedId = await SubmittedRequestAsync(run, owners, 50m, "k2");
        Guid approvedOrder;
        Guid orphanOrder;
        await using (StoreScope a = run.Store(CompanyA))
        {
            await ApproveRequestAsync(run, a, blockedId, 3);
            await ApproveRequestAsync(run, a, rejectedId, 3, "k-approve-request-2");
            Guid blockedLine = (await a.Store.FindRequestAsync(blockedId))!.Request.Lines[0].Id;
            Guid rejectedLine = (await a.Store.FindRequestAsync(rejectedId))!.Request.Lines[0].Id;
            PurchaseOrder builtA = Order(owners, blockedId, blockedLine, 40m);
            approvedOrder = (await a.Store.StoreOrderAsync(run.Envelope("k-order-a"), Hash("order-a"), owners.Account, Day, () => builtA, VersionsOf(builtA))).Order.Order.Id;
            PurchaseOrder builtB = Order(owners, rejectedId, rejectedLine, 50m);
            orphanOrder = (await a.Store.StoreOrderAsync(run.Envelope("k-order-b"), Hash("order-b"), owners.Account, Day, () => builtB, VersionsOf(builtB))).Order.Order.Id;
            await a.Store.MutateOrderAsync(run.Envelope("k-approve-a"), Hash("approve-a"), PurchasingCommands.OrderApproveV1, approvedOrder, (o, now) => o.Approve(2, OrderApprover, now), Approved);
        }

        await using (StoreScope a = run.Store(CompanyA))
        {
            // No.161 §1: the bound approved order is cancelled first; the store hands the domain the fact read in the same save.
            var blocked = await Assert.ThrowsAsync<PurchasingRuleException>(() => a.Store.MutateRequestAsync(run.Envelope("k-reject-blocked", actor: Manager),
                Hash("reject-blocked"), PurchasingCommands.RequestRejectV1, blockedId,
                (r, bindings, now) => r.Reject(4, Manager, "SENTETIK", bindings.HasApprovedOrder, now), RequestRejected));
            Assert.Equal(PurchasingRuleCodes.RequestHasApprovedOrder, blocked.Code);
        }

        Assert.Equal("Approved", await StatusOfAsync(run, blockedId));

        await using (StoreScope a = run.Store(CompanyA))
        {
            // A draft order binds nothing: its request is rejected with the reason, and keeps its approval.
            var rejected = await a.Store.MutateRequestAsync(run.Envelope("k-reject", actor: Manager), Hash("reject"), PurchasingCommands.RequestRejectV1, rejectedId,
                (r, bindings, now) => r.Reject(4, Manager, "SENTETIK ihtiyaç kalktı", bindings.HasApprovedOrder, now), RequestRejected);
            Assert.Equal((PurchaseRequestStatus.Rejected, 5L, "SENTETIK ihtiyaç kalktı"),
                (rejected!.Request.Request.Status, rejected.Request.Request.Version, rejected.Request.Request.Rejection!.Reason));
            Assert.NotNull((await a.Store.FindRequestAsync(rejectedId))!.Request.Approval);
        }

        await using (StoreScope a = run.Store(CompanyA))
        {
            // ALS-D2 (fail-closed): the rejected request's draft order never becomes a commitment; nothing is written.
            var refused = await Assert.ThrowsAsync<PurchasingStoreException>(() => a.Store.MutateOrderAsync(run.Envelope("k-approve-orphan"), Hash("approve-orphan"),
                PurchasingCommands.OrderApproveV1, orphanOrder, (o, now) => o.Approve(2, OrderApprover, now), Approved));
            Assert.Equal((PurchasingStoreCodes.SourceRequestNotApproved, "sourceRequestId", "not_approved"),
                (refused.Code, refused.Errors[0].Field, refused.Errors[0].Code));
        }

        Assert.Equal(1, await run.ScalarAsync<int>("SELECT COUNT(*) FROM purchasing.PurchaseOrders WHERE Status = 2"));
        Assert.Equal(2L, await run.ScalarAsync<long>("SELECT Version FROM purchasing.PurchaseOrders WHERE Id = @id", ("@id", orphanOrder)));
        Assert.Equal(1, await run.ScalarAsync<int>("SELECT COUNT(*) FROM audit.AuditEvents WHERE EventType = N'purchasing.purchase-request.rejected.v1'"));
        Assert.Equal(0, await run.ScalarAsync<int>("SELECT COUNT(*) FROM purchasing.PurchaseAttempts WHERE CommandName = @command AND RequestId = @id",
            ("@command", PurchasingCommands.RequestRejectV1), ("@id", blockedId)));
    }

    [SqlServerFact]
    public async Task A_rejection_and_a_binding_order_approval_racing_on_one_request_leave_exactly_one_of_them()
    {
        await using Run run = await Run.StartAsync();
        Owners owners = Owners.For(CompanyA);
        Guid requestId = await SubmittedRequestAsync(run, owners, 10m);
        Guid orderId;
        await using (StoreScope a = run.Store(CompanyA))
        {
            await ApproveRequestAsync(run, a, requestId, 3);
            Guid line = (await a.Store.FindRequestAsync(requestId))!.Request.Lines[0].Id;
            PurchaseOrder built = Order(owners, requestId, line, 6m);
            orderId = (await a.Store.StoreOrderAsync(run.Envelope("k-order"), Hash("order"), owners.Account, Day, () => built, VersionsOf(built))).Order.Order.Id;
        }

        // Both read "no approved order" / "request approved" before either saves; both move the request row; one row version wins.
        Barrier barrier = new Barrier(2);
        async Task<object> RejectAsync()
        {
            await using StoreScope writer = run.Store(CompanyA, new BarrierInterceptor(barrier));
            try
            {
                var result = await writer.Store.MutateRequestAsync(run.Envelope("k-race-reject", actor: Manager), Hash("race-reject"), PurchasingCommands.RequestRejectV1,
                    requestId, (r, bindings, now) => r.Reject(4, Manager, "SENTETIK", bindings.HasApprovedOrder, now), RequestRejected);
                return result!.Outcome;
            }
            catch (PurchasingStoreException exception)
            {
                return exception.Code;
            }
        }

        async Task<object> ApproveOrderAsync()
        {
            await using StoreScope writer = run.Store(CompanyA, new BarrierInterceptor(barrier));
            try
            {
                var result = await writer.Store.MutateOrderAsync(run.Envelope("k-race-order"), Hash("race-order"), PurchasingCommands.OrderApproveV1, orderId,
                    (o, now) => o.Approve(2, OrderApprover, now), Approved);
                return result!.Outcome;
            }
            catch (PurchasingStoreException exception)
            {
                return exception.Code;
            }
        }

        var results = await Task.WhenAll(Task.Run(RejectAsync), Task.Run(ApproveOrderAsync));
        Assert.Single(results, result => result is PurchasingWriteOutcome.Recorded);
        Assert.Single(results, result => result is string code && code == PurchasingStoreCodes.ConcurrentChange);

        var rejected = await StatusOfAsync(run, requestId) == "Rejected";
        var approvedOrders = await run.ScalarAsync<int>("SELECT COUNT(*) FROM purchasing.PurchaseOrders WHERE Status = 2 AND SourceRequestId = @id", ("@id", requestId));
        // No.161 §1 holds whichever won: never a rejected request with an approved order bound to it.
        Assert.True(rejected ^ (approvedOrders == 1), $"rejected={rejected}, approvedOrders={approvedOrders}");

        await using (StoreScope a = run.Store(CompanyA))
        {
            // The loser measures again on its retry and is refused by the winner's fact.
            if (rejected)
            {
                var retry = await Assert.ThrowsAsync<PurchasingStoreException>(() => a.Store.MutateOrderAsync(run.Envelope("k-race-order"), Hash("race-order"),
                    PurchasingCommands.OrderApproveV1, orderId, (o, now) => o.Approve(2, OrderApprover, now), Approved));
                Assert.Equal(PurchasingStoreCodes.SourceRequestNotApproved, retry.Code);
            }
            else
            {
                var retry = await Assert.ThrowsAsync<PurchasingRuleException>(() => a.Store.MutateRequestAsync(run.Envelope("k-race-reject", actor: Manager),
                    Hash("race-reject"), PurchasingCommands.RequestRejectV1, requestId,
                    (r, bindings, now) => r.Reject(4, Manager, "SENTETIK", bindings.HasApprovedOrder, now), RequestRejected));
                Assert.Equal(PurchasingRuleCodes.RequestHasApprovedOrder, retry.Code);
            }
        }
    }

    [SqlServerFact]
    public async Task Company_B_neither_sees_nor_decides_company_As_request()
    {
        await using Run run = await Run.StartAsync();
        Guid requestId = await SubmittedRequestAsync(run, Owners.For(CompanyA), 5m);

        await using (StoreScope b = run.Store(CompanyB))
        {
            Assert.Null(await b.Store.FindRequestAsync(requestId));
            Assert.Empty((await b.Store.ListRequestsAsync(10, null, PurchaseRequestStatus.Submitted)).Items);
            Assert.Null(await b.Store.MutateRequestAsync(run.Envelope("k-b-approve", actor: Manager), Hash("b-approve"), PurchasingCommands.RequestApproveV1, requestId,
                (r, _, now) => r.Approve(3, Manager, now), RequestApproved));
            Assert.Null(await b.Store.MutateRequestAsync(run.Envelope("k-b-reject", actor: Manager), Hash("b-reject"), PurchasingCommands.RequestRejectV1, requestId,
                (r, bindings, now) => r.Reject(3, Manager, "SENTETIK", bindings.HasApprovedOrder, now), RequestRejected));
        }

        Assert.Equal("Submitted", await StatusOfAsync(run, requestId));
        Assert.Equal((1, 1, 2, 2, 3L), await run.RequestCountsAsync(requestId));
    }

    [SqlServerFact]
    public async Task The_migration_backfills_a_recorded_submitter_and_its_Down_refuses_while_a_decision_exists()
    {
        await using Run run = await Run.StartAsync();
        Owners owners = Owners.For(CompanyA);
        Guid requestId;
        await using (StoreScope a = run.Store(CompanyA))
        {
            // A submission without its actor (the shape before A1-1): the attempt still records who submitted.
            PurchaseRequest built = Request(owners, 5m);
            requestId = (await a.Store.StoreRequestAsync(run.Envelope("k-legacy"), Hash("legacy"), Day, () => built, VersionsOf(built))).Request.Request.Id;
            await a.Store.MutateRequestAsync(run.Envelope("k-legacy-submit"), Hash("legacy-submit"), PurchasingCommands.RequestSubmitV1, requestId,
                (r, _, now) => r.Submit(2, now), Submitted);
            // Fail-closed: no one decides a request whose submitter is unknown.
            var unknown = await Assert.ThrowsAsync<PurchasingRuleException>(() => a.Store.MutateRequestAsync(run.Envelope("k-legacy-approve", actor: Manager),
                Hash("legacy-approve"), PurchasingCommands.RequestApproveV1, requestId, (r, _, now) => r.Approve(3, Manager, now), RequestApproved));
            Assert.Equal(PurchasingRuleCodes.RequestMakerUnknown, unknown.Code);
        }

        Assert.Equal(DBNull.Value, await run.ScalarAsync<object>("SELECT SubmittedBy FROM purchasing.PurchaseRequests WHERE Id = @id", ("@id", requestId)));
        var migration = new AddPurchaseRequestDecisions();
        await run.ExecuteAsync(Assert.Single(migration.UpOperations.OfType<SqlOperation>()).Sql);
        Assert.Equal(run.Actor, await run.ScalarAsync<string>("SELECT SubmittedBy FROM purchasing.PurchaseRequests WHERE Id = @id", ("@id", requestId)));

        await using (StoreScope a = run.Store(CompanyA))
        {
            await ApproveRequestAsync(run, a, requestId, 3);
        }

        var guard = await Assert.ThrowsAsync<SqlException>(() => run.ExecuteAsync(Assert.IsType<SqlOperation>(migration.DownOperations[0]).Sql));
        Assert.Equal(51164, guard.Number);
        Assert.Equal("Approved", await StatusOfAsync(run, requestId));
    }

    // ---- helpers --------------------------------------------------------------------------------

    /// <summary>A request created and submitted by the run's actor (the two makers), Submitted at version 3.</summary>
    private static async Task<Guid> SubmittedRequestAsync(Run run, Owners owners, decimal quantity, string keyPrefix = "k")
    {
        await using StoreScope a = run.Store(owners.Company);
        PurchaseRequest built = Request(owners, quantity, run.Actor);
        Guid id = (await a.Store.StoreRequestAsync(run.Envelope($"{keyPrefix}-req"), Hash($"{keyPrefix}-req"), Day, () => built, VersionsOf(built))).Request.Request.Id;
        await a.Store.MutateRequestAsync(run.Envelope($"{keyPrefix}-submit"), Hash($"{keyPrefix}-submit"), PurchasingCommands.RequestSubmitV1, id,
            (r, _, now) => r.Submit(2, run.Actor, now), Submitted);
        return id;
    }

    /// <summary>The manager (not the run's actor, who prepared and submitted) approves the request at <paramref name="version"/>.</summary>
    private static async Task<StoredPurchaseRequest> ApproveRequestAsync(Run run, StoreScope scope, Guid requestId, long version, string key = "k-approve-request")
    {
        var result = await scope.Store.MutateRequestAsync(run.Envelope(key, actor: Manager), Hash(key),
            PurchasingCommands.RequestApproveV1, requestId, (r, _, now) => r.Approve(version, Manager, now), RequestApproved);
        return result!.Request;
    }

    private static Task<string> StatusOfAsync(Run run, Guid requestId) =>
        run.ScalarAsync<int>("SELECT Status FROM purchasing.PurchaseRequests WHERE Id = @id", ("@id", requestId))
            .ContinueWith(task => ((PurchaseRequestStatus)task.Result).ToString(), TaskScheduler.Default);
}
