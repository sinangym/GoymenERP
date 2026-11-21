using GoymenERP.Modules.Manufacturing.Domain.Common;
using GoymenERP.Modules.Manufacturing.Domain.Plans;

namespace GoymenERP.Modules.Manufacturing.Domain.Orders;

/// <summary>
/// F-009 D2 (URT-D28, analysis §4.3): the reversal of part or all of one output declaration. R6 — nothing is deleted:
/// the declaration stays as it was recorded and this new fact, linked to it, takes the reversed mass back. The net
/// output of a declaration is its mass minus the sum of its reversals, and that sum never exceeds the declaration
/// (<see cref="ManufacturingRuleCodes.OutputReversalExceedsDeclared"/>). Every class is reversed the same way; the
/// stock movement a reversal causes (none for process loss) belongs to Inventory and is not performed here.
/// </summary>
public sealed record OutputReversal(
    Guid ReversalId,
    Guid OutputId,
    Kilograms Mass,
    string Reason,
    DateTimeOffset ReversedAtUtc)
{
    /// <summary>The longest reason kept (the module's decision reason length); a longer one is refused, never cut.</summary>
    public const int ReasonMaximumLength = ProductionPlan.DecisionReasonMaximumLength;
}
