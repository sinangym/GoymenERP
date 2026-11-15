using GoymenERP.Modules.Manufacturing.Domain.Plans;

namespace GoymenERP.Manufacturing.UnitTests;

/// <summary>
/// URT-D66 (2) test shorthand: a plan without a termin is not approved, so a case that approves a draft plan and does not
/// care about its termin first enters <see cref="DueDate"/>, a day after <see cref="BusinessToday"/>. Entering a termin
/// moves only the termin revision, never the plan's version, so every version the case reads stays as it was. A plan that
/// is not a draft, or that already has a termin, is left as it is. Tests about the termin call the plan directly.
/// </summary>
internal static class PlanTermin
{
    /// <summary>The Europe/Istanbul business day the shorthand enters its termin against.</summary>
    public static readonly DateOnly BusinessToday = new(2026, 11, 1);

    /// <summary>The termin the shorthand enters.</summary>
    public static readonly DateOnly DueDate = new(2026, 12, 31);

    public static ProductionPlan WithDueDate(this ProductionPlan plan)
    {
        if (plan.Status == PlanStatus.Draft && plan.RequestedDueDate is null)
        {
            plan.ChangeDueDate(plan.Version, plan.DueDateRevision, DueDate, orderClosed: false, BusinessToday);
        }

        return plan;
    }
}
