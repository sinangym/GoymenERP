using GoymenERP.Modules.Manufacturing.Application;
using GoymenERP.Modules.Manufacturing.Domain.Common;
using GoymenERP.Modules.Manufacturing.Domain.Orders;
using GoymenERP.Modules.Manufacturing.Domain.Plans;
using Microsoft.EntityFrameworkCore;

namespace GoymenERP.Modules.Manufacturing.Infrastructure.Persistence;

/// <summary>
/// URT-SC33 (URT-D86, URT-D97, URT-D99): the queue of a work center, for the termin suggestion. Read only, from this
/// module's own plan, revision, input, order, fact and output reversal rows of the server-resolved company; nothing is
/// written.
/// The queue rule is <see cref="IManufacturingWorkCenterQueueStore"/>'s (reversible, reported in the slice).
/// </summary>
public sealed partial class ManufacturingStore : IManufacturingWorkCenterQueueStore
{
    public async Task<ManufacturingWorkCenterQueue> ReadWorkCenterQueueAsync(Guid workCenterId, Guid targetPlanId,
        CancellationToken cancellationToken = default)
    {
        var companyId = RequireResolvedCompany();
        if (workCenterId == Guid.Empty)
        {
            throw new ArgumentException("İş merkezi kimliği boş olamaz.", nameof(workCenterId));
        }

        const int approved = (int)PlanStatus.Approved;
        const int closed = (int)OrderStatus.Closed;
        var targetRow = await dbContext.Plans.AsNoTracking()
            .Where(row => row.CompanyId == companyId && row.Id == targetPlanId)
            .Select(row => new { row.Id, row.RequestedDueDate, row.CreatedAtUtc })
            .SingleOrDefaultAsync(cancellationToken)
            ?? throw Refused(ManufacturingStoreCodes.PlanNotFound, "Bu şirketin böyle bir planı yok.");
        var candidates = await dbContext.Plans.AsNoTracking()
            .Where(row => row.CompanyId == companyId && row.Status == approved && row.Id != targetPlanId
                && !dbContext.Orders.Any(order =>
                    order.CompanyId == companyId && order.PlanId == row.Id && order.Status == closed))
            .Select(row => new { row.Id, row.WorkCenterId, row.RequestedDueDate, row.CreatedAtUtc, row.Version })
            .ToListAsync(cancellationToken);

        // URT-D97 (reversible, reported): the termin that orders the queue is the order's termin once the plan has an
        // order (URT-D66 lets it move there), otherwise the plan's requested termin.
        var orderPlanIds = candidates.Select(candidate => candidate.Id).Append(targetPlanId).ToArray();
        var orders = await dbContext.Orders.AsNoTracking()
            .Where(order => order.CompanyId == companyId && orderPlanIds.Contains(order.PlanId)
                && order.Status != (int)OrderStatus.Cancelled)
            .Select(order => new { order.Id, order.PlanId, order.DueDate })
            .ToListAsync(cancellationToken);
        var targetOrder = orders.SingleOrDefault(order => order.PlanId == targetPlanId);

        // The priority score is zero until it is stored (URT-D103 follow-up): every plan then ties on it.
        var target = new ProductionQueueEntryV1(targetRow.Id,
            targetOrder is null ? targetRow.RequestedDueDate : targetOrder.DueDate, 0, targetRow.CreatedAtUtc,
            Kilograms.Zero, Kilograms.Zero);

        if (candidates.Count == 0)
        {
            return new ManufacturingWorkCenterQueue([], target);
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
            return new ManufacturingWorkCenterQueue([], target);
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

        // URT-D99 (field choice reversible, reported): the processed kg of a queued plan is the kg declared as output on
        // its order — every output class, process loss included — net of the output reversals.
        var queuedIds = queued.Select(entry => entry.PlanId).ToHashSet();
        var queuedOrderIds = orders.Where(order => queuedIds.Contains(order.PlanId)).Select(order => order.Id).ToArray();
        const int outputDeclaration = (int)FactKind.OutputDeclaration;
        var declared = await dbContext.Facts.AsNoTracking()
            .Where(fact => fact.CompanyId == companyId && fact.Kind == outputDeclaration
                && queuedOrderIds.Contains(fact.OrderId))
            .GroupBy(fact => fact.OrderId)
            .Select(group => new { OrderId = group.Key, MassKg = group.Sum(fact => fact.MassKg) })
            .ToListAsync(cancellationToken);
        var reversed = await dbContext.OutputReversals.AsNoTracking()
            .Where(reversal => reversal.CompanyId == companyId && queuedOrderIds.Contains(reversal.OrderId))
            .GroupBy(reversal => reversal.OrderId)
            .Select(group => new { OrderId = group.Key, MassKg = group.Sum(reversal => reversal.MassKg) })
            .ToListAsync(cancellationToken);

        var entries = new List<ProductionQueueEntryV1>(queued.Count);
        foreach (var (planId, revisionNo) in queued)
        {
            // A plan always has at least one planned input; without one the queue is not answered.
            var plannedKg = revisionNo == 0
                ? createdInputs.SingleOrDefault(input => input.PlanId == planId)?.PlannedKg
                : revisionInputs.SingleOrDefault(input => input.PlanId == planId && input.RevisionNo == revisionNo)
                    ?.PlannedKg;
            if (plannedKg is null)
            {
                throw new ManufacturingRuleException(ManufacturingRuleCodes.StoredRecordInvalid,
                    "Kuyruktaki planın planlanan girdisi yok.");
            }

            var candidate = candidates.Single(row => row.Id == planId);
            var order = orders.SingleOrDefault(row => row.PlanId == planId);
            var processed = 0m;
            if (order is not null)
            {
                processed = (declared.SingleOrDefault(row => row.OrderId == order.Id)?.MassKg ?? 0m)
                    - (reversed.SingleOrDefault(row => row.OrderId == order.Id)?.MassKg ?? 0m);
                if (processed < 0m)
                {
                    // More reversed than declared is a damaged ledger, not "nothing processed".
                    throw new ManufacturingRuleException(ManufacturingRuleCodes.StoredRecordInvalid,
                        "Kuyruktaki emrin geri alınan çıktısı beyan edilenden fazla.");
                }
            }

            entries.Add(new ProductionQueueEntryV1(planId,
                order is null ? candidate.RequestedDueDate : order.DueDate, 0, candidate.CreatedAtUtc,
                new Kilograms(plannedKg.Value), new Kilograms(processed)));
        }

        return new ManufacturingWorkCenterQueue(entries, target);
    }
}
