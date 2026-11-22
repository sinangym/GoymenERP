using GoymenERP.BuildingBlocks.Work;
using GoymenERP.Modules.Manufacturing.Application;
using GoymenERP.Modules.Manufacturing.Domain.Orders;
using GoymenERP.Modules.Manufacturing.Domain.Plans;
using GoymenERP.Modules.Manufacturing.Infrastructure.Persistence;

namespace GoymenERP.Modules.Manufacturing.Infrastructure.WorkInbox;

/// <summary>
/// W1-A (AI-D31, AI-D32, YOL-D18): the production plans waiting for an approval decision, as Manufacturing lists them for the
/// Work inbox. Two states wait, both decided on the plan detail screen by a holder of <c>manufacturing.plans.approve</c> other
/// than the plan's preparer (the inbox decides nothing):
/// <list type="bullet">
/// <item>the first approval — a Draft plan (<c>ApproveManufacturingPlanV1</c>, URT-D2); it waits since its creation or, when
/// revised, since its last Draft revision (a resubmission);</item>
/// <item>the re-approval — an Approved plan released by a cancelled order, with no open order and more releases than
/// re-approvals (<c>ReapproveManufacturingPlanV1</c>, F-009 D5, URT-D35); it waits since the order's cancellation.</item>
/// </list>
/// Every pending plan of the company is returned, the caller's own included: Work hides a maker's own item. The maker is the
/// preparer of the plan's current version as the domain names it — the last reviser, else the actor who saved the plan. A plan
/// stored before preparers were kept (URT-D2, U2-d) and never revised has no preparer; it is left out (it names no maker).
/// <para>
/// No threshold, formula or tolerance makes a plan wait: every plan needs approval. The rules say which approval it waits for
/// and which recorded condition the approval would refuse today (a rejection not yet answered by a revision, a missing termin);
/// none carries numbers. Plans carry no document number, so the label is the plan's identity.
/// </para>
/// </summary>
internal sealed class ManufacturingPlanWorkInboxSourceV1 : IWorkInboxSourceV1
{
    public const string ModuleName = "manufacturing";
    public const string KindName = "manufacturing.plan.v1";

    /// <summary>A Draft plan waits for its first approval.</summary>
    public const string FirstApprovalRule = "manufacturing.plan-first-approval";

    /// <summary>An Approved plan released by a cancelled order waits for its re-approval (URT-D35).</summary>
    public const string ReapprovalRule = "manufacturing.plan-reapproval-after-order-cancel";

    /// <summary>The Draft's last step is a rejection; no revision answered it yet.</summary>
    public const string RejectedNotRevisedRule = "manufacturing.plan-rejected-not-revised";

    /// <summary>The Draft has no termin; approval is refused until one is entered (URT-D66 (2)).</summary>
    public const string DueDateMissingRule = "manufacturing.plan-due-date-missing";

    private readonly ManufacturingWorkInboxRows rows;
    private readonly ManufacturingWorkInboxFence fence;

    public ManufacturingPlanWorkInboxSourceV1(ManufacturingDbContext dbContext, ManufacturingWorkInboxFence fence)
        : this(new ManufacturingWorkInboxDbRows(dbContext), fence)
    {
    }

    internal ManufacturingPlanWorkInboxSourceV1(ManufacturingWorkInboxRows rows, ManufacturingWorkInboxFence fence)
    {
        this.rows = rows;
        this.fence = fence;
    }

    public string Module => ModuleName;

    public string Kind => KindName;

    public string ApproverPermission => ManufacturingPermissionManifestV1.PlansApprove;


    public async Task<WorkInboxSourceResultV1> ListPendingAsync(
        WorkInboxSourceContextV1 context,
        int maxCount,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maxCount);
        cancellationToken.ThrowIfCancellationRequested();
        if (await fence.CheckAsync(context, ApproverPermission, cancellationToken) is { } refused)
        {
            return refused;
        }

        List<Pending> candidates;
        try
        {
            candidates = [.. await ReadDraftsAsync(context.CompanyId, maxCount + 1, cancellationToken),
                .. await ReadReleasedAsync(context.CompanyId, maxCount + 1, cancellationToken)];
        }
        catch (Exception) when (!cancellationToken.IsCancellationRequested)
        {
            return await fence.UnavailableAsync(context, ApproverPermission, cancellationToken);
        }

        if (await fence.CheckAsync(context, ApproverPermission, cancellationToken) is { } afterRead)
        {
            return afterRead;
        }

        // Each read is the oldest maxCount + 1 of its state, so the oldest maxCount of both are among them.
        var ordered = candidates.OrderBy(candidate => candidate.WaitingSinceUtc).ThenBy(candidate => candidate.PlanId).ToList();
        if (ordered.Select(candidate => candidate.PlanId).Distinct().Count() != ordered.Count)
        {
            // A plan is a Draft or Approved, never both: two answers for one plan break Manufacturing's own invariants.
            return WorkInboxSourceResultV1.Unavailable();
        }

        var items = new List<WorkInboxItemV1>(Math.Min(ordered.Count, maxCount));
        foreach (var candidate in ordered.Take(maxCount))
        {
            if (candidate.PlanId == Guid.Empty
                || candidate.Reapproval && (candidate.ReleasedByOrderId is not { } orderId || orderId == Guid.Empty))
            {
                // A record Manufacturing cannot read as its own invariants say: unavailable, never a partial list.
                return WorkInboxSourceResultV1.Unavailable();
            }

            if (string.IsNullOrWhiteSpace(candidate.PreparedBy))
            {
                continue;
            }

            items.Add(Item(candidate));
        }

        return WorkInboxSourceResultV1.Found(context.CompanyId, items, ordered.Count > maxCount);
    }

    private async Task<IReadOnlyList<Pending>> ReadDraftsAsync(Guid companyId, int take, CancellationToken cancellationToken)
    {
        const int draft = (int)PlanStatus.Draft;
        var read = await rows.ListAsync(
            (from plan in rows.Plans
             where plan.CompanyId == companyId && plan.Status == draft
             select new
             {
                 plan.Id,
                 plan.CreatedAtUtc,
                 plan.CreatedBy,
                 plan.RequestedDueDate,
                 LastRevisedAtUtc = rows.PlanRevisions
                     .Where(revision => revision.CompanyId == companyId && revision.PlanId == plan.Id
                         && revision.PlanVersion <= plan.Version)
                     .OrderByDescending(revision => revision.RevisionNo)
                     .Select(revision => (DateTimeOffset?)revision.RevisedAtUtc)
                     .FirstOrDefault(),
                 LastRevisedBy = rows.PlanRevisions
                     .Where(revision => revision.CompanyId == companyId && revision.PlanId == plan.Id
                         && revision.PlanVersion <= plan.Version)
                     .OrderByDescending(revision => revision.RevisionNo)
                     .Select(revision => revision.RevisedBy)
                     .FirstOrDefault(),
                 RejectedNotRevised = rows.PlanRejections
                     .Any(rejection => rejection.CompanyId == companyId && rejection.PlanId == plan.Id
                         && rejection.PlanVersion == plan.Version),
             } into plan
             let waitingSinceUtc = plan.LastRevisedAtUtc ?? plan.CreatedAtUtc
             orderby waitingSinceUtc, plan.Id
             select new
             {
                 plan.Id,
                 WaitingSinceUtc = waitingSinceUtc,
                 PreparedBy = plan.LastRevisedBy ?? plan.CreatedBy,
                 plan.RequestedDueDate,
                 plan.RejectedNotRevised,
             })
            .Take(take),
            cancellationToken);
        return [.. read.Select(plan => new Pending(plan.Id, false, plan.WaitingSinceUtc, plan.PreparedBy,
            plan.RejectedNotRevised, plan.RequestedDueDate is null, null))];
    }

    private async Task<IReadOnlyList<Pending>> ReadReleasedAsync(Guid companyId, int take, CancellationToken cancellationToken)
    {
        const int approved = (int)PlanStatus.Approved;
        const int cancelled = (int)OrderStatus.Cancelled;
        var read = await rows.ListAsync(
            (from plan in rows.Plans
             where plan.CompanyId == companyId && plan.Status == approved
                 && !rows.Orders.Any(order => order.CompanyId == companyId && order.PlanId == plan.Id && order.Status != cancelled)
                 && rows.Orders.Count(order => order.CompanyId == companyId && order.PlanId == plan.Id && order.Status == cancelled
                     && order.PlanVersionAfterCancel <= plan.Version)
                 > rows.PlanReapprovals.Count(reapproval => reapproval.CompanyId == companyId && reapproval.PlanId == plan.Id
                     && reapproval.PlanVersion <= plan.Version)
             select new
             {
                 plan.Id,
                 plan.CreatedBy,
                 LastRevisedBy = rows.PlanRevisions
                     .Where(revision => revision.CompanyId == companyId && revision.PlanId == plan.Id
                         && revision.PlanVersion <= plan.Version)
                     .OrderByDescending(revision => revision.RevisionNo)
                     .Select(revision => revision.RevisedBy)
                     .FirstOrDefault(),
                 ReleasedAtUtc = rows.Orders
                     .Where(order => order.CompanyId == companyId && order.PlanId == plan.Id && order.Status == cancelled
                         && order.PlanVersionAfterCancel <= plan.Version)
                     .OrderByDescending(order => order.PlanVersionAfterCancel)
                     .Select(order => order.CancelledAtUtc)
                     .FirstOrDefault(),
                 ReleasedByOrderId = rows.Orders
                     .Where(order => order.CompanyId == companyId && order.PlanId == plan.Id && order.Status == cancelled
                         && order.PlanVersionAfterCancel <= plan.Version)
                     .OrderByDescending(order => order.PlanVersionAfterCancel)
                     .Select(order => (Guid?)order.Id)
                     .FirstOrDefault(),
             } into plan
             orderby plan.ReleasedAtUtc, plan.Id
             select new
             {
                 plan.Id,
                 plan.ReleasedAtUtc,
                 PreparedBy = plan.LastRevisedBy ?? plan.CreatedBy,
                 plan.ReleasedByOrderId,
             })
            .Take(take),
            cancellationToken);
        var pending = new List<Pending>(read.Count);
        foreach (var plan in read)
        {
            if (plan.ReleasedAtUtc is not { } releasedAtUtc)
            {
                // A cancelled order always carries its cancellation time (its check constraint): a broken record.
                throw new InvalidOperationException("Manufacturing order cancellation without its time.");
            }

            pending.Add(new Pending(plan.Id, true, releasedAtUtc, plan.PreparedBy, false, false, plan.ReleasedByOrderId));
        }

        return pending;
    }

    private static WorkInboxItemV1 Item(Pending candidate)
    {
        var rules = new List<WorkApprovalRuleResultV1>(3)
        {
            new(candidate.Reapproval ? ReapprovalRule : FirstApprovalRule, null, null, null, null),
        };
        if (candidate.RejectedNotRevised)
        {
            rules.Add(new(RejectedNotRevisedRule, null, null, null, null));
        }

        if (candidate.DueDateMissing)
        {
            rules.Add(new(DueDateMissingRule, null, null, null, null));
        }

        var references = candidate.ReleasedByOrderId is { } orderId
            ? new[]
            {
                new WorkApprovalReferenceV1("manufacturing-order", ManufacturingWorkInboxFence.Id(orderId), orderId,
                    ManufacturingWorkInboxFence.OrderLink(orderId)),
            }
            : [];
        return new WorkInboxItemV1(ModuleName, KindName, candidate.PlanId, ManufacturingWorkInboxFence.Id(candidate.PlanId),
            candidate.WaitingSinceUtc.ToUniversalTime(), ManufacturingWorkInboxFence.PlanLink(candidate.PlanId),
            candidate.PreparedBy!.Trim(), rules, references);
    }

    private sealed record Pending(
        Guid PlanId,
        bool Reapproval,
        DateTimeOffset WaitingSinceUtc,
        string? PreparedBy,
        bool RejectedNotRevised,
        bool DueDateMissing,
        Guid? ReleasedByOrderId);
}
