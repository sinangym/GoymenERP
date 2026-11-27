using System.Globalization;
using GoymenERP.BuildingBlocks.Work;
using GoymenERP.Modules.Manufacturing.Application;

namespace GoymenERP.Api.WorkRuntime;

public sealed partial class WorkDecisionHostV1
{
    private async Task<WorkDecisionResultV1> DecideManufacturingPlanAsync(Guid id, WorkDecisionRequestV1 request,
        string key, WorkApprovalDecisionSnapshotV1 snapshot, string? actor, CancellationToken token)
    {
        if (manufacturingPlans is null) throw new WorkDecisionException(503);
        // The explicit payload selects the native command, not the current queue rule. A later order
        // release must not redirect an old first-approval key, or block an immutable reapproval replay.
        var reapproval = request.ManufacturingPlanReapproval;
        var currentReapproval = snapshot.Item.Rules.Any(rule => rule.RuleCode == "manufacturing.plan-reapproval-after-order-cancel");
        if (request.ExpectedVersion == snapshot.SourceVersion && request.Approve
            && (reapproval is not null && !currentReapproval || reapproval is null && currentReapproval))
            throw new WorkDecisionException(400, "work_decision_invalid");
        if (!long.TryParse(request.ExpectedVersion, NumberStyles.None, CultureInfo.InvariantCulture, out var expected)
            || expected < 1 || expected.ToString(CultureInfo.InvariantCulture) != request.ExpectedVersion)
            throw new WorkDecisionException(400, "work_decision_invalid");
        var answer = reapproval is not null
            ? await manufacturingPlans.ReapprovePlanAsync(id, new(expected, reapproval.SelectedLabelIds), key,
                ManufacturingPermissionManifestV1.PlansApprove, token)
            : request.Approve
            ? await manufacturingPlans.ApprovePlanAsync(id, new(expected), key, ManufacturingPermissionManifestV1.PlansApprove, token)
            : await manufacturingPlans.RejectPlanAsync(id, new(expected, request.Reason), key, ManufacturingPermissionManifestV1.PlansApprove, token);
        if (answer.Status != ManufacturingApiStatus.Ok)
            throw new WorkDecisionException(answer.Status switch
            {
                ManufacturingApiStatus.Invalid => 400, ManufacturingApiStatus.Denied => 403,
                ManufacturingApiStatus.NotFound => 404, ManufacturingApiStatus.Conflict => 409,
                ManufacturingApiStatus.Unprocessable => 422, _ => 503,
            }, answer.Code, answer.IsReplay, answer.OriginalCorrelationId);
        // PlanView is the native immutable command result (not a ProductionOrder or a current read).
        var plan = answer.Resource;
        if (plan is null || plan.CompanyId != snapshot.CompanyId || plan.PlanId != id
            || expected == long.MaxValue || plan.Version != expected + 1 || !Guid.TryParse(actor, out var actorId)
            || (plan.PreparedBy is not null && (string.IsNullOrWhiteSpace(plan.PreparedBy) || WorkInboxRulesV1.SameUser(plan.PreparedBy, actor))))
            throw new WorkDecisionException(503);
        if (reapproval is not null)
        {
            // Only the owner's immutable response proves this decision. Never synthesize a receipt from
            // the current read, which may already describe a later allocation, order or release.
            var history = plan.Reapprovals;
            if (history is null || history.Any(entry => entry is null)) throw new WorkDecisionException(503);
            var latest = history.OrderByDescending(entry => entry.Number).FirstOrDefault();
            if (plan.Status != "Approved" || plan.RequiresReapproval || plan.OrderId is not null || plan.Cancellation is not null
                || latest is null || latest.Number < 1 || latest.PlanVersion != plan.Version
                || history.Count(entry => entry.PlanVersion == plan.Version) != 1
                || history.Any(entry => entry.PlanVersion < 1 || entry.PlanVersion > plan.Version || entry.Number < 1)
                || history.Count(entry => entry.Number == latest.Number) != 1
                || latest.ReapprovedByUserId != actorId || latest.ReapprovedAtUtc == default
                || reapproval.SelectedLabelIds is not { Count: > 0 } selected || selected.Contains(Guid.Empty)
                || selected.Distinct().Count() != selected.Count || plan.Inputs is null || plan.Inputs.Any(input => input is null)
                || !selected.Order().SequenceEqual(plan.Inputs.Select(input => input.ParentRollId).Order()))
                throw new WorkDecisionException(503);
        }
        else if (request.Approve)
        {
            if (plan.Status != "Approved" || plan.ApprovedByUserId != actorId || plan.ApprovedAtUtc is not { } approvedAt
                || approvedAt == default || plan.RequiresReapproval || plan.Reapprovals is { Count: > 0 }
                || plan.OrderId is not null || plan.Cancellation is not null)
                throw new WorkDecisionException(503);
        }
        else
        {
            var rejections = plan.Rejections?.Where(rejection => rejection.PlanVersion == plan.Version).ToArray();
            if (plan.Status != "Draft" || rejections is not { Length: 1 }
                || rejections[0].RejectedByUserId != actorId || rejections[0].Reason != request.Reason?.Trim()
                || rejections[0].RejectedAtUtc == default)
                throw new WorkDecisionException(503);
        }
        return new(snapshot.CompanyId, ManufacturingPlan, id, plan.Status, answer.IsReplay,
            answer.OriginalCorrelationId ?? (answer.IsReplay ? throw new WorkDecisionException(503) : correlation.CorrelationId));
    }
}
