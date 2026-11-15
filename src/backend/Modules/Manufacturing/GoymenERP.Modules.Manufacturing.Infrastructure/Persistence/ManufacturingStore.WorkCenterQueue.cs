using GoymenERP.Modules.Manufacturing.Application;
using GoymenERP.Modules.Manufacturing.Domain.Common;
using GoymenERP.Modules.Manufacturing.Domain.Orders;
using GoymenERP.Modules.Manufacturing.Domain.Plans;
using Microsoft.EntityFrameworkCore;

namespace GoymenERP.Modules.Manufacturing.Infrastructure.Persistence;

/// <summary>
/// URT-SC33 (URT-D86): the planned work queued on a work center ahead of a plan, for the termin suggestion. Read only,
/// from this module's own plan, revision, input and order rows of the server-resolved company; nothing is written.
/// The queue rule is <see cref="IManufacturingWorkCenterQueueStore"/>'s (reversible, reported in the slice).
/// </summary>
public sealed partial class ManufacturingStore : IManufacturingWorkCenterQueueStore
{
    public async Task<ManufacturingWorkCenterQueue> ReadWorkCenterQueueAsync(Guid workCenterId, Guid excludedPlanId,
        ManufacturingWorkCenterQueueKey? queuedBefore, CancellationToken cancellationToken = default)
    {
        var companyId = RequireResolvedCompany();
        if (workCenterId == Guid.Empty)
        {
            throw new ArgumentException("İş merkezi kimliği boş olamaz.", nameof(workCenterId));
        }

        const int approved = (int)PlanStatus.Approved;
        const int closed = (int)OrderStatus.Closed;
        var candidates = await dbContext.Plans.AsNoTracking()
            .Where(row => row.CompanyId == companyId && row.Status == approved && row.Id != excludedPlanId
                && !dbContext.Orders.Any(order =>
                    order.CompanyId == companyId && order.PlanId == row.Id && order.Status == closed))
            .Select(row => new { row.Id, row.WorkCenterId, row.ApprovedAtUtc, row.Version })
            .ToListAsync(cancellationToken);
        if (candidates.Any(candidate => candidate.ApprovedAtUtc is null))
        {
            throw new ManufacturingRuleException(ManufacturingRuleCodes.StoredRecordInvalid,
                "Onaylı planın onay zamanı yok.");
        }

        // The queue order key is compared here, in .NET (approval moment, then the Guid's own ordering), so the rule
        // does not depend on the database's uniqueidentifier sort order.
        if (queuedBefore is { } key)
        {
            candidates = candidates
                .Where(candidate => candidate.ApprovedAtUtc!.Value < key.ApprovedAtUtc
                    || (candidate.ApprovedAtUtc.Value == key.ApprovedAtUtc && candidate.Id.CompareTo(key.PlanId) < 0))
                .ToList();
        }

        if (candidates.Count == 0)
        {
            return new ManufacturingWorkCenterQueue(0, Kilograms.Zero);
        }

        var ids = candidates.Select(candidate => candidate.Id).ToArray();
        var headers = await dbContext.PlanRevisions.AsNoTracking()
            .Where(revision => revision.CompanyId == companyId && ids.Contains(revision.PlanId))
            .Select(revision => new { revision.PlanId, revision.RevisionNo, revision.WorkCenterId, revision.PlanVersion })
            .ToListAsync(cancellationToken);

        // MFG-PLAN-EDIT: the CURRENT content is the newest revision the row's version covers (the plan list's rule).
        var queued = new List<(Guid PlanId, int RevisionNo)>();
        foreach (var candidate in candidates)
        {
            var current = headers
                .Where(revision => revision.PlanId == candidate.Id && revision.PlanVersion <= candidate.Version)
                .MaxBy(revision => revision.RevisionNo);
            var currentWorkCenter = current?.WorkCenterId ?? candidate.WorkCenterId;
            if (currentWorkCenter == workCenterId)
            {
                queued.Add((candidate.Id, current?.RevisionNo ?? 0));
            }
        }

        if (queued.Count == 0)
        {
            return new ManufacturingWorkCenterQueue(0, Kilograms.Zero);
        }

        var created = queued.Where(entry => entry.RevisionNo == 0).Select(entry => entry.PlanId).ToArray();
        var revised = queued.Where(entry => entry.RevisionNo != 0).Select(entry => entry.PlanId).ToArray();
        var createdInputs = await dbContext.PlanInputs.AsNoTracking()
            .Where(input => input.CompanyId == companyId && created.Contains(input.PlanId))
            .GroupBy(input => input.PlanId)
            .Select(group => new { PlanId = group.Key, PlannedKg = group.Sum(input => input.PlannedKg) })
            .ToListAsync(cancellationToken);
        var revisionInputs = await dbContext.PlanRevisionInputs.AsNoTracking()
            .Where(input => input.CompanyId == companyId && revised.Contains(input.PlanId))
            .GroupBy(input => new { input.PlanId, input.RevisionNo })
            .Select(group => new
            {
                group.Key.PlanId,
                group.Key.RevisionNo,
                PlannedKg = group.Sum(input => input.PlannedKg),
            })
            .ToListAsync(cancellationToken);

        var total = 0m;
        foreach (var (planId, revisionNo) in queued)
        {
            // A plan always has at least one planned input; without one the queue is not answered.
            var plannedKg = revisionNo == 0
                ? createdInputs.SingleOrDefault(input => input.PlanId == planId)?.PlannedKg
                : revisionInputs.SingleOrDefault(input => input.PlanId == planId && input.RevisionNo == revisionNo)
                    ?.PlannedKg;
            total += plannedKg ?? throw new ManufacturingRuleException(ManufacturingRuleCodes.StoredRecordInvalid,
                "Kuyruktaki planın planlanan girdisi yok.");
        }

        return new ManufacturingWorkCenterQueue(queued.Count, new Kilograms(total));
    }
}
