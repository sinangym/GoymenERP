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
        // Only first approval is composed here. Reapproval requires the human's explicit label selection,
        // which this staged purpose DTO does not contain. Never infer it from the current plan or its inputs.
        if (snapshot.Item.Rules.Any(rule => rule.RuleCode == "manufacturing.plan-reapproval-after-order-cancel")
            || request.Approve && snapshot.Pending
                && !snapshot.Item.Rules.Any(rule => rule.RuleCode == "manufacturing.plan-first-approval"))
            throw new WorkDecisionException(503);
        if (!long.TryParse(request.ExpectedVersion, NumberStyles.None, CultureInfo.InvariantCulture, out var expected)
            || expected < 1 || expected.ToString(CultureInfo.InvariantCulture) != request.ExpectedVersion)
            throw new WorkDecisionException(400, "work_decision_invalid");
        var answer = request.Approve
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
        if (request.Approve)
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
