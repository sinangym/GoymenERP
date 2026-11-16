using GoymenERP.Modules.Manufacturing.Domain.Common;

namespace GoymenERP.Modules.Manufacturing.Domain.Plans;

// URT-SC33 (SINAN-CEVAP 33 follow-up) — the work center queue the termin suggestion counts ahead of a plan:
// - URT-D97: the queue order is the termin (earliest first; a plan without a termin after every dated plan), then the
//   priority score (higher first), then the creation moment (earlier first); the plan id only breaks an exact tie so the
//   order is total and deterministic.
// - URT-D99: each queued plan counts its REMAINING kg: planned input kg minus the kg already processed, never below 0.
// Pure and deterministic; the application decides which plans are queued and reads the stored values.

/// <summary>One plan in a work center queue (or the plan whose termin is suggested, placed by the same key).</summary>
/// <param name="PlanId">The plan.</param>
/// <param name="DueDate">The termin that orders the queue; null sorts after every dated plan.</param>
/// <param name="PriorityScore">URT-D97/URT-D103: the priority score; higher is earlier. Zero when none was entered.</param>
/// <param name="CreatedAtUtc">The plan's creation moment.</param>
/// <param name="PlannedInputKg">The plan's current planned input kg.</param>
/// <param name="ProcessedKg">The kg already processed on the plan's order (zero without an order).</param>
public sealed record ProductionQueueEntryV1(
    Guid PlanId,
    DateOnly? DueDate,
    int PriorityScore,
    DateTimeOffset CreatedAtUtc,
    Kilograms PlannedInputKg,
    Kilograms ProcessedKg);

/// <summary>The work queued ahead of a plan: how many plans and their remaining kg (URT-D97, URT-D99).</summary>
public sealed record ProductionQueueAheadV1(int PlanCount, Kilograms RemainingKg);

/// <summary>URT-D99: the remaining kg of one queued plan.</summary>
public static class ProductionQueueRemainingKgCalculatorV1
{
    /// <summary>Planned input kg minus processed kg; never below zero (an over-processed plan has nothing left).</summary>
    public static Kilograms Remaining(Kilograms plannedInputKg, Kilograms processedKg) =>
        processedKg.Value >= plannedInputKg.Value
            ? Kilograms.Zero
            : new Kilograms(plannedInputKg.Value - processedKg.Value);
}

/// <summary>URT-D97: the queue order of a work center, and the work ordered ahead of one plan.</summary>
public static class ProductionWorkCenterQueueOrderV1
{
    /// <summary>The entries in queue order: termin (nulls last), priority score descending, creation, plan id.</summary>
    public static IReadOnlyList<ProductionQueueEntryV1> Order(IEnumerable<ProductionQueueEntryV1> entries)
    {
        ArgumentNullException.ThrowIfNull(entries);
        var list = entries.ToList();
        if (list.Any(entry => entry is null))
        {
            throw new ArgumentException("Kuyruk girdisi boş olamaz.", nameof(entries));
        }

        list.Sort(Compare);
        return list;
    }

    /// <summary>
    /// The plans ordered before <paramref name="target"/> and the sum of their remaining kg. The target itself is never
    /// counted, also when <paramref name="queued"/> holds it; it is placed by its own key (a draft by where it would
    /// stand once approved).
    /// </summary>
    public static ProductionQueueAheadV1 Ahead(IEnumerable<ProductionQueueEntryV1> queued,
        ProductionQueueEntryV1 target)
    {
        ArgumentNullException.ThrowIfNull(target);
        var count = 0;
        var remaining = Kilograms.Zero;
        foreach (var entry in Order(queued))
        {
            if (entry.PlanId == target.PlanId || Compare(entry, target) >= 0)
            {
                continue;
            }

            count++;
            remaining += ProductionQueueRemainingKgCalculatorV1.Remaining(entry.PlannedInputKg, entry.ProcessedKg);
        }

        return new ProductionQueueAheadV1(count, remaining);
    }

    private static int Compare(ProductionQueueEntryV1 left, ProductionQueueEntryV1 right)
    {
        var dueDate = (left.DueDate, right.DueDate) switch
        {
            (null, null) => 0,
            (null, _) => 1,
            (_, null) => -1,
            ({ } l, { } r) => l.CompareTo(r),
        };
        if (dueDate != 0)
        {
            return dueDate;
        }

        var priority = right.PriorityScore.CompareTo(left.PriorityScore);
        if (priority != 0)
        {
            return priority;
        }

        var created = left.CreatedAtUtc.CompareTo(right.CreatedAtUtc);
        return created != 0 ? created : left.PlanId.CompareTo(right.PlanId);
    }
}
