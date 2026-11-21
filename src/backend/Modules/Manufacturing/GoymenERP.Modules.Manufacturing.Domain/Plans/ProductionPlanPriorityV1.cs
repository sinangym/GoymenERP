using GoymenERP.Modules.Manufacturing.Domain.Common;

namespace GoymenERP.Modules.Manufacturing.Domain.Plans;

/// <summary>URT-D97/D103: a signed Int32 score, default zero, higher first. No manual queue rank is implied.</summary>
public sealed record ProductionPlanPriorityV1(int Score, long Revision)
{
    public const int ReasonMaximumLength = 4000;
    public static ProductionPlanPriorityV1 Default { get; } = new(0, 0);

    public ProductionPlanPriorityV1 Change(long expectedRevision, int score, string? reason)
    {
        if (expectedRevision != Revision) throw new ManufacturingVersionConflictException(expectedRevision, Revision);
        if (string.IsNullOrWhiteSpace(reason) || reason.Trim().Length > ReasonMaximumLength)
            throw new ManufacturingRuleException("manufacturing_plan_priority_reason_required", "Öncelik değişikliğinin gerekçesi zorunludur.");
        if (score == Score)
            throw new ManufacturingRuleException("manufacturing_plan_priority_unchanged", "Öncelik puanı değişmedi.");
        return new(score, checked(Revision + 1));
    }
}
