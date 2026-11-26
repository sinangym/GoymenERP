using GoymenERP.Modules.Purchasing.Application.Contracts;
using GoymenERP.Modules.Purchasing.Domain.Requests;

namespace GoymenERP.Api.IntegrationTests;

public sealed partial class PurchasingSqlServerRelationalAcceptanceTests
{
    [SqlServerFact]
    public async Task Guarded_approval_replay_returns_current_rejected_request_without_repeating_the_original_decision()
    {
        await using Run run = await Run.StartAsync();
        var requestId = await SubmittedRequestAsync(run, Owners.For(CompanyA), 100m);
        const string approvalKey = "work-current-state-approval";
        await using (var approver = run.Store(CompanyA))
        {
            var approved = await approver.Store.MutateRequestGuardedAsync(
                run.Envelope(approvalKey, actor: Manager), Hash(approvalKey), PurchasingCommands.RequestApproveV1,
                requestId, (request, _, now) => request.Approve(3, Manager, now), RequestApproved,
                _ => Task.CompletedTask);
            Assert.Equal((PurchaseRequestStatus.Approved, 4L),
                (approved!.Request.Request.Status, approved.Request.Request.Version));
        }

        // A lawful later decision is retained; the original receipt does not promise an immutable response snapshot.
        const string rejectingActor = "SENTETIK-other-checker";
        await using (var rejector = run.Store(CompanyA))
        {
            var rejected = await rejector.Store.MutateRequestGuardedAsync(
                run.Envelope("work-current-state-rejection", actor: rejectingActor), Hash("work-current-state-rejection"),
                PurchasingCommands.RequestRejectV1, requestId,
                (request, bindings, now) => request.Reject(4, rejectingActor, "SENTETIK ihtiyaç kalktı", bindings.HasApprovedOrder, now),
                RequestRejected, _ => Task.CompletedTask);
            Assert.Equal((PurchaseRequestStatus.Rejected, 5L),
                (rejected!.Request.Request.Status, rejected.Request.Request.Version));
        }

        var before = await run.RequestCountsAsync(requestId);
        await using (var replay = run.Store(CompanyA))
        {
            var checks = 0;
            var answer = await replay.Store.MutateRequestGuardedAsync(
                run.Envelope(approvalKey, correlation: "SENTETIK-lost-response-retry", actor: Manager), Hash(approvalKey),
                PurchasingCommands.RequestApproveV1, requestId,
                (_, _, _) => throw new InvalidOperationException("Replay must not approve a rejected request"),
                RequestApproved, _ => { checks++; return Task.CompletedTask; });
            Assert.Equal((PurchasingWriteOutcome.Replayed, run.Correlation), (answer!.Outcome, answer.OriginalCorrelationId));
            Assert.Equal((requestId, PurchaseRequestStatus.Rejected, 5L),
                (answer.Request.Request.Id, answer.Request.Request.Status, answer.Request.Request.Version));
            Assert.Equal(Manager, answer.Request.Request.Approval!.DecidedBy);
            Assert.Equal(rejectingActor, answer.Request.Request.Rejection!.DecidedBy);
            Assert.True(checks >= 2);
        }
        Assert.Equal(before, await run.RequestCountsAsync(requestId));
    }
}
